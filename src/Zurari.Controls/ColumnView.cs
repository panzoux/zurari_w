using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace Zurari.Controls;

/// <summary>
/// Raw pointer-press data for one entry row, before <see cref="ColumnBrowser"/> adds
/// the column index (which this view does not know about itself).
/// </summary>
internal readonly record struct EntryPointerPressInfo(int EntryIndex, ModifierKeys Modifiers, MouseButton Button);

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

    /// <summary>
    /// Raised on a mouse-button press over an entry row. <see cref="ColumnBrowser"/>
    /// translates this into the public <see cref="EntryPointerPressedEventArgs"/>
    /// (adding the column index, which this view does not know about itself).
    /// </summary>
    internal event EventHandler<EntryPointerPressInfo>? EntryPointerPressed;

    /// <summary>Raised on a double-click over an entry row, carrying the entry index.</summary>
    internal event EventHandler<int>? EntryActivationRequested;

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
            List.PreviewMouseDown -= OnListPreviewMouseDown;
        }

        List = GetTemplateChild(ListPartName) as ListBox;
        if (List is not null)
        {
            List.SelectionChanged += OnListSelectionChanged;
            List.PreviewMouseDown += OnListPreviewMouseDown;
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

    /// <summary>
    /// Translates a raw mouse-button press on an entry row into
    /// <see cref="EntryPointerPressed"/> (and <see cref="EntryActivationRequested"/> on a
    /// double-click). Selection here is purely visual and reverted by
    /// <see cref="OnListSelectionChanged"/> — this view never decides where the cursor goes.
    /// </summary>
    private void OnListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var entryIndex = FindEntryIndex(e.OriginalSource as DependencyObject);
        if (entryIndex is null)
        {
            return;
        }

        EntryPointerPressed?.Invoke(
            this, new EntryPointerPressInfo(entryIndex.Value, Keyboard.Modifiers, e.ChangedButton));

        if (e.ClickCount == 2 && e.ChangedButton == MouseButton.Left)
        {
            EntryActivationRequested?.Invoke(this, entryIndex.Value);
        }
    }

    /// <summary>Walks up from a click's <c>OriginalSource</c> to the containing <see cref="ListBoxItem"/>.</summary>
    private int? FindEntryIndex(DependencyObject? source)
    {
        if (List is null)
        {
            return null;
        }

        while (source is not null && source is not ListBoxItem)
        {
            source = source is Visual visual
                ? VisualTreeHelper.GetParent(visual)
                : LogicalTreeHelper.GetParent(source);
        }

        if (source is not ListBoxItem item)
        {
            return null;
        }

        var index = List.ItemContainerGenerator.IndexFromContainer(item);
        return index >= 0 ? index : null;
    }

    /// <summary>
    /// Estimates how many rows currently fit in the list's viewport, for the
    /// PageUp/PageDown hint in <see cref="CursorMoveRequestedEventArgs"/>. Returns 0 when
    /// it cannot be determined (no items realized yet, or the viewport has not been measured).
    /// </summary>
    internal int GetVisibleRowCount()
    {
        if (List is null || List.Items.Count == 0)
        {
            return 0;
        }

        if (FindVisualChild<ScrollViewer>(List) is not { ViewportHeight: > 0 } scrollViewer)
        {
            return 0;
        }

        if (List.ItemContainerGenerator.ContainerFromIndex(0) is not FrameworkElement { ActualHeight: > 0 } container)
        {
            return 0;
        }

        return Math.Max(1, (int)(scrollViewer.ViewportHeight / container.ActualHeight));
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            if (FindVisualChild<T>(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }
}
