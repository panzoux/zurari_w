using System.Collections.Immutable;
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
    private readonly Channel<Effect> _channel;
    private readonly Action<Msg> _post;
    private readonly CancellationTokenSource _cts;
    private readonly Task[] _workers;

    /// <summary>
    /// Starts <paramref name="workerCount"/> background workers that pull queued effects and
    /// execute them. <paramref name="post"/> may be invoked concurrently from any worker thread;
    /// the caller is responsible for marshalling results onto whatever thread owns application
    /// state (the UI thread in the real app). <paramref name="external"/>, if given, lets the
    /// caller cancel the whole runtime cooperatively in addition to <see cref="Dispose"/>.
    /// </summary>
    public WorkerRuntime(Action<Msg> post, int workerCount = 2, CancellationToken? external = null)
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
