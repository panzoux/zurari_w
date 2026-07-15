using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Zurari.Controls.Tests;

public class InputEventTests
{
    [StaFact]
    public void Down_key_raises_cursor_move_requested_with_focused_column_index()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 2, entryCount: 10, focused: 1);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        CursorMoveRequestedEventArgs? raised = null;
        browser.CursorMoveRequested += (_, e) => raised = e;

        RaiseKey(browser, Key.Down);

        Assert.NotNull(raised);
        Assert.Equal(1, raised!.ColumnIndex);
        Assert.Equal(CursorMove.Down, raised.Move);
        Assert.Same(columns, browser.Columns);
    }

    [StaFact]
    public void PageDown_carries_a_positive_visible_row_count_for_a_populated_viewport()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 1000);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        CursorMoveRequestedEventArgs? raised = null;
        browser.CursorMoveRequested += (_, e) => raised = e;

        RaiseKey(browser, Key.PageDown);

        Assert.NotNull(raised);
        Assert.Equal(CursorMove.PageDown, raised!.Move);
        Assert.True(raised.VisibleRowCount > 0, $"expected a positive visible row count, got {raised.VisibleRowCount}");
    }

    [StaFact]
    public void Enter_raises_entry_activated_with_the_vm_cursor_index()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        columns = [columns[0] with { CursorIndex = 3 }];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        EntryActivatedEventArgs? raised = null;
        browser.EntryActivated += (_, e) => raised = e;

        RaiseKey(browser, Key.Enter);

        Assert.NotNull(raised);
        Assert.Equal(0, raised!.ColumnIndex);
        Assert.Equal(3, raised.EntryIndex);
    }

    [StaFact]
    public void Right_on_a_directory_cursor_raises_entry_activated()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        // index 0 is a Directory per VirtualizationTests.MakeColumns (i % 5 == 0).
        columns = [columns[0] with { CursorIndex = 0 }];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        EntryActivatedEventArgs? activated = null;
        ColumnFocusRequestedEventArgs? focusRequested = null;
        browser.EntryActivated += (_, e) => activated = e;
        browser.ColumnFocusRequested += (_, e) => focusRequested = e;

        RaiseKey(browser, Key.Right);

        Assert.NotNull(activated);
        Assert.Equal(0, activated!.ColumnIndex);
        Assert.Equal(0, activated.EntryIndex);
        Assert.Null(focusRequested);
    }

    [StaFact]
    public void Right_on_a_file_cursor_with_a_column_to_the_right_raises_column_focus_requested()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 2, entryCount: 10, focused: 0);
        // index 1 is a File per VirtualizationTests.MakeColumns (i % 5 == 0 is Directory).
        columns = [columns[0] with { CursorIndex = 1 }, columns[1]];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        EntryActivatedEventArgs? activated = null;
        ColumnFocusRequestedEventArgs? focusRequested = null;
        browser.EntryActivated += (_, e) => activated = e;
        browser.ColumnFocusRequested += (_, e) => focusRequested = e;

        RaiseKey(browser, Key.Right);

        Assert.Null(activated);
        Assert.NotNull(focusRequested);
        Assert.Equal(1, focusRequested!.ColumnIndex);
    }

    [StaFact]
    public void Backspace_raises_navigate_up_requested_for_the_focused_column()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 2, entryCount: 10, focused: 1);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        NavigateUpRequestedEventArgs? raised = null;
        browser.NavigateUpRequested += (_, e) => raised = e;

        RaiseKey(browser, Key.Back);

        Assert.NotNull(raised);
        Assert.Equal(1, raised!.ColumnIndex);
    }

    [StaFact]
    public void Left_raises_navigate_up_requested_even_for_the_root_column()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        NavigateUpRequestedEventArgs? raised = null;
        browser.NavigateUpRequested += (_, e) => raised = e;

        RaiseKey(browser, Key.Left);

        Assert.NotNull(raised);
        Assert.Equal(0, raised!.ColumnIndex);
    }

    [StaFact]
    public void Mouse_press_on_a_realized_list_box_item_raises_entry_pointer_pressed_without_moving_the_cursor()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        columns = [columns[0] with { CursorIndex = 0 }];
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);
        var item = list!.ItemContainerGenerator.ContainerFromIndex(4) as ListBoxItem;
        Assert.NotNull(item);
        var columnView = FindAncestor<ColumnView>(item!);
        Assert.NotNull(columnView);

        EntryPointerPressedEventArgs? pressed = null;
        browser.EntryPointerPressed += (_, e) => pressed = e;

        RaisePress(item!, MouseButton.Left);

        Assert.NotNull(pressed);
        Assert.Equal(0, pressed!.ColumnIndex);
        Assert.Equal(4, pressed.EntryIndex);
        Assert.Equal(MouseButton.Left, pressed.Button);

        // ColumnView.OnListPreviewMouseDown is expected to report PointToScreen(GetPosition(this))
        // for the owning ColumnView. The test's synthetic mouse-down carries no real device
        // position (it reflects wherever the environment's actual cursor happens to be, which
        // varies by machine/CI - not a fixed off-screen offset), so instead of asserting a
        // specific coordinate, re-derive the same value independently right after the press and
        // require it to match exactly: this proves the reported ScreenPosition really is a
        // PointToScreen conversion relative to the column, not e.g. an un-translated client point.
        var expectedScreenPosition = columnView!.PointToScreen(Mouse.GetPosition(columnView));
        Assert.Equal(expectedScreenPosition, pressed.ScreenPosition);

        // The list's selection is a pure projection of the VM cursor; a mouse press
        // on a row must not hijack it (the host decides whether/how the cursor moves).
        Assert.Equal(0, list.SelectedIndex);
        Assert.Same(columns, browser.Columns);
    }

    [StaFact]
    public void Mouse_press_on_a_non_focused_column_also_raises_column_focus_requested()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 2, entryCount: 10, focused: 0);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 1);
        Assert.NotNull(list);
        var item = list!.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem;
        Assert.NotNull(item);

        ColumnFocusRequestedEventArgs? focusRequested = null;
        browser.ColumnFocusRequested += (_, e) => focusRequested = e;

        RaisePress(item!, MouseButton.Left);

        Assert.NotNull(focusRequested);
        Assert.Equal(1, focusRequested!.ColumnIndex);
    }

    [StaFact]
    public void Double_click_on_an_entry_row_raises_entry_activated()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);
        var item = list!.ItemContainerGenerator.ContainerFromIndex(2) as ListBoxItem;
        Assert.NotNull(item);

        EntryActivatedEventArgs? activated = null;
        browser.EntryActivated += (_, e) => activated = e;

        RaisePress(item!, MouseButton.Left, clickCount: 2);

        Assert.NotNull(activated);
        Assert.Equal(0, activated!.ColumnIndex);
        Assert.Equal(2, activated.EntryIndex);
    }

    [StaFact]
    public void List_box_and_its_items_are_not_keyboard_focusable_so_arrow_keys_reach_the_browser()
    {
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 10);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindListBox(browser, 0);
        Assert.NotNull(list);
        Assert.False(list!.Focusable);

        var item = list.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        Assert.NotNull(item);
        Assert.False(item!.Focusable);
    }

    private static void RaiseKey(UIElement target, Key key)
    {
        var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(target), 0, key)
        {
            RoutedEvent = Keyboard.KeyDownEvent,
        };
        target.RaiseEvent(e);
    }

    private static void RaisePress(UIElement target, MouseButton button, int clickCount = 1)
    {
        var routedEvent = button switch
        {
            MouseButton.Left => Mouse.PreviewMouseDownEvent,
            MouseButton.Right => Mouse.PreviewMouseDownEvent,
            _ => Mouse.PreviewMouseDownEvent,
        };
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = routedEvent,
        };
        SetClickCount(e, clickCount);
        target.RaiseEvent(e);
    }

    private static void SetClickCount(MouseButtonEventArgs e, int clickCount)
    {
        // MouseButtonEventArgs has no public constructor overload accepting a click count
        // (WPF derives it from real click timing); tests fake it via the backing field.
        var field = typeof(MouseButtonEventArgs)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .FirstOrDefault(f => f.Name.Contains("count", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(field);
        field!.SetValue(e, clickCount);
    }

    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        var current = node;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = current is Visual visual ? VisualTreeHelper.GetParent(visual) : null;
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
}
