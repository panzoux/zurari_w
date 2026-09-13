using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace Zurari.Controls;

/// <summary>
/// Finder-style multi-column browser. A dumb view: state in, input out.
/// <para>
/// State in: <see cref="Columns"/> is an immutable snapshot the host replaces
/// wholesale after every state change (no INotifyPropertyChanged/INotifyCollectionChanged;
/// assigning the property is the only way to change what is displayed).
/// </para>
/// <para>
/// Input out: keyboard and mouse input is translated into the events below and
/// never interpreted locally — the control does not even move its own cursor.
/// The only view-local state is column widths (reported via <see cref="ColumnWidthsChanged"/>;
/// persistence is the host's job).
/// </para>
/// </summary>
[TemplatePart(Name = PanelPartName, Type = typeof(StackPanel))]
[TemplatePart(Name = ScrollPartName, Type = typeof(ScrollViewer))]
public sealed class ColumnBrowser : Control
{
    internal const string PanelPartName = "PART_ColumnsPanel";

    internal const string ScrollPartName = "PART_HorizontalScroll";

    private const double DefaultColumnWidth = 240;
    private const double MinColumnWidth = 120;

    /// <summary>
    /// Opt-in switch for the <c>[input]</c>-prefixed <see cref="System.Diagnostics.Trace"/> lines
    /// <see cref="ColumnView"/> emits from its mouse-input handlers, added to diagnose a bug where
    /// a touchpad's physical left button sometimes moves the cursor but never enters the directory.
    /// Default <c>false</c> so tracing costs nothing in normal use; set to <c>true</c> only when
    /// the host was started with <c>--debug-input</c> (see <c>Zurari.App</c>'s startup code), which
    /// also attaches a <see cref="System.Diagnostics.TraceListener"/> that forwards these lines to
    /// a log file via <c>Zurari.Runtime.DebugLog</c> (Controls itself must never touch the
    /// filesystem - see <c>BannedSymbols.txt</c>).
    /// </summary>
    public static bool InputTraceEnabled { get; set; }

    /// <summary>Identifies the <see cref="Columns"/> dependency property.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(IReadOnlyList<ColumnVm>),
        typeof(ColumnBrowser),
        new FrameworkPropertyMetadata(null, static (d, _) => ((ColumnBrowser)d).RebuildColumns()));

    private readonly List<double> columnWidths = [];

    private StackPanel? columnsPanel;

    private ScrollViewer? horizontalScroll;

    static ColumnBrowser()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColumnBrowser), new FrameworkPropertyMetadata(typeof(ColumnBrowser)));
    }

    /// <summary>Columns to display, leftmost first. Snapshot semantics — replace, never mutate.</summary>
    public IReadOnlyList<ColumnVm>? Columns
    {
        get => (IReadOnlyList<ColumnVm>?)GetValue(ColumnsProperty);
        set => SetValue(ColumnsProperty, value);
    }

    /// <summary>Raised after a column-width drag completes, so the host can persist widths.</summary>
    public event EventHandler<ColumnWidthsChangedEventArgs>? ColumnWidthsChanged;

    /// <summary>Raised for ↑↓/PageUp/PageDown/Home/End on the focused column.</summary>
    public event EventHandler<CursorMoveRequestedEventArgs>? CursorMoveRequested;

    /// <summary>Raised when a column should become the focused column (click on a non-focused column).</summary>
    public event EventHandler<ColumnFocusRequestedEventArgs>? ColumnFocusRequested;

    /// <summary>Raised for Enter / double-click / → on a directory entry.</summary>
    public event EventHandler<EntryActivatedEventArgs>? EntryActivated;

    /// <summary>
    /// Raised for a true click (press-then-release without crossing the drag threshold) on an
    /// entry row - the host maps this to directory activation, distinct from
    /// <see cref="EntryPointerPressed"/> which fires on every press including ones that turn
    /// into a drag.
    /// </summary>
    public event EventHandler<EntryClickedEventArgs>? EntryClicked;

    /// <summary>A section header's hide/show control was clicked.</summary>
    public event EventHandler<EntryClickedEventArgs>? SectionToggleRequested;

    /// <summary>Raised for Backspace / ← (the control does not special-case the root column).</summary>
    public event EventHandler<NavigateUpRequestedEventArgs>? NavigateUpRequested;

    /// <summary>Raised on a mouse-button press over an entry row.</summary>
    public event EventHandler<EntryPointerPressedEventArgs>? EntryPointerPressed;

    /// <summary>
    /// Raised once per left-button drag gesture that starts on an entry row and crosses the system
    /// drag threshold. The host is responsible for starting the actual OLE drag
    /// (<c>DragDrop.DoDragDrop</c>) — this control never touches the clipboard/shell itself.
    /// </summary>
    public event EventHandler<EntryDragRequestedEventArgs>? EntryDragRequested;

    /// <summary>Raised when files are dropped from Explorer (or another app) onto a column.</summary>
    public event EventHandler<FileDropRequestedEventArgs>? FileDropRequested;

    /// <summary>
    /// Raised when a rubber-band (rectangle) drag on empty space in a column is released over at
    /// least one entry row. The host maps this to a mark-range update.
    /// </summary>
    public event EventHandler<MarkRangeRequestedEventArgs>? MarkRangeRequested;

    /// <summary>
    /// Raised the moment a rubber-band drag activates in a column (before release) - see
    /// <see cref="RubberBandStartedEventArgs"/>.
    /// </summary>
    public event EventHandler<RubberBandStartedEventArgs>? RubberBandStarted;

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        columnsPanel = GetTemplateChild(PanelPartName) as StackPanel;
        horizontalScroll = GetTemplateChild(ScrollPartName) as ScrollViewer;
        RebuildColumns();
    }

    /// <summary>
    /// Syncs PART_ColumnsPanel children with the <see cref="Columns"/> snapshot.
    /// Existing <see cref="ColumnView"/>s are reused so a snapshot swap does not
    /// tear down virtualized lists that merely changed contents.
    /// </summary>
    private void RebuildColumns()
    {
        if (columnsPanel is null)
        {
            return;
        }

        IReadOnlyList<ColumnVm> columns = Columns ?? [];
        var children = columnsPanel.Children;

        while (children.Count > columns.Count)
        {
            children.RemoveAt(children.Count - 1);
        }

        while (children.Count < columns.Count)
        {
            var view = new ColumnView();
            view.RenameSubmitted += (_, e) => RenameSubmitted?.Invoke(this, e);
            view.RenameCancelled += (_, _) => RenameCancelled?.Invoke(this, EventArgs.Empty);
            view.ResizeDelta += OnColumnResizeDelta;
            view.ResizeCompleted += OnColumnResizeCompleted;
            view.EntryPointerPressed += OnColumnEntryPointerPressed;
            view.EntryActivationRequested += OnColumnEntryActivationRequested;
            view.EntryClicked += OnColumnEntryClicked;
            view.SectionToggleRequested += OnColumnSectionToggleRequested;
            view.EntryDragRequested += OnColumnEntryDragRequested;
            view.FileDropRequested += OnColumnFileDropRequested;
            view.MarkRangeRequested += OnColumnMarkRangeRequested;
            view.RubberBandStarted += OnColumnRubberBandStarted;
            children.Add(view);
        }

        while (columnWidths.Count < columns.Count)
        {
            columnWidths.Add(DefaultColumnWidth);
        }

        for (var i = 0; i < columns.Count; i++)
        {
            var view = (ColumnView)children[i];
            view.Column = columns[i];
            view.Width = columnWidths[i];
        }

        // Before the layout pass this assignment triggers - see ReserveScrolledWidth.
        ReserveScrolledWidth();

        // Deferred to the Loaded priority so layout has assigned the views their final sizes
        // before the scroll computation runs (a freshly (re)sized column has no arranged bounds
        // yet at the point Columns is assigned, and neither has the viewport).
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(ApplyHorizontalScroll));
    }

    /// <summary>
    /// Holds the width the current scroll position needs, so that a column going away does not
    /// drag every other column sideways.
    /// </summary>
    /// <remarks>
    /// A cursor on a folder opens that folder in a column to the right of the focused one, and
    /// moving the cursor down onto a file takes it away again. Nothing about the other columns
    /// changed, so none of them should move - but a <see cref="ScrollViewer"/> clamps its offset to
    /// whatever its content is still wide enough to justify, so losing that column pulled the
    /// remaining ones across the screen. Reserving the width keeps the offset legal: the space the
    /// vanished column held simply goes blank, and the next child column to open takes it back.
    /// <para>
    /// Must run before the layout pass that drops the column, which is why it is called here rather
    /// than from <see cref="ApplyHorizontalScroll"/> - by then the offset has already been clamped
    /// and the value worth keeping is gone.
    /// </para>
    /// </remarks>
    private void ReserveScrolledWidth()
    {
        if (columnsPanel is null || horizontalScroll is null)
        {
            return;
        }

        columnsPanel.MinWidth = horizontalScroll.HorizontalOffset + horizontalScroll.ViewportWidth;
    }

    /// <summary>
    /// Puts the horizontal viewport where this snapshot needs it: the child column beside the
    /// cursor in view, the focused column in view, and otherwise exactly where the user left it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Showing the folder under the cursor is the entire reason that child column exists, so a
    /// snapshot that opens one past the right edge scrolls to it. Everything else is deliberately
    /// one-way: this never scrolls left to close up blank space, only far enough to bring the
    /// focused column back when a snapshot would otherwise leave it off screen.
    /// </para>
    /// <para>
    /// Computed from <see cref="columnWidths"/> rather than by calling <c>BringIntoView</c> on a
    /// column view, so the offset is one number this method owns - instead of the outcome of
    /// however many bring-into-view requests happened to reach the scroll viewer, in whatever order
    /// (see <see cref="ColumnView"/>'s RequestBringIntoView handler, which is what keeps the rows
    /// inside a column from scrolling the browser sideways at all).
    /// </para>
    /// </remarks>
    private void ApplyHorizontalScroll()
    {
        if (columnsPanel is null || horizontalScroll is null)
        {
            return;
        }

        var columns = Columns;
        var focusedIndex = FindFocusedColumnIndex(columns);
        var viewport = horizontalScroll.ViewportWidth;
        if (focusedIndex < 0 || viewport <= 0)
        {
            return;
        }

        var offset = horizontalScroll.HorizontalOffset;

        // Right, far enough to reveal the child column beside the cursor - or the focused column
        // itself, when the cursor is on something that opens no column.
        var revealIndex = Math.Min(focusedIndex + 1, columns!.Count - 1);
        offset = Math.Max(offset, RightEdge(revealIndex) - viewport);

        // Left, but never further than the focused column's own left edge.
        offset = Math.Max(0, Math.Min(offset, RightEdge(focusedIndex) - columnWidths[focusedIndex]));

        columnsPanel.MinWidth = offset + viewport;
        horizontalScroll.ScrollToHorizontalOffset(offset);
    }

    /// <summary>Right edge of one column inside the panel, from the widths this control assigned.</summary>
    private double RightEdge(int columnIndex)
    {
        var edge = 0d;
        for (var i = 0; i <= columnIndex && i < columnWidths.Count; i++)
        {
            edge += columnWidths[i];
        }

        return edge;
    }

    private void OnColumnResizeDelta(object? sender, double horizontalChange)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        columnWidths[index] = Math.Max(MinColumnWidth, columnWidths[index] + horizontalChange);
        view.Width = columnWidths[index];
    }

    private void OnColumnResizeCompleted(object? sender, EventArgs e)
    {
        ColumnWidthsChanged?.Invoke(this, new ColumnWidthsChangedEventArgs([.. columnWidths]));
    }

    private void OnColumnEntryPointerPressed(object? sender, EntryPointerPressInfo info)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        EntryPointerPressed?.Invoke(
            this,
            new EntryPointerPressedEventArgs(
                index, info.EntryIndex, info.Modifiers, info.Button, info.ScreenPosition));

        var columns = Columns;
        if (columns is not null && index < columns.Count && !columns[index].IsFocused)
        {
            ColumnFocusRequested?.Invoke(this, new ColumnFocusRequestedEventArgs(index));
        }
    }

    private void OnColumnEntryActivationRequested(object? sender, int entryIndex)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        EntryActivated?.Invoke(this, new EntryActivatedEventArgs(index, entryIndex));
    }

    private void OnColumnEntryClicked(object? sender, int entryIndex)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        EntryClicked?.Invoke(this, new EntryClickedEventArgs(index, entryIndex));
    }

    private void OnColumnSectionToggleRequested(object? sender, int entryIndex)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index >= 0)
        {
            SectionToggleRequested?.Invoke(this, new EntryClickedEventArgs(index, entryIndex));
        }
    }

    private void OnColumnEntryDragRequested(object? sender, int entryIndex)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        EntryDragRequested?.Invoke(this, new EntryDragRequestedEventArgs(index, entryIndex));
    }

    private void OnColumnFileDropRequested(object? sender, FileDropInfo info)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        FileDropRequested?.Invoke(
            this,
            new FileDropRequestedEventArgs(index, info.Paths, info.TargetEntryIndex, info.ShiftHeld, info.CtrlHeld));
    }

    private void OnColumnMarkRangeRequested(object? sender, MarkRangeRequestInfo info)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        MarkRangeRequested?.Invoke(
            this, new MarkRangeRequestedEventArgs(index, info.FromIndex, info.ToIndex, info.Additive));
    }

    private void OnColumnRubberBandStarted(object? sender, bool additive)
    {
        if (sender is not ColumnView view || columnsPanel is null)
        {
            return;
        }

        var index = columnsPanel.Children.IndexOf(view);
        if (index < 0)
        {
            return;
        }

        RubberBandStarted?.Invoke(this, new RubberBandStartedEventArgs(index, additive));
    }

    /// <inheritdoc />
    protected override void OnKeyDown(KeyEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        var columns = Columns;
        var focusedIndex = FindFocusedColumnIndex(columns);
        if (focusedIndex < 0)
        {
            return;
        }

        var focusedColumn = columns![focusedIndex];

        switch (e.Key)
        {
            case Key.Up:
                RaiseCursorMove(focusedIndex, CursorMove.Up);
                e.Handled = true;
                break;
            case Key.Down:
                RaiseCursorMove(focusedIndex, CursorMove.Down);
                e.Handled = true;
                break;
            case Key.PageUp:
                RaiseCursorMove(focusedIndex, CursorMove.PageUp);
                e.Handled = true;
                break;
            case Key.PageDown:
                RaiseCursorMove(focusedIndex, CursorMove.PageDown);
                e.Handled = true;
                break;
            case Key.Home:
                RaiseCursorMove(focusedIndex, CursorMove.Home);
                e.Handled = true;
                break;
            case Key.End:
                RaiseCursorMove(focusedIndex, CursorMove.End);
                e.Handled = true;
                break;
            case Key.Right:
                HandleRight(focusedIndex, focusedColumn, columns.Count);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Back:
                NavigateUpRequested?.Invoke(this, new NavigateUpRequestedEventArgs(focusedIndex));
                e.Handled = true;
                break;
            case Key.Enter:
                if (focusedColumn.CursorIndex >= 0)
                {
                    EntryActivated?.Invoke(
                        this, new EntryActivatedEventArgs(focusedIndex, focusedColumn.CursorIndex));
                }

                e.Handled = true;
                break;
        }
    }

    private static int FindFocusedColumnIndex(IReadOnlyList<ColumnVm>? columns)
    {
        if (columns is null)
        {
            return -1;
        }

        for (var i = 0; i < columns.Count; i++)
        {
            if (columns[i].IsFocused)
            {
                return i;
            }
        }

        return -1;
    }

    private void HandleRight(int focusedIndex, ColumnVm focusedColumn, int columnCount)
    {
        if (focusedColumn.CursorIndex < 0 || focusedColumn.CursorIndex >= focusedColumn.Entries.Count)
        {
            return;
        }

        var entry = focusedColumn.Entries[focusedColumn.CursorIndex];
        if (entry.Kind is EntryKind.Directory or EntryKind.Drive)
        {
            EntryActivated?.Invoke(this, new EntryActivatedEventArgs(focusedIndex, focusedColumn.CursorIndex));
        }
        else if (focusedIndex + 1 < columnCount)
        {
            ColumnFocusRequested?.Invoke(this, new ColumnFocusRequestedEventArgs(focusedIndex + 1));
        }
    }

    private void RaiseCursorMove(int columnIndex, CursorMove move)
    {
        CursorMoveRequested?.Invoke(
            this, new CursorMoveRequestedEventArgs(columnIndex, move, GetVisibleRowCount(columnIndex)));
    }

    /// <summary>Raised when a rename editor is accepted, with the typed name.</summary>
    public event EventHandler<RenameSubmittedEventArgs>? RenameSubmitted;

    /// <summary>Raised when a rename editor is abandoned.</summary>
    public event EventHandler? RenameCancelled;

    /// <summary>
    /// Shows the rename editor over one row, or takes it away when <paramref name="name"/> is
    /// <c>null</c>. Idempotent: called on every render from whatever the state currently says, which
    /// is what keeps the editor a projection rather than something with a life of its own.
    /// </summary>
    public void SyncRenameEditor(int columnIndex, int entryIndex, string? name, string? error)
    {
        if (columnsPanel is null)
        {
            return;
        }

        for (var i = 0; i < columnsPanel.Children.Count; i++)
        {
            if (columnsPanel.Children[i] is not ColumnView view)
            {
                continue;
            }

            if (i == columnIndex && name is not null)
            {
                view.ShowRenameEditor(entryIndex, name, error);
            }
            else if (view.IsRenaming)
            {
                view.HideRenameEditor();
            }
        }
    }

    /// <summary>
    /// Screen rectangle of one row, for placing a context menu raised by the menu key rather than by
    /// a click. <c>null</c> when that column or row is not currently realized.
    /// </summary>
    public Rect? TryGetRowScreenRect(int columnIndex, int entryIndex)
    {
        if (columnsPanel is null || columnIndex < 0 || columnIndex >= columnsPanel.Children.Count)
        {
            return null;
        }

        return (columnsPanel.Children[columnIndex] as ColumnView)?.TryGetRowScreenRect(entryIndex);
    }

    private int GetVisibleRowCount(int columnIndex)
    {
        if (columnsPanel is null || columnIndex < 0 || columnIndex >= columnsPanel.Children.Count)
        {
            return 0;
        }

        return (columnsPanel.Children[columnIndex] as ColumnView)?.GetVisibleRowCount() ?? 0;
    }
}
