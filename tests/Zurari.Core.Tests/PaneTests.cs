using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class PaneTests
{
    private static Column Dir(string path, params string[] names) =>
        new Column(new Location.RealDirectory(path), [.. names.Select(n => new Entry(n, EntryKind.File))], Cursor: names.Length > 0 ? 0 : -1, Load: LoadState.Loaded);

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
}
