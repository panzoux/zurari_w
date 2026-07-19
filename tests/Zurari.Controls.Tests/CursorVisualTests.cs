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
    public void Snapshot_swap_with_unchanged_cursor_does_not_call_ScrollIntoView()
    {
        // Phase 5 bug B3 (defense in depth): a re-render unrelated to this column - e.g. a sibling
        // column reloading after a job completes - still pushes a brand-new ColumnVm/EntryVm[]
        // snapshot down to every realized ColumnView (StateProjection.Project always allocates
        // fresh arrays on every render), even when this column's own data is unchanged
        // content-for-content. Re-scrolling to the cursor row on every such render would yank back
        // any scrolling the user did in between, even though the cursor never moved - so
        // ScrollIntoView must not be invoked a second time here. Asserted via the
        // ScrollIntoViewInvoked test hook directly (see its remarks) rather than an actual scroll
        // offset, which is unreliable to observe deterministically across a virtualized
        // ItemsSource swap within a single WPF dispatcher pass.
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 100_000);
        columns = [columns[0] with { CursorIndex = 50000 }];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var columnView = FindVisualChild<ColumnView>(browser);
        Assert.NotNull(columnView);

        var scrollIntoViewCallCount = 0;
        columnView!.ScrollIntoViewInvoked += (_, _) => scrollIntoViewCallCount++;

        // A content-equivalent but instance-distinct snapshot (fresh arrays throughout, exactly
        // like a real StateProjection.Project call), same CursorIndex (still 50000).
        var equivalent = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 100_000);
        equivalent = [equivalent[0] with { CursorIndex = 50000 }];
        browser.Columns = equivalent;
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        Assert.Equal(0, scrollIntoViewCallCount);

        // Sanity check the hook actually fires when the cursor DOES move (otherwise a count of 0
        // above would be vacuously true because the hook is broken, not because the guard works).
        var moved = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 100_000);
        moved = [moved[0] with { CursorIndex = 60000 }];
        browser.Columns = moved;
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        Assert.Equal(1, scrollIntoViewCallCount);
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

    // Drop-target row highlight (Item 1): real DragEventArgs cannot be synthesized (internal
    // constructors, per DragDropTests's remarks), so the hit-test path itself (OnDragOver walking
    // FindEntryIndex, checking EntryVm.Kind) is a manual/Phase-4-checklist item. What IS unit-
    // testable, mirroring Selected_marked_row_paints_the_style_background_on_screen_not_the_os_theme_gray
    // below, is that setting the attached property on a realized container paints the strong
    // selection blue via the template's actual Border - not just the container's Background
    // property - proving the trigger and its precedence are wired correctly in Generic.xaml.
    [StaFact]
    public void IsDropTargetRow_attached_property_paints_the_strong_selection_blue_on_screen()
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
        Assert.NotEqual(StrongCursorBackground, GetBackgroundColor(row));

        ColumnView.SetIsDropTargetRow(row!, true);
        TestWindow.DoEvents();

        Assert.Equal(StrongCursorBackground, GetBackgroundColor(row));

        var border = FindVisualChild<Border>(row!);
        Assert.NotNull(border);
        var painted = Assert.IsType<SolidColorBrush>(border!.Background);
        Assert.Equal(StrongCursorBackground, painted.Color);
    }

    [StaFact]
    public void IsDropTargetRow_beats_IsMarked_and_IsRubberBandHover()
    {
        var entries = new[]
        {
            new EntryVm("marked-and-hovered", EntryKind.File, IsMarked: true, SizeText: null, DateText: null),
        };
        var columns = new[] { new ColumnVm("col", entries, CursorIndex: -1, IsFocused: true) };
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        var row = list!.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        Assert.NotNull(row);
        ColumnView.SetIsRubberBandHover(row!, true);
        TestWindow.DoEvents();
        Assert.Equal(MarkedBackground, GetBackgroundColor(row!)); // marked already beats hover

        ColumnView.SetIsDropTargetRow(row!, true);
        TestWindow.DoEvents();

        Assert.Equal(StrongCursorBackground, GetBackgroundColor(row!));
    }

    [StaFact]
    public void Selected_marked_row_paints_the_style_background_on_screen_not_the_os_theme_gray()
    {
        // Regression: the DEFAULT ListBoxItem template paints selected rows with the OS theme's
        // (inactive-)selection brushes on an internal Border, overriding the style triggers on
        // screen even though the container's Background property reads correctly. The cursor row
        // of a fresh multi-selection therefore looked gray instead of marked blue. The minimal
        // template in Generic.xaml must keep the internal Border bound to the container Background.
        var entries = new[]
        {
            new EntryVm("a", EntryKind.File, IsMarked: true, SizeText: null, DateText: null),
            new EntryVm("b", EntryKind.File, IsMarked: true, SizeText: null, DateText: null),
        };
        var columns = new[] { new ColumnVm("col", entries, CursorIndex: 1, IsFocused: false) };
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        var cursorRow = list!.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
        Assert.NotNull(cursorRow);
        Assert.True(cursorRow!.IsSelected);

        // The visual actually painted behind the row content is the template's root Border.
        var border = FindVisualChild<Border>(cursorRow);
        Assert.NotNull(border);
        var painted = Assert.IsType<SolidColorBrush>(border!.Background);
        Assert.Equal(GetBackgroundColor(cursorRow), painted.Color);
        Assert.Equal(MarkedBackground, painted.Color);
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
