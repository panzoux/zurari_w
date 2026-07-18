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
    [InlineData(true, 0, true)] // empty space: always rubber-band, timing irrelevant
    [InlineData(true, 10_000, true)] // empty space: still rubber-band even after a long hold
    [InlineData(false, 0, true)] // row (marked or not), moved immediately: rubber-band
    [InlineData(false, 299, true)] // row, just under the threshold: rubber-band
    [InlineData(false, 300, false)] // row, at the threshold: drag
    [InlineData(false, 301, false)] // row, past the threshold: drag
    [InlineData(false, 10_000, false)] // row, long hold: drag - applies uniformly to marked rows too
    public void ShouldStartRubberBand_matches_the_Finder_timing_rules(
        bool onEmptySpace, long elapsedMs, bool expectedRubberBand)
    {
        Assert.Equal(
            expectedRubberBand, RubberBandTracker.ShouldStartRubberBand(onEmptySpace, elapsedMs));
    }

    [Fact]
    public void RubberBandVsDragThresholdMs_is_300()
    {
        Assert.Equal(300, RubberBandTracker.RubberBandVsDragThresholdMs);
    }

    [Fact]
    public void HasEscapedAnchor_is_false_until_the_current_index_first_differs_from_the_anchor()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 4);
        tracker.Move(PastThreshold(origin));

        Assert.False(tracker.HasEscapedAnchor);

        tracker.UpdateCurrentIndex(4); // still on the anchor row
        Assert.False(tracker.HasEscapedAnchor);

        tracker.UpdateCurrentIndex(5); // escaped
        Assert.True(tracker.HasEscapedAnchor);
    }

    [Fact]
    public void HasEscapedAnchor_stays_true_after_the_range_shrinks_back_to_the_anchor()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 4);
        tracker.Move(PastThreshold(origin));

        tracker.UpdateCurrentIndex(6);
        Assert.True(tracker.HasEscapedAnchor);

        tracker.UpdateCurrentIndex(4); // back to the anchor row
        Assert.True(tracker.HasEscapedAnchor, "escaping once must be sticky for the rest of the gesture");
    }

    [Fact]
    public void HasEscapedAnchor_resets_on_the_next_press()
    {
        var tracker = new RubberBandTracker();
        var origin = new Point(100, 100);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: 4);
        tracker.Move(PastThreshold(origin));
        tracker.UpdateCurrentIndex(9);
        Assert.True(tracker.HasEscapedAnchor);
        tracker.Release();

        tracker.Press(new Point(300, 300), onEmptySpace: true, anchorIndex: 2);

        Assert.False(tracker.HasEscapedAnchor);
    }

    [Fact]
    public void ClickDisplacementToleranceDips_is_10()
    {
        Assert.Equal(10.0, RubberBandTracker.ClickDisplacementToleranceDips);
    }

    [Theory]
    // startedOnRow, escapedAnchor, maxDisplacement, expected
    [InlineData(false, false, 0.0, false)] // empty-space band: never converts, even if it never escaped
    [InlineData(false, true, 0.0, false)] // empty-space band: never converts, even a large escape
    [InlineData(false, true, 500.0, false)]
    [InlineData(true, false, 0.0, true)] // row band that never escaped the anchor: click
    [InlineData(true, false, 500.0, true)] // never escaped, regardless of displacement
    [InlineData(true, true, 9.9, true)] // escaped, but within the displacement tolerance: still a click
    [InlineData(true, true, 10.0, false)] // escaped, at the tolerance boundary: mark-range
    [InlineData(true, true, 10.1, false)] // escaped, past the tolerance: mark-range
    public void ShouldConvertToClick_matches_the_wiggle_click_truth_table(
        bool startedOnRow, bool escapedAnchor, double maxDisplacement, bool expected)
    {
        Assert.Equal(
            expected, RubberBandTracker.ShouldConvertToClick(startedOnRow, escapedAnchor, maxDisplacement));
    }

    // GestureReleaseAction is internal, and a [Theory]'s InlineData/method signature must be at
    // least as accessible as the (necessarily public) test method itself - so the expected action
    // travels through InlineData as its underlying int and is cast back inside the test body.
    [Theory]
    // dragFired, rowWasPressed, bandProducedRange, startedOnRow, escapedAnchor, maxDisplacement, expected
    [InlineData(false, true, false, false, false, 0.0, (int)RubberBandTracker.GestureReleaseAction.Click)] // plain click, no drag at all
    [InlineData(false, false, false, false, false, 0.0, (int)RubberBandTracker.GestureReleaseAction.None)] // idle press/release on empty space, never crossed threshold
    [InlineData(false, false, true, false, false, 0.0, (int)RubberBandTracker.GestureReleaseAction.MarkRange)] // THE BUG: empty-space band, dragTracker never armed (dragFired stays false) - must still mark
    [InlineData(false, false, true, false, true, 500.0, (int)RubberBandTracker.GestureReleaseAction.MarkRange)] // empty-space band with a big escape: still marks (never converts to click)
    [InlineData(true, true, true, true, false, 0.0, (int)RubberBandTracker.GestureReleaseAction.Click)] // row band, never escaped anchor: wiggle-click conversion
    [InlineData(true, true, true, true, true, 5.0, (int)RubberBandTracker.GestureReleaseAction.Click)] // row band, escaped but within displacement tolerance: wiggle-click conversion
    [InlineData(true, true, true, true, true, 50.0, (int)RubberBandTracker.GestureReleaseAction.MarkRange)] // row band, genuinely dragged: marks
    [InlineData(true, true, false, true, false, 0.0, (int)RubberBandTracker.GestureReleaseAction.None)] // dragFired true but the band itself never produced a range (e.g. empty column)
    public void DecideRelease_matches_the_expected_action(
        bool dragFired,
        bool rowWasPressed,
        bool bandProducedRange,
        bool startedOnRow,
        bool escapedAnchor,
        double maxDisplacement,
        int expected)
    {
        Assert.Equal(
            (RubberBandTracker.GestureReleaseAction)expected,
            RubberBandTracker.DecideRelease(
                dragFired, rowWasPressed, bandProducedRange, startedOnRow, escapedAnchor, maxDisplacement));
    }

    [Fact]
    public void DecideRelease_reproduces_the_upward_empty_space_band_bug_end_to_end()
    {
        // Regression pin for Item B: directory with rows "26"/"27"/"28" (indices 0,1,2), press in
        // the empty space below "28" (anchors on the last row via ColumnView.FindNearestRowIndex),
        // drag up to "26". The empty-space press never arms DragGestureTracker at all, so
        // FiredThisGesture stays permanently false for this gesture - reading that as "never
        // dragged, so it's a click" is exactly the regression from commit ab6fc62. The mark range
        // must still be requested for [0, 2], even though it collapses to nothing but a drag.
        var tracker = new RubberBandTracker();
        const int lastRowIndex = 2;
        var origin = new Point(100, 500);
        tracker.Press(origin, onEmptySpace: true, anchorIndex: lastRowIndex);
        tracker.Move(new Point(origin.X, origin.Y - 200)); // past the threshold, dragging up

        var range = tracker.UpdateCurrentIndex(0);
        Assert.Equal((0, lastRowIndex), range);

        var escapedAnchor = tracker.HasEscapedAnchor;
        var rect = tracker.Release();

        var action = RubberBandTracker.DecideRelease(
            dragFired: false, // dragTracker.FiredThisGesture - never armed for an empty-space press
            rowWasPressed: false, // dragTracker.PressedEntryIndex - never set either
            bandProducedRange: rect is not null && range is not null,
            startedOnRow: false,
            escapedAnchor,
            maxDisplacement: 200.0);

        Assert.Equal(RubberBandTracker.GestureReleaseAction.MarkRange, action);
    }
}
