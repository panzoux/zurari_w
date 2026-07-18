using System.Windows;

namespace Zurari.Controls;

/// <summary>
/// Pure state machine for the rubber-band (rectangle) select gesture: press on empty space →
/// move past the system drag threshold → returns the current rectangle on every subsequent move;
/// release returns the final rectangle (or <c>null</c> if the gesture never activated). Extracted
/// from <see cref="ColumnView"/> so the threshold/rect-math rules are unit-testable without STA
/// mouse synthesis — mirrors <see cref="DragGestureTracker"/>.
/// </summary>
internal sealed class RubberBandTracker
{
    /// <summary>
    /// Elapsed time since press, in milliseconds, below which a press-then-move starting on an
    /// unmarked row starts a rubber-band instead of a file drag (Finder-style timing) — see
    /// <see cref="ShouldStartRubberBand"/>.
    /// </summary>
    internal const int RubberBandVsDragThresholdMs = 300;

    /// <summary>
    /// Maximum pointer displacement (DIPs) from the press point, for the whole gesture, below which
    /// a ROW-STARTED rubber band that escaped its anchor row still converts back to a click at
    /// release - see <see cref="ShouldConvertToClick"/>. Covers the case where the press landed near
    /// a row boundary: a few-px wiggle crosses into the neighbour row (latching
    /// <see cref="HasEscapedAnchor"/>) even though the user never meant to drag anywhere.
    /// </summary>
    internal const double ClickDisplacementToleranceDips = 10.0;

    private Point? origin;
    private bool active;
    private Rect? currentRect;
    private int anchorIndex = -1;
    private int currentIndex = -1;
    private bool escapedAnchor;

    /// <summary>True once the gesture has crossed the drag threshold, until the next <see cref="Release"/>.</summary>
    public bool IsActive => active;

    /// <summary>
    /// True once <see cref="UpdateCurrentIndex"/> has ever been called with an index different from
    /// <see cref="AnchorIndex"/> during the current gesture (since the last <see cref="Press"/>), and
    /// stays true for the rest of the gesture even if the pointer moves back onto the anchor row -
    /// escaping once is what matters, not where the range ends up. <see cref="ColumnView"/> uses this
    /// to tell an actual rubber-band drag apart from a physical-button press that merely wiggled the
    /// pointer a few pixels: a row-started gesture that never escaped its anchor is a click, not a
    /// mark-range (see the "wiggle-click" fix in <c>OnListPreviewMouseUp</c>).
    /// </summary>
    public bool HasEscapedAnchor => escapedAnchor;

    /// <summary>
    /// The row index nearest the press position, as supplied to <see cref="Press"/>
    /// (-1 when the press was not on empty space, or the column had no rows at all).
    /// </summary>
    public int AnchorIndex => anchorIndex;

    /// <summary>
    /// The inclusive row-index range currently covered by the gesture ([min(anchor,current) ..
    /// max(anchor,current)]), or <c>null</c> when the gesture is not active or has no valid anchor
    /// (e.g. an empty column). Pure min/max math over indices supplied by <see cref="Press"/> and
    /// <see cref="UpdateCurrentIndex"/> - hit-testing those indices from real pointer/viewport
    /// coordinates is <see cref="ColumnView"/>'s job, not this class's.
    /// </summary>
    public (int From, int To)? LiveRange
    {
        get
        {
            if (!active || anchorIndex < 0 || currentIndex < 0)
            {
                return null;
            }

            return (Math.Min(anchorIndex, currentIndex), Math.Max(anchorIndex, currentIndex));
        }
    }

    /// <summary>
    /// Starts a new gesture at <paramref name="positionInList"/>. <paramref name="onEmptySpace"/>
    /// records whether the press landed off any entry row; a press on a row never activates
    /// rubber-band (<see cref="Move"/> stays silent for the rest of that gesture).
    /// <paramref name="anchorIndex"/> is the row nearest the press (-1 for an empty column, or
    /// when the press was not on empty space - it is ignored in that case).
    /// </summary>
    public void Press(Point positionInList, bool onEmptySpace, int anchorIndex = -1)
    {
        origin = onEmptySpace ? positionInList : null;
        active = false;
        currentRect = null;
        this.anchorIndex = onEmptySpace ? anchorIndex : -1;
        currentIndex = this.anchorIndex;
        escapedAnchor = false;
    }

    /// <summary>
    /// Updates the row index currently under the pointer and returns the resulting
    /// <see cref="LiveRange"/>. Callers pass an already-clamped index (first/last visible row when
    /// the pointer is outside the viewport); this method does no clamping itself. Also latches
    /// <see cref="HasEscapedAnchor"/> once <paramref name="currentIndex"/> first differs from
    /// <see cref="AnchorIndex"/>.
    /// </summary>
    public (int From, int To)? UpdateCurrentIndex(int currentIndex)
    {
        this.currentIndex = currentIndex;
        if (currentIndex != anchorIndex)
        {
            escapedAnchor = true;
        }

        return LiveRange;
    }

    /// <summary>
    /// Advances the gesture. Returns the current rectangle (origin to <paramref name="position"/>)
    /// once the pointer has travelled past the system drag threshold from the press point;
    /// <c>null</c> before that (including when the press was not on empty space, or before any
    /// press at all).
    /// </summary>
    public Rect? Move(Point position)
    {
        if (origin is not { } start)
        {
            return null;
        }

        if (!active)
        {
            if (Math.Abs(position.X - start.X) < SystemParameters.MinimumHorizontalDragDistance
                && Math.Abs(position.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance)
            {
                return null;
            }

            active = true;
        }

        currentRect = new Rect(start, position);
        return currentRect;
    }

    /// <summary>
    /// Ends the gesture, returning the final rectangle (as of the last <see cref="Move"/>) if it
    /// was ever active (<c>null</c> otherwise — a plain click, a press that never crossed the
    /// threshold, or a press not on empty space). The next press starts fresh.
    /// </summary>
    public Rect? Release()
    {
        var result = active ? currentRect : null;
        origin = null;
        active = false;
        currentRect = null;
        anchorIndex = -1;
        currentIndex = -1;
        escapedAnchor = false;
        return result;
    }

    /// <summary>
    /// Finder-style decision for which gesture a press-then-move starts, made once movement first
    /// crosses the system drag threshold. A press on empty space is always eligible (there is
    /// nothing to drag); otherwise - including on a MARKED row, per user feedback: the timing rule
    /// applies uniformly regardless of mark state - it depends on how long the button was held
    /// before the pointer moved: under <see cref="RubberBandVsDragThresholdMs"/> starts a
    /// rubber-band (anchored at the press point/row), at or past it starts the file drag (dragging
    /// the marked SET, if the pressed row is marked) — matching Finder's press-and-hold-then-drag
    /// vs. press-and-immediately-drag distinction. Ctrl held at the drag-threshold crossing
    /// overrides this entirely and always starts an (additive) rubber band - see the Ctrl handling
    /// in <see cref="ColumnView.OnListPreviewMouseMove"/>, which this method does not know about.
    /// </summary>
    internal static bool ShouldStartRubberBand(bool onEmptySpace, long elapsedMs)
    {
        if (onEmptySpace)
        {
            return true;
        }

        return elapsedMs < RubberBandVsDragThresholdMs;
    }

    /// <summary>
    /// Wiggle-click rule: a ROW-STARTED rubber band (<paramref name="startedOnRow"/>) converts back
    /// to a plain click at release when it either never escaped its anchor row
    /// (<c>!</c><paramref name="escapedAnchor"/>) OR the pointer's max displacement from the press
    /// point over the whole gesture never exceeded <see cref="ClickDisplacementToleranceDips"/> -
    /// covering a wiggle that happened to cross a nearby row boundary anyway. An EMPTY-SPACE band
    /// (<c>!</c><paramref name="startedOnRow"/>) is never converted - it always applies marks, even
    /// for a final single-row range (see the empty-space rubber-band bug this guards against: it
    /// must never be swallowed by the row-only click rule below).
    /// </summary>
    internal static bool ShouldConvertToClick(bool startedOnRow, bool escapedAnchor, double maxDisplacement)
    {
        if (!startedOnRow)
        {
            return false;
        }

        return !escapedAnchor || maxDisplacement < ClickDisplacementToleranceDips;
    }

    /// <summary>What a gesture's button-up should raise, computed purely from state captured before
    /// either tracker (<see cref="DragGestureTracker"/> or this one) resets it.</summary>
    internal enum GestureReleaseAction
    {
        /// <summary>Raise neither event - e.g. an empty-space press that never crossed the drag threshold.</summary>
        None,

        /// <summary><see cref="ColumnView.EntryClicked"/> - a true click or a wiggle-click conversion.</summary>
        Click,

        /// <summary><see cref="ColumnView.MarkRangeRequested"/> - a genuine rubber-band release.</summary>
        MarkRange,
    }

    /// <summary>
    /// Decides <see cref="GestureReleaseAction"/> for <see cref="ColumnView.OnListPreviewMouseUp"/>.
    /// <paramref name="dragFired"/> is <see cref="DragGestureTracker.FiredThisGesture"/>, which is
    /// only meaningful when <paramref name="rowWasPressed"/> is true: an empty-space press never
    /// arms <see cref="DragGestureTracker"/> at all (only this tracker), so <paramref name="dragFired"/>
    /// stays permanently false for an empty-space gesture regardless of whether a rubber band
    /// actually ran - reading it as "never dragged, so it's a click" for that case is exactly the
    /// bug this method fixes: an empty-space rubber band's marks were being silently dropped at
    /// release. <paramref name="bandProducedRange"/> is whether the rubber band both activated
    /// (<see cref="Release"/> returned a rect) and resolved a valid live range (<see cref="LiveRange"/>
    /// non-null just before that) - false for a column with no rows at all, or a press that never
    /// crossed the threshold.
    /// </summary>
    internal static GestureReleaseAction DecideRelease(
        bool dragFired,
        bool rowWasPressed,
        bool bandProducedRange,
        bool startedOnRow,
        bool escapedAnchor,
        double maxDisplacement)
    {
        if (!dragFired && rowWasPressed)
        {
            return GestureReleaseAction.Click;
        }

        if (!bandProducedRange)
        {
            // A ROW press whose gesture became a rubber band (startedOnRow) but was released
            // before the band ever activated is a stillborn band: the pointer wiggled past the
            // drag threshold and stopped - that is a click, not "nothing" (observed with
            // physical touchpad buttons: press, ~8px jitter, quick release swallowed the click).
            // startedOnRow=false with dragFired=true means the gesture became a FILE drag
            // instead - OLE owns that release, so None stays correct there.
            return startedOnRow && rowWasPressed
                ? GestureReleaseAction.Click
                : GestureReleaseAction.None;
        }

        return ShouldConvertToClick(startedOnRow, escapedAnchor, maxDisplacement)
            ? GestureReleaseAction.Click
            : GestureReleaseAction.MarkRange;
    }
}
