using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class SplitTests
{
    private static Column Files(string path, params string[] names)
    {
        var entries = ImmutableArray.CreateBuilder<Entry>(names.Length);
        foreach (var name in names)
        {
            entries.Add(new Entry(name, EntryKind.File));
        }

        return new Column(
            new Location.RealDirectory(path),
            entries.MoveToImmutable(),
            Cursor: names.Length > 0 ? 0 : -1,
            Load: LoadState.Loaded);
    }

    private static AppState Browsing() =>
        AppState.SinglePane([Files(@"C:\a", "x.txt", "y.txt")]);

    [Fact]
    public void Opening_adds_a_bottom_pane_at_the_focused_location_and_keeps_focus()
    {
        var state = Browsing();

        var (next, effects) = Transition.Apply(state, new Msg.ToggleSplit());

        Assert.Equal(2, next.Panes.Length);
        Assert.Equal(0, next.FocusedPane);
        Assert.Equal(new PaneId(1), next.Panes[1].Id);
        Assert.Equal(2, next.NextPaneId);
        Assert.Equal(new Location.RealDirectory(@"C:\a"), next.Panes[1].Columns[0].Location);
        Assert.Equal(LoadState.Loading, next.Panes[1].Columns[0].Load);
        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(new ColumnRef(new PaneId(1), 0), read.At);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Opening_copies_the_focused_view_unless_one_is_given()
    {
        var state = Browsing();
        state = state.WithFocused(p => p with { View = ViewOptions.Default with { ShowHidden = true } });
        var given = ViewOptions.Default with { Sort = SortOrder.Default with { Mode = SortMode.Size } };

        var (copied, _) = Transition.Apply(state, new Msg.ToggleSplit());
        var (restored, _) = Transition.Apply(state, new Msg.ToggleSplit(given));

        Assert.True(copied.Panes[1].View.ShowHidden);
        Assert.Equal(SortMode.Size, restored.Panes[1].View.Sort.Mode);
    }

    [Fact]
    public void Closing_keeps_only_the_focused_pane()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (focusedBottom, _) = Transition.Apply(split, new Msg.FocusPane(new PaneId(1)));

        var (closed, _) = Transition.Apply(focusedBottom, new Msg.ToggleSplit());

        Assert.Single(closed.Panes);
        Assert.Equal(new PaneId(1), closed.Panes[0].Id);
        Assert.Equal(0, closed.FocusedPane);
        Assert.Empty(closed.CheckInvariants());
    }

    [Fact]
    public void A_result_for_the_closed_pane_is_ignored()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (closed, _) = Transition.Apply(split, new Msg.ToggleSplit());

        var (next, effects) = Transition.Apply(
            closed, new Msg.DirectoryLoaded(new ColumnRef(new PaneId(1), 0), new Location.RealDirectory(@"C:\a"), []));

        Assert.Same(closed, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Reopening_gets_a_fresh_pane_id()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (closed, _) = Transition.Apply(split, new Msg.ToggleSplit());
        var (reopened, _) = Transition.Apply(closed, new Msg.ToggleSplit());

        Assert.Equal(new PaneId(2), reopened.Panes[1].Id);
    }

    [Fact]
    public void FocusPane_moves_focus_and_ignores_unknown_panes()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());

        var (bottom, _) = Transition.Apply(split, new Msg.FocusPane(new PaneId(1)));
        var (ignored, _) = Transition.Apply(split, new Msg.FocusPane(new PaneId(9)));

        Assert.Equal(1, bottom.FocusedPane);
        Assert.Same(split, ignored);
    }

    [Fact]
    public void Focusing_the_other_pane_ends_sort_mode_and_a_rename()
    {
        var (split, _) = Transition.Apply(Browsing(), new Msg.ToggleSplit());
        var (sorting, _) = Transition.Apply(split, new Msg.EnterSortMode());

        var (moved, _) = Transition.Apply(sorting, new Msg.FocusPane(new PaneId(1)));

        Assert.Equal(InputMode.Normal, moved.InputMode);
        Assert.Null(moved.Rename);
    }
}
