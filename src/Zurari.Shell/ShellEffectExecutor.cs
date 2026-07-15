using System.Runtime.InteropServices;
using System.Threading.Channels;
using Zurari.Core;

namespace Zurari.Shell;

/// <summary>
/// Executes shell-only <see cref="Effect"/>s (recycle-bin delete today; copy/move/context-menu in
/// later Phase 4 tasks) and reports the outcome back through the <c>post</c> delegate supplied at
/// construction. Mirrors <c>Zurari.Runtime.WorkerRuntime</c>'s shape (a <see cref="Channel{T}"/>
/// feeding a single worker, cooperative <see cref="Dispose"/>) but lives in Zurari.Shell because
/// Zurari.Runtime cannot reference Zurari.Shell (Arch regels): the App composition root routes
/// filesystem effects to WorkerRuntime and shell effects to this type by a plain type switch.
/// </summary>
/// <remarks>
/// The single worker runs on a dedicated STA thread rather than the thread pool. <c>IFileOperation</c>
/// is documented to prefer STA — it pumps window messages internally for its (suppressed here)
/// progress UI — and thread-pool threads are MTA by default, so a plain <c>Task.Run</c> worker
/// would risk COM marshaling/threading issues that only show up intermittently.
/// </remarks>
public sealed class ShellEffectExecutor : IDisposable
{
    private readonly Channel<Effect> channel;
    private readonly Action<Msg> post;
    private readonly CancellationTokenSource cts;
    private readonly Thread worker;

    /// <summary>
    /// Starts one STA background worker that pulls queued effects and executes them.
    /// <paramref name="post"/> may be invoked from the worker thread; the caller is responsible
    /// for marshalling results onto whatever thread owns application state (the UI thread in the
    /// real app).
    /// </summary>
    public ShellEffectExecutor(Action<Msg> post)
    {
        ArgumentNullException.ThrowIfNull(post);

        this.post = post;
        channel = Channel.CreateUnbounded<Effect>();
        cts = new CancellationTokenSource();

        worker = new Thread(() => RunWorker(cts.Token))
        {
            IsBackground = true,
            Name = "Zurari.ShellEffectExecutor",
        };
        worker.SetApartmentState(ApartmentState.STA);
        worker.Start();
    }

    /// <summary>Enqueues an effect for execution by the worker thread. Never blocks.</summary>
    public void Submit(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        channel.Writer.TryWrite(effect);
    }

    private void RunWorker(CancellationToken token)
    {
        var reader = channel.Reader;
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (!reader.WaitToReadAsync(token).AsTask().GetAwaiter().GetResult())
                {
                    return;
                }

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
            case Effect.DeleteToRecycleBin deleteToRecycleBin:
                ExecuteDeleteToRecycleBin(deleteToRecycleBin);
                break;
            default:
                // Filesystem effects (ReadDirectory, ...) are routed to WorkerRuntime by the App
                // composition root and never reach this executor; ignore anything unrecognized.
                break;
        }
    }

    private void ExecuteDeleteToRecycleBin(Effect.DeleteToRecycleBin effect)
    {
        object? fileOperation = null;
        FileOperationInterop.IShellItem? item = null;
        try
        {
            fileOperation = new FileOperationInterop.FileOperation();
            var op = (FileOperationInterop.IFileOperation)fileOperation;

            var hr = op.SetOperationFlags(
                FileOperationInterop.FOF_ALLOWUNDO
                | FileOperationInterop.FOF_NOCONFIRMATION
                | FileOperationInterop.FOF_SILENT
                | FileOperationInterop.FOF_NOERRORUI);
            ThrowIfFailed(hr, "SetOperationFlags");

            hr = FileOperationInterop.SHCreateItemFromParsingName(
                effect.TargetFullPath,
                IntPtr.Zero,
                FileOperationInterop.IidIShellItem,
                out item);
            ThrowIfFailed(hr, "SHCreateItemFromParsingName");

            hr = op.DeleteItem(item, IntPtr.Zero);
            ThrowIfFailed(hr, "DeleteItem");

            hr = op.PerformOperations();
            ThrowIfFailed(hr, "PerformOperations");

            hr = op.GetAnyOperationsAborted(out var aborted);
            ThrowIfFailed(hr, "GetAnyOperationsAborted");

            if (aborted)
            {
                post(new Msg.DeleteFailed(effect.ColumnIndex, effect.Path, "Delete operation was aborted."));
                return;
            }

            post(new Msg.DeleteCompleted(effect.ColumnIndex, effect.Path));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mirrors Zurari.Runtime.WorkerRuntime.ExecuteReadDirectory: a single effect's
            // interop failure (invalid path, COM error, cast failure...) must never take the
            // worker thread down, so it is reported as a Msg instead of propagating.
            post(new Msg.DeleteFailed(effect.ColumnIndex, effect.Path, ex.Message));
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }

            if (fileOperation is not null)
            {
                Marshal.ReleaseComObject(fileOperation);
            }
        }
    }

    private static void ThrowIfFailed(int hr, string what)
    {
        if (hr < 0)
        {
            throw new InvalidOperationException($"{what} failed (HRESULT 0x{hr:X8}).");
        }
    }

    /// <summary>
    /// Stops accepting new work, cancels in-flight work cooperatively, and waits (bounded to a
    /// few seconds) for the worker to exit. Effects already queued but not yet started are
    /// dropped. Safe to call even while effects are queued: this returns promptly rather than
    /// draining the backlog.
    /// </summary>
    public void Dispose()
    {
        channel.Writer.TryComplete();
        cts.Cancel();
        worker.Join(TimeSpan.FromSeconds(5));
        cts.Dispose();
    }
}
