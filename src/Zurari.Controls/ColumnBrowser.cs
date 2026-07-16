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
public sealed class ColumnBrowser : Control
{
    internal const string PanelPartName = "PART_ColumnsPanel";

    private const double DefaultColumnWidth = 240;
    private const double MinColumnWidth = 120;

    /// <summary>Identifies the <see cref="Columns"/> dependency property.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(IReadOnlyList<ColumnVm>),
        typeof(ColumnBrowser),
        new FrameworkPropertyMetadata(null, static (d, _) => ((ColumnBrowser)d).RebuildColumns()));

    private readonly List<double> columnWidths = [];

    private StackPanel? columnsPanel;

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

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        columnsPanel = GetTemplateChild(PanelPartName) as StackPanel;
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
            view.ResizeDelta += OnColumnResizeDelta;
            view.ResizeCompleted += OnColumnResizeCompleted;
            view.EntryPointerPressed += OnColumnEntryPointerPressed;
            view.EntryActivationRequested += OnColumnEntryActivationRequested;
            view.EntryDragRequested += OnColumnEntryDragRequested;
            view.FileDropRequested += OnColumnFileDropRequested;
            children.Add(view);
        }

        while (columnWidths.Count < columns.Count)
        {
            columnWidths.Add(DefaultColumnWidth);
        }

        ColumnView? focusedView = null;
        for (var i = 0; i < columns.Count; i++)
        {
            var view = (ColumnView)children[i];
            view.Column = columns[i];
            view.Width = columnWidths[i];
            if (columns[i].IsFocused)
            {
                focusedView = view;
            }
        }

        // Bring the focused column into the horizontal viewport. BringIntoView walks
        // up through PART_HorizontalScroll (or any ancestor ScrollViewer) on its own,
        // so no direct reference to the scroll viewer template part is needed here.
        // Deferred to the Loaded priority so layout has assigned the view its
        // final size before the scroll computation runs (a freshly (re)sized
        // column has no arranged bounds yet at the point Columns is assigned).
        if (focusedView is not null)
        {
            var target = focusedView;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => target.BringIntoView()));
        }
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

        FileDropRequested?.Invoke(this, new FileDropRequestedEventArgs(index, info.Paths, info.IsMove));
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

    private int GetVisibleRowCount(int columnIndex)
    {
        if (columnsPanel is null || columnIndex < 0 || columnIndex >= columnsPanel.Children.Count)
        {
            return 0;
        }

        return (columnsPanel.Children[columnIndex] as ColumnView)?.GetVisibleRowCount() ?? 0;
    }
}
