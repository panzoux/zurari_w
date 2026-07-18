using System.Windows;

namespace Zurari.Controls.Tests;

/// <summary>
/// Rectangle-select gesture rules live in the pure <see cref="RubberBandTracker"/> state machine
/// and are unit-tested here directly, mirroring <see cref="DragDropTests"/>'s coverage of
/// <see cref="DragGestureTracker"/> - synthesized WPF mouse events cannot fake the live cursor
/// travel that a real rubber-band drag needs.
/// </summary>
public class RubberBandTrackerTests
{
    private static Point PastThreshold(Point origin) => new(
        origin.X + SystemParameters.MinimumHorizontalDragDistance + 5,
        origin.Y + SystemParameters.MinimumVerticalDragDistance + 5);

    [Fact]
    public void Move_stays_silent_below_the_drag_threshold()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true);

        Assert.Null(tracker.Move(new Point(origin.X + 1, origin.Y)));
    }

    [Fact]
    public void Move_returns_the_rect_once_past_the_drag_threshold()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true);

        var rect = tracker.Move(PastThreshold(origin));

        Assert.NotNull(rect);
        Assert.Equal(origin.X, rect!.Value.X);
        Assert.Equal(origin.Y, rect.Value.Y);
    }

    [Fact]
    public void Move_never_activates_when_the_press_was_not_on_empty_space()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: false);

        Assert.Null(tracker.Move(PastThreshold(origin)));
        Assert.False(tracker.IsActive);
    }

    [Fact]
    public void Move_without_a_press_never_activates()
    {
        var tracker = new RubberBandTracker();

        Assert.Null(tracker.Move(new Point(500, 500)));
    }

    [Fact]
    public void Rect_math_handles_dragging_up_and_left_of_the_origin()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(200, 200);
        tracker.Press(origin, onEmptySpace: true);

        var rect = tracker.Move(new Point(origin.X - 50, origin.Y - 80));

        Assert.NotNull(rect);
        Assert.Equal(150, rect!.Value.X);
        Assert.Equal(120, rect.Value.Y);
        Assert.Equal(50, rect.Value.Width);
        Assert.Equal(80, rect.Value.Height);
    }

    [Fact]
    public void IsActive_is_false_before_and_true_after_a_threshold_crossing()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true);

        Assert.False(tracker.IsActive);

        tracker.Move(PastThreshold(origin));

        Assert.True(tracker.IsActive);
    }

    [Fact]
    public void Release_returns_the_final_rect_from_the_last_move()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true);
        tracker.Move(PastThreshold(origin));
        var finalPoint = new Point(origin.X + 300, origin.Y + 40);

        tracker.Move(finalPoint);
        var released = tracker.Release();

        Assert.NotNull(released);
        Assert.Equal(new Rect(origin, finalPoint), released!.Value);
    }

    [Fact]
    public void Release_returns_null_when_never_activated()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true);
        tracker.Move(new Point(origin.X + 1, origin.Y + 1)); // below threshold

        Assert.Null(tracker.Release());
    }

    [Fact]
    public void Release_returns_null_when_the_press_was_not_on_empty_space()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: false);
        tracker.Move(PastThreshold(origin));

        Assert.Null(tracker.Release());
    }

    [Fact]
    public void Release_resets_state_for_the_next_press()
    {
        var tracker = new RubberBandTracker();
        var first = new Point(100, 100);
        tracker.Press(first, onEmptySpace: true);
        tracker.Move(PastThreshold(first));
        tracker.Release();

        Assert.False(tracker.IsActive);
        Assert.Null(tracker.Move(new Point(500, 500)));

        var second = new Point(300, 300);
        tracker.Press(second, onEmptySpace: true);
        var rect = tracker.Move(PastThreshold(second));

        Assert.NotNull(rect);
        Assert.Equal(second.X, rect!.Value.X);
    }

    [Fact]
    public void LiveRange_is_null_before_the_drag_threshold_is_crossed()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 5);

        Assert.Null(tracker.UpdateCurrentIndex(5));
    }

    [Fact]
    public void LiveRange_is_null_when_the_anchor_index_is_negative()
    {
        // e.g. an empty column: FindNearestRowIndex has nothing to hit-test against.
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: -1);
        tracker.Move(PastThreshold(origin));

        Assert.Null(tracker.UpdateCurrentIndex(-1));
    }

    [Fact]
    public void LiveRange_spans_anchor_to_current_when_dragging_downward()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 3);
        tracker.Move(PastThreshold(origin));

        var range = tracker.UpdateCurrentIndex(9);

        Assert.Equal((3, 9), range);
    }

    [Fact]
    public void LiveRange_reverses_direction_when_the_pointer_moves_back_above_the_anchor()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 6);
        tracker.Move(PastThreshold(origin));

        var downward = tracker.UpdateCurrentIndex(10);
        Assert.Equal((6, 10), downward);

        var upward = tracker.UpdateCurrentIndex(2);

        Assert.Equal((2, 6), upward);
    }

    [Fact]
    public void LiveRange_is_a_single_row_when_current_equals_anchor()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 4);
        tracker.Move(PastThreshold(origin));

        var range = tracker.UpdateCurrentIndex(4);

        Assert.Equal((4, 4), range);
    }

    [Fact]
    public void AnchorIndex_is_minus_one_when_the_press_was_not_on_empty_space()
    {
        var tracker = new RubberBandTracker();
        tracker.Press(new Point(100, 100), onEmptySpace: false, anchorIndex: 7);

        Assert.Equal(-1, tracker.AnchorIndex);
    }

    [Fact]
    public void Release_clears_the_live_range_for_the_next_gesture()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 1);
        tracker.Move(PastThreshold(origin));
        tracker.UpdateCurrentIndex(5);
        tracker.Release();

        Assert.Equal(-1, tracker.AnchorIndex);
        Assert.Null(tracker.LiveRange);
    }

    [Fact]
    public void LiveRange_anchored_below_the_last_row_includes_the_last_row_while_dragging_upward()
    {
        // Press on empty space below the last row anchors on that row (FindNearestRowIndex clamps
        // to it in ColumnView); dragging up from there must still include it in every live range,
        // and in the final range on release - see ColumnView.ResolveAnchorPixelY's remarks on why
        // the anchor row must stay the fixed endpoint regardless of the pixel math.
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 500);
        const int lastRowIndex = 9;
        tracker.Press(origin, onEmptySpace: true, anchorIndex: lastRowIndex);
        tracker.Move(new Point(origin.X, origin.Y - 50));

        var range = tracker.UpdateCurrentIndex(4);
        Assert.Equal((4, lastRowIndex), range);

        tracker.Move(new Point(origin.X, origin.Y - 200));
        var finalRange = tracker.UpdateCurrentIndex(0);
        Assert.Equal((0, lastRowIndex), finalRange);

        tracker.Release();
    }

    [Theory]
    [InlineData(true, false, 0, true)] // empty space: always rubber-band, timing irrelevant
    [InlineData(true, false, 10_000, true)] // empty space: still rubber-band even after a long hold
    [InlineData(true, true, 10_000, true)] // empty space ignores the marked flag entirely
    [InlineData(false, true, 0, false)] // marked row: always a drag, even with no delay at all
    [InlineData(false, true, 10_000, false)] // marked row: always a drag, even after a long hold
    [InlineData(false, false, 0, true)] // unmarked row, moved immediately: rubber-band
    [InlineData(false, false, 299, true)] // unmarked row, just under the threshold: rubber-band
    [InlineData(false, false, 300, false)] // unmarked row, at the threshold: drag
    [InlineData(false, false, 301, false)] // unmarked row, past the threshold: drag
    public void ShouldStartRubberBand_matches_the_Finder_timing_rules(
        bool onEmptySpace, bool rowIsMarked, long elapsedMs, bool expectedRubberBand)
    {
        Assert.Equal(
            expectedRubberBand, RubberBandTracker.ShouldStartRubberBand(onEmptySpace, rowIsMarked, elapsedMs));
    }

    [Fact]
    public void RubberBandVsDragThresholdMs_is_300()
    {
        Assert.Equal(300, RubberBandTracker.RubberBandVsDragThresholdMs);
    }
}
