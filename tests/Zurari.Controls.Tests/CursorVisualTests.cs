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

    // Marked entries (R3): row background (Explorer/Finder-style) instead of the old bold+colored
    // text. Asserted via the container's own Background brush - the trigger-driven property -
    // rather than pixel colors, per Generic.xaml's ItemContainerStyle trigger order.
    private static readonly Color MarkedBackground = (Color)ColorConverter.ConvertFromString("#FFCCE8FF")!;
    private static readonly Color RubberBandHoverBackground = (Color)ColorConverter.ConvertFromString("#FFE3F0FB")!;
    private static readonly Color StrongCursorBackground = (Color)ColorConverter.ConvertFromString("#FFB3D3F2")!;

    [StaFact]
    public void Marked_entries_render_with_a_background_and_unmarked_do_not_and_stay_unbold()
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
        Assert.NotEqual(FontWeights.Bold, marked!.FontWeight);
        Assert.NotEqual(MarkedBackground, GetBackgroundColor(plain));
        Assert.Equal(MarkedBackground, GetBackgroundColor(marked));
    }

    [StaFact]
    public void Cursor_row_that_is_also_marked_reads_as_the_strong_cursor_blue_in_the_focused_column()
    {
        var entries = new[]
        {
            new EntryVm("cursor-and-marked", EntryKind.File, IsMarked: true, SizeText: null, DateText: null),
        };
        var columns = new[] { new ColumnVm("col", entries, CursorIndex: 0, IsFocused: true) };
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        var row = list!.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        Assert.NotNull(row);

        Assert.Equal(StrongCursorBackground, GetBackgroundColor(row));
    }

    [StaFact]
    public void IsRubberBandHover_attached_property_renders_the_lighter_preview_background()
    {
        var entries = new[]
        {
            new EntryVm("plain", EntryKind.File, IsMarked: false, SizeText: null, DateText: null),
        };
        var columns = new[] { new ColumnVm("col", entries, CursorIndex: -1, IsFocused: true) };
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        var row = list!.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        Assert.NotNull(row);

        Assert.NotEqual(RubberBandHoverBackground, GetBackgroundColor(row));

        ColumnView.SetIsRubberBandHover(row!, true);
        TestWindow.DoEvents();

        Assert.Equal(RubberBandHoverBackground, GetBackgroundColor(row));
    }

    private static Color GetBackgroundColor(ListBoxItem item) =>
        item.Background is SolidColorBrush brush ? brush.Color : Colors.Transparent;

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

    [StaFact]
    public void Directory_row_shows_chevron_and_file_row_does_not()
    {
        var entries = new[]
        {
            new EntryVm("sub", EntryKind.Directory, IsMarked: false, SizeText: null, DateText: null),
            new EntryVm("a.txt", EntryKind.File, IsMarked: false, SizeText: null, DateText: null),
        };
        var columns = new[] { new ColumnVm("col", entries, CursorIndex: -1, IsFocused: true) };
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);

        var directoryRow = list!.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        var fileRow = list.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
        Assert.NotNull(directoryRow);
        Assert.NotNull(fileRow);

        var directoryChevron = FindChevron(directoryRow!);
        Assert.NotNull(directoryChevron);
        Assert.Equal(Visibility.Visible, directoryChevron!.Visibility);

        var fileChevron = FindChevron(fileRow!);
        Assert.True(fileChevron is null || fileChevron.Visibility != Visibility.Visible);
    }

    private static TextBlock? FindChevron(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock { Text: "›" } textBlock)
            {
                return textBlock;
            }

            var nested = FindChevron(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
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
