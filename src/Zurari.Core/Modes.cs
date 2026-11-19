using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>
/// A <c>/</c> search: matches are highlighted in one column, nothing is hidden.
/// </summary>
/// <param name="ColumnIndex">The column searched - the focused one when the search opened.</param>
/// <param name="AnchorName">The entry the cursor was on when it opened; Esc goes back to it.</param>
/// <param name="Query">What has been typed.</param>
public sealed record SearchState(int ColumnIndex, string? AnchorName, NameQuery Query)
{
    /// <summary>Indices into the column's <see cref="Column.Entries"/> that match, ascending.</summary>
    public ImmutableArray<int> Matches { get; init; } = [];

    /// <summary>Index into <see cref="Matches"/> of the current match, or -1 when there is none.</summary>
    public int MatchIndex { get; init; } = -1;

    /// <summary>
    /// The listing <see cref="Matches"/> were computed on. When the column's entries are no longer
    /// this instance (a read that was already in flight landed), the matches are computed again.
    /// </summary>
    public ImmutableArray<Entry> MatchedEntries { get; init; } = [];
}

/// <summary>
/// A Leap: the focused column is narrowed to what is typed (its <see cref="Column.Filter"/>), and
/// walking into a folder starts a new filter there.
/// </summary>
/// <param name="RootIndex">The column the Leap started in; Esc and Ctrl+K go back to it.</param>
/// <param name="RootCursorName">The entry the cursor was on there when it started.</param>
/// <param name="Trail">
/// The filter that led into each folder walked into, outermost first - the <c>mapm/</c> of
/// <c>mapm/we</c>. Stepping out puts the last one back on the parent column, migemo patterns and all.
/// </param>
public sealed record LeapState(int RootIndex, string? RootCursorName, ImmutableArray<NameQuery> Trail)
{
    /// <summary>
    /// Whether a folder that is the only match should be walked into once the focused column has
    /// loaded and its matches are settled. Set by typing and by walking in, so a chain of
    /// single-folder levels unrolls one <see cref="Msg.DirectoryLoaded"/> at a time - no timers.
    /// </summary>
    public bool AutoEnterArmed { get; init; }
}
