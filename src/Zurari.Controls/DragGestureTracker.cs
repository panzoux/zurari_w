using System.Windows;

namespace Zurari.Controls;

/// <summary>
/// Pure state machine for the drag-out gesture: press → move past the system drag
/// threshold → fires exactly once; release (or a move with the button no longer down)
/// resets. Extracted from <see cref="ColumnView"/> so the once-per-gesture rules are
/// unit-testable without STA mouse synthesis — <see cref="System.Windows.Input.MouseEventArgs"/>
/// reports the live OS cursor/button state, which tests cannot fake.
/// </summary>
internal sealed class DragGestureTracker
{
    private Point? start;
    private int? entryIndex;
    private bool fired;

    /// <summary>Starts a new gesture at <paramref name="position"/> over the given entry.</summary>
    public void Press(Point position, int pressedEntryIndex)
    {
        start = position;
        entryIndex = pressedEntryIndex;
        fired = false;
    }

    /// <summary>
    /// Advances the gesture. Returns the pressed entry index exactly once, when the
    /// pointer first travels past the system drag threshold with the button still
    /// down; otherwise null. A move without the button down cancels the gesture
    /// (the release happened outside the window and was never observed).
    /// </summary>
    public int? Move(Point position, bool leftButtonDown)
    {
        if (fired || start is not { } origin || entryIndex is not { } index)
        {
            return null;
        }

        if (!leftButtonDown)
        {
            Release();
            return null;
        }

        if (Math.Abs(position.X - origin.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - origin.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return null;
        }

        fired = true;
        return index;
    }

    /// <summary>Ends the gesture; the next press starts fresh.</summary>
    public void Release()
    {
        start = null;
        entryIndex = null;
        fired = false;
    }
}
