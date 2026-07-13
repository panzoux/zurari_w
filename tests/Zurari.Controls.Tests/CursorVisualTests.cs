using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Zurari.Controls.Tests;

public class CursorVisualTests
{
    [StaFact]
    public void Cursor_row_is_realized_and_within_the_list_viewport()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 100_000);
        columns = [columns[0] with { CursorIndex = 5000 }];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        var container = list!.ItemContainerGenerator.ContainerFromIndex(5000) as ListBoxItem;
        Assert.NotNull(container);

        var bounds = container!.TransformToAncestor(list).TransformBounds(
            new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        var listBounds = new Rect(0, 0, list.ActualWidth, list.ActualHeight);

        Assert.True(
            bounds.IntersectsWith(listBounds),
            $"cursor row bounds {bounds} do not intersect list viewport {listBounds}");
    }

    [StaFact]
    public void Snapshot_swap_scrolls_new_cursor_row_into_view()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 100_000);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var replaced = new[] { columns[0] with { CursorIndex = 90000 } };
        browser.Columns = replaced;
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        var container = list!.ItemContainerGenerator.ContainerFromIndex(90000) as ListBoxItem;
        Assert.NotNull(container);

        var bounds = container!.TransformToAncestor(list).TransformBounds(
            new Rect(0, 0, container.ActualWidth, container.ActualHeight));
        var listBounds = new Rect(0, 0, list.ActualWidth, list.ActualHeight);

        Assert.True(
            bounds.IntersectsWith(listBounds),
            $"cursor row bounds {bounds} do not intersect list viewport {listBounds}");
    }

    [StaFact]
    public void Focused_column_is_scrolled_into_the_horizontal_viewport()
    {
        // 8 columns * 240 width > 1200 window width; last column is focused.
        var columns = VirtualizationTests.MakeColumns(columnCount: 8, entryCount: 10, focused: 7);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser, width: 1200, height: 800);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var scrollViewer = FindVisualChild<ScrollViewer>(browser);
        Assert.NotNull(scrollViewer);
        Assert.True(
            scrollViewer!.HorizontalOffset > 0,
            $"expected horizontal scroll to have moved, offset was {scrollViewer.HorizontalOffset}");
    }

    [StaFact]
    public void Selected_cursor_row_reflects_column_vm_and_ignores_direct_selection_changes()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        columns = [columns[0] with { CursorIndex = 3 }];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);
        Assert.Equal(3, list!.SelectedIndex);

        // The control never moves its own cursor: forcing selection elsewhere must
        // snap back to whatever the VM says CursorIndex is.
        list.SelectedIndex = 7;
        TestWindow.DoEvents();

        Assert.Equal(3, list.SelectedIndex);
    }

    [StaFact]
    public void Marked_entries_render_with_bold_font_weight_and_unmarked_do_not()
    {
        var entries = new[]
        {
            new EntryVm("plain", EntryKind.File, IsMarked: false, SizeText: null, DateText: null),
            new EntryVm("marked", EntryKind.File, IsMarked: true, SizeText: null, DateText: null),
        };
        var columns = new[] { new ColumnVm("col", entries, CursorIndex: -1, IsFocused: true) };
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        var plain = list!.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        var marked = list.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
        Assert.NotNull(plain);
        Assert.NotNull(marked);

        Assert.NotEqual(FontWeights.Bold, plain!.FontWeight);
        Assert.Equal(FontWeights.Bold, marked!.FontWeight);
    }

    [StaFact]
    public void Focused_column_is_marked_via_attached_property_distinct_from_unfocused_column()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 2, entryCount: 5, focused: 1);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var unfocusedList = FindListBox(browser, 0);
        var focusedList = FindListBox(browser, 1);
        Assert.NotNull(unfocusedList);
        Assert.NotNull(focusedList);

        Assert.False(ColumnView.GetIsColumnFocused(unfocusedList!));
        Assert.True(ColumnView.GetIsColumnFocused(focusedList!));
    }

    private static ListBox? FindListBox(DependencyObject root, int columnIndex)
    {
        var found = new List<ListBox>();
        CollectListBoxes(root, found);
        return columnIndex < found.Count ? found[columnIndex] : null;
    }

    private static void CollectListBoxes(DependencyObject root, List<ListBox> found)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ListBox lb)
            {
                found.Add(lb);
            }

            CollectListBoxes(child, found);
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
