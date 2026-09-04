using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Zurari.Runtime;

/// <summary>
/// On-disk cache of generated video thumbnails, so the second visit to a file costs a file read
/// instead of an ffmpeg run.
/// </summary>
/// <remarks>
/// <para>
/// Failures are cached too, and that is the point rather than an extra. A file ffmpeg cannot decode
/// - a truncated download, an <c>.mp4</c> whose <c>moov</c> atom never arrived - otherwise costs the
/// full timeout *every time the cursor lands on it*, forever. Only verdicts ffmpeg actually reached
/// are stored; see <see cref="ThumbnailFailure"/> for why a timeout or a missing ffmpeg must not be.
/// </para>
/// <para>
/// The key is the file's path, last-write time and length, so an edited file misses and regenerates
/// rather than showing a stale frame. Nothing is ever overwritten: a changed file simply writes a
/// new entry and orphans the old one, which is what makes <see cref="Sweep"/> necessary.
/// </para>
/// </remarks>
public sealed class ThumbnailCache
{
    /// <summary>Entries older than this are swept. Long enough to survive a holiday.</summary>
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);

    /// <summary>Total cache size allowed before the oldest entries are swept.</summary>
    private const long MaxTotalBytes = 256L * 1024 * 1024;

    private const string HitExtension = ".png";
    private const string MissExtension = ".miss";

    private readonly string? _directory;

    /// <summary>
    /// Creates a cache under <paramref name="directory"/>, defaulting to
    /// <c>%LOCALAPPDATA%\zurari\thumbs</c>.
    /// </summary>
    /// <remarks>
    /// The directory is injectable so tests do not write to the real per-user cache - the same
    /// reason <see cref="UserSettingsStore"/> takes one.
    /// </remarks>
    public ThumbnailCache(string? directory = null)
    {
        _directory = directory ?? DefaultDirectory();

        // Eviction runs off-thread: the cache is constructed during startup, and enumerating a
        // large directory there would delay the first window. Losing a sweep costs nothing.
        InitialSweep = Task.Run(Sweep);
    }

    /// <summary>
    /// The startup sweep, so a caller can tell when the cache has stopped touching its directory.
    /// </summary>
    /// <remarks>
    /// Nothing in the app waits for it - that is the point of running it off-thread. It is exposed
    /// because a constructor that starts background work otherwise leaves the type with no quiescent
    /// point at all: a test that made a cache and then deleted its directory was racing a sweep that
    /// was still walking it, and failed intermittently with UnauthorizedAccessException.
    /// </remarks>
    internal Task InitialSweep { get; }

    private static string? DefaultDirectory()
    {
        try
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return local.Length == 0 ? null : Path.Combine(local, "zurari", "thumbs");
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// Looks up a previously stored result for <paramref name="videoPath"/>. Returns <c>false</c>
    /// when there is no usable entry - including when the file has changed since one was written.
    /// </summary>
    public bool TryGet(string videoPath, out byte[]? bytes, out string? failureDetail)
    {
        bytes = null;
        failureDetail = null;

        if (KeyFor(videoPath) is not { } key)
        {
            return false;
        }

        try
        {
            var hit = Path.Combine(_directory!, key + HitExtension);
            if (File.Exists(hit))
            {
                bytes = File.ReadAllBytes(hit);
                Touch(hit);
                return bytes.Length > 0;
            }

            var miss = Path.Combine(_directory!, key + MissExtension);
            if (File.Exists(miss))
            {
                failureDetail = File.ReadAllText(miss, Encoding.UTF8);
                Touch(miss);
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A cache is an optimization; a read failure just means regenerating.
        }

        return false;
    }

    /// <summary>Stores a generated thumbnail for <paramref name="videoPath"/>. Best effort.</summary>
    public void StoreSuccess(string videoPath, byte[] png)
    {
        ArgumentNullException.ThrowIfNull(png);
        Write(videoPath, HitExtension, file => File.WriteAllBytes(file, png));
    }

    /// <summary>
    /// Records that ffmpeg rejected <paramref name="videoPath"/>, so the next visit reports the same
    /// reason immediately instead of paying the timeout again. Best effort.
    /// </summary>
    public void StoreFailure(string videoPath, string detail) =>
        Write(videoPath, MissExtension, file => File.WriteAllText(file, detail, Encoding.UTF8));

    private void Write(string videoPath, string extension, Action<string> write)
    {
        if (KeyFor(videoPath) is not { } key)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(_directory!);

            // Written beside the target and moved into place, so a cancelled or crashed run cannot
            // leave a half-written entry that would later be served as a valid thumbnail.
            var final = Path.Combine(_directory!, key + extension);
            var temp = final + ".tmp";
            write(temp);
            File.Move(temp, final, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Best effort - failing to cache is not worth surfacing to the user.
        }
    }

    /// <summary>
    /// Deletes entries older than <see cref="MaxAge"/>, then oldest-first until the cache fits in
    /// <see cref="MaxTotalBytes"/>. Safe to call concurrently with reads and writes: everything is
    /// best effort and a deleted entry simply regenerates.
    /// </summary>
    public void Sweep()
    {
        if (_directory is null)
        {
            return;
        }

        try
        {
            var dir = new DirectoryInfo(_directory);
            if (!dir.Exists)
            {
                return;
            }

            var files = dir.GetFiles();
            var cutoff = DateTime.UtcNow - MaxAge;
            var survivors = new List<FileInfo>(files.Length);

            foreach (var file in files)
            {
                if (file.LastWriteTimeUtc < cutoff)
                {
                    TryDelete(file);
                }
                else
                {
                    survivors.Add(file);
                }
            }

            var total = survivors.Sum(f => f.Length);
            if (total <= MaxTotalBytes)
            {
                return;
            }

            survivors.Sort((a, b) => a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc));
            foreach (var file in survivors)
            {
                if (total <= MaxTotalBytes)
                {
                    return;
                }

                total -= file.Length;
                TryDelete(file);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Sweeping is housekeeping; a failure just leaves the cache larger for now.
        }
    }

    private static void TryDelete(FileInfo file)
    {
        try
        {
            file.Delete();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another process is reading it, or it is already gone.
        }
    }

    /// <summary>
    /// Marks an entry as recently used, so <see cref="Sweep"/>'s oldest-first eviction keeps the
    /// files actually being looked at rather than the most recently generated ones.
    /// </summary>
    private static void Touch(string file)
    {
        try
        {
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Only affects eviction order.
        }
    }

    /// <summary>
    /// Cache key for <paramref name="videoPath"/>: a hash of its full path, plus the last-write
    /// time and length that the entry describes. Returns <c>null</c> when the file cannot be
    /// stat'ed, which simply means "do not cache this".
    /// </summary>
    private string? KeyFor(string videoPath)
    {
        if (_directory is null || string.IsNullOrEmpty(videoPath))
        {
            return null;
        }

        try
        {
            var info = new FileInfo(videoPath);
            if (!info.Exists)
            {
                return null;
            }

            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(info.FullName));

            // Upper case at the analyzer's insistence (CA1308): lowercasing can collapse distinct
            // characters in some cultures. Irrelevant for hex, but the case here is arbitrary
            // anyway - it only has to be stable, since it is a filename we both write and read.
            var prefix = Convert.ToHexString(hash.AsSpan(0, 16));
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{prefix}_{info.LastWriteTimeUtc.Ticks:x16}_{info.Length:x16}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
