using System.Collections.Immutable;
using Zurari.App;
using Zurari.Core;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.App.Tests;

/// <summary>
/// The decisions MainWindow makes about rows and panes, kept here as pure functions so they can be
/// tested without a window: when a row must redraw, who shows the rename editor, what to watch.
/// </summary>
public class StateProjectionRowTests
{
    private static Column Listing(string path, params string[] names)
    {
        var entries = new Entry[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            entries[i] = new Entry(names[i], EntryKind.File);
        }

        return new Column(new Location.RealDirectory(path), [.. entries], Cursor: 0, Load: LoadState.Loaded);
    }

    /// <summary>
    /// A swap puts the other pane in the row. Here both panes hold the very same columns array, so the
    /// pane id is the only thing that tells the row's guard it has to redraw.
    /// </summary>
    [Fact]
    public void The_render_key_names_the_pane_so_a_swap_redraws_the_row()
    {
        var shared = ImmutableArray.Create(Listing(@"C:\a", "x"));
        var state = AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), shared, 0), new Pane(new PaneId(1), shared, 0)],
            FocusedPane = 0,
            NextPaneId = 2,
        };

        var (swapped, _) = Transition.Apply(state, new Msg.SwapPanes());

        Assert.NotEqual(
            StateProjection.RenderKey(state, state.Panes[0]),
            StateProjection.RenderKey(swapped, swapped.Panes[0]));
    }

    [Fact]
    public void The_same_pane_with_nothing_changed_keeps_its_render_key()
    {
        var state = TwoPanes(Listing(@"C:\a", "x"), Listing(@"C:\b", "p"));

        Assert.Equal(StateProjection.RenderKey(state, state.Panes[1]), StateProjection.RenderKey(state, state.Panes[1]));
    }

    [Fact]
    public void A_rename_is_shown_on_its_own_row_and_nowhere_else()
    {
        var state = TwoPanes(Listing(@"C:\a", "x.txt"), Listing(@"C:\b", "p.txt", "q.txt"));
        var (renaming, _) = Transition.Apply(state, new Msg.RenameRequested(new ColumnRef(new PaneId(1), 0)));

        Assert.IsType<StateProjection.RenameView.Hidden>(StateProjection.ProjectRename(renaming, renaming.Panes[0]));
        var shown = Assert.IsType<StateProjection.RenameView.Shown>(
            StateProjection.ProjectRename(renaming, renaming.Panes[1]));
        Assert.Equal(0, shown.ColumnIndex);
        Assert.Equal(0, shown.EntryIndex);
        Assert.Equal("p.txt", shown.Label);
    }

    [Fact]
    public void Without_a_rename_every_row_hides_the_editor()
    {
        var state = TwoPanes(Listing(@"C:\a", "x"), Listing(@"C:\b", "p"));

        Assert.IsType<StateProjection.RenameView.Hidden>(StateProjection.ProjectRename(state, state.Panes[0]));
        Assert.IsType<StateProjection.RenameView.Hidden>(StateProjection.ProjectRename(state, state.Panes[1]));
    }

    [Fact]
    public void A_rename_whose_column_now_shows_another_folder_is_stale()
    {
        var state = TwoPanes(Listing(@"C:\a", "x.txt"), Listing(@"C:\b", "x.txt"));
        var rename = new RenameState(new ColumnRef(new PaneId(1), 0), new Location.RealDirectory(@"C:\elsewhere"), "x.txt");
        var renaming = state with { Rename = rename };

        Assert.Equal(new StateProjection.RenameView.Stale(rename), StateProjection.ProjectRename(renaming, renaming.Panes[1]));

        // The other row has nothing to say about it: only the owner decides.
        Assert.IsType<StateProjection.RenameView.Hidden>(StateProjection.ProjectRename(renaming, renaming.Panes[0]));
    }

    [Fact]
    public void A_rename_whose_row_is_gone_is_stale()
    {
        var state = TwoPanes(Listing(@"C:\a", "x.txt"), Listing(@"C:\b", "other.txt"));
        var rename = new RenameState(new ColumnRef(new PaneId(1), 0), new Location.RealDirectory(@"C:\b"), "x.txt");
        var renaming = state with { Rename = rename };

        Assert.Equal(new StateProjection.RenameView.Stale(rename), StateProjection.ProjectRename(renaming, renaming.Panes[1]));
    }

    [Fact]
    public void A_rename_in_a_column_that_no_longer_exists_is_hidden()
    {
        var state = TwoPanes(Listing(@"C:\a", "x.txt"), Listing(@"C:\b", "x.txt"));
        var rename = new RenameState(new ColumnRef(new PaneId(1), 5), new Location.RealDirectory(@"C:\b"), "x.txt");
        var renaming = state with { Rename = rename };

        Assert.IsType<StateProjection.RenameView.Hidden>(StateProjection.ProjectRename(renaming, renaming.Panes[1]));
    }

    [Fact]
    public void Watched_paths_cover_every_pane_once_and_skip_virtual_locations()
    {
        var drives = new Column(Location.Drives.Instance, [], Load: LoadState.Loaded);
        var bin = new Column(Location.RecycleBin.Instance, [], Load: LoadState.Loaded);
        var state = AppState.Initial with
        {
            Panes =
            [
                new Pane(new PaneId(0), [drives, Listing(@"C:\a", "x"), Listing(@"C:\a\sub", "y")], 0),
                new Pane(new PaneId(1), [Listing(@"C:\a", "x"), bin, Listing(@"D:\b", "z")], 0),
            ],
            NextPaneId = 2,
        };

        Assert.Equal([@"C:\a", @"C:\a\sub", @"D:\b"], StateProjection.WatchedPaths(state));
    }

    [Fact]
    public void Watched_paths_shrink_when_the_split_closes()
    {
        var state = TwoPanes(Listing(@"C:\a", "x"), Listing(@"D:\b", "z"));
        var (closed, _) = Transition.Apply(state, new Msg.ToggleSplit());

        Assert.Equal([@"C:\a"], StateProjection.WatchedPaths(closed));
    }
}
