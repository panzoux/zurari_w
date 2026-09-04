using System.Collections.Concurrent;
using System.IO;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.App.Tests;

/// <summary>
/// End-to-end coverage of the composition root's plumbing (MessageLoop + real WorkerRuntime +
/// StateProjection) over a real temporary directory tree, without ever creating a WPF window.
/// This replaces manual keyboard testing for the navigation flow: refresh, enter a directory,
/// move the cursor, and go back up, asserting the projected view model after each step.
/// </summary>
public class HeadlessBrowsingTests
{
    private static string CreateTempTree()
    {
        var root = TempDirectory.Create("zurari-app-tests");
        Directory.CreateDirectory(Path.Combine(root, "Alpha"));
        Directory.CreateDirectory(Path.Combine(root, "Beta"));
        File.WriteAllText(Path.Combine(root, "root.txt"), "hi");
        File.WriteAllText(Path.Combine(root, "Alpha", "inner.txt"), "hello");
        return root;
    }

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

    [Fact]
    public void Refresh_enter_directory_cursor_move_and_go_to_parent_reflect_in_projection()
    {
        var root = CreateTempTree();
        try
        {
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            var loop = new MessageLoop(
                initial: new AppState { Columns = [new Column(new Location.RealDirectory(root), Entries: [])], FocusedColumn = 0 },
                runEffect: runtime.Submit);

            // 1. Refresh loads the root column.
            loop.Dispatch(new Msg.Refresh());
            DrainUntil(loop, queue, () => loop.State.Columns[0].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));

            // Two columns: the root, plus the one that opened beside the cursor because it landed
            // on a directory.
            var rootVms = StateProjection.Project(loop.State);
            Assert.Equal(2, rootVms.Count);
            Assert.Equal(3, rootVms[0].Entries.Count);
            Assert.True(rootVms[0].IsFocused);

            // Directories sort first: Alpha, Beta, root.txt.
            var alphaIndex = IndexOf(rootVms[0], "Alpha");
            Assert.Equal(0, rootVms[0].CursorIndex);

            // 2. Move the cursor onto "Alpha" and enter it.
            loop.Dispatch(new Msg.CursorTo(0, alphaIndex));
            Assert.Equal(alphaIndex, loop.State.Columns[0].Cursor);

            // Entering moves into the column that is already there rather than reloading it.
            loop.Dispatch(new Msg.EnterDirectory(0, alphaIndex));
            Assert.Equal(2, loop.State.Columns.Length);
            Assert.Equal(1, loop.State.FocusedColumn);
            DrainUntil(loop, queue, () => loop.State.Columns[1].Load == LoadState.Loaded, TimeSpan.FromSeconds(10));

            var afterEnterVms = StateProjection.Project(loop.State);
            Assert.Equal(2, afterEnterVms.Count);
            Assert.Equal("Alpha", afterEnterVms[1].Title);
            Assert.False(afterEnterVms[0].IsFocused);
            Assert.True(afterEnterVms[1].IsFocused);
            Assert.Single(afterEnterVms[1].Entries);
            Assert.Equal("inner.txt", afterEnterVms[1].Entries[0].Name);

            // 3. Move the cursor down in the child column (clamps at the single entry).
            loop.Dispatch(new Msg.CursorDown(1));
            Assert.Equal(0, loop.State.Columns[1].Cursor);

            // 4. Go back to the parent column: focus moves left, right columns stay.
            loop.Dispatch(new Msg.GoToParent(1));
            Assert.Equal(0, loop.State.FocusedColumn);
            Assert.Equal(2, loop.State.Columns.Length);

            var afterParentVms = StateProjection.Project(loop.State);
            Assert.True(afterParentVms[0].IsFocused);
            Assert.False(afterParentVms[1].IsFocused);
        }
        finally
        {
            TempDirectory.Delete(root);
        }
    }

    private static int IndexOf(Controls.ColumnVm column, string name)
    {
        for (var i = 0; i < column.Entries.Count; i++)
        {
            if (column.Entries[i].Name == name)
            {
                return i;
            }
        }

        throw new InvalidOperationException($"Entry \"{name}\" not found.");
    }
}
