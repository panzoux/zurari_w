using System.Windows;
using System.Windows.Controls;

namespace Zurari.Controls;

/// <summary>
/// Picks the row layout for an <see cref="EntryVm"/>: a section label, or an ordinary entry.
/// </summary>
/// <remarks>
/// A selector rather than one template with visibility triggers, because the two layouts share
/// almost nothing - a header has no icon, no chevron and no size column, and sits on different
/// spacing. Under <c>VirtualizationMode="Recycling"</c> the panel re-uses containers as rows scroll,
/// and swapping a container between templates is exactly what a selector is for; triggers would
/// leave both layouts realized in every row.
/// </remarks>
public sealed class EntryTemplateSelector : DataTemplateSelector
{
    /// <summary>Layout for <see cref="EntryKind.Header"/>.</summary>
    public DataTemplate? HeaderTemplate { get; set; }

    /// <summary>Layout for every other kind.</summary>
    public DataTemplate? EntryTemplate { get; set; }

    /// <inheritdoc />
    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is EntryVm { Kind: EntryKind.Header } ? HeaderTemplate : EntryTemplate;
}
