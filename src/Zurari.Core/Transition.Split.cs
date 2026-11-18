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

    private static AppState SyncToOtherPane(AppState state)
    {
        if (state.Panes.Length != 2)
        {
            return state;
        }

        var mine = state.Focused;
        var other = state.Panes[1 - state.FocusedPane];
        var columns = ImmutableArray.CreateBuilder<Column>(other.Columns.Length);
        foreach (var column in other.Columns)
        {
            columns.Add(column.WithView(mine.View));
        }

        return state.WithPane(mine with { Columns = columns.MoveToImmutable(), FocusedColumn = other.FocusedColumn })
            with
        { Rename = null };
    }
}
