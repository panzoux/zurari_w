using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Zurari.Core;

/// <summary>
/// <c>/</c> search and Leap (7b). Both act on the focused pane only: a mode is closed - as a confirm -
/// the moment its pane loses focus, so at most one pane ever has one open.
/// </summary>
public static partial class Transition
{
    /// <summary>
    /// How many single-folder levels one message may walk through. A junction that leads back to its
    /// own ancestor would otherwise be walked into for ever.
    /// </summary>
    private const int MaxAutoEnterDepth = 64;

    private static bool AnyModeOpen(AppState state)
    {
        foreach (var pane in state.Panes)
        {
            if (pane.HasMode)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Whether a search or Leap may open: not while renaming (the keys belong to the name being typed),
    /// not on top of another mode, and only where there is a column to act on.
    /// </summary>
    private static bool CanOpenMode(AppState state) =>
        state.Rename is null && !AnyModeOpen(state) && state.Focused.FocusedColumnOrNull is not null;

    /// <summary>Asks for the migemo expansion of every word in <paramref name="query"/> not asked for yet.</summary>
    private static NameQuery Requesting(NameQuery query, List<Effect> effects)
    {
        var words = query.WordsToExpand();
        foreach (var word in words)
        {
            effects.Add(new Effect.ExpandMigemo(word));
        }

        return query.WithPending(words);
    }

    /// <summary>
    /// After a mode closes: the re-read that was held back while it was open (<see cref="Pane.NeedsRefresh"/>)
    /// is owed now, for every column of the pane - zrr's <c>RefreshPaneNowAsync</c>.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) Settle(AppState state, PaneId id, IReadOnlyList<Effect> effects)
    {
        if (state.PaneOf(id) is not { NeedsRefresh: true } pane || pane.HasMode)
        {
            return (state, effects);
        }

        var columns = pane.Columns;
        var reads = new List<Effect>(effects);
        for (var i = 0; i < columns.Length; i++)
        {
            columns = columns.SetItem(i, columns[i] with { Load = LoadState.Loading });
            reads.Add(new Effect.ReadDirectory(pane.Ref(i), columns[i].Location));
        }

        return (state.WithPane(pane with { Columns = columns, NeedsRefresh = false }), reads);
    }

    // ---- / search ------------------------------------------------------------------------------

    private static (AppState, IReadOnlyList<Effect>) BeginSearch(AppState state)
    {
        if (!CanOpenMode(state))
        {
            return (state, NoEffects);
        }

        var pane = state.Focused;
        var column = pane.FocusedColumnOrNull!;
        var search = new SearchState(pane.FocusedColumn, column.CursorName, NameQuery.Empty)
        {
            MatchedEntries = column.Entries,
        };
        return (LeaveSortMode(state).WithPane(pane with { Search = search }), NoEffects);
    }

    private static (AppState, IReadOnlyList<Effect>) ChangeSearch(AppState state, Func<NameQuery, NameQuery> change)
    {
        var pane = state.Focused;
        if (pane.Search is not { } search)
        {
            return (state, NoEffects);
        }

        var query = change(search.Query);
        if (ReferenceEquals(query, search.Query))
        {
            return (state, NoEffects);
        }

        var effects = new List<Effect>();
        query = Requesting(query, effects);
        return (Research(state, pane, search with { Query = query }, moveCursor: true), effects);
    }

    /// <summary>
    /// Computes <paramref name="search"/>'s matches again and stores it on <paramref name="pane"/>.
    /// </summary>
    /// <param name="state">The state <paramref name="pane"/> is current in.</param>
    /// <param name="pane">The pane searched.</param>
    /// <param name="search">The search, with the query to match.</param>
    /// <param name="moveCursor">
    /// The query changed: go to the first match at or after where the search started, wrapping to the
    /// first (zrr's <c>FindNearestMatchIndex</c>), or back to where it started when the query is now
    /// empty. Otherwise only the listing changed, and the cursor stays where it is.
    /// </param>
    private static AppState Research(AppState state, Pane pane, SearchState search, bool moveCursor)
    {
        var column = pane.Columns[search.ColumnIndex];
        var matches = MatchesOf(column.Entries, search.Query);
        var anchor = search.AnchorName is { } name ? IndexOfName(column.Entries, name) : -1;
        var cursor = column.Cursor;
        int matchIndex;
        if (matches.IsEmpty)
        {
            matchIndex = -1;
            if (moveCursor && search.Query.IsEmpty && anchor >= 0)
            {
                cursor = anchor;
            }
        }
        else
        {
            matchIndex = FirstAtOrAfter(matches, moveCursor ? Math.Max(anchor, 0) : cursor);
            if (moveCursor)
            {
                cursor = matches[matchIndex];
            }
        }

        var updated = search with { Matches = matches, MatchIndex = matchIndex, MatchedEntries = column.Entries };
        var columns = cursor == column.Cursor
            ? pane.Columns
            : pane.Columns.SetItem(search.ColumnIndex, column with { Cursor = cursor });
        return state.WithPane(pane with { Search = updated, Columns = columns });
    }

    private static ImmutableArray<int> MatchesOf(ImmutableArray<Entry> entries, NameQuery query)
    {
        if (query.IsEmpty)
        {
            return [];
        }

        var matches = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Kind != EntryKind.Header && query.Match(entries[i].Label) != MatchTier.None)
            {
                matches.Add(i);
            }
        }

        return matches.ToImmutable();
    }

    /// <summary>Index into <paramref name="matches"/> of the first at or after <paramref name="from"/>, else 0.</summary>
    private static int FirstAtOrAfter(ImmutableArray<int> matches, int from)
    {
        for (var i = 0; i < matches.Length; i++)
        {
            if (matches[i] >= from)
            {
                return i;
            }
        }

        return 0;
    }

    private static AppState SearchStep(AppState state, int delta)
    {
        var pane = state.Focused;
        if (pane.Search is not { Matches.IsEmpty: false } search || delta == 0)
        {
            return state;
        }

        var count = search.Matches.Length;
        var index = search.MatchIndex < 0
            ? (delta > 0 ? 0 : count - 1)
            : (((search.MatchIndex + (delta % count)) % count) + count) % count;
        var column = pane.Columns[search.ColumnIndex];
        return state.WithPane(pane with
        {
            Search = search with { MatchIndex = index },
            Columns = pane.Columns.SetItem(search.ColumnIndex, column with { Cursor = search.Matches[index] }),
        });
    }

    private static (AppState, IReadOnlyList<Effect>) EndSearch(
        AppState state, PaneId id, bool restore, IReadOnlyList<Effect> effects)
    {
        if (state.PaneOf(id) is not { Search: { } search } pane)
        {
            return (state, effects);
        }

        var columns = pane.Columns;
        if (restore && search.AnchorName is { } name && search.ColumnIndex < columns.Length)
        {
            var column = columns[search.ColumnIndex];
            var anchor = IndexOfName(column.Entries, name);
            if (anchor >= 0 && anchor != column.Cursor)
            {
                columns = columns.SetItem(search.ColumnIndex, column with { Cursor = anchor });
            }
        }

        return Settle(state.WithPane(pane with { Search = null, Columns = columns }), id, effects);
    }

    // ---- Leap ----------------------------------------------------------------------------------

    private static (AppState, IReadOnlyList<Effect>) BeginLeap(AppState state)
    {
        if (!CanOpenMode(state))
        {
            return (state, NoEffects);
        }

        var pane = state.Focused;
        var column = pane.FocusedColumnOrNull!;
        var leap = new LeapState(pane.FocusedColumn, column.CursorName, []);
        var columns = pane.Columns.SetItem(
            pane.FocusedColumn, column.WithFilter(NameQuery.Empty, pane.View, cursorToBest: false));
        return (LeaveSortMode(state).WithPane(pane with { Leap = leap, Columns = columns }), NoEffects);
    }

    private static (AppState, IReadOnlyList<Effect>) LeapTextChanged(AppState state, string text)
    {
        var pane = state.Focused;
        if (pane.Leap is not { } leap || pane.FocusedColumnOrNull is not { Filter: { } filter } column)
        {
            return (state, NoEffects);
        }

        var query = filter.WithText(text);
        if (ReferenceEquals(query, filter))
        {
            return (state, NoEffects);
        }

        var effects = new List<Effect>();
        query = Requesting(query, effects);
        var columns = pane.Columns.SetItem(pane.FocusedColumn, column.WithFilter(query, pane.View, cursorToBest: true));
        return (state.WithPane(pane with { Columns = columns, Leap = leap with { AutoEnterArmed = true } }), effects);
    }

    private static (AppState, IReadOnlyList<Effect>) LeapStepIn(AppState state)
    {
        var pane = state.Focused;
        if (pane.Leap is not { } leap
            || pane.FocusedColumnOrNull is not { } column
            || column.Cursor < 0
            || column.Cursor >= column.Entries.Length
            || column.Entries[column.Cursor].Kind is not (EntryKind.Directory or EntryKind.Drive))
        {
            return (state, NoEffects);
        }

        var focused = pane.FocusedColumn;
        var parent = column.WithFilter(null, pane.View, cursorToBest: false);
        var withParent = state.WithPane(pane with { Columns = pane.Columns.SetItem(focused, parent) });
        var (entered, effects) = EnterDirectory(withParent, pane.FocusedRef, parent.Cursor, focusChild: true);
        if (entered.PaneOf(pane.Id) is not { } enteredPane || enteredPane.FocusedColumn != focused + 1)
        {
            return (state, NoEffects);
        }

        var child = enteredPane.Columns[focused + 1].WithFilter(NameQuery.Empty, enteredPane.View, cursorToBest: false);
        return (
            entered.WithPane(enteredPane with
            {
                Columns = enteredPane.Columns.SetItem(focused + 1, child),
                Leap = leap with { Trail = leap.Trail.Add(column.Filter ?? NameQuery.Empty), AutoEnterArmed = true },
            }),
            effects);
    }

    private static AppState LeapStepOut(AppState state)
    {
        var pane = state.Focused;
        var focused = pane.FocusedColumn;
        if (pane.Leap is not { Trail.IsEmpty: false } leap || focused <= leap.RootIndex || focused >= pane.Columns.Length)
        {
            return state;
        }

        // The folder the cursor is on in the parent is the one walked into, and it matched the filter
        // that led there, so the cursor can stay put - and with it the columns to its right.
        var columns = pane.Columns
            .SetItem(focused, pane.Columns[focused].WithFilter(null, pane.View, cursorToBest: false))
            .SetItem(focused - 1, pane.Columns[focused - 1].WithFilter(leap.Trail[^1], pane.View, cursorToBest: false));
        return state.WithPane(pane with
        {
            Columns = columns,
            FocusedColumn = focused - 1,
            Leap = leap with { Trail = leap.Trail.RemoveAt(leap.Trail.Length - 1), AutoEnterArmed = false },
        });
    }

    private static AppState LeapToRoot(AppState state)
    {
        var pane = state.Focused;
        if (pane.Leap is not { } leap || leap.RootIndex >= pane.Columns.Length)
        {
            return state;
        }

        if (pane.FocusedColumn == leap.RootIndex && pane.Columns[leap.RootIndex].Filter is { IsEmpty: true })
        {
            return state;
        }

        var columns = WithoutFilters(pane.Columns, pane.View);
        columns = columns.SetItem(
            leap.RootIndex, columns[leap.RootIndex].WithFilter(NameQuery.Empty, pane.View, cursorToBest: false));
        return state.WithPane(pane with
        {
            Columns = columns,
            FocusedColumn = leap.RootIndex,
            Leap = leap with { Trail = [], AutoEnterArmed = false },
        });
    }

    private static (AppState, IReadOnlyList<Effect>) EndLeap(
        AppState state, PaneId id, bool restore, IReadOnlyList<Effect> effects)
    {
        if (state.PaneOf(id) is not { Leap: { } leap } pane)
        {
            return (state, effects);
        }

        var columns = WithoutFilters(pane.Columns, pane.View);
        var focused = pane.FocusedColumn;
        if (restore && leap.RootIndex < columns.Length)
        {
            focused = leap.RootIndex;
            var root = columns[focused];
            var cursor = leap.RootCursorName is { } name ? IndexOfName(root.Entries, name) : -1;
            if (cursor >= 0 && cursor != root.Cursor)
            {
                columns = columns.SetItem(focused, root with { Cursor = cursor });
            }
        }

        return Settle(state.WithPane(pane with { Columns = columns, FocusedColumn = focused, Leap = null }), id, effects);
    }

    private static ImmutableArray<Column> WithoutFilters(ImmutableArray<Column> columns, ViewOptions view)
    {
        var result = columns;
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i].Filter is not null)
            {
                result = result.SetItem(i, columns[i].WithFilter(null, view, cursorToBest: false));
            }
        }

        return result;
    }

    /// <summary>
    /// Walks into the only match when it is a folder, for as long as that keeps being true of columns
    /// already loaded. A column still loading leaves the Leap armed; its <see cref="Msg.DirectoryLoaded"/>
    /// comes back through here.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) TryAutoEnter(AppState state, PaneId id, IReadOnlyList<Effect> effects)
    {
        for (var step = 0; step < MaxAutoEnterDepth; step++)
        {
            if (state.Focused.Id != id
                || state.Focused is not { Leap: { AutoEnterArmed: true } leap } pane
                || pane.FocusedColumnOrNull is not { Filter: { } filter } column)
            {
                return (state, effects);
            }

            if (column.Load == LoadState.Loading)
            {
                return (state, effects);
            }

            // A migemo pattern still on its way could add matches, unless the letters already matched
            // something - then migemo-only rows are not shown anyway.
            if (column.Load == LoadState.Loaded && !filter.Pending.IsEmpty && !HasLiteralMatch(column.Entries, filter))
            {
                return (state, effects);
            }

            state = state.WithPane(pane with { Leap = leap with { AutoEnterArmed = false } });
            var only = column.Load == LoadState.Loaded && leap.Trail.Length < MaxAutoEnterDepth
                ? OnlyRow(column.Entries)
                : -1;
            if (only < 0 || column.Entries[only].Kind is not (EntryKind.Directory or EntryKind.Drive))
            {
                return (state, effects);
            }

            if (only != column.Cursor)
            {
                state = state.WithColumn(state.Focused.FocusedRef, column with { Cursor = only });
            }

            var (entered, more) = LeapStepIn(state);
            if (ReferenceEquals(entered, state))
            {
                return (state, effects);
            }

            state = entered;
            effects = Append(effects, [.. more]);
        }

        return (state, effects);
    }

    private static bool HasLiteralMatch(ImmutableArray<Entry> entries, NameQuery filter)
    {
        foreach (var entry in entries)
        {
            if (entry.Kind != EntryKind.Header && filter.Match(entry.Label) is MatchTier.Prefix or MatchTier.Substring)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Index of the one row that is not a section header, or -1 when there is not exactly one.</summary>
    private static int OnlyRow(ImmutableArray<Entry> entries)
    {
        var only = -1;
        for (var i = 0; i < entries.Length; i++)
        {
            if (entries[i].Kind == EntryKind.Header)
            {
                continue;
            }

            if (only >= 0)
            {
                return -1;
            }

            only = i;
        }

        return only;
    }

    // ---- shared ----------------------------------------------------------------------------------

    private static (AppState, IReadOnlyList<Effect>) MigemoExpanded(AppState state, string word, Regex? pattern)
    {
        foreach (var id in PaneIds(state))
        {
            var pane = state.PaneOf(id)!;
            if (pane.Search is { } search)
            {
                var query = search.Query.WithPattern(word, pattern);
                if (!ReferenceEquals(query, search.Query))
                {
                    // Late matches join the list, but a cursor the user already moved stays put.
                    state = Research(state, pane, search with { Query = query }, moveCursor: search.Matches.IsEmpty);
                }
            }
            else if (pane.Leap is { } leap)
            {
                var trail = leap.Trail;
                for (var i = 0; i < trail.Length; i++)
                {
                    trail = trail.SetItem(i, trail[i].WithPattern(word, pattern));
                }

                var columns = pane.Columns;
                if (pane.FocusedColumnOrNull is { Filter: { } filter } column)
                {
                    var query = filter.WithPattern(word, pattern);
                    if (!ReferenceEquals(query, filter))
                    {
                        columns = columns.SetItem(pane.FocusedColumn, column.WithFilter(query, pane.View, cursorToBest: false));
                    }
                }

                if (columns != pane.Columns || trail != leap.Trail)
                {
                    state = state.WithPane(pane with { Columns = columns, Leap = leap with { Trail = trail } });
                }
            }
        }

        return (state, NoEffects);
    }

    private static PaneId[] PaneIds(AppState state)
    {
        var ids = new PaneId[state.Panes.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            ids[i] = state.Panes[i].Id;
        }

        return ids;
    }

    /// <summary>
    /// Post-step after every message, before the per-pane reconciles: closes a mode whose pane lost
    /// focus or whose column moved out from under it (as a confirm), recomputes search matches over a
    /// listing that changed, and lets an armed Leap walk into a lone folder.
    /// </summary>
    /// <remarks>
    /// A Leap moves the focused column itself, so "focus moved" for it means the focused column has
    /// no filter: every Leap step puts one there, and nothing else does.
    /// </remarks>
    private static (AppState, IReadOnlyList<Effect>) ReconcileModes(AppState state, IReadOnlyList<Effect> effects)
    {
        if (!AnyModeOpen(state))
        {
            return (state, effects);
        }

        foreach (var id in PaneIds(state))
        {
            var pane = state.PaneOf(id)!;
            var focused = state.Focused.Id == id;
            if (pane.Search is { } search)
            {
                if (!focused || pane.FocusedColumn != search.ColumnIndex || search.ColumnIndex >= pane.Columns.Length)
                {
                    (state, effects) = EndSearch(state, id, restore: false, effects);
                }
                else if (pane.Columns[search.ColumnIndex].Entries != search.MatchedEntries)
                {
                    state = Research(state, pane, search, moveCursor: false);
                }
            }
            else if (pane.Leap is { } leap)
            {
                if (!focused
                    || pane.FocusedColumnOrNull?.Filter is null
                    || pane.FocusedColumn - leap.RootIndex != leap.Trail.Length)
                {
                    (state, effects) = EndLeap(state, id, restore: false, effects);
                }
                else if (leap.AutoEnterArmed)
                {
                    (state, effects) = TryAutoEnter(state, id, effects);
                }
            }
        }

        return (state, effects);
    }
}
