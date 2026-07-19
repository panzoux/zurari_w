using System.Collections.Immutable;
using System.Text;
using System.Threading.Channels;
using Zurari.Core;

namespace Zurari.Runtime;

/// <summary>
/// Executes <see cref="Effect"/>s on a fixed pool of background workers and reports the
/// resulting <see cref="Msg"/>s back through the <c>post</c> delegate supplied at construction.
/// This is the only layer allowed to perform file I/O; Core stays pure and describes I/O as data.
/// </summary>
public sealed class WorkerRuntime : IDisposable
{
    /// <summary>
    /// Above this size, an image preview is reported as <see cref="PreviewKind.Binary"/> instead
    /// of decoding the whole file into memory. Overridable per-instance (see the constructor) so
    /// tests can exercise the "too big" branch without writing a 50MB fixture.
    /// </summary>
    private const long DefaultPreviewImageSizeLimitBytes = 50L * 1024 * 1024;

    /// <summary>How much of a file <see cref="Effect.LoadPreview"/> reads to sniff/decode as text.</summary>
    private const int PreviewHeadBytes = 64 * 1024;

    /// <summary>
    /// How much of the already-read head is carried along on a <see cref="PreviewKind.Binary"/>
    /// result (in <see cref="Msg.PreviewLoaded.ImageBytes"/> - see its remarks) for the hex-dump
    /// view. A hex pane never needs more than a handful of KB, so this is far smaller than
    /// <see cref="PreviewHeadBytes"/>.
    /// </summary>
    private const int PreviewHexHeadBytes = 4096;

    private readonly Channel<Effect> _channel;
    private readonly Action<Msg> _post;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _workers;
    private readonly long _previewImageSizeLimitBytes;

    static WorkerRuntime()
    {
        // Shift-JIS (codepage 932) is not one of .NET Core's built-in encodings; this registers
        // the full legacy codepage table so Effect.LoadPreview's Shift-JIS fallback can use
        // Encoding.GetEncoding(932) below.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>
    /// Starts <paramref name="workerCount"/> background workers that pull queued effects and
    /// execute them. <paramref name="post"/> may be invoked concurrently from any worker thread;
    /// the caller is responsible for marshalling results onto whatever thread owns application
    /// state (the UI thread in the real app). <paramref name="external"/>, if given, lets the
    /// caller cancel the whole runtime cooperatively in addition to <see cref="Dispose"/>.
    /// <paramref name="previewImageSizeLimitBytes"/> overrides <see cref="DefaultPreviewImageSizeLimitBytes"/>
    /// (test-only knob; production callers should omit it).
    /// </summary>
    public WorkerRuntime(
        Action<Msg> post, int workerCount = 2, CancellationToken? external = null, long? previewImageSizeLimitBytes = null)
    {
        ArgumentNullException.ThrowIfNull(post);
        if (workerCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(workerCount), workerCount, "At least one worker is required.");
        }

        _post = post;
        _channel = Channel.CreateUnbounded<Effect>();
        _cts = external.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(external.Value)
            : new CancellationTokenSource();
        _previewImageSizeLimitBytes = previewImageSizeLimitBytes ?? DefaultPreviewImageSizeLimitBytes;

        _workers = new Task[workerCount];
        for (var i = 0; i < workerCount; i++)
        {
            _workers[i] = Task.Run(() => RunWorkerAsync(_cts.Token));
        }
    }

    /// <summary>Enqueues an effect for execution by one of the worker threads. Never blocks.</summary>
    public void Submit(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        _channel.Writer.TryWrite(effect);
    }

    private async Task RunWorkerAsync(CancellationToken token)
    {
        var reader = _channel.Reader;
        try
        {
            while (await reader.WaitToReadAsync(token).ConfigureAwait(false))
            {
                while (reader.TryRead(out var effect))
                {
                    if (token.IsCancellationRequested)
                    {
                        return;
                    }

                    Execute(effect);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Dispose() requested cancellation; exit quietly.
        }
        catch (ChannelClosedException)
        {
            // Writer completed while we were waiting; exit quietly.
        }
    }

    private void Execute(Effect effect)
    {
        switch (effect)
        {
            case Effect.ReadDirectory readDirectory:
                ExecuteReadDirectory(readDirectory);
                break;
            case Effect.LoadPreview loadPreview:
                ExecuteLoadPreview(loadPreview);
                break;
        }
    }

    private void ExecuteReadDirectory(Effect.ReadDirectory effect)
    {
        try
        {
            var entries = effect.Path.Length == 0
                ? ReadDrives()
                : ReadDirectoryEntries(effect.Path);
            _post(new Msg.DirectoryLoaded(effect.ColumnIndex, effect.Path, entries));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _post(new Msg.DirectoryLoadFailed(effect.ColumnIndex, effect.Path, ex.Message));
        }
    }

    private static ImmutableArray<Entry> ReadDrives()
    {
        var builder = ImmutableArray.CreateBuilder<Entry>();
        foreach (var drive in DriveInfo.GetDrives())
        {
            // Not-ready drives (empty CD/DVD, disconnected mapped drives...) are still listed;
            // browsing into one will simply fail later with DirectoryLoadFailed.
            builder.Add(new Entry(drive.Name, EntryKind.Drive, SizeBytes: -1));
        }

        return builder.ToImmutable();
    }

    private static ImmutableArray<Entry> ReadDirectoryEntries(string path)
    {
        var directories = new List<Entry>();
        var files = new List<Entry>();
        var dirInfo = new DirectoryInfo(path);

        foreach (var dir in dirInfo.EnumerateDirectories())
        {
            try
            {
                directories.Add(new Entry(dir.Name, EntryKind.Directory, SizeBytes: -1, Modified: dir.LastWriteTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Broken reparse point or similar: drop this one entry, keep listing the rest.
            }
        }

        foreach (var file in dirInfo.EnumerateFiles())
        {
            try
            {
                files.Add(new Entry(file.Name, EntryKind.File, SizeBytes: file.Length, Modified: file.LastWriteTime));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Same as above: skip this file, keep the rest of the listing.
            }
        }

        directories.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var builder = ImmutableArray.CreateBuilder<Entry>(directories.Count + files.Count);
        builder.AddRange(directories);
        builder.AddRange(files);
        return builder.MoveToImmutable();
    }

    /// <summary>
    /// Reads up to <see cref="PreviewHeadBytes"/> of <see cref="Effect.LoadPreview.Path"/>,
    /// sniffs it with <see cref="FileTypeDetector"/>, and posts the outcome back:
    /// <list type="bullet">
    /// <item>An image kind whose whole file is at or under <see cref="_previewImageSizeLimitBytes"/>
    /// -&gt; the entire file's bytes as <see cref="PreviewKind.Image"/>.</item>
    /// <item>An image kind over that limit -&gt; <see cref="PreviewKind.Binary"/> with a
    /// "(サイズ超過)" suffix on the detected label.</item>
    /// <item>Anything that decodes as text (BOM-tagged UTF-8/UTF-16, strict UTF-8, or Shift-JIS,
    /// and containing no NUL byte in the sampled head) -&gt; <see cref="PreviewKind.Text"/> with
    /// the decoded head.</item>
    /// <item>Everything else -&gt; <see cref="PreviewKind.Binary"/> with the detected label.</item>
    /// </list>
    /// Both <see cref="PreviewKind.Binary"/> cases above also carry up to
    /// <see cref="PreviewHexHeadBytes"/> of the head in the result's <c>ImageBytes</c> (see
    /// <see cref="HexHead"/> and <see cref="Msg.PreviewLoaded"/>'s remarks) so the App layer can
    /// render a hex dump.
    /// Any exception (missing file, access denied, ...) is reported as
    /// <see cref="Msg.PreviewFailed"/> instead of propagating - a bad preview request must never
    /// take down a worker.
    /// </summary>
    private void ExecuteLoadPreview(Effect.LoadPreview effect)
    {
        try
        {
            using var stream = new FileStream(
                effect.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            var headLength = (int)Math.Min(stream.Length, PreviewHeadBytes);
            var head = new byte[headLength];
            stream.ReadExactly(head);

            var detected = FileTypeDetector.Detect(head);

            if (detected.Category == FileCategory.Image)
            {
                PostImagePreview(effect, stream, detected, head);
                return;
            }

            if (TryDecodeAsText(head, out var text))
            {
                _post(new Msg.PreviewLoaded(effect.Generation, PreviewKind.Text, text, ImmutableArray<byte>.Empty, null));
                return;
            }

            _post(new Msg.PreviewLoaded(
                effect.Generation, PreviewKind.Binary, null, HexHead(head), detected.Label));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _post(new Msg.PreviewFailed(effect.Generation, ex.Message));
        }
    }

    /// <summary>
    /// Slices <paramref name="head"/> down to <see cref="PreviewHexHeadBytes"/> for the hex-dump
    /// view carried on a <see cref="PreviewKind.Binary"/> result - see <see cref="PreviewHexHeadBytes"/>.
    /// </summary>
    private static ImmutableArray<byte> HexHead(byte[] head)
    {
        var length = Math.Min(head.Length, PreviewHexHeadBytes);
        return ImmutableArray.Create(head, 0, length);
    }

    private void PostImagePreview(Effect.LoadPreview effect, FileStream stream, DetectedType detected, byte[] head)
    {
        if (stream.Length > _previewImageSizeLimitBytes)
        {
            _post(new Msg.PreviewLoaded(
                effect.Generation, PreviewKind.Binary, null, HexHead(head), $"{detected.Label} (サイズ超過)"));
            return;
        }

        stream.Position = 0;
        var allBytes = new byte[stream.Length];
        stream.ReadExactly(allBytes);
        _post(new Msg.PreviewLoaded(effect.Generation, PreviewKind.Image, null, [.. allBytes], null));
    }

    /// <summary>
    /// Best-effort text decode of <paramref name="head"/>: a BOM (UTF-8/UTF-16 LE/UTF-16 BE)
    /// always wins and is decoded directly; otherwise a NUL byte anywhere in <paramref name="head"/>
    /// rules text out outright (BOM-less UTF-16 legitimately contains NULs, which is exactly why
    /// that path is handled above instead), and the remainder is tried as strict UTF-8, falling
    /// back to strict Shift-JIS. Returns <c>false</c> (binary) if nothing decodes cleanly.
    /// </summary>
    private static bool TryDecodeAsText(byte[] head, out string text)
    {
        if (head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF)
        {
            text = Encoding.UTF8.GetString(head, 3, head.Length - 3);
            return true;
        }

        if (head.Length >= 2 && head[0] == 0xFF && head[1] == 0xFE)
        {
            text = Encoding.Unicode.GetString(head, 2, head.Length - 2);
            return true;
        }

        if (head.Length >= 2 && head[0] == 0xFE && head[1] == 0xFF)
        {
            text = Encoding.BigEndianUnicode.GetString(head, 2, head.Length - 2);
            return true;
        }

        if (head.Length == 0)
        {
            text = string.Empty;
            return true;
        }

        if (Array.IndexOf(head, (byte)0) >= 0)
        {
            text = string.Empty;
            return false;
        }

        var utf8Strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        try
        {
            text = utf8Strict.GetString(head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            // Not valid UTF-8; fall through to the Shift-JIS attempt below.
        }

        try
        {
            var sjisStrict = Encoding.GetEncoding(
                932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            text = sjisStrict.GetString(head);
            return true;
        }
        catch (DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>
    /// Stops accepting new work, cancels in-flight work cooperatively, and waits (bounded to a
    /// few seconds) for all workers to exit. Effects already queued but not yet started are
    /// dropped. Safe to call even while effects are queued: this returns promptly rather than
    /// draining the backlog.
    /// </summary>
    public void Dispose()
    {
        _channel.Writer.TryComplete();
        _cts.Cancel();
        try
        {
            Task.WaitAll(_workers, TimeSpan.FromSeconds(5));
        }
        catch (AggregateException)
        {
            // A worker faulted while unwinding from cancellation; nothing more to do on Dispose.
        }

        _cts.Dispose();
    }
}
