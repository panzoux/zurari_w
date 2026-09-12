namespace Zurari.Runtime;

/// <summary>
/// Runs shell thumbnail extractions one at a time on a single STA thread, keeping at most one
/// request waiting: a newer request replaces the waiter rather than queueing behind it.
/// </summary>
/// <remarks>
/// <para>
/// The call is a synchronous, out-of-process COM call that cannot be cancelled, so a
/// <see cref="CancellationToken"/> cannot reach inside it. Before this existed, a superseded preview
/// signalled its token and the next one started immediately anyway, which meant arrowing through a
/// folder of videos left an unbounded number of extractions in flight - each measured at 282-722 ms
/// against real files. Holding an arrow down now costs one extraction in flight and one waiting,
/// whatever the folder's size.
/// </para>
/// <para>
/// STA because thumbnail providers are commonly STA-only. On a thread-pool thread (MTA) the shell
/// marshals the call into an apartment of its own choosing; it works, and it is not worth continuing
/// to bet on.
/// </para>
/// </remarks>
internal sealed class ShellThumbnailThread : IDisposable
{
    private readonly Func<string, int, byte[]?> _extract;
    private readonly Thread _thread;
    private readonly object _gate = new();

    /// <summary>The one request waiting to run, if any. Replaced, never queued - see the remarks.</summary>
    private Pending? _waiting;

    private bool _disposed;

    public ShellThumbnailThread(Func<string, int, byte[]?> extract)
    {
        ArgumentNullException.ThrowIfNull(extract);
        _extract = extract;
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "zurari shell thumbnails",
        };
        // Guarded because Runtime targets plain net8.0 and apartments are a Windows notion. Nothing
        // is lost elsewhere: the extractor is injected from Zurari.Shell, which only exists here.
        if (OperatingSystem.IsWindows())
        {
            _thread.SetApartmentState(ApartmentState.STA);
        }

        _thread.Start();
    }

    /// <summary>
    /// Asks for <paramref name="path"/>'s thumbnail. The task completes with the PNG bytes, or with
    /// <c>null</c> when the shell has none - or when a newer request displaced this one before it
    /// ever ran.
    /// </summary>
    public Task<byte[]?> Request(string path, int size)
    {
        var pending = new Pending(path, size);

        lock (_gate)
        {
            if (_disposed)
            {
                pending.Completion.TrySetResult(null);
                return pending.Completion.Task;
            }

            var displaced = _waiting;
            _waiting = pending;
            Monitor.Pulse(_gate);

            // Whoever was waiting is stale by definition - the cursor has moved past it - so it is
            // told "nothing" rather than left waiting for a result nobody will look at.
            displaced?.Completion.TrySetResult(null);
        }

        return pending.Completion.Task;
    }

    private void Run()
    {
        while (true)
        {
            Pending taken;
            lock (_gate)
            {
                while (_waiting is null && !_disposed)
                {
                    Monitor.Wait(_gate);
                }

                if (_disposed)
                {
                    return;
                }

                taken = _waiting!;
                _waiting = null;
            }

            byte[]? extracted = null;
            try
            {
                extracted = _extract(taken.Path, taken.Size);
            }
            catch (Exception ex) when (ex is not (OutOfMemoryException or StackOverflowException))
            {
                // A throwing extractor must not take this thread with it: it is the only one, and
                // losing it would hang every later request. No thumbnail is the same answer as none.
            }

            taken.Completion.TrySetResult(extracted);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _waiting?.Completion.TrySetResult(null);
            _waiting = null;
            Monitor.PulseAll(_gate);
        }

        // Brief, because the thread may be inside an extraction that cannot be cancelled. It is a
        // background thread, so an extraction still running at shutdown does not hold the process.
        _thread.Join(TimeSpan.FromSeconds(1));
    }

    private sealed class Pending(string path, int size)
    {
        public string Path { get; } = path;

        public int Size { get; } = size;

        /// <summary>
        /// Continuations run asynchronously so that completing a request never runs a caller's work
        /// on this thread - it has one job, and anything else queued onto it delays every thumbnail.
        /// </summary>
        public TaskCompletionSource<byte[]?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
