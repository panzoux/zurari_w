using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class PaneTests
{
    private static Column Dir(string path, params string[] names) =>
        new Column(new Location.RealDirectory(path), [.. names.Select(n => new Entry(n, EntryKind.File))], Cursor: names.Length > 0 ? 0 : -1, Load: LoadState.Loaded);

    /// <summary>
    /// Two panes, top (id 0, focused) and bottom (id 1), each showing one column. Every test that
    /// uses this addresses pane 1 - the unfocused one - to prove a Msg reaches the pane it names
    /// rather than always landing on the focused pane. 7a.3 may add its own <c>TwoPanes</c> helper
    /// elsewhere; this one stays here for reuse.
    /// </summary>
    private static AppState TwoPanes(Column top, Column bottom) =>
        AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [top], 0), new Pane(new PaneId(1), [bottom], 0)],
            FocusedPane = 0,
            NextPaneId = 2,
        };

    [Fact]
    public void Initial_has_one_pane_with_the_drive_list()
    {
        var state = AppState.Initial;

        Assert.Single(state.Panes);
        Assert.Equal(0, state.FocusedPane);
        Assert.Equal(new PaneId(0), state.Focused.Id);
        Assert.Equal(1, state.NextPaneId);
        Assert.IsType<Location.Drives>(state.Focused.Columns[0].Location);
        Assert.Empty(state.CheckInvariants());
    }

    [Fact]
    public void ColumnAt_resolves_by_pane_id_and_rejects_unknown_panes_and_indices()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a")]);
        var pane = state.Focused.Id;

        Assert.NotNull(state.ColumnAt(new ColumnRef(pane, 0)));
        Assert.Null(state.ColumnAt(new ColumnRef(pane, 1)));
        Assert.Null(state.ColumnAt(new ColumnRef(pane, -1)));
        Assert.Null(state.ColumnAt(new ColumnRef(new PaneId(99), 0)));
    }

    [Fact]
    public void WithColumn_replaces_only_the_addressed_column()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a"), Dir(@"C:\x", "b")]);
        var at = new ColumnRef(state.Focused.Id, 1);
        var replaced = Dir(@"C:\y", "c");

        var next = state.WithColumn(at, replaced);

        Assert.Same(replaced, next.Focused.Columns[1]);
        Assert.Same(state.Focused.Columns[0], next.Focused.Columns[0]);
    }

    [Fact]
    public void WithColumn_for_an_unknown_pane_returns_the_same_state()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a")]);

        var next = state.WithColumn(new ColumnRef(new PaneId(99), 0), Dir(@"D:\"));

        Assert.Same(state, next);
    }

    [Fact]
    public void WithPane_for_an_unknown_pane_id_returns_the_same_state()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a")]);
        var unknown = new Pane(new PaneId(99), [Dir(@"D:\")], 0);

        var next = state.WithPane(unknown);

        Assert.Same(state, next);
    }

    [Fact]
    public void WithPane_with_the_identical_pane_instance_returns_the_same_state()
    {
        var state = AppState.SinglePane([Dir(@"C:\", "a")]);

        var next = state.WithPane(state.Focused);

        Assert.Same(state, next);
    }

    [Fact]
    public void The_shim_writes_into_the_focused_pane_even_when_it_is_not_pane_zero()
    {
        var pane0 = new Pane(new PaneId(0), [Dir(@"C:\")], 0);
        var pane1 = new Pane(new PaneId(1), [Dir(@"D:\")], 0);
        var state = AppState.Initial with { Panes = [pane0, pane1], FocusedPane = 1, NextPaneId = 2 };
        var preview = PreviewState.Initial with { Generation = 1 };

        var next = state with { Preview = preview };

        Assert.Equal(preview, next.Panes[1].Preview);
        Assert.Same(pane0, next.Panes[0]);
    }

    [Fact]
    public void Invariants_reject_duplicate_pane_ids_and_ids_not_below_NextPaneId()
    {
        var pane = new Pane(new PaneId(0), [Dir(@"C:\")], 0);
        var duplicate = AppState.Initial with { Panes = [pane, pane], NextPaneId = 1 };
        var tooHigh = AppState.Initial with { Panes = [pane with { Id = new PaneId(5) }], NextPaneId = 1 };

        Assert.Contains(duplicate.CheckInvariants(), v => v.Contains("more than once", StringComparison.Ordinal));
        Assert.Contains(tooHigh.CheckInvariants(), v => v.Contains("NextPaneId", StringComparison.Ordinal));
    }

    [Fact]
    public void Invariants_reject_more_than_two_panes_and_an_out_of_range_focus()
    {
        var p = (int id) => new Pane(new PaneId(id), [Dir(@"C:\")], 0);
        var three = AppState.Initial with { Panes = [p(0), p(1), p(2)], NextPaneId = 3 };
        var badFocus = AppState.Initial with { FocusedPane = 1 };

        Assert.Contains(three.CheckInvariants(), v => v.Contains("1 or 2 panes", StringComparison.Ordinal));
        Assert.Contains(badFocus.CheckInvariants(), v => v.Contains("FocusedPane", StringComparison.Ordinal));
    }

    [Fact]
    public void Column_invariants_are_checked_in_every_pane()
    {
        var broken = new Column(new Location.RealDirectory(@"C:\"), [new Entry("a", EntryKind.File)], Cursor: 7);
        var state = AppState.Initial with
        {
            Panes = [new Pane(new PaneId(0), [Dir(@"C:\")], 0), new Pane(new PaneId(1), [broken], 0)],
            NextPaneId = 2,
        };

        Assert.Contains(state.CheckInvariants(), v => v.Contains("Panes[1].Columns[0].Cursor", StringComparison.Ordinal));
    }

    [Fact]
    public void A_listing_for_a_pane_that_no_longer_exists_is_ignored()
    {
        var state = AppState.SinglePane([new Column(new Location.RealDirectory(@"C:\"), Entries: [])]);
        var ghost = new ColumnRef(new PaneId(42), 0);

        var (next, effects) = Transition.Apply(
            state, new Msg.DirectoryLoaded(ghost, new Location.RealDirectory(@"C:\"), [new Entry("a", EntryKind.File)]));

        Assert.Same(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Entering_a_directory_reads_it_for_the_same_pane()
    {
        var root = new Column(
            new Location.RealDirectory(@"C:\"), [new Entry("sub", EntryKind.Directory)], Cursor: 0, Load: LoadState.Loaded);
        var state = AppState.SinglePane([root]);

        var (_, effects) = Transition.Apply(state, new Msg.EnterDirectory(state.Focused.Ref(0), 0));

        var read = Assert.Single(effects.OfType<Effect.ReadDirectory>());
        Assert.Equal(state.Focused.Ref(1), read.At);
    }

    [Fact]
    public void EnterDirectory_on_the_unfocused_pane_reads_into_that_pane_and_leaves_the_other_untouched()
    {
        var top = Dir(@"C:\", "a");
        var bottom = new Column(
            new Location.RealDirectory(@"D:\"), [new Entry("sub", EntryKind.Directory)], Cursor: 0, Load: LoadState.Loaded);
        var state = TwoPanes(top, bottom);
        var at = new ColumnRef(new PaneId(1), 0);

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(at, 0));

        var read = Assert.Single(effects.OfType<Effect.ReadDirectory>());
        Assert.Equal(new PaneId(1), read.At.Pane);
        Assert.Equal(2, next.Panes[1].Columns.Length);
        Assert.Same(state.Panes[0], next.Panes[0]);
        Assert.Equal(0, next.FocusedPane);
    }

    [Fact]
    public void Refresh_rereads_columns_in_both_panes()
    {
        var state = TwoPanes(Dir(@"C:\", "a"), Dir(@"D:\", "b"));

        var (_, effects) = Transition.Apply(state, new Msg.Refresh());

        var reads = effects.OfType<Effect.ReadDirectory>().ToList();
        Assert.Contains(reads, r => r.At.Pane == new PaneId(0));
        Assert.Contains(reads, r => r.At.Pane == new PaneId(1));
    }

    [Fact]
    public void ExternalDirectoryChanged_rereads_only_the_pane_showing_that_path()
    {
        var top = Dir(@"C:\", "a");
        var bottom = Dir(@"D:\stale", "b");
        var state = TwoPanes(top, bottom);

        var (next, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged(@"D:\stale"));

        var read = Assert.Single(effects.OfType<Effect.ReadDirectory>());
        Assert.Equal(new PaneId(1), read.At.Pane);
        Assert.Same(state.Panes[0], next.Panes[0]);
    }

    [Fact]
    public void DirectoryLoaded_reveal_moves_the_target_panes_focus_not_FocusedPane()
    {
        var top = Dir(@"C:\", "x");
        var loadingColumn = new Column(new Location.RealDirectory(@"D:\"), Entries: [], Load: LoadState.Loading);
        var otherColumn = Dir(@"D:\other", "y");
        var state = TwoPanes(top, loadingColumn);
        var pane1 = state.Panes[1] with { Columns = [loadingColumn, otherColumn], FocusedColumn = 1 };
        state = state.WithPane(pane1) with { RevealTarget = "b" };
        var at = new ColumnRef(new PaneId(1), 0);

        var (next, _) = Transition.Apply(
            state,
            new Msg.DirectoryLoaded(
                at, new Location.RealDirectory(@"D:\"), [new Entry("a", EntryKind.File), new Entry("b", EntryKind.File)]));

        Assert.Equal("b", next.Panes[1].Columns[0].CursorName);
        Assert.Equal(0, next.Panes[1].FocusedColumn);
        Assert.Equal(0, next.FocusedPane);
        Assert.Null(next.RevealTarget);
    }

    [Fact]
    public void Rename_on_the_unfocused_pane_emits_RenameEntry_for_that_pane()
    {
        var top = Dir(@"C:\", "a");
        var bottom = Dir(@"D:\", "old.txt");
        var state = TwoPanes(top, bottom);
        var at = new ColumnRef(new PaneId(1), 0);

        var (editing, _) = Transition.Apply(state, new Msg.RenameRequested(at));
        var (_, effects) = Transition.Apply(editing, new Msg.RenameSubmitted("new.txt"));

        var renamed = Assert.Single(effects.OfType<Effect.RenameEntry>());
        Assert.Equal(at, renamed.At);
    }

    [Fact]
    public void ToggleMark_on_the_unfocused_pane_marks_only_that_panes_entry()
    {
        var top = Dir(@"C:\", "a");
        var bottom = Dir(@"D:\", "b");
        var state = TwoPanes(top, bottom);
        var at = new ColumnRef(new PaneId(1), 0);

        var (next, _) = Transition.Apply(state, new Msg.ToggleMark(at, 0));

        Assert.True(next.Panes[1].Columns[0].Entries[0].IsMarked);
        Assert.Same(state.Panes[0], next.Panes[0]);
    }
}
