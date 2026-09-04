using System.Collections.Immutable;
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
    private readonly bool suppressUi;

    /// <summary>
    /// Starts one STA background worker that pulls queued effects and executes them.
    /// <paramref name="post"/> may be invoked from the worker thread; the caller is responsible
    /// for marshalling results onto whatever thread owns application state (the UI thread in the
    /// real app).
    /// </summary>
    public ShellEffectExecutor(Action<Msg> post)
        : this(post, suppressUi: false)
    {
    }

    /// <summary>
    /// As <see cref="ShellEffectExecutor(Action{Msg})"/>, but with <paramref name="suppressUi"/>
    /// additionally suppressing <c>IFileOperation</c>'s progress/overwrite/error UI for
    /// <see cref="Effect.ShellCopyOrMove"/>. Only meant for tests: a real transfer's whole point is
    /// the shell's own progress dialog (see the Phase 4 design notes), so the public constructor
    /// never suppresses it.
    /// </summary>
    internal ShellEffectExecutor(Action<Msg> post, bool suppressUi)
    {
        ArgumentNullException.ThrowIfNull(post);

        this.post = post;
        this.suppressUi = suppressUi;
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
            case Effect.ReadDirectory { Location: Location.RecycleBin } readRecycleBin:
                ExecuteReadRecycleBin(readRecycleBin);
                break;
            case Effect.ShellCopyOrMove shellCopyOrMove:
                ExecuteShellCopyOrMove(shellCopyOrMove);
                break;
            case Effect.MountImage mountImage:
                ExecuteMountImage(mountImage);
                break;
            default:
                // Filesystem effects (ReadDirectory, ...) are routed to WorkerRuntime by the App
                // composition root and never reach this executor; ignore anything unrecognized.
                break;
        }
    }

    /// <summary>
    /// Lists the recycle bin. The one listing that does not come from the filesystem, which is why
    /// it is served here rather than by the Runtime.
    /// </summary>
    /// <remarks>
    /// A deleted folder is reported as a directory and can be browsed, since the parsing name is a
    /// real path. Restoring is still the way to get something back; that is the shell context menu's
    /// job, which now works here because the row carries a name the shell can resolve.
    /// </remarks>
    private void ExecuteReadRecycleBin(Effect.ReadDirectory effect)
    {
        try
        {
            // A row is named by the shell's parsing name - where the item lives now
            // (C:\$Recycle.Bin\S-1-5-...\$R...) - because that is what every operation on it needs:
            // the context menu, and opening it for a preview. The original path stopped existing
            // when the item was deleted.
            //
            // The label is the original file's name, always - never a qualified path. Qualifying
            // duplicates the way the drive pane does was actively worse here: in a bin of hundreds
            // nearly every name collides, so nearly every row turned into an end-truncated path and
            // the whole list read as identical. Where it came from goes to OriginalPath, which the
            // preview shows in full.
            var entries = RecycleBinFolder.EnumerateParsingNames()
                .Select(item => new Entry(
                    item.Parsing,
                    item.IsFolder ? EntryKind.Directory : EntryKind.File,
                    SizeBytes: -1,
                    DisplayName: LastSegment(item.Original),
                    OriginalPath: item.Original))
                .ToImmutableArray();
            post(new Msg.DirectoryLoaded(effect.ColumnIndex, effect.Location, entries));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            post(new Msg.DirectoryLoadFailed(effect.ColumnIndex, effect.Location, ex.Message));
        }
    }

    /// <summary>How long <see cref="ExecuteMountImage"/> waits for the new drive to turn up.</summary>
    /// <remarks>
    /// The verb returns once the shell has taken the request, not once the volume is mounted. A
    /// second is generous for an ISO on a local disk and short enough that a failure is not mistaken
    /// for a hang.
    /// </remarks>
    private static readonly TimeSpan MountAppearTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Mounts a disc image and reports which drive it became.
    /// </summary>
    /// <remarks>
    /// The drive letter is not returned by anything: the verb succeeds and the volume appears a
    /// moment later. Comparing the set of drives before and after is the only way to name it, and
    /// naming it is what lets the cursor land on it. <c>DriveInfo</c> here is not the file I/O this
    /// layer is banned from - it is "which volumes exist", which is inseparable from having just
    /// created one.
    /// </remarks>
    private void ExecuteMountImage(Effect.MountImage effect)
    {
        var before = DriveNames();

        if (ShellVerbs.Invoke("mount", effect.Path) is { } error)
        {
            post(new Msg.NoticeRaised($"マウントできません: {error.Message}"));
            return;
        }

        var deadline = DateTime.UtcNow + MountAppearTimeout;
        while (DateTime.UtcNow < deadline)
        {
            foreach (var name in DriveNames())
            {
                if (!before.Contains(name))
                {
                    post(new Msg.ImageMounted(name));
                    return;
                }
            }

            Thread.Sleep(100);
        }

        // Mounted, but nothing new turned up - an image that was already mounted, most likely. Say
        // so rather than leaving the keypress looking ignored.
        post(new Msg.NoticeRaised("新しいドライブは見つかりませんでした"));
    }

    private static HashSet<string> DriveNames()
    {
        try
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var drive in System.IO.DriveInfo.GetDrives())
            {
                names.Add(drive.Name);
            }

            return names;
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The file name at the end of a deleted item's original path.</summary>
    private static string LastSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var separator = trimmed.LastIndexOfAny(['\\', '/']);
        return separator < 0 || separator == trimmed.Length - 1 ? trimmed : trimmed[(separator + 1)..];
    }

    /// <summary>
    /// Moves every path in <see cref="Effect.DeleteToRecycleBin.Targets"/> to the recycle bin as a
    /// single batch: one <c>IShellItem</c> plus <c>DeleteItem</c> call per target, then one
    /// <c>PerformOperations</c>. If any target fails to parse (or any <c>DeleteItem</c> call
    /// fails), the whole batch is reported failed with that target's message - none of the queued
    /// deletes have been performed yet at that point, since <c>PerformOperations</c> has not run.
    /// <see cref="Effect.DeleteToRecycleBin.Permanent"/> (Shift+Delete) omits
    /// <see cref="FileOperationInterop.FOF_ALLOWUNDO"/>, which is what routes the delete through the
    /// recycle bin in the first place - without it, <c>IFileOperation</c> deletes outright.
    /// </summary>
    private void ExecuteDeleteToRecycleBin(Effect.DeleteToRecycleBin effect)
    {
        object? fileOperation = null;
        var items = new List<FileOperationInterop.IShellItem>();
        try
        {
            fileOperation = new FileOperationInterop.FileOperation();
            var op = (FileOperationInterop.IFileOperation)fileOperation;

            var flags = FileOperationInterop.FOF_NOCONFIRMATION
                | FileOperationInterop.FOF_SILENT
                | FileOperationInterop.FOF_NOERRORUI;
            if (!effect.Permanent)
            {
                flags |= FileOperationInterop.FOF_ALLOWUNDO;
            }

            var hr = op.SetOperationFlags(flags);
            ThrowIfFailed(hr, "SetOperationFlags");

            foreach (var targetFullPath in effect.Targets)
            {
                hr = FileOperationInterop.SHCreateItemFromParsingName(
                    targetFullPath,
                    IntPtr.Zero,
                    FileOperationInterop.IidIShellItem,
                    out var item);
                ThrowIfFailed(hr, "SHCreateItemFromParsingName");
                items.Add(item);

                hr = op.DeleteItem(item, IntPtr.Zero);
                ThrowIfFailed(hr, "DeleteItem");
            }

            hr = op.PerformOperations();
            ThrowIfFailed(hr, "PerformOperations");

            hr = op.GetAnyOperationsAborted(out var aborted);
            ThrowIfFailed(hr, "GetAnyOperationsAborted");

            if (aborted)
            {
                post(new Msg.ShellOpFailed(effect.ColumnIndex, effect.ColumnLocation, "Delete operation was aborted."));
                return;
            }

            post(new Msg.ShellOpCompleted(effect.ColumnIndex, effect.ColumnLocation, DistinctParents(effect.Targets)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Mirrors Zurari.Runtime.WorkerRuntime.ExecuteReadDirectory: a single effect's
            // interop failure (invalid path, COM error, cast failure...) must never take the
            // worker thread down, so it is reported as a Msg instead of propagating.
            post(new Msg.ShellOpFailed(effect.ColumnIndex, effect.ColumnLocation, ex.Message));
        }
        finally
        {
            foreach (var item in items)
            {
                Marshal.ReleaseComObject(item);
            }

            if (fileOperation is not null)
            {
                Marshal.ReleaseComObject(fileOperation);
            }
        }
    }

    /// <summary>
    /// Copies or moves <see cref="Effect.ShellCopyOrMove.Paths"/> into
    /// <see cref="Effect.ShellCopyOrMove.DestPath"/> via <c>IFileOperation</c> - which may be a
    /// subdirectory row rather than the column's own path - and reports the outcome keyed by
    /// <see cref="Effect.ShellCopyOrMove.ColumnPath"/> so the staleness check and re-read target
    /// the column, not the row. Unlike the recycle-bin
    /// delete above, this deliberately omits <c>FOF_SILENT</c>/<c>FOF_NOERRORUI</c>/
    /// <c>FOF_NOCONFIRMATION</c> in normal operation — the whole point of delegating to
    /// <c>IFileOperation</c> instead of writing a copy loop (see the Phase 4 design notes) is to
    /// get the shell's own progress dialog and overwrite prompts for large transfers. Tests pass
    /// <see cref="suppressUi"/> to opt out of any UI so they run headless.
    /// </summary>
    private void ExecuteShellCopyOrMove(Effect.ShellCopyOrMove effect)
    {
        object? fileOperation = null;
        FileOperationInterop.IShellItem? destItem = null;
        var sourceItems = new List<FileOperationInterop.IShellItem>();
        try
        {
            fileOperation = new FileOperationInterop.FileOperation();
            var op = (FileOperationInterop.IFileOperation)fileOperation;

            var flags = FileOperationInterop.FOF_ALLOWUNDO | FileOperationInterop.FOF_NOCONFIRMMKDIR;
            if (suppressUi)
            {
                flags |= FileOperationInterop.FOF_NOCONFIRMATION
                    | FileOperationInterop.FOF_SILENT
                    | FileOperationInterop.FOF_NOERRORUI;
            }

            var hr = op.SetOperationFlags(flags);
            ThrowIfFailed(hr, "SetOperationFlags");

            hr = FileOperationInterop.SHCreateItemFromParsingName(
                effect.DestPath, IntPtr.Zero, FileOperationInterop.IidIShellItem, out destItem);
            ThrowIfFailed(hr, "SHCreateItemFromParsingName(dest)");

            foreach (var sourcePath in effect.Paths)
            {
                hr = FileOperationInterop.SHCreateItemFromParsingName(
                    sourcePath, IntPtr.Zero, FileOperationInterop.IidIShellItem, out var sourceItem);
                ThrowIfFailed(hr, "SHCreateItemFromParsingName(source)");
                sourceItems.Add(sourceItem);

                hr = effect.IsMove
                    ? op.MoveItem(sourceItem, destItem, null, IntPtr.Zero)
                    : op.CopyItem(sourceItem, destItem, null, IntPtr.Zero);
                ThrowIfFailed(hr, effect.IsMove ? "MoveItem" : "CopyItem");
            }

            hr = op.PerformOperations();
            ThrowIfFailed(hr, "PerformOperations");

            hr = op.GetAnyOperationsAborted(out var aborted);
            ThrowIfFailed(hr, "GetAnyOperationsAborted");

            if (aborted)
            {
                // Keyed to the column, not to DestPath. Msg.ShellOpFailed is matched against the
                // column's own location, so reporting the drop destination here meant that an
                // aborted row-granular drop (DestPath = a subdirectory row, not the column) failed
                // that check, was discarded, and left the column stuck in LoadState.Loading. The
                // success and exception paths below always keyed on the column; this one did not.
                post(new Msg.ShellOpFailed(effect.ColumnIndex, effect.ColumnLocation, "Operation was aborted."));
                return;
            }

            post(new Msg.ShellOpCompleted(effect.ColumnIndex, effect.ColumnLocation, AffectedDirs(effect)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            post(new Msg.ShellOpFailed(effect.ColumnIndex, effect.ColumnLocation, ex.Message));
        }
        finally
        {
            foreach (var sourceItem in sourceItems)
            {
                Marshal.ReleaseComObject(sourceItem);
            }

            if (destItem is not null)
            {
                Marshal.ReleaseComObject(destItem);
            }

            if (fileOperation is not null)
            {
                Marshal.ReleaseComObject(fileOperation);
            }
        }
    }

    /// <summary>
    /// Every directory whose contents changed as a result of <paramref name="effect"/>: its
    /// destination, plus - only for a move, since a copy leaves its sources untouched - the
    /// distinct parents of its sources.
    /// </summary>
    private static ImmutableArray<string> AffectedDirs(Effect.ShellCopyOrMove effect)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        builder.Add(effect.DestPath);

        if (effect.IsMove)
        {
            foreach (var parent in DistinctParents(effect.Paths))
            {
                if (!builder.Contains(parent, StringComparer.OrdinalIgnoreCase))
                {
                    builder.Add(parent);
                }
            }
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// The distinct, non-null parent directories of <paramref name="paths"/> (comparison is
    /// case-insensitive). A path whose parent cannot be determined (invalid characters, etc.) is
    /// skipped rather than throwing - mirrors the rest of this type's policy of never letting a
    /// single bad input take the worker down.
    /// </summary>
    private static ImmutableArray<string> DistinctParents(ImmutableArray<string> paths)
    {
        var builder = ImmutableArray.CreateBuilder<string>();
        foreach (var path in paths)
        {
            string? parent;
            try
            {
                parent = System.IO.Path.GetDirectoryName(path);
            }
            catch (ArgumentException)
            {
                parent = null;
            }

            if (parent is not null && !builder.Contains(parent, StringComparer.OrdinalIgnoreCase))
            {
                builder.Add(parent);
            }
        }

        return builder.ToImmutable();
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
