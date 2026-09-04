using System.Collections.Concurrent;
using System.IO;
using System.Linq;
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
        var dir = TempDirectory.Create("zurari-e2e");
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
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// A restart, twice over: pin a folder and collapse a section in one session, then start a fresh
    /// runtime and loop over the same settings and check both came back.
    /// </summary>
    /// <remarks>
    /// Persistence had been asserted only as "the value reached the file". That is half the claim,
    /// and the missing half is where it broke - closing the window wrote the preview width as a
    /// fresh record and erased everything else, so nothing ever came back.
    /// </remarks>
    [StaFact]
    public void What_was_pinned_and_collapsed_comes_back_on_the_next_launch()
    {
        var dir = TempDirectory.Create("zurari-restart");
        var target = Path.Combine(dir, "panzoux");
        Directory.CreateDirectory(target);
        var settingsDir = Path.Combine(dir, "cfg");
        try
        {
            var store = new Zurari.Runtime.UserSettingsStore(settingsDir);

            // --- first session ---
            {
                var queue = new ConcurrentQueue<Msg>();
                using var runtime = new WorkerRuntime(queue.Enqueue, settings: store);
                var loop = NewLoop(runtime, new Location.RealDirectory(dir));

                loop.Dispatch(new Msg.Refresh());
                DrainUntil(loop, queue, () => loop.State.Columns[0].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));

                loop.Dispatch(new Msg.PinEntryAtCursor(0));
                DrainUntil(loop, queue, () => store.Load().PinnedPaths?.Count > 0, TimeSpan.FromSeconds(10));
            }

            // Closing the window. Saving one preference must not erase the others.
            store.Update(s => s with { PreviewWidth = 280 });

            // --- second session: a cold start against the same settings ---
            {
                var queue = new ConcurrentQueue<Msg>();
                using var runtime = new WorkerRuntime(queue.Enqueue, settings: new Zurari.Runtime.UserSettingsStore(settingsDir));
                var loop = NewLoop(runtime, Location.Drives.Instance);

                loop.Dispatch(new Msg.Refresh());
                DrainUntil(
                    loop,
                    queue,
                    () => loop.State.Columns[0].Load == LoadState.Loaded
                        && loop.State.Columns[0].Entries.Any(e => e.Name == target),
                    TimeSpan.FromSeconds(10));

                var pinned = loop.State.Columns[0].Entries.Single(e => e.Name == target);
                Assert.True(pinned.IsRemovable);
                Assert.Equal("panzoux", pinned.Label);

                // Collapse ドライブ specifically, not just the first header - the pinned row lives
                // under お気に入り, and collapsing that would hide the very thing session three checks.
                var header = loop.State.Columns[0].Entries.ToList()
                    .FindIndex(e => e.Kind == EntryKind.Header && e.Group == EntryGroups.Drives);
                loop.Dispatch(new Msg.ToggleSection(0, header));
                DrainUntil(loop, queue, () => store.Load().CollapsedGroups?.Count > 0, TimeSpan.FromSeconds(10));
                Assert.Equal([EntryGroups.Drives], store.Load().CollapsedGroups);
            }

            store.Update(s => s with { PreviewWidth = 300 });

            // --- third session: the collapse has to come back too ---
            {
                var queue = new ConcurrentQueue<Msg>();
                using var runtime = new WorkerRuntime(queue.Enqueue, settings: new Zurari.Runtime.UserSettingsStore(settingsDir));
                var loop = NewLoop(runtime, Location.Drives.Instance);

                loop.Dispatch(new Msg.Refresh());
                DrainUntil(
                    loop,
                    queue,
                    () => !loop.State.Columns[0].CollapsedGroups.IsEmpty,
                    TimeSpan.FromSeconds(10));

                var column = loop.State.Columns[0];
                Assert.Equal([EntryGroups.Drives], column.CollapsedGroups);
                Assert.DoesNotContain(column.Entries, e => e.Kind == EntryKind.Drive);
                Assert.Contains(column.AllEntries, e => e.Kind == EntryKind.Drive);

                // The pin is in a section that was left open, so it is on screen.
                Assert.Contains(column.Entries, e => e.Name == target);
            }
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>A loop wired exactly as <c>MainWindow</c> wires it - routing included.</summary>
    private static MessageLoop NewLoop(WorkerRuntime runtime, Location start) =>
        new(
            initial: new AppState
            {
                Columns = [new Column(start, Entries: [])],
                FocusedColumn = 0,
            },
            runEffect: e =>
            {
                Assert.Equal(EffectTarget.Runtime, EffectRouting.For(e));
                runtime.Submit(e);
            });
}
