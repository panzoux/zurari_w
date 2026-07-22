using System.Windows;
using System.Windows.Controls;

namespace Zurari.Controls.Tests;

/// <summary>
/// Regression coverage for a real-world bug: pressing Down/Up repeatedly, one row at a time,
/// stopped scrolling once the cursor reached the bottom/top edge of the viewport - unlike the
/// existing <see cref="CursorVisualTests"/> large-jump cases, which happen to always land on a
/// row far from any edge and never exercise this path.
/// </summary>
public class IncrementalScrollTests
{
    [StaFact]
    public void Repeated_single_step_cursor_moves_keep_the_cursor_row_visible()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 500);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        // Advance the cursor one row at a time, exactly like repeated Down presses, well past
        // however many rows fit in the viewport - if scrolling ever silently stalls at an edge,
        // some later index will never be realized/visible.
        for (var i = 1; i <= 60; i++)
        {
            var next = new[] { columns[0] with { CursorIndex = i } };
            browser.Columns = next;
            TestWindow.DoEvents();
            TestWindow.DoEvents();
        }

        var container = list!.ItemContainerGenerator.ContainerFromIndex(60) as ListBoxItem;
        Assert.NotNull(container);

        var bounds = container!.TransformToAncestor(list).TransformBounds(
            new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        var listBounds = new Rect(0, 0, list.ActualWidth, list.ActualHeight);

        Assert.True(
            bounds.IntersectsWith(listBounds),
            $"cursor row 60 bounds {bounds} do not intersect list viewport {listBounds} "
            + "after 60 single-step moves - scrolling stalled somewhere along the way");
    }

    [StaFact]
    public void Repeated_single_step_moves_in_a_small_window_keep_the_cursor_row_visible()
    {
        // Closer to the real app's window (600px tall, minus title/status/job strip) than the
        // 800px-tall TestWindow default - fewer rows fit in the viewport, so scrolling kicks in
        // much sooner and more often.
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 500);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser, width: 300, height: 300);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        for (var i = 1; i <= 80; i++)
        {
            var next = new[] { columns[0] with { CursorIndex = i } };
            browser.Columns = next;
            TestWindow.DoEvents();
            TestWindow.DoEvents();

            var container = list!.ItemContainerGenerator.ContainerFromIndex(i) as ListBoxItem;
            Assert.True(container is not null, $"row {i} was never realized");

            var bounds = container!.TransformToAncestor(list).TransformBounds(
                new Rect(0, 0, container.ActualWidth, container.ActualHeight));
            var listBounds = new Rect(0, 0, list.ActualWidth, list.ActualHeight);

            Assert.True(
                bounds.IntersectsWith(listBounds),
                $"cursor row {i} bounds {bounds} do not intersect list viewport {listBounds}");
        }
    }

    [StaFact]
    public void Cursor_moves_without_pumping_between_steps_still_end_up_visible()
    {
        // Simulates fast key-repeat: several Columns reassignments land before the dispatcher
        // gets a chance to run a layout pass between them (only one DoEvents at the very end),
        // which is what a held-down arrow key does in the real app.
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 500);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser, width: 300, height: 300);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        for (var i = 1; i <= 50; i++)
        {
            browser.Columns = [columns[0] with { CursorIndex = i }];
        }

        TestWindow.DoEvents();
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var container = list!.ItemContainerGenerator.ContainerFromIndex(50) as ListBoxItem;
        Assert.NotNull(container);

        var bounds = container!.TransformToAncestor(list).TransformBounds(
            new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        var listBounds = new Rect(0, 0, list.ActualWidth, list.ActualHeight);

        Assert.True(
            bounds.IntersectsWith(listBounds),
            $"cursor row 50 bounds {bounds} do not intersect list viewport {listBounds}");
    }

    private static ListBox? FindListBox(DependencyObject root, int columnIndex)
    {
        var found = new List<ListBox>();
        CollectListBoxes(root, found);
        return columnIndex < found.Count ? found[columnIndex] : null;
    }

    private static void CollectListBoxes(DependencyObject root, List<ListBox> found)
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ListBox lb)
            {
                found.Add(lb);
            }

            CollectListBoxes(child, found);
        }
    }
}
