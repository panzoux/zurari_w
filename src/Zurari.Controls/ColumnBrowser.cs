using System.Windows;
using System.Windows.Controls;
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
}
