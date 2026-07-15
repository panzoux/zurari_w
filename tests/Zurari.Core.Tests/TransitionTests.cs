using System.Collections.Immutable;
using CsCheck;
using Zurari.Core;

namespace Zurari.Core.Tests;

public class TransitionTests
{
    private static readonly Entry Dir = new("sub", EntryKind.Directory);
    private static readonly Entry File1 = new("a.txt", EntryKind.File);
    private static readonly Entry File2 = new("b.txt", EntryKind.File);
    private static readonly Entry Drive = new(@"C:\", EntryKind.Drive);

    private static AppState StateWithColumns(params Column[] columns) =>
        AppState.Initial with { Columns = [.. columns], FocusedColumn = 0 };

    [Fact]
    public void Noop_returns_same_state_and_no_effects()
    {
        var (state, effects) = Transition.Apply(AppState.Initial, new Msg.Noop());

        Assert.Same(AppState.Initial, state);
        Assert.Empty(effects);
    }

    [Fact]
    public void Apply_rejects_nulls()
    {
        Assert.Throws<ArgumentNullException>(() => Transition.Apply(null!, new Msg.Noop()));
        Assert.Throws<ArgumentNullException>(() => Transition.Apply(AppState.Initial, null!));
    }

    [Fact]
    public void CursorDown_advances_by_one()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.Equal(1, next.Columns[0].Cursor);
        Assert.Empty(effects);
    }

    [Fact]
    public void CursorDown_clamps_at_last_entry()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 2, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorUp_clamps_at_first_entry()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorUp(0));

        Assert.Equal(0, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorUp_on_empty_column_stays_minus_one()
    {
        var column = new Column(@"C:\", [], Cursor: -1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorUp(0));

        Assert.Equal(-1, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorPageDown_treats_non_positive_page_size_as_one()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorPageDown(0, PageSize: 0));

        Assert.Equal(1, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorPageDown_clamps_to_last_entry()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorPageDown(0, PageSize: 100));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorHome_moves_to_first_entry()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 2, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorHome(0));

        Assert.Equal(0, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorEnd_moves_to_last_entry()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorEnd(0));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorTo_moves_cursor_to_given_entry()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorTo(0, 2));

        Assert.Equal(2, next.Columns[0].Cursor);
        Assert.Empty(effects);
    }

    [Fact]
    public void CursorTo_clamps_out_of_range_entry_index()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorTo(0, 99));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorTo_on_empty_column_stays_minus_one()
    {
        var column = new Column(@"C:\", [], Cursor: -1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorTo(0, 0));

        Assert.Equal(-1, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorTo_with_out_of_range_column_is_ignored()
    {
        var column = new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorTo(9, 0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(5)]
    public void Cursor_msgs_with_out_of_range_column_are_ignored(int columnIndex)
    {
        var column = new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(columnIndex));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void FocusColumn_changes_focus_when_in_range()
    {
        var state = StateWithColumns(
            new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub", [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.FocusColumn(1));

        Assert.Equal(1, next.FocusedColumn);
        Assert.Empty(effects);
    }

    [Fact]
    public void FocusColumn_out_of_range_is_ignored()
    {
        var state = StateWithColumns(new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, _) = Transition.Apply(state, new Msg.FocusColumn(3));

        Assert.Equal(0, next.FocusedColumn);
    }

    [Fact]
    public void EnterDirectory_truncates_right_columns_and_emits_ReadDirectory()
    {
        var state = StateWithColumns(
            new Column(@"C:\", [Dir, File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub", [File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub\stale", [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(0, next.Columns[0].Cursor);
        Assert.Equal(@"C:\sub", next.Columns[1].Path);
        Assert.Equal(-1, next.Columns[1].Cursor);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        Assert.Equal(1, next.FocusedColumn);

        var effect = Assert.Single(effects);
        var readDirectory = Assert.IsType<Effect.ReadDirectory>(effect);
        Assert.Equal(1, readDirectory.ColumnIndex);
        Assert.Equal(@"C:\sub", readDirectory.Path);
    }

    [Fact]
    public void EnterDirectory_at_virtual_root_uses_drive_name_as_child_path()
    {
        var state = StateWithColumns(new Column("", [Drive], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(@"C:\", next.Columns[1].Path);
        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(@"C:\", effect.Path);
    }

    [Fact]
    public void EnterDirectory_on_file_selects_it_and_moves_focus_without_effects()
    {
        var state = StateWithColumns(new Column(@"C:\", [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 2));

        Assert.Single(next.Columns);
        Assert.Equal(2, next.Columns[0].Cursor);
        Assert.Equal(0, next.FocusedColumn);
        Assert.Empty(effects);
    }

    [Fact]
    public void EnterDirectory_on_file_truncates_columns_to_its_right()
    {
        var state = StateWithColumns(
            new Column(@"C:\", [Dir, File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub", [File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub\stale", [], Load: LoadState.Loading)) with
        { FocusedColumn = 2 };

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 1));

        Assert.Single(next.Columns);
        Assert.Equal(1, next.Columns[0].Cursor);
        Assert.Equal(0, next.FocusedColumn);
        Assert.Empty(effects);
    }

    [Fact]
    public void EnterDirectory_with_out_of_range_entry_index_is_a_no_op()
    {
        var state = StateWithColumns(new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 7));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void GoToParent_moves_focus_left_and_keeps_right_columns()
    {
        var state = StateWithColumns(
            new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub", [File1], Cursor: 0, Load: LoadState.Loaded)) with
        { FocusedColumn = 1 };

        var (next, effects) = Transition.Apply(state, new Msg.GoToParent(1));

        Assert.Equal(0, next.FocusedColumn);
        Assert.Equal(2, next.Columns.Length);
        Assert.Empty(effects);
    }

    [Fact]
    public void GoToParent_at_column_zero_is_a_no_op()
    {
        var state = StateWithColumns(new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.GoToParent(0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Refresh_emits_one_ReadDirectory_per_column_and_marks_all_loading()
    {
        var state = StateWithColumns(
            new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(@"C:\sub", [File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.Refresh());

        Assert.All(next.Columns, c => Assert.Equal(LoadState.Loading, c.Load));
        // Entries are kept so the UI does not flash while reloading.
        Assert.Single(next.Columns[0].Entries);
        Assert.Single(next.Columns[1].Entries);

        Assert.Equal(2, effects.Count);
        var first = Assert.IsType<Effect.ReadDirectory>(effects[0]);
        var second = Assert.IsType<Effect.ReadDirectory>(effects[1]);
        Assert.Equal((0, @"C:\"), (first.ColumnIndex, first.Path));
        Assert.Equal((1, @"C:\sub"), (second.ColumnIndex, second.Path));
    }

    [Fact]
    public void DirectoryLoaded_fills_entries_and_clamps_cursor_to_shrunk_range()
    {
        var column = new Column(@"C:\", [Dir, File1, File2], Cursor: 2, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(
            state,
            new Msg.DirectoryLoaded(0, @"C:\", [Dir]));

        Assert.Equal(LoadState.Loaded, next.Columns[0].Load);
        Assert.Single(next.Columns[0].Entries);
        Assert.Equal(0, next.Columns[0].Cursor);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoaded_with_no_entries_sets_cursor_to_minus_one()
    {
        var column = new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoaded(0, @"C:\", []));

        Assert.Equal(-1, next.Columns[0].Cursor);
    }

    [Fact]
    public void DirectoryLoaded_with_wrong_path_is_ignored_as_stale()
    {
        var column = new Column(@"C:\", [], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(
            state,
            new Msg.DirectoryLoaded(0, @"C:\stale", [Dir]));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoaded_with_out_of_range_column_is_ignored_as_stale()
    {
        var state = StateWithColumns(new Column(@"C:\", [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.DirectoryLoaded(9, @"C:\", [Dir]));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoadFailed_sets_error_and_keeps_old_entries()
    {
        var column = new Column(@"C:\", [Dir], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DirectoryLoadFailed(0, @"C:\", "access denied"));

        Assert.Equal(LoadState.Error, next.Columns[0].Load);
        Assert.Equal("access denied", next.Columns[0].ErrorMessage);
        Assert.Single(next.Columns[0].Entries);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoadFailed_with_wrong_path_is_ignored_as_stale()
    {
        var column = new Column(@"C:\", [], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoadFailed(0, @"C:\stale", "oops"));

        Assert.Equal(state, next);
    }
}

/// <summary>
/// Property-based tests (rwf's proptest equivalent). As Msg variants are added,
/// extend <see cref="GenMsg"/> so every property automatically covers them.
/// </summary>
public class TransitionProperties
{
    private static Gen<int> GenColumnIndex => Gen.Int[-2, 4];

    private static Gen<int> GenEntryIndex => Gen.Int[-2, 4];

    private static Gen<int> GenPageSize => Gen.Int[-3, 8];

    private static Gen<string> GenPath => Gen.OneOfConst(
        "",
        @"C:\",
        @"D:\",
        @"C:\Users",
        @"C:\Users\someone",
        @"C:\stale");

    private static Gen<EntryKind> GenEntryKind => Gen.OneOfConst(
        EntryKind.Drive,
        EntryKind.Directory,
        EntryKind.File);

    private static Gen<Entry> GenEntry =>
        Gen.Select(Gen.OneOfConst("a", "b", "c"), GenEntryKind)
            .Select(t => new Entry(t.Item1, t.Item2));

    private static Gen<ImmutableArray<Entry>> GenEntries =>
        GenEntry.List[0, 3].Select(list => list.ToImmutableArray());

    private static Gen<Msg> GenMsg => Gen.OneOf(
        Gen.Const<Msg>(new Msg.Noop()),
        GenColumnIndex.Select(i => (Msg)new Msg.CursorUp(i)),
        GenColumnIndex.Select(i => (Msg)new Msg.CursorDown(i)),
        Gen.Select(GenColumnIndex, GenPageSize).Select(t => (Msg)new Msg.CursorPageUp(t.Item1, t.Item2)),
        Gen.Select(GenColumnIndex, GenPageSize).Select(t => (Msg)new Msg.CursorPageDown(t.Item1, t.Item2)),
        GenColumnIndex.Select(i => (Msg)new Msg.CursorHome(i)),
        GenColumnIndex.Select(i => (Msg)new Msg.CursorEnd(i)),
        Gen.Select(GenColumnIndex, GenEntryIndex).Select(t => (Msg)new Msg.CursorTo(t.Item1, t.Item2)),
        GenColumnIndex.Select(i => (Msg)new Msg.FocusColumn(i)),
        Gen.Select(GenColumnIndex, GenEntryIndex).Select(t => (Msg)new Msg.EnterDirectory(t.Item1, t.Item2)),
        GenColumnIndex.Select(i => (Msg)new Msg.GoToParent(i)),
        Gen.Const<Msg>(new Msg.Refresh()),
        Gen.Select(GenColumnIndex, GenPath, GenEntries).Select(t => (Msg)new Msg.DirectoryLoaded(t.Item1, t.Item2, t.Item3)),
        Gen.Select(GenColumnIndex, GenPath).Select(t => (Msg)new Msg.DirectoryLoadFailed(t.Item1, t.Item2, "error")));

    [Fact]
    public void Any_msg_sequence_yields_valid_state_and_effects()
    {
        GenMsg.List[0, 50].Sample(msgs =>
        {
            var state = AppState.Initial;
            foreach (var msg in msgs)
            {
                var (next, effects) = Transition.Apply(state, msg);
                Assert.NotNull(next);
                Assert.NotNull(effects);
                Assert.Empty(next.CheckInvariants());
                state = next;
            }
        });
    }

    [Fact]
    public void Apply_is_deterministic()
    {
        GenMsg.Sample(msg =>
        {
            var first = Transition.Apply(AppState.Initial, msg);
            var second = Transition.Apply(AppState.Initial, msg);

            AssertStatesEqual(first.State, second.State);
            Assert.Equal(first.Effects, second.Effects);
        });
    }

    // AppState.Columns is an ImmutableArray<Column>, and ImmutableArray<T>.Equals compares the
    // underlying array by reference, not by content. Two independently-built states with equal
    // content but different array instances would otherwise (incorrectly) compare unequal, so
    // this compares field by field; xunit's Assert.Equal already does element-wise comparison
    // for the IEnumerable<Entry> members (ImmutableArray<Entry>).
    private static void AssertStatesEqual(AppState expected, AppState actual)
    {
        Assert.Equal(expected.FocusedColumn, actual.FocusedColumn);
        Assert.Equal(expected.Columns.Length, actual.Columns.Length);
        for (var i = 0; i < expected.Columns.Length; i++)
        {
            var e = expected.Columns[i];
            var a = actual.Columns[i];
            Assert.Equal(e.Path, a.Path);
            Assert.Equal(e.Cursor, a.Cursor);
            Assert.Equal(e.ScrollOffset, a.ScrollOffset);
            Assert.Equal(e.Load, a.Load);
            Assert.Equal(e.ErrorMessage, a.ErrorMessage);
            Assert.Equal(e.Entries, a.Entries);
        }
    }
}
