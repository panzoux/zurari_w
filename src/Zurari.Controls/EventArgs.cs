using System.Windows;
using System.Windows.Input;

namespace Zurari.Controls;

/// <summary>Cursor movement kinds. The host decides what each one means (page size etc.).</summary>
public enum CursorMove
{
    Up,
    Down,
    PageUp,
    PageDown,
    Home,
    End,
}

/// <summary>Raised for ↑↓/PageUp/PageDown/Home/End on the focused column.</summary>
public sealed class CursorMoveRequestedEventArgs(int columnIndex, CursorMove move, int visibleRowCount) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public CursorMove Move { get; } = move;

    /// <summary>Rows currently visible in the column's viewport (hint for PageUp/PageDown); 0 if unknown.</summary>
    public int VisibleRowCount { get; } = visibleRowCount;
}

/// <summary>Raised when a column should become the focused column (click on a non-focused column).</summary>
public sealed class ColumnFocusRequestedEventArgs(int columnIndex) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;
}

/// <summary>Raised for Enter / double-click / → on a directory entry.</summary>
public sealed class EntryActivatedEventArgs(int columnIndex, int entryIndex) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public int EntryIndex { get; } = entryIndex;
}

/// <summary>Raised for Backspace / ← (except on the root column).</summary>
public sealed class NavigateUpRequestedEventArgs(int columnIndex) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;
}

/// <summary>
/// Raised for a true click (press-then-release without crossing the drag threshold) on an entry
/// row. The host maps this to directory activation; a press that turns into a drag instead raises
/// <see cref="EntryDragRequestedEventArgs"/>, never this.
/// </summary>
public sealed class EntryClickedEventArgs(int columnIndex, int entryIndex) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public int EntryIndex { get; } = entryIndex;
}

/// <summary>
/// Raised for a pointer press on an entry. The host interprets modifiers
/// (e.g. Ctrl+click toggles a mark); the control only reports the input.
/// </summary>
public sealed class EntryPointerPressedEventArgs(
    int columnIndex, int entryIndex, ModifierKeys modifiers, MouseButton button, Point screenPosition) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public int EntryIndex { get; } = entryIndex;

    public ModifierKeys Modifiers { get; } = modifiers;

    public MouseButton Button { get; } = button;

    /// <summary>
    /// Screen (device) coordinates of the press, e.g. for positioning a context menu.
    /// The control only reports where the press happened; it never interprets it.
    /// </summary>
    public Point ScreenPosition { get; } = screenPosition;
}

/// <summary>Raised after a column-width drag completes, so the host can persist widths.</summary>
public sealed class ColumnWidthsChangedEventArgs(IReadOnlyList<double> widths) : EventArgs
{
    public IReadOnlyList<double> Widths { get; } = widths;
}

/// <summary>
/// Raised once per left-button drag gesture that starts on an entry row and crosses the system
/// drag threshold. The control only reports that a drag-out gesture happened; it never starts the
/// OLE drag itself (the host owns <c>DragDrop.DoDragDrop</c> since it alone knows the full path).
/// </summary>
public sealed class EntryDragRequestedEventArgs(int columnIndex, int entryIndex) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public int EntryIndex { get; } = entryIndex;
}

/// <summary>
/// Raised when files are dropped from Explorer (or another app) onto a column. The control only
/// reports the drop (which row, if any, the pointer was over; raw modifier state); the host
/// decides the destination and whether to copy or move.
/// </summary>
public sealed class FileDropRequestedEventArgs(
    int columnIndex, IReadOnlyList<string> paths, int targetEntryIndex, bool shiftHeld, bool ctrlHeld) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public IReadOnlyList<string> Paths { get; } = paths;

    /// <summary>The row the pointer was over at drop time, or -1 for the column background.</summary>
    public int TargetEntryIndex { get; } = targetEntryIndex;

    /// <summary>True when Shift was held at drop time.</summary>
    public bool ShiftHeld { get; } = shiftHeld;

    /// <summary>True when Ctrl was held at drop time.</summary>
    public bool CtrlHeld { get; } = ctrlHeld;
}

/// <summary>
/// Raised when a rubber-band (rectangle) drag on empty space is released over at least one entry
/// row. The control only reports the resolved row range and whether Ctrl was held at release
/// (additive) or not (replaces the selection); the host decides how marks are applied.
/// </summary>
public sealed class MarkRangeRequestedEventArgs(int columnIndex, int fromIndex, int toIndex, bool additive) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public int FromIndex { get; } = fromIndex;

    public int ToIndex { get; } = toIndex;

    /// <summary>True when Ctrl was held at release (add to the existing selection).</summary>
    public bool Additive { get; } = additive;
}

/// <summary>
/// Raised the moment a rubber-band (rectangle) drag ACTIVATES (the pointer first crosses the drag
/// threshold), before it is released. The host uses this to clear the column's existing marks the
/// instant a replace-mode (non-additive) band starts, rather than waiting for release - so stale
/// marks never render alongside the live rubber-band highlight during the drag.
/// </summary>
public sealed class RubberBandStartedEventArgs(int columnIndex, bool additive) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    /// <summary>True when Ctrl was held at the moment the band activated (adds to existing marks).</summary>
    public bool Additive { get; } = additive;
}

/// <summary>Raised when the rename editor is accepted, carrying what was typed.</summary>
public sealed class RenameSubmittedEventArgs(string newName) : EventArgs
{
    public string NewName { get; } = newName;
}
