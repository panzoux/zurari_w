using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>
/// Which pane something belongs to. Stable for the pane's lifetime, unlike its position: swapping the
/// panes moves them, and a result still in flight must land in the pane that asked for it.
/// </summary>
public readonly record struct PaneId(int Value);

/// <summary>A column, named by the pane it is in and its index within that pane.</summary>
public readonly record struct ColumnRef(PaneId Pane, int Index);

/// <summary>
/// One miller-column browser: what it shows and where its cursor is, plus the preview and view
/// options that go with it. Everything that is "where am I looking" lives here; everything shared
/// between panes (jobs, cursor memory, notices) stays on <see cref="AppState"/>.
/// </summary>
/// <param name="Id">See <see cref="PaneId"/>.</param>
/// <param name="Columns">The columns shown, left to right.</param>
/// <param name="FocusedColumn">Index into <paramref name="Columns"/> of the column the cursor keys act on.</param>
public sealed record Pane(PaneId Id, ImmutableArray<Column> Columns, int FocusedColumn)
{
    /// <summary>What this pane's preview shows.</summary>
    public PreviewState Preview { get; init; } = PreviewState.Initial;

    /// <summary>How this pane's listings are ordered and filtered.</summary>
    public ViewOptions View { get; init; } = ViewOptions.Default;

    /// <summary>The <c>/</c> search open in this pane, or <c>null</c>.</summary>
    public SearchState? Search { get; init; }

    /// <summary>The Leap open in this pane, or <c>null</c>.</summary>
    public LeapState? Leap { get; init; }

    /// <summary>
    /// A re-read was asked for while a search or Leap was open, and is owed when it closes. Re-reading
    /// under an open mode would move the rows its matches and filter were computed on (zrr's rule).
    /// </summary>
    public bool NeedsRefresh { get; init; }

    /// <summary>Whether a search or Leap is open in this pane.</summary>
    public bool HasMode => Search is not null || Leap is not null;

    /// <summary>The column the cursor keys act on, or <c>null</c> when <see cref="FocusedColumn"/> is out of range.</summary>
    public Column? FocusedColumnOrNull =>
        FocusedColumn >= 0 && FocusedColumn < Columns.Length ? Columns[FocusedColumn] : null;

    /// <summary>A reference to <see cref="FocusedColumn"/>.</summary>
    public ColumnRef FocusedRef => new(Id, FocusedColumn);

    /// <summary>A reference to column <paramref name="index"/> of this pane.</summary>
    public ColumnRef Ref(int index) => new(Id, index);
}
