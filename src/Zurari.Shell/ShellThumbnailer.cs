using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Zurari.Shell;

/// <summary>
/// Explorer's own thumbnail for a file, as PNG bytes - without ever leaving a <c>Thumbs.db</c>
/// behind.
/// </summary>
/// <remarks>
/// <para>
/// Tried before ffmpeg for video: it needs no external tool, uses whatever codecs Windows already
/// has, and handles formats ffmpeg-without-extras does not.
/// </para>
/// <para>
/// <b>No <c>Thumbs.db</c>, by two independent means.</b> Windows still writes a hidden
/// <c>Thumbs.db</c> into a network folder when a thumbnail is extracted there, unless a policy says
/// otherwise - and on this machine no policy does. It is the file that makes a network folder
/// impossible to delete while anything has it open. So:
/// </para>
/// <list type="number">
/// <item><see cref="ShellThumbnailInterop.WTS_EXTRACTDONOTCACHE"/> tells the shell's thumbnail
/// cache to write the result nowhere - not <c>Thumbs.db</c>, not its own central cache. This app keeps
/// the PNG in its own cache under %LOCALAPPDATA% instead.</item>
/// <item>Anything that is not plainly on a local volume is refused before the shell is involved at
/// all: a UNC path, or a drive letter Windows reports as a network drive. That check is made on the
/// string and <c>GetDriveType</c>, so refusing costs no network round trip.</item>
/// </list>
/// <para>
/// Either one alone would do. Both are here because the first depends on the shell honouring a flag
/// and the second depends on classifying a path correctly, and neither is worth betting a stray file
/// in someone's share on. The Runtime adds a third, from the open file handle, which catches a local
/// path that is really a link into a share.
/// </para>
/// </remarks>
public static class ShellThumbnailer
{
    /// <summary>
    /// The shell's thumbnail for <paramref name="path"/> at up to <paramref name="size"/> pixels on
    /// its longer side, or <c>null</c> - no thumbnail provider, not a local file, or any failure.
    /// </summary>
    /// <remarks>
    /// Never throws. A thumbnail is a nicety, and the caller has ffmpeg and a hex dump to fall back on.
    /// </remarks>
    public static byte[]? TryGetPng(string path, int size) =>
        !string.IsNullOrEmpty(path) && size > 0 && IsPlainlyLocal(path) ? ExtractPng(path, size) : null;

    /// <summary>
    /// The extraction alone, with no check on where the file is. Internal so a test can aim it at a
    /// network path on purpose - the only way to prove the do-not-cache flag, rather than the guard in
    /// front of it, is what keeps <c>Thumbs.db</c> out of a share.
    /// </summary>
    internal static byte[]? ExtractPng(string path, int size)
    {
        FileOperationInterop.IShellItem? item = null;
        ShellThumbnailInterop.IThumbnailCache? cache = null;
        ShellThumbnailInterop.ISharedBitmap? shared = null;
        var hbitmap = IntPtr.Zero;
        try
        {
            if (FileOperationInterop.SHCreateItemFromParsingName(
                    path, IntPtr.Zero, FileOperationInterop.IidIShellItem, out item) != 0
                || item is null)
            {
                return null;
            }

            cache = (ShellThumbnailInterop.IThumbnailCache)new ShellThumbnailInterop.LocalThumbnailCache();
            var hr = cache.GetThumbnail(
                item,
                (uint)size,
                ShellThumbnailInterop.WTS_EXTRACTDONOTCACHE | ShellThumbnailInterop.WTS_SCALETOREQUESTEDSIZE,
                out shared,
                out _,
                IntPtr.Zero);

            if (hr != 0 || shared is null || shared.Detach(out hbitmap) != 0 || hbitmap == IntPtr.Zero)
            {
                return null;
            }

            return EncodePng(hbitmap);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or ArgumentException
            or ExternalException)
        {
            return null;
        }
        finally
        {
            if (hbitmap != IntPtr.Zero)
            {
                ShellThumbnailInterop.DeleteObject(hbitmap);
            }

            ReleaseIfComObject(shared);
            ReleaseIfComObject(cache);
            ReleaseIfComObject(item);
        }
    }

    /// <summary>
    /// Whether <paramref name="path"/> is on a volume this machine owns - a local fixed, removable,
    /// optical or RAM disk. Anything uncertain answers no.
    /// </summary>
    /// <remarks>
    /// Decided from the string and <c>GetDriveType</c>, neither of which goes near the network, so a
    /// share that is slow or gone cannot make this slow. A path that is a link into a share passes
    /// here; the Runtime catches that case from the handle it already has open.
    /// </remarks>
    public static bool IsPlainlyLocal(string path)
    {
        if (string.IsNullOrEmpty(path) || path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            var root = Path.GetPathRoot(path);
            if (string.IsNullOrEmpty(root) || root.Length < 2 || root[1] != ':')
            {
                return false;
            }

            return new DriveInfo(root).DriveType is DriveType.Fixed or DriveType.Removable
                or DriveType.CDRom or DriveType.Ram;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Converts a GDI bitmap into PNG bytes. The caller still owns the handle.</summary>
    private static byte[] EncodePng(IntPtr hbitmap)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            hbitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        source.Freeze();

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        return stream.ToArray();
    }

    private static void ReleaseIfComObject(object? value)
    {
        if (value is not null && Marshal.IsComObject(value))
        {
            Marshal.ReleaseComObject(value);
        }
    }
}
