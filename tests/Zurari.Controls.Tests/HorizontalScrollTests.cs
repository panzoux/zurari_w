using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zurari.Controls.Tests;

/// <summary>
/// Where the columns sit horizontally, which is <see cref="ColumnBrowser"/>'s decision alone.
/// Three rules, all of them from watching the real app misbehave:
/// <list type="number">
/// <item>a child ("peek") column that appears past the right edge is scrolled to;</item>
/// <item>columns never slide sideways on their own - losing that child column leaves the rest
/// exactly where they were, with blank space where it used to be;</item>
/// <item>the focused column is always visible, even when that means scrolling back left.</item>
/// </list>
/// </summary>
public class HorizontalScrollTests
{
    /// <summary>Three 240px columns fit in this window's ~744px viewport; a fourth does not.</summary>
    private const double WindowWidth = 760;

    private const double WindowHeight = 600;

    [StaFact]
    public void A_peek_column_past_the_right_edge_is_scrolled_to()
    {
        var browser = new ColumnBrowser { Columns = WithPeek(Columns()) };
        using var host = new TestWindow(browser, WindowWidth, WindowHeight);
        Settle();

        AssertFullyVisible(browser, 3);
        AssertFullyVisible(browser, 2);
    }

    [StaFact]
    public void Losing_the_peek_column_leaves_every_other_column_where_it_was()
    {
        var columns = Columns();
        var browser = new ColumnBrowser { Columns = WithPeek(columns) };
        using var host = new TestWindow(browser, WindowWidth, WindowHeight);
        Settle();

        var before = ColumnLeft(browser, 2);
        var offsetBefore = HorizontalOffset(browser);
        Assert.True(offsetBefore > 0, "the peek column should have scrolled the browser to it");

        // The cursor moves off the folder onto a file: Core drops the child column.
        browser.Columns = columns;
        Settle();

        Assert.Equal(before, ColumnLeft(browser, 2), precision: 3);
        Assert.Equal(offsetBefore, HorizontalOffset(browser), precision: 3);
    }

    [StaFact]
    public void The_peek_column_comes_back_into_the_space_it_left()
    {
        var columns = Columns();
        var browser = new ColumnBrowser { Columns = WithPeek(columns) };
        using var host = new TestWindow(browser, WindowWidth, WindowHeight);
        Settle();

        var before = ColumnLeft(browser, 2);

        browser.Columns = columns;
        Settle();
        browser.Columns = WithPeek(columns);
        Settle();

        Assert.Equal(before, ColumnLeft(browser, 2), precision: 3);
        AssertFullyVisible(browser, 3);
    }

    /// <summary>
    /// The reported sequence, end to end: enter a folder (which scrolls, and opens a child column
    /// of its own), come back out, then put the cursor on a file. The column the user is standing
    /// in has to hold its place on screen across all three, blank space and all.
    /// </summary>
    [StaFact]
    public void Entering_a_folder_coming_back_and_landing_on_a_file_holds_the_column_in_place()
    {
        var browser = new ColumnBrowser { Columns = Deep(4, focused: 2) };
        using var host = new TestWindow(browser, WindowWidth, WindowHeight);
        Settle();

        // Enter the folder under the cursor: focus moves right, and its own child column opens.
        browser.Columns = Deep(5, focused: 3);
        Settle();

        var entered = ColumnLeft(browser, 2);

        // Back out: the columns to the right survive, only the focus moves.
        browser.Columns = Deep(5, focused: 2);
        Settle();

        Assert.Equal(entered, ColumnLeft(browser, 2), precision: 3);

        // Cursor onto a file: everything right of the focused column is dropped at once.
        browser.Columns = Deep(3, focused: 2);
        Settle();

        Assert.Equal(entered, ColumnLeft(browser, 2), precision: 3);
    }

    [StaFact]
    public void Holding_the_position_never_strands_the_focused_column_off_screen()
    {
        // Scrolled deep, then somewhere shallow: holding the old offset would leave the user
        // staring at the blank space the reservation made. The focused column wins.
        var deep = VirtualizationTests.MakeColumns(columnCount: 8, entryCount: 20, focused: 7);
        var browser = new ColumnBrowser { Columns = deep };
        using var host = new TestWindow(browser, WindowWidth, WindowHeight);
        Settle();

        Assert.True(HorizontalOffset(browser) > 0, "8 columns should have scrolled the browser");

        browser.Columns = deep.Take(2).Select((c, i) => c with { IsFocused = i == 1 }).ToArray();
        Settle();

        AssertFullyVisible(browser, 1);
    }

    /// <summary>
    /// <paramref name="count"/> columns with the focus on <paramref name="focused"/>; anything
    /// past the focused column is a child column, the last of which carries a cursor of its own.
    /// </summary>
    private static IReadOnlyList<ColumnVm> Deep(int count, int focused)
    {
        var columns = VirtualizationTests.MakeColumns(count, entryCount: 60, focused);
        return [.. columns.Select((c, i) => i == count - 1 && i > focused ? c with { CursorIndex = 4 } : c)];
    }

    private static IReadOnlyList<ColumnVm> Columns() =>
        VirtualizationTests.MakeColumns(columnCount: 3, entryCount: 60, focused: 2);

    /// <summary>The same columns plus the child column the cursor's folder opens, cursor and all.</summary>
    private static IReadOnlyList<ColumnVm> WithPeek(IReadOnlyList<ColumnVm> columns) =>
        [.. columns, VirtualizationTests.MakeColumns(columnCount: 1, entryCount: 60)[0] with { CursorIndex = 4 }];

    /// <summary>Two full layout/render passes - the scrolling here runs at Loaded priority.</summary>
    private static void Settle()
    {
        TestWindow.DoEvents();
        TestWindow.DoEvents();
        TestWindow.DoEvents();
    }

    /// <summary>X of one column's left edge within the browser - what "moved sideways" means.</summary>
    private static double ColumnLeft(ColumnBrowser browser, int columnIndex) =>
        Views(browser)[columnIndex].TransformToAncestor(browser).Transform(new Point(0, 0)).X;

    /// <summary>The column must be wholly inside the browser, not clipped at either edge.</summary>
    private static void AssertFullyVisible(ColumnBrowser browser, int columnIndex)
    {
        var left = ColumnLeft(browser, columnIndex);
        var right = left + Views(browser)[columnIndex].ActualWidth;
        Assert.True(
            left >= -0.5 && right <= browser.ActualWidth + 0.5,
            $"column {columnIndex} spans {left}..{right}, outside the browser's 0..{browser.ActualWidth}");
    }

    private static double HorizontalOffset(ColumnBrowser browser)
    {
        var scrollViewer = FindScrollViewer(browser);
        Assert.NotNull(scrollViewer);
        return scrollViewer!.HorizontalOffset;
    }

    private static List<ColumnView> Views(DependencyObject root)
    {
        var found = new List<ColumnView>();
        Collect(root, found);
        return found;
    }

    private static void Collect(DependencyObject root, List<ColumnView> found)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ColumnView view)
            {
                found.Add(view);
            }

            Collect(child, found);
        }
    }

    private static ScrollViewer? FindScrollViewer(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer found)
            {
                return found;
            }

            if (FindScrollViewer(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
