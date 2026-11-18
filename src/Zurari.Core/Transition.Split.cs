using System.Collections.Immutable;

namespace Zurari.Core;

public static partial class Transition
{
    private static (AppState, IReadOnlyList<Effect>) ToggleSplit(AppState state, ViewOptions? newPaneView)
    {
        if (state.Panes.Length == 2)
        {
            var kept = state.Focused;
            var dropped = state.Panes[1 - state.FocusedPane];
            var closed = state with { Panes = [kept], FocusedPane = 0, Rename = null, InputMode = InputMode.Normal };

            // A preview still loading for the pane that is going would hold a worker until it times
            // out, and nothing would ever consume its result: say so.
            return dropped.Preview.Kind == PreviewKind.Loading
                ? (closed, [new Effect.CancelPreview(dropped.Id, dropped.Preview.Generation)])
                : (closed, NoEffects);
        }

        var source = state.Focused.FocusedColumnOrNull;
        if (source is null)
        {
            return (state, NoEffects);
        }

        var id = new PaneId(state.NextPaneId);
        var column = new Column(source.Location, Entries: [], Cursor: -1, Load: LoadState.Loading);
        var pane = new Pane(id, [column], 0) { View = newPaneView ?? state.Focused.View };
        var next = state with { Panes = state.Panes.Add(pane), NextPaneId = state.NextPaneId + 1 };
        return (next, [new Effect.ReadDirectory(pane.Ref(0), source.Location)]);
    }

    private static AppState FocusPane(AppState state, PaneId id)
    {
        var index = state.IndexOfPane(id);
        if (index < 0 || index == state.FocusedPane)
        {
            return state;
        }

        return state with { FocusedPane = index, Rename = null, InputMode = InputMode.Normal };
    }

    // A rename in progress belongs to a row on screen; swapping moves panes between rows and syncing
    // replaces the columns it was typed into, so both end it (as FocusPane does).
    private static AppState SwapPanes(AppState state) =>
        state.Panes.Length == 2
            ? state with { Panes = [state.Panes[1], state.Panes[0]], Rename = null, InputMode = InputMode.Normal }
            : state;

    // A selection belongs to the pane where it was made, so the copy carries no marks. A column still
    // loading in the other pane is read again for this one: the pending read answers to the other
    // pane's column, so a copy of it would otherwise stay Loading for ever.
    private static (AppState, IReadOnlyList<Effect>) SyncToOtherPane(AppState state)
    {
        if (state.Panes.Length != 2)
        {
            return (state, NoEffects);
        }

        var mine = state.Focused;
        var other = state.Panes[1 - state.FocusedPane];
        var columns = ImmutableArray.CreateBuilder<Column>(other.Columns.Length);
        var effects = new List<Effect>();
        for (var i = 0; i < other.Columns.Length; i++)
        {
            var column = other.Columns[i];
            columns.Add(column.WithAllEntries(WithoutMarks(column.AllEntries), mine.View));
            if (column.Load == LoadState.Loading)
            {
                effects.Add(new Effect.ReadDirectory(mine.Ref(i), column.Location));
            }
        }

        var synced = state.WithPane(mine with { Columns = columns.MoveToImmutable(), FocusedColumn = other.FocusedColumn });
        return (synced with { Rename = null, InputMode = InputMode.Normal }, effects);
    }

    private static (AppState, IReadOnlyList<Effect>) CopyToOtherPane(AppState state, bool isMove)
    {
        if (state.Panes.Length != 2)
        {
            return (state with { Notice = "分割していません (Ctrl+W で分割)" }, NoEffects);
        }

        var mine = state.Focused;
        if (mine.FocusedColumnOrNull is not { } column)
        {
            return (state, NoEffects);
        }

        // The bin, the drive list: not real folders, so there is nothing here to copy from.
        if (column.Location.FilesystemPath is null)
        {
            return (state with { Notice = CannotTransferHere }, NoEffects);
        }

        var sources = column.PathsToActOn();
        if (sources.IsEmpty)
        {
            return (state, NoEffects);
        }

        var other = state.Panes[1 - state.FocusedPane];
        if (DirectoryUnderCursor(other) is not { } dest)
        {
            return (state with { Notice = CannotTransferHere }, NoEffects);
        }

        foreach (var source in sources)
        {
            if (IsSameOrInside(dest, source))
            {
                return (state with { Notice = "フォルダーをそれ自身の中へコピー・移動することはできません" }, NoEffects);
            }
        }

        // Everything already lives in the destination (normal right after `o`): PasteRequested would
        // drop it all without a word. Same-or-inside was refused above, so only the parent rule is left.
        if (FilterDropSources(dest, sources).IsEmpty)
        {
            return (state with { Notice = "コピー先が元と同じフォルダーです" }, NoEffects);
        }

        // `at` only checks that the source column still exists; the destination is destOverride.
        return PasteRequested(state, mine.FocusedRef, sources, isMove, destOverride: dest);
    }

    private const string CannotTransferHere = "ここにはコピー・移動できません";

    /// <summary>
    /// zrr's ResolvePaneDirectory: the folder under <paramref name="pane"/>'s cursor, else its focused
    /// column's own location. <c>null</c> when that column is not itself a real directory (the drive
    /// list, the recycle bin).
    /// </summary>
    /// <remarks>
    /// Not simply the column's location: the path bar shows the folder under the cursor, and zrr
    /// shipped a bug where copying went one level above what the screen said. But never descend from
    /// a column that is not a real folder: a deleted folder in the bin looks like a directory, and a
    /// drive row would make the drive list a destination, which Ctrl+V refuses too.
    /// </remarks>
    private static string? DirectoryUnderCursor(Pane pane)
    {
        if (pane.FocusedColumnOrNull is not { Location.FilesystemPath: not null } column)
        {
            return null;
        }

        if (ChildOfCursor(column) is { FilesystemPath: { } child })
        {
            return child;
        }

        return column.Location.FilesystemPath;
    }

    private static ImmutableArray<Entry> WithoutMarks(ImmutableArray<Entry> entries)
    {
        var cleared = ImmutableArray.CreateBuilder<Entry>(entries.Length);
        foreach (var entry in entries)
        {
            cleared.Add(entry.IsMarked ? entry with { IsMarked = false } : entry);
        }

        return cleared.MoveToImmutable();
    }
}
