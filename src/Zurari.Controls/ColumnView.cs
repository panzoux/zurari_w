using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace Zurari.Controls;

/// <summary>
/// One column of a <see cref="ColumnBrowser"/>: title header, virtualized entry
/// list and a resize thumb on the right edge. Created and owned by the browser;
/// as dumb as its owner — it renders a <see cref="ColumnVm"/> and forwards input.
/// </summary>
[TemplatePart(Name = ListPartName, Type = typeof(ListBox))]
[TemplatePart(Name = ThumbPartName, Type = typeof(Thumb))]
public sealed class ColumnView : Control
{
    internal const string ListPartName = "PART_List";
    internal const string ThumbPartName = "PART_ResizeThumb";

    /// <summary>Identifies the <see cref="Column"/> dependency property.</summary>
    public static readonly DependencyProperty ColumnProperty = DependencyProperty.Register(
        nameof(Column),
        typeof(ColumnVm),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(null, static (d, _) => ((ColumnView)d).SyncFromColumn()));

    /// <summary>
    /// Identifies the attached <c>IsColumnFocused</c> property. Set on the
    /// <see cref="ColumnView"/> itself (never on individual containers) and
    /// inherited down through the template into <see cref="List"/> and its
    /// item containers, so <c>ItemContainerStyle</c> triggers can render a
    /// stronger cursor-row highlight for the focused column and a weaker one
    /// for the rest — independent of WPF's own keyboard-focus-driven
    /// selection colors, which is not what "focused column" means here.
    /// Public so it can be read back (e.g. from tests) without exposing any
    /// other implementation detail of <see cref="ColumnView"/>.
    /// </summary>
    public static readonly DependencyProperty IsColumnFocusedProperty = DependencyProperty.RegisterAttached(
        "IsColumnFocused",
        typeof(bool),
        typeof(ColumnView),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));

    private bool suppressSelectionChanged;

    static ColumnView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(
            typeof(ColumnView), new FrameworkPropertyMetadata(typeof(ColumnView)));
    }

    internal ColumnView()
    {
    }

    /// <summary>Snapshot of the column to display.</summary>
    public ColumnVm? Column
    {
        get => (ColumnVm?)GetValue(ColumnProperty);
        set => SetValue(ColumnProperty, value);
    }

    /// <summary>Raised while the resize thumb is dragged, with the horizontal delta in DIPs.</summary>
    internal event EventHandler<double>? ResizeDelta;

    /// <summary>Raised when a resize drag completes.</summary>
    internal event EventHandler? ResizeCompleted;

    internal ListBox? List { get; private set; }

    /// <summary>Gets the <see cref="IsColumnFocusedProperty"/> attached value.</summary>
    public static bool GetIsColumnFocused(DependencyObject obj)
    {
        ArgumentNullException.ThrowIfNull(obj);
        return (bool)obj.GetValue(IsColumnFocusedProperty);
    }

    /// <summary>Sets the <see cref="IsColumnFocusedProperty"/> attached value.</summary>
    public static void SetIsColumnFocused(DependencyObject obj, bool value)
    {
        ArgumentNullException.ThrowIfNull(obj);
        obj.SetValue(IsColumnFocusedProperty, value);
    }

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        if (List is not null)
        {
            List.SelectionChanged -= OnListSelectionChanged;
        }

        List = GetTemplateChild(ListPartName) as ListBox;
        if (List is not null)
        {
            List.SelectionChanged += OnListSelectionChanged;
        }

        if (GetTemplateChild(ThumbPartName) is Thumb thumb)
        {
            thumb.DragDelta += (_, e) => ResizeDelta?.Invoke(this, e.HorizontalChange);
            thumb.DragCompleted += (_, _) => ResizeCompleted?.Invoke(this, EventArgs.Empty);
        }

        SyncFromColumn();
    }

    /// <summary>
    /// Applies the current <see cref="Column"/> snapshot to the list: the
    /// cursor row (<see cref="ColumnVm.CursorIndex"/>) becomes the selection
    /// (and is scrolled into view), and the focused-column marker is updated.
    /// Called whenever <see cref="Column"/> changes and once the template
    /// has been applied.
    /// </summary>
    private void SyncFromColumn()
    {
        var column = Column;
        SetIsColumnFocused(this, column?.IsFocused ?? false);

        if (List is null)
        {
            return;
        }

        var cursorIndex = column?.CursorIndex ?? -1;
        suppressSelectionChanged = true;
        try
        {
            List.SelectedIndex = cursorIndex;
        }
        finally
        {
            suppressSelectionChanged = false;
        }

        if (column is not null && cursorIndex >= 0 && cursorIndex < column.Entries.Count)
        {
            List.ScrollIntoView(column.Entries[cursorIndex]);
        }
    }

    /// <summary>
    /// The list's selection exists purely to render the cursor row; it must
    /// never become an independent source of truth. Any selection change not
    /// caused by <see cref="SyncFromColumn"/> (e.g. a stray click, before
    /// input wiring lands in a later task) is reverted back to the VM cursor.
    /// </summary>
    private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressSelectionChanged || List is null)
        {
            return;
        }

        suppressSelectionChanged = true;
        try
        {
            List.SelectedIndex = Column?.CursorIndex ?? -1;
        }
        finally
        {
            suppressSelectionChanged = false;
        }
    }
}
