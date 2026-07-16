using System.Windows;

namespace Zurari.Controls.Tests;

/// <summary>
/// Drag-out (OUT) and drop-in (IN) coverage. The gesture rules live in the pure
/// <see cref="DragGestureTracker"/> state machine and are unit-tested here directly:
/// synthesized WPF mouse events cannot fake the live cursor/button state that
/// <c>MouseEventArgs.GetPosition</c>/<c>LeftButton</c> report, so the event handlers in
/// <see cref="ColumnView"/> stay one-liners delegating to the tracker. Raising real
/// <see cref="DragEventArgs"/> for the drop-in path is equally impractical (internal
/// constructors) — the OLE plumbing on both paths is covered by the Phase 4 manual
/// checklist; the event-args shape is asserted below.
/// </summary>
public class DragDropTests
{
    private static Point PastThreshold(Point origin) => new(
        origin.X + SystemParameters.MinimumHorizontalDragDistance + 5,
        origin.Y);

    [Fact]
    public void Tracker_fires_once_past_threshold_with_pressed_entry_index()
    {
        var tracker = new DragGestureTracker();
        var origin = new Point(100, 100);

        tracker.Press(origin, pressedEntryIndex: 3);

        Assert.Equal(3, tracker.Move(PastThreshold(origin), leftButtonDown: true));
    }

    [Fact]
    public void Tracker_does_not_refire_within_the_same_gesture()
    {
        var tracker = new DragGestureTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, pressedEntryIndex: 3);
        tracker.Move(PastThreshold(origin), leftButtonDown: true);

        var further = new Point(origin.X + 200, origin.Y + 200);

        Assert.Null(tracker.Move(further, leftButtonDown: true));
    }

    [Fact]
    public void Tracker_stays_silent_below_threshold()
    {
        var tracker = new DragGestureTracker();
        var origin = new Point(300, 300);
        tracker.Press(origin, pressedEntryIndex: 0);

        Assert.Null(tracker.Move(new Point(origin.X + 1, origin.Y), leftButtonDown: true));
    }

    [Fact]
    public void Tracker_without_press_never_fires()
    {
        var tracker = new DragGestureTracker();

        Assert.Null(tracker.Move(new Point(500, 500), leftButtonDown: true));
    }

    [Fact]
    public void Tracker_cancels_when_button_is_no_longer_down()
    {
        var tracker = new DragGestureTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, pressedEntryIndex: 3);

        // Release happened outside the window: the move reports button-up and cancels.
        Assert.Null(tracker.Move(PastThreshold(origin), leftButtonDown: false));

        // The stale gesture must not revive on a later move with the button down again.
        Assert.Null(tracker.Move(PastThreshold(origin), leftButtonDown: true));
    }

    [Fact]
    public void Tracker_release_then_new_press_starts_a_fresh_gesture()
    {
        var tracker = new DragGestureTracker();
        var first = new Point(100, 100);
        tracker.Press(first, pressedEntryIndex: 3);
        tracker.Move(PastThreshold(first), leftButtonDown: true);
        tracker.Release();

        var second = new Point(200, 200);
        tracker.Press(second, pressedEntryIndex: 7);

        Assert.Equal(7, tracker.Move(PastThreshold(second), leftButtonDown: true));
    }

    [Fact]
    public void EntryDragRequestedEventArgs_carries_column_and_entry_index()
    {
        var args = new EntryDragRequestedEventArgs(columnIndex: 2, entryIndex: 5);

        Assert.Equal(2, args.ColumnIndex);
        Assert.Equal(5, args.EntryIndex);
    }

    [Fact]
    public void FileDropRequestedEventArgs_carries_column_paths_and_move_flag()
    {
        string[] paths = ["C:\\a.txt", "C:\\b.txt"];

        var args = new FileDropRequestedEventArgs(columnIndex: 1, paths: paths, isMove: true);

        Assert.Equal(1, args.ColumnIndex);
        Assert.Equal(paths, args.Paths);
        Assert.True(args.IsMove);
    }
}
