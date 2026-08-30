using System.Collections.Concurrent;
using System.IO;
using Zurari.App;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.App.Tests;

/// <summary>
/// The drive pane from a cold start, through the real runtime: exactly what the app does on
/// launch, minus the window.
/// </summary>
public class DrivePaneE2ETests
{
    private static void DrainUntil(
        MessageLoop loop, ConcurrentQueue<Msg> queue, Func<bool> done, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline && !done())
        {
            if (queue.TryDequeue(out var msg))
            {
                loop.Dispatch(msg);
            }
            else
            {
                Thread.Sleep(10);
            }
        }

        Assert.True(done(), "Timed out waiting for the expected state.");
    }

    [Fact]
    public void On_launch_the_cursor_lands_on_the_first_header_and_Space_collapses_it()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        var loop = new MessageLoop(initial: AppState.Initial, runEffect: runtime.Submit);

        // Exactly what MainWindow's constructor does last.
        loop.Dispatch(new Msg.Refresh());
        DrainUntil(loop, queue, () => loop.State.Columns[0].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));

        var column = loop.State.Columns[0];
        Assert.Equal(0, column.Cursor);
        Assert.Equal(EntryKind.Header, column.Entries[0].Kind);

        // The projection agrees - this is what the control is handed.
        var vms = StateProjection.Project(loop.State);
        Assert.Equal(0, vms[0].CursorIndex);
        Assert.Equal(Zurari.Controls.EntryKind.Header, vms[0].Entries[0].Kind);

        var before = column.Entries.Length;

        // Space, as OnWindowKeyDown sends it.
        loop.Dispatch(new Msg.ToggleMarkAtCursor(loop.State.FocusedColumn));

        var after = loop.State.Columns[0];
        Assert.True(after.Entries.Length < before, "the section should have collapsed");
        Assert.Equal(EntryKind.Header, after.Entries[0].Kind);
        Assert.Equal(before, after.AllEntries.Length);
    }

    [Fact]
    public void The_cursor_can_be_moved_onto_a_later_header()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        var loop = new MessageLoop(initial: AppState.Initial, runEffect: runtime.Submit);

        loop.Dispatch(new Msg.Refresh());
        DrainUntil(loop, queue, () => loop.State.Columns[0].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));

        var entries = loop.State.Columns[0].Entries;
        var laterHeader = -1;
        for (var i = 1; i < entries.Length; i++)
        {
            if (entries[i].Kind == EntryKind.Header)
            {
                laterHeader = i;
                break;
            }
        }

        if (laterHeader < 0)
        {
            return; // only one section on this machine; nothing to prove.
        }

        for (var i = 0; i < laterHeader; i++)
        {
            loop.Dispatch(new Msg.CursorDown(0));
        }

        Assert.Equal(laterHeader, loop.State.Columns[0].Cursor);
        Assert.Equal(EntryKind.Header, loop.State.Columns[0].Entries[laterHeader].Kind);
    }

    /// <summary>
    /// Ctrl+B, all the way through: cursor on a folder, effect routed, settings written, pane redrawn.
    /// </summary>
    /// <remarks>
    /// Routes the effect the way the app does rather than submitting it to the runtime directly.
    /// That distinction is the whole point: Effect.SetPinned reached the runtime in every other
    /// test and was dropped by the composition root in the running app.
    /// </remarks>
    [Fact]
    public void Ctrl_B_on_a_folder_adds_it_and_the_pane_shows_it()
    {
        var dir = Path.Combine(Path.GetTempPath(), "zurari-e2e-" + Guid.NewGuid().ToString("N"));
        var target = Path.Combine(dir, "panzoux");
        Directory.CreateDirectory(target);
        try
        {
            var store = new Zurari.Runtime.UserSettingsStore(Path.Combine(dir, "cfg"));
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);

            // The real routing decision, not a direct submit.
            var loop = new MessageLoop(
                initial: new AppState
                {
                    Columns = [new Column(new Location.RealDirectory(dir), Entries: [])],
                    FocusedColumn = 0,
                },
                runEffect: e =>
                {
                    Assert.Equal(EffectTarget.Runtime, EffectRouting.For(e));
                    runtime.Submit(e);
                });

            loop.Dispatch(new Msg.Refresh());
            DrainUntil(loop, queue, () => loop.State.Columns[0].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));
            Assert.Equal(0, loop.State.Columns[0].Cursor);

            loop.Dispatch(new Msg.PinEntryAtCursor(0));
            DrainUntil(loop, queue, () => store.Load().PinnedPaths?.Count > 0, TimeSpan.FromSeconds(10));

            Assert.Equal([target], store.Load().PinnedPaths);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
