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
}
