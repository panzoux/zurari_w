using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>
/// Finder/Explorer columns: the column right of the focused one shows whatever the cursor is
/// resting on, without entering it.
/// </summary>
public class ChildColumnTests
{
    private static readonly Entry Sub = new("sub", EntryKind.Directory);
    private static readonly Entry Other = new("other", EntryKind.Directory);
    private static readonly Entry Doc = new("a.txt", EntryKind.File);

    private static AppState With(params Column[] columns) =>
        new() { Columns = [.. columns], FocusedColumn = columns.Length - 1 };

    private static Column Root(int cursor, params Entry[] entries) =>
        new(new Location.RealDirectory(@"C:\"), [.. entries], Cursor: cursor, Load: LoadState.Loaded);

    [Fact]
    public void Landing_on_a_folder_opens_a_column_for_it_without_moving_focus()
    {
        var state = With(Root(2, Doc, Other, Sub));

        var (next, effects) = Transition.Apply(state, new Msg.CursorUp(0));

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(new Location.RealDirectory(@"C:\other"), next.Columns[1].Location);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        Assert.Equal(0, next.FocusedColumn);

        var read = Assert.Single(effects.OfType<Effect.ReadDirectory>());
        Assert.Equal(1, read.ColumnIndex);
        Assert.True(read.Speculative, "a column the cursor is only resting on is speculative");
    }

    [Fact]
    public void Landing_on_a_file_takes_the_column_away_again()
    {
        var state = With(Root(0, Doc, Sub));
        var (opened, _) = Transition.Apply(state, new Msg.CursorTo(0, 1));
        Assert.Equal(2, opened.Columns.Length);

        var (closed, _) = Transition.Apply(opened, new Msg.CursorTo(0, 0));

        Assert.Single(closed.Columns);
    }

    [Fact]
    public void A_header_opens_nothing()
    {
        var column = new Column(
            Location.Drives.Instance,
            [new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives), new Entry(@"C:\", EntryKind.Drive)],
            Cursor: 1,
            Load: LoadState.Loaded);

        var (next, _) = Transition.Apply(With(column), new Msg.CursorUp(0));

        Assert.Single(next.Columns);
    }

    [Fact]
    public void A_drive_row_opens_the_drive()
    {
        var column = new Column(
            Location.Drives.Instance,
            [new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives), new Entry(@"C:\", EntryKind.Drive)],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (next, _) = Transition.Apply(With(column), new Msg.CursorDown(0));

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(new Location.RealDirectory(@"C:\"), next.Columns[1].Location);
    }

    /// <summary>
    /// The reason this reads only the focused column. The child loads asynchronously, and that load
    /// runs the reconciliation again - so a rule of "extend wherever a cursor sits on a folder" would
    /// have each load trigger the next, unrolling a whole chain by itself and never stopping inside a
    /// junction that points at its own ancestor.
    /// </summary>
    [Fact]
    public void The_childs_own_contents_arriving_does_not_open_a_grandchild()
    {
        var empty = With(new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1));
        var (opened, _) = Transition.Apply(empty, new Msg.DirectoryLoaded(
            0, new Location.RealDirectory(@"C:\"), [Sub]));
        Assert.Equal(2, opened.Columns.Length);

        // The child's listing arrives, and its own cursor lands on a folder.
        var (loaded, effects) = Transition.Apply(
            opened,
            new Msg.DirectoryLoaded(1, new Location.RealDirectory(@"C:\sub"), [new Entry("deep", EntryKind.Directory)]));

        Assert.Equal(2, loaded.Columns.Length);
        Assert.Empty(effects.OfType<Effect.ReadDirectory>());
    }

    /// <summary>
    /// Moving focus left must keep the chain: the cursor there is already on the folder the next
    /// column shows, so there is nothing to replace.
    /// </summary>
    [Fact]
    public void Going_back_to_the_parent_keeps_the_columns_that_are_already_open()
    {
        var state = new AppState
        {
            Columns =
            [
                Root(0, Sub),
                new Column(new Location.RealDirectory(@"C:\sub"), [Other], Cursor: 0, Load: LoadState.Loaded),
                new Column(new Location.RealDirectory(@"C:\sub\other"), [], Cursor: -1, Load: LoadState.Loaded),
            ],
            FocusedColumn = 1,
        };

        var (next, effects) = Transition.Apply(state, new Msg.GoToParent(1));

        Assert.Equal(0, next.FocusedColumn);
        Assert.Equal(3, next.Columns.Length);
        Assert.Empty(effects.OfType<Effect.ReadDirectory>());
    }

    /// <summary>
    /// A message that changes nothing must change nothing - the reconciliation compares before with
    /// after, rather than rebuilding the pane on every <c>Apply</c>.
    /// </summary>
    [Fact]
    public void A_message_that_does_nothing_opens_nothing()
    {
        var state = With(Root(0, Sub));

        var (next, effects) = Transition.Apply(state, new Msg.Noop());

        Assert.Single(next.Columns);
        Assert.Empty(effects);
    }

    [Fact]
    public void Entering_a_folder_still_moves_focus_into_it()
    {
        var state = With(Root(0, Sub));

        var (next, _) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(1, next.FocusedColumn);
    }

    [Fact]
    public void Refreshing_leaves_the_open_columns_where_they_are()
    {
        var state = new AppState
        {
            Columns =
            [
                Root(0, Sub),
                new Column(new Location.RealDirectory(@"C:\sub"), [Other], Cursor: 0, Load: LoadState.Loaded),
            ],
            FocusedColumn = 0,
        };

        var (next, effects) = Transition.Apply(state, new Msg.Refresh());

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(2, effects.OfType<Effect.ReadDirectory>().Count());
        Assert.DoesNotContain(effects.OfType<Effect.ReadDirectory>(), e => e.Speculative);
    }

    [Fact]
    public void Every_state_it_produces_still_satisfies_the_invariants()
    {
        var state = With(Root(0, Doc, Sub, Other));

        foreach (var index in new[] { 1, 2, 0, 2, 1 })
        {
            (state, _) = Transition.Apply(state, new Msg.CursorTo(0, index));
            Assert.Empty(state.CheckInvariants());
        }
    }
}
