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
    private Point? origin;
    private bool active;
    private Rect? currentRect;

    /// <summary>True once the gesture has crossed the drag threshold, until the next <see cref="Release"/>.</summary>
    public bool IsActive => active;

    /// <summary>
    /// Starts a new gesture at <paramref name="positionInList"/>. <paramref name="onEmptySpace"/>
    /// records whether the press landed off any entry row; a press on a row never activates
    /// rubber-band (<see cref="Move"/> stays silent for the rest of that gesture).
    /// </summary>
    public void Press(Point positionInList, bool onEmptySpace)
    {
        origin = onEmptySpace ? positionInList : null;
        active = false;
        currentRect = null;
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
        return result;
    }
}
