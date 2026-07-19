using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Zurari.Controls.Tests;

/// <summary>
/// Regression coverage for a bug where pressing on a column's vertical scrollbar started a
/// rubber-band selection (the empty-space branch of <see cref="ColumnView.OnListPreviewMouseDown"/>)
/// and captured the mouse, making it impossible to drag the scrollbar thumb to scroll.
/// </summary>
public class ScrollBarInputTests
{
    [StaFact]
    public void Press_on_the_vertical_scrollbar_does_not_start_a_rubber_band_or_capture_the_mouse()
    {
        // Many entries in a short window guarantees the ListBox's vertical ScrollBar is realized.
        var columns = VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 1000);
        var browser = new ColumnBrowser { Columns = columns };
        using var host = new TestWindow(browser, height: 200);
        TestWindow.DoEvents();
        TestWindow.DoEvents();

        var list = FindListBox(browser);
        Assert.NotNull(list);

        var scrollBar = FindVisualChild<ScrollBar>(list!);
        Assert.NotNull(scrollBar);

        RubberBandStartedEventArgs? rubberBandStarted = null;
        MarkRangeRequestedEventArgs? markRangeRequested = null;
        browser.RubberBandStarted += (_, e) => rubberBandStarted = e;
        browser.MarkRangeRequested += (_, e) => markRangeRequested = e;

        RaisePress(scrollBar!, MouseButton.Left);

        Assert.False(list!.IsMouseCaptured);

        // Move the pointer as a rubber-band drag would - if the press had wrongly armed one,
        // this is what would raise RubberBandStarted.
        RaiseMove(scrollBar!);
        Assert.Null(rubberBandStarted);

        RaiseRelease(scrollBar!, MouseButton.Left);
        Assert.Null(markRangeRequested);
        Assert.False(list.IsMouseCaptured);
    }

    private static void RaisePress(UIElement target, MouseButton button)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
        };
        target.RaiseEvent(e);
    }

    private static void RaiseRelease(UIElement target, MouseButton button)
    {
        var e = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, button)
        {
            RoutedEvent = Mouse.PreviewMouseUpEvent,
        };
        target.RaiseEvent(e);
    }

    private static void RaiseMove(UIElement target)
    {
        var e = new MouseEventArgs(Mouse.PrimaryDevice, 0)
        {
            RoutedEvent = Mouse.PreviewMouseMoveEvent,
        };
        target.RaiseEvent(e);
    }

    private static ListBox? FindListBox(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ListBox lb)
            {
                return lb;
            }

            if (FindListBox(child) is { } found)
            {
                return found;
            }
        }

        return null;
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

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
