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

    private static AppState Split() => Transition.Apply(Browsing(), new Msg.ToggleSplit()).State;

    private static (AppState State, IReadOnlyList<Effect> Effects) StartRename(AppState state)
    {
        var result = Transition.Apply(state, new Msg.RenameRequested(state.Focused.FocusedRef));
        Assert.NotNull(result.State.Rename);
        return result;
    }

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
    public void Closing_ends_sort_mode_and_a_rename()
    {
        var (renaming, _) = StartRename(Split());
        var (sorting, _) = Transition.Apply(Split(), new Msg.EnterSortMode());

        var (closedRenaming, _) = Transition.Apply(renaming, new Msg.ToggleSplit());
        var (closedSorting, _) = Transition.Apply(sorting, new Msg.ToggleSplit());

        Assert.Null(closedRenaming.Rename);
        Assert.Equal(InputMode.Normal, closedSorting.InputMode);
    }

    [Fact]
    public void Closing_cancels_the_preview_still_loading_in_the_closed_pane()
    {
        var (focusedBottom, _) = Transition.Apply(Split(), new Msg.FocusPane(new PaneId(1)));
        var bottomAt = new ColumnRef(new PaneId(1), 0);
        var (loaded, _) = Transition.Apply(
            focusedBottom,
            new Msg.DirectoryLoaded(bottomAt, new Location.RealDirectory(@"C:\a"), [new Entry("x.txt", EntryKind.File), new Entry("y.txt", EntryKind.File)]));
        var (moved, _) = Transition.Apply(loaded, new Msg.CursorDown(bottomAt));
        var (focusedTop, _) = Transition.Apply(moved, new Msg.FocusPane(new PaneId(0)));
        var bottom = focusedTop.PaneOf(new PaneId(1))!;
        Assert.Equal(PreviewKind.Loading, bottom.Preview.Kind);

        var (closed, effects) = Transition.Apply(focusedTop, new Msg.ToggleSplit());

        var cancel = Assert.Single(TestEffects.Of<Effect.CancelPreview>(effects));
        Assert.Equal(new PaneId(1), cancel.Pane);
        Assert.Equal(bottom.Preview.Generation, cancel.Generation);
        Assert.Single(closed.Panes);
    }

    [Fact]
    public void Closing_with_nothing_loading_cancels_nothing()
    {
        var (_, effects) = Transition.Apply(Split(), new Msg.ToggleSplit());

        Assert.Empty(effects);
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
        var split = Split();
        var (sorting, _) = Transition.Apply(split, new Msg.EnterSortMode());
        var (renaming, _) = StartRename(split);

        var (movedFromSort, _) = Transition.Apply(sorting, new Msg.FocusPane(new PaneId(1)));
        var (movedFromRename, _) = Transition.Apply(renaming, new Msg.FocusPane(new PaneId(1)));

        Assert.Equal(InputMode.Normal, movedFromSort.InputMode);
        Assert.Null(movedFromRename.Rename);
    }

    [Fact]
    public void FocusPane_on_the_focused_pane_changes_nothing()
    {
        var split = Split();

        var (same, effects) = Transition.Apply(split, new Msg.FocusPane(new PaneId(0)));

        Assert.Same(split, same);
        Assert.Empty(effects);
    }

    private static AppState SplitWith(Column top, Column bottom) => AppState.Initial with
    {
        Panes = [new Pane(new PaneId(0), [top], 0), new Pane(new PaneId(1), [bottom], 0)],
        FocusedPane = 0,
        NextPaneId = 2,
    };

    [Fact]
    public void Swap_exchanges_the_panes_and_keeps_focus_on_the_same_position()
    {
        var state = SplitWith(Files(@"C:\a", "x"), Files(@"C:\b", "p"));

        var (next, _) = Transition.Apply(state, new Msg.SwapPanes());

        Assert.Equal(new PaneId(1), next.Panes[0].Id);
        Assert.Equal(new PaneId(0), next.Panes[1].Id);
        Assert.Equal(0, next.FocusedPane);
    }

    [Fact]
    public void Swap_with_one_pane_does_nothing()
    {
        var state = Browsing();

        var (next, effects) = Transition.Apply(state, new Msg.SwapPanes());

        Assert.Same(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Sync_copies_loaded_columns_without_reading_anything()
    {
        var other = Files(@"C:\b", "p", "q") with { Cursor = 1 };
        var state = SplitWith(Files(@"C:\a", "x"), other);

        var (next, effects) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Equal(new Location.RealDirectory(@"C:\b"), next.Panes[0].Columns[0].Location);
        Assert.Equal("q", next.Panes[0].Columns[0].CursorName);
        Assert.Equal(new PaneId(0), next.Panes[0].Id);
        Assert.Empty(TestEffects.Of<Effect.ReadDirectory>(effects));
    }

    [Fact]
    public void Sync_re_derives_the_copied_columns_under_this_panes_view()
    {
        var other = new Column(
            new Location.RealDirectory(@"C:\b"),
            [new Entry("big", EntryKind.File, SizeBytes: 9), new Entry("small", EntryKind.File, SizeBytes: 1)],
            Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(Files(@"C:\a", "x"), other);
        state = state.WithPane(state.Panes[0] with
        {
            View = ViewOptions.Default with { Sort = SortOrder.Default with { Mode = SortMode.Size } },
        });

        var (next, _) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Equal("small", next.Panes[0].Columns[0].Entries[0].Name);
    }

    [Fact]
    public void Sync_reads_a_copied_column_that_is_still_loading()
    {
        var loading = new Column(new Location.RealDirectory(@"C:\b"), Entries: [], Cursor: -1, Load: LoadState.Loading);
        var state = SplitWith(Files(@"C:\a", "x"), loading);

        var (next, effects) = Transition.Apply(state, new Msg.SyncToOtherPane());

        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(next.Panes[0].Ref(0), read.At);
        Assert.Equal(LoadState.Loading, next.Panes[0].Columns[0].Load);

        var (loaded, _) = Transition.Apply(
            next,
            new Msg.DirectoryLoaded(read.At, read.Location, [new Entry("p", EntryKind.File)]));

        Assert.Equal(LoadState.Loaded, loaded.Panes[0].Columns[0].Load);
        Assert.Equal("p", loaded.Panes[0].Columns[0].Entries[0].Name);
    }

    [Fact]
    public void Sync_does_not_copy_marks()
    {
        var other = Files(@"C:\b", "p", "q");
        var marked = other.AllEntries.SetItem(0, other.AllEntries[0] with { IsMarked = true });
        other = other.WithAllEntries(marked, ViewOptions.Default);
        var state = SplitWith(Files(@"C:\a", "x"), other);

        var (next, _) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.False(next.Panes[0].Columns[0].Entries[0].IsMarked);
        Assert.False(next.Panes[0].Columns[0].AllEntries[0].IsMarked);
        Assert.True(next.Panes[1].Columns[0].Entries[0].IsMarked);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Swap_ends_a_rename()
    {
        var (renaming, _) = StartRename(Split());

        var (next, _) = Transition.Apply(renaming, new Msg.SwapPanes());

        Assert.Null(next.Rename);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Swap_leaves_focus_on_the_same_position_showing_the_other_panes_content()
    {
        var state = SplitWith(Files(@"C:\a", "x"), Files(@"C:\b", "p"));
        state = state with { FocusedPane = 1 };

        var (next, _) = Transition.Apply(state, new Msg.SwapPanes());

        Assert.Equal(1, next.FocusedPane);
        Assert.Equal(new PaneId(0), next.Focused.Id);
        Assert.Equal(new Location.RealDirectory(@"C:\a"), next.Focused.Columns[0].Location);
    }

    [Fact]
    public void Sync_ends_a_rename_and_sort_mode()
    {
        var (renaming, _) = StartRename(Split());
        var (sorting, _) = Transition.Apply(Split(), new Msg.EnterSortMode());

        var (fromRename, _) = Transition.Apply(renaming, new Msg.SyncToOtherPane());
        var (fromSort, _) = Transition.Apply(sorting, new Msg.SyncToOtherPane());

        Assert.Null(fromRename.Rename);
        Assert.Equal(InputMode.Normal, fromSort.InputMode);
    }

    [Fact]
    public void Sync_copies_every_column_and_the_focused_column()
    {
        var pane = new Pane(new PaneId(1), [Files(@"C:\b", "p"), Files(@"C:\b\p", "q")], 1);
        var state = AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [Files(@"C:\a", "x")], 0), pane],
            FocusedPane = 0,
            NextPaneId = 2,
        };

        var (next, _) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Equal(2, next.Panes[0].Columns.Length);
        Assert.Equal(1, next.Panes[0].FocusedColumn);
        Assert.Equal(new Location.RealDirectory(@"C:\b\p"), next.Panes[0].Columns[1].Location);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Sync_from_the_bottom_pane_copies_the_top_pane()
    {
        var state = SplitWith(Files(@"C:\a", "x"), Files(@"C:\b", "p"));
        state = state with { FocusedPane = 1 };

        var (next, _) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Equal(new Location.RealDirectory(@"C:\a"), next.Panes[1].Columns[0].Location);
        Assert.Equal(new Location.RealDirectory(@"C:\a"), next.Panes[0].Columns[0].Location);
        Assert.Equal(new PaneId(1), next.Panes[1].Id);
    }

    [Fact]
    public void Sync_with_one_pane_does_nothing()
    {
        var state = Browsing();

        var (next, effects) = Transition.Apply(state, new Msg.SyncToOtherPane());

        Assert.Same(state, next);
        Assert.Empty(effects);
    }
}
