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
/// reports the drop; the host decides how to copy/move.
/// </summary>
public sealed class FileDropRequestedEventArgs(int columnIndex, IReadOnlyList<string> paths, bool isMove) : EventArgs
{
    public int ColumnIndex { get; } = columnIndex;

    public IReadOnlyList<string> Paths { get; } = paths;

    /// <summary>True when the drop should move rather than copy (Shift held, or a Move-only effect).</summary>
    public bool IsMove { get; } = isMove;
}
