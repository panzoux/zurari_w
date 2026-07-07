using System.Windows;
using System.Windows.Controls;

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
public sealed class ColumnBrowser : Control
{
    /// <summary>Identifies the <see cref="Columns"/> dependency property.</summary>
    public static readonly DependencyProperty ColumnsProperty = DependencyProperty.Register(
        nameof(Columns),
        typeof(IReadOnlyList<ColumnVm>),
        typeof(ColumnBrowser),
        new FrameworkPropertyMetadata(null));

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
}
