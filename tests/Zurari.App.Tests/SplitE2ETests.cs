using System.Collections.Concurrent;
using System.IO;
using Zurari.Core;
using Zurari.Runtime;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.App.Tests;

/// <summary>Split the window, copy a file from the top pane into the bottom one, see it arrive.</summary>
public class SplitE2ETests
{
    private static void DrainUntil(MessageLoop loop, ConcurrentQueue<Msg> queue, Func<bool> done, TimeSpan timeout)
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

    /// <summary>Whether <paramref name="column"/> lists an entry called <paramref name="name"/>. A loop: LINQ is banned.</summary>
    private static bool HasEntry(Column column, string name)
    {
        foreach (var entry in column.Entries)
        {
            if (entry.Name == name)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Builds the top pane at a source directory holding hello.txt and the bottom pane at an empty
    /// destination, refreshes, copies or moves to the other pane and hands the settled loop to
    /// <paramref name="check"/>.
    /// </summary>
    private static void RunTransfer(bool isMove, Action<MessageLoop, string, string> check, Func<MessageLoop, bool> settled)
    {
        var source = TempDirectory.Create("zurari-split-src");
        var dest = TempDirectory.Create("zurari-split-dst");
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue, settings: new UserSettingsStore(IsolatedSettings.NewDirectory()));
        var jobs = new JobEngine(queue.Enqueue);
        try
        {
            File.WriteAllText(Path.Combine(source, "hello.txt"), "hello");

            void RunEffect(Effect effect)
            {
                switch (effect)
                {
                    case Effect.ReadDirectory:
                        runtime.Submit(effect);
                        break;
                    case Effect.RunFileJob:
                    case Effect.CancelJob:
                        jobs.Submit(effect);
                        break;
                }
            }

            // The layout Ctrl+W and navigation would produce (both are covered by Core tests):
            // top pane at the source, bottom pane at the destination.
            var initial = TwoPanes(
                new Column(new Location.RealDirectory(source), Entries: []),
                new Column(new Location.RealDirectory(dest), Entries: []));
            var loop = new MessageLoop(initial, RunEffect);
            loop.Dispatch(new Msg.Refresh());
            DrainUntil(
                loop,
                queue,
                () => loop.State.Panes[0].Columns[0].Load == LoadState.Loaded
                    && loop.State.Panes[1].Columns[0].Load == LoadState.Loaded
                    && loop.State.Panes[0].Columns[0].CursorName == "hello.txt",
                TimeSpan.FromSeconds(10));

            loop.Dispatch(new Msg.CopyToOtherPane(IsMove: isMove));

            DrainUntil(loop, queue, () => settled(loop), TimeSpan.FromSeconds(15));
            check(loop, source, dest);
        }
        finally
        {
            runtime.Dispose();
            jobs.Dispose();
            TempDirectory.Delete(source);
            TempDirectory.Delete(dest);
        }
    }

    [Fact]
    public void Startup_restores_the_split_and_each_panes_stored_view_from_a_settings_file()
    {
        var dir = TempDirectory.Create("zurari-split-startup");
        var settingsDir = IsolatedSettings.NewDirectory();
        var store = new UserSettingsStore(settingsDir);
        var queue = new ConcurrentQueue<Msg>();
        var runtime = new WorkerRuntime(queue.Enqueue, settings: store);
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");
            store.Update(s => s with
            {
                SplitOpen = true,
                SplitRatio = 0.4,
                PaneViews =
                [
                    new PaneViewSettings("Size", true, false, true),
                    new PaneViewSettings("Extension", false, true, false),
                ],
            });

            void RunEffect(Effect effect)
            {
                if (effect is Effect.ReadDirectory)
                {
                    runtime.Submit(effect);
                }
            }

            var loop = new MessageLoop(
                AppState.SinglePane([new Column(new Location.RealDirectory(dir), Entries: [])]),
                RunEffect);

            foreach (var msg in SplitLayout.StartupMsgs(store.Load(), loop.State.Focused.Id))
            {
                loop.Dispatch(msg);
            }

            DrainUntil(
                loop,
                queue,
                () => loop.State.Panes.Length == 2
                    && loop.State.Panes[0].Columns[0].Load == LoadState.Loaded
                    && loop.State.Panes[1].Columns[0].Load == LoadState.Loaded,
                TimeSpan.FromSeconds(10));

            Assert.Equal(new ViewOptions(new SortOrder(SortMode.Size, true, false), true), loop.State.Panes[0].View);
            Assert.Equal(new ViewOptions(new SortOrder(SortMode.Extension, false, true), false), loop.State.Panes[1].View);
            Assert.Equal(0, loop.State.FocusedPane);
        }
        finally
        {
            runtime.Dispose();
            TempDirectory.Delete(dir);
            TempDirectory.Delete(settingsDir);
        }
    }

    [Fact]
    public void Copying_to_the_other_pane_lands_in_the_directory_it_shows()
    {
        RunTransfer(
            isMove: false,
            settled: loop => HasEntry(loop.State.Panes[1].Columns[0], "hello.txt"),
            check: (loop, source, dest) =>
            {
                Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
                Assert.True(File.Exists(Path.Combine(source, "hello.txt")), "a copy leaves the source in place");
            });
    }

    [Fact]
    public void Moving_to_the_other_pane_lands_in_the_directory_it_shows_and_leaves_the_source()
    {
        RunTransfer(
            isMove: true,
            settled: loop => HasEntry(loop.State.Panes[1].Columns[0], "hello.txt")
                && !HasEntry(loop.State.Panes[0].Columns[0], "hello.txt"),
            check: (loop, source, dest) =>
            {
                Assert.True(File.Exists(Path.Combine(dest, "hello.txt")));
                Assert.False(File.Exists(Path.Combine(source, "hello.txt")), "a move removes the source");
            });
    }
}
