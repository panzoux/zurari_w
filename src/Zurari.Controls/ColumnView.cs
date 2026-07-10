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
        new FrameworkPropertyMetadata(null));

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

    /// <inheritdoc />
    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        List = GetTemplateChild(ListPartName) as ListBox;
        if (GetTemplateChild(ThumbPartName) is Thumb thumb)
        {
            thumb.DragDelta += (_, e) => ResizeDelta?.Invoke(this, e.HorizontalChange);
            thumb.DragCompleted += (_, _) => ResizeCompleted?.Invoke(this, EventArgs.Empty);
        }
    }
}
