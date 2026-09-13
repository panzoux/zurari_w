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
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.Equal(1, next.Columns[0].Cursor);
        // Landing on File1 triggers a preview load (see Transition.ReconcilePreview).
        Assert.Equal(@"C:\a.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void CursorDown_clamps_at_last_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 2, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorUp_clamps_at_first_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorUp(0));

        Assert.Equal(0, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorUp_on_empty_column_stays_minus_one()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorUp(0));

        Assert.Equal(-1, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorPageDown_treats_non_positive_page_size_as_one()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorPageDown(0, PageSize: 0));

        Assert.Equal(1, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorPageDown_clamps_to_last_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorPageDown(0, PageSize: 100));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorHome_moves_to_first_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 2, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorHome(0));

        Assert.Equal(0, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorEnd_moves_to_last_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorEnd(0));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorTo_moves_cursor_to_given_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorTo(0, 2));

        Assert.Equal(2, next.Columns[0].Cursor);
        // Landing on File2 triggers a preview load (see Transition.ReconcilePreview).
        Assert.Equal(@"C:\b.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void CursorTo_clamps_out_of_range_entry_index()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorTo(0, 99));

        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorTo_on_empty_column_stays_minus_one()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorTo(0, 0));

        Assert.Equal(-1, next.Columns[0].Cursor);
    }

    [Fact]
    public void CursorTo_with_out_of_range_column_is_ignored()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded);
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
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(columnIndex));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void FocusColumn_changes_focus_when_in_range()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.FocusColumn(1));

        Assert.Equal(1, next.FocusedColumn);
        Assert.Empty(effects);
    }

    [Fact]
    public void FocusColumn_out_of_range_is_ignored()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, _) = Transition.Apply(state, new Msg.FocusColumn(3));

        Assert.Equal(0, next.FocusedColumn);
    }

    [Fact]
    public void EnterDirectory_truncates_right_columns_and_emits_ReadDirectory()
    {
        // The column beside the cursor is showing somewhere else, so entering has to read.
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\elsewhere"), [File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\elsewhere\stale"), [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(0, next.Columns[0].Cursor);
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), next.Columns[1].Location);
        Assert.Equal(-1, next.Columns[1].Cursor);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        Assert.Equal(1, next.FocusedColumn);

        var readDirectory = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(1, readDirectory.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), readDirectory.Location);
    }

    /// <summary>
    /// Entering the folder the cursor was resting on moves into the column that is already there.
    /// Re-reading it would flash 読み込み中… over contents already on screen.
    /// </summary>
    [Fact]
    public void EnterDirectory_moves_into_the_column_that_is_already_showing_it()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(1, next.FocusedColumn);
        Assert.Equal(LoadState.Loaded, next.Columns[1].Load);
        Assert.Equal(0, next.Columns[1].Cursor);
        Assert.Empty(effects.OfType<Effect.ReadDirectory>());
    }

    [Fact]
    public void EnterDirectory_at_virtual_root_uses_drive_name_as_child_path()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(new Location.RealDirectory(@"C:\"), next.Columns[1].Location);
        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(new Location.RealDirectory(@"C:\"), effect.Location);
    }

    [Fact]
    public void EnterDirectory_on_file_selects_it_and_moves_focus()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 2));

        Assert.Single(next.Columns);
        Assert.Equal(2, next.Columns[0].Cursor);
        Assert.Equal(0, next.FocusedColumn);
        // No directory-read effect for a file, but selecting it does trigger a preview load.
        Assert.Equal(@"C:\b.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void EnterDirectory_on_file_truncates_columns_to_its_right()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub\stale"), [], Load: LoadState.Loading)) with
        { FocusedColumn = 2 };

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 1));

        Assert.Single(next.Columns);
        Assert.Equal(1, next.Columns[0].Cursor);
        Assert.Equal(0, next.FocusedColumn);
        // Selecting File1 (and losing focus from the now-truncated column 2) triggers a preview load.
        Assert.Equal(@"C:\a.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
        Assert.Equal(@"C:\a.txt", Assert.Single(effects.OfType<Effect.OpenWithDefaultApp>()).Path);
    }

    [Fact]
    public void EnterDirectory_with_out_of_range_entry_index_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 7));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void EnterDirectory_default_focuses_the_new_child_column()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, _) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(1, next.FocusedColumn);
    }

    [Fact]
    public void EnterDirectory_with_FocusChild_false_opens_the_child_but_keeps_focus_on_the_parent()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0, FocusChild: false));

        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), next.Columns[1].Location);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        Assert.Equal(0, next.FocusedColumn);
        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(1, effect.ColumnIndex);
    }

    [Fact]
    public void EnterDirectory_on_file_with_FocusChild_false_still_focuses_the_columnIndex()
    {
        // File selection semantics are unaffected by FocusChild - there is no child column to
        // focus either way, so FocusedColumn is always the pressed column itself.
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 2, FocusChild: false));

        Assert.Single(next.Columns);
        Assert.Equal(2, next.Columns[0].Cursor);
        Assert.Equal(0, next.FocusedColumn);
        // Selecting File2 triggers a preview load.
        Assert.Equal(@"C:\b.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
        Assert.Equal(@"C:\b.txt", Assert.Single(effects.OfType<Effect.OpenWithDefaultApp>()).Path);
    }

    [Fact]
    public void GoToParent_moves_focus_left_and_keeps_right_columns()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File1], Cursor: 0, Load: LoadState.Loaded)) with
        { FocusedColumn = 1 };

        var (next, effects) = Transition.Apply(state, new Msg.GoToParent(1));

        Assert.Equal(0, next.FocusedColumn);
        Assert.Equal(2, next.Columns.Length);
        Assert.Empty(effects);
    }

    [Fact]
    public void GoToParent_at_column_zero_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.GoToParent(0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void Refresh_emits_one_ReadDirectory_per_column_and_marks_all_loading()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.Refresh());

        Assert.All(next.Columns, c => Assert.Equal(LoadState.Loading, c.Load));
        // Entries are kept so the UI does not flash while reloading.
        Assert.Single(next.Columns[0].Entries);
        Assert.Single(next.Columns[1].Entries);

        Assert.Equal(2, effects.Count);
        var first = Assert.IsType<Effect.ReadDirectory>(effects[0]);
        var second = Assert.IsType<Effect.ReadDirectory>(effects[1]);
        Assert.Equal(0, first.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\"), first.Location);
        Assert.Equal(1, second.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), second.Location);
    }

    [Fact]
    public void DirectoryLoaded_fills_entries_and_clamps_cursor_to_shrunk_range()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 2, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(
            state,
            new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [Dir]));

        Assert.Equal(LoadState.Loaded, next.Columns[0].Load);
        Assert.Single(next.Columns[0].Entries);
        Assert.Equal(0, next.Columns[0].Cursor);

        // The clamped cursor landed on a directory, so the pane fills in the column beside it.
        Assert.Equal(
            new Effect.ReadDirectory(1, new Location.RealDirectory(@"C:\sub"), Speculative: true),
            Assert.Single(effects));
    }

    [Fact]
    public void DirectoryLoaded_with_no_entries_sets_cursor_to_minus_one()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), []));

        Assert.Equal(-1, next.Columns[0].Cursor);
    }

    [Fact]
    public void DirectoryLoaded_with_wrong_path_is_ignored_as_stale()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(
            state,
            new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\stale"), [Dir]));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoaded_with_out_of_range_column_is_ignored_as_stale()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.DirectoryLoaded(9, new Location.RealDirectory(@"C:\"), [Dir]));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoadFailed_sets_error_and_keeps_old_entries()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DirectoryLoadFailed(0, new Location.RealDirectory(@"C:\"), "access denied"));

        Assert.Equal(LoadState.Error, next.Columns[0].Load);
        Assert.Equal("access denied", next.Columns[0].ErrorMessage);
        Assert.Single(next.Columns[0].Entries);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoadFailed_with_wrong_path_is_ignored_as_stale()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoadFailed(0, new Location.RealDirectory(@"C:\stale"), "oops"));

        Assert.Equal(state, next);
    }

    [Fact]
    public void DeleteEntry_on_file_marks_column_loading_and_emits_DeleteToRecycleBin()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DeleteEntry(0, 1));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(3, next.Columns[0].Entries.Length);

        var effect = Assert.IsType<Effect.DeleteToRecycleBin>(Assert.Single(effects));
        Assert.Equal(0, effect.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\"), effect.ColumnLocation);
        Assert.Equal([@"C:\a.txt"], effect.Targets);
        Assert.False(effect.Permanent);
    }

    [Fact]
    public void DeleteEntry_with_Permanent_true_carries_it_through_to_the_effect()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (_, effects) = Transition.Apply(state, new Msg.DeleteEntry(0, 1, Permanent: true));

        var effect = Assert.IsType<Effect.DeleteToRecycleBin>(Assert.Single(effects));
        Assert.True(effect.Permanent);
    }

    [Fact]
    public void DeleteEntry_on_directory_marks_column_loading_and_emits_DeleteToRecycleBin()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DeleteEntry(0, 0));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        var effect = Assert.IsType<Effect.DeleteToRecycleBin>(Assert.Single(effects));
        Assert.Equal([@"C:\sub"], effect.Targets);
    }

    [Fact]
    public void DeleteEntry_on_drive_is_a_no_op()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.DeleteEntry(0, 0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DeleteEntry_with_out_of_range_entry_index_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.DeleteEntry(0, 7));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DeleteEntry_with_out_of_range_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.DeleteEntry(9, 0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ToggleMark_flips_mark_and_moves_cursor_to_it()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.ToggleMark(0, 2));

        Assert.True(next.Columns[0].Entries[2].IsMarked);
        Assert.Equal(2, next.Columns[0].Cursor);
        // Moving the cursor onto File2 triggers a preview load.
        Assert.Equal(@"C:\b.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void ToggleMark_flips_back_off_when_applied_twice()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (once, _) = Transition.Apply(state, new Msg.ToggleMark(0, 1));
        var (twice, _) = Transition.Apply(once, new Msg.ToggleMark(0, 1));

        Assert.False(twice.Columns[0].Entries[1].IsMarked);
    }

    /// <summary>
    /// Rows in the drive pane cannot be marked.
    /// </summary>
    /// <remarks>
    /// Reversed deliberately: this used to assert that a drive could be marked. Marks exist to
    /// gather files for a copy, move or delete, and none of those means anything applied to a drive
    /// or a favorite — so there was never anything for a mark here to feed. Refusing it also frees
    /// Space to mean the one useful thing in this pane, collapsing a section.
    /// </remarks>
    [Fact]
    public void ToggleMark_does_nothing_in_the_drive_pane()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive], Cursor: 0, Load: LoadState.Loaded));

        var (next, _) = Transition.Apply(state, new Msg.ToggleMark(0, 0));

        Assert.False(next.Columns[0].Entries[0].IsMarked);
    }

    [Fact]
    public void ToggleMark_with_out_of_range_entry_index_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ToggleMark(0, 7));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ToggleMark_with_out_of_range_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ToggleMark(9, 0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ToggleMarkAtCursor_flips_cursor_entry_and_advances_cursor()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));

        Assert.True(next.Columns[0].Entries[0].IsMarked);
        Assert.Equal(1, next.Columns[0].Cursor);
        // Advancing onto File1 triggers a preview load.
        Assert.Equal(@"C:\a.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void ToggleMarkAtCursor_clamps_cursor_advance_at_last_entry()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));

        Assert.True(next.Columns[0].Entries[1].IsMarked);
        Assert.Equal(1, next.Columns[0].Cursor);
    }

    [Fact]
    public void ToggleMarkAtCursor_on_empty_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ToggleMarkAtCursor_with_out_of_range_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(9));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ClearMarks_unmarks_every_entry_in_the_column()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir with { IsMarked = true }, File1 with { IsMarked = true }, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.ClearMarks(0));

        Assert.All(next.Columns[0].Entries, e => Assert.False(e.IsMarked));
        Assert.Empty(effects);
    }

    [Fact]
    public void ClearMarks_with_no_marks_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ClearMarks(0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ClearMarks_with_out_of_range_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ClearMarks(9));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DeleteMarked_emits_one_effect_with_all_marked_full_paths_in_entry_order()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"),
            [Dir with { IsMarked = true }, File1, File2 with { IsMarked = true }],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DeleteMarked(0));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(3, next.Columns[0].Entries.Length);

        var effect = Assert.IsType<Effect.DeleteToRecycleBin>(Assert.Single(effects));
        Assert.Equal(0, effect.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\"), effect.ColumnLocation);
        Assert.Equal([@"C:\sub", @"C:\b.txt"], effect.Targets);
        Assert.False(effect.Permanent);
    }

    [Fact]
    public void DeleteMarked_with_Permanent_true_carries_it_through_to_the_effect()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [File1 with { IsMarked = true }], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (_, effects) = Transition.Apply(state, new Msg.DeleteMarked(0, Permanent: true));

        var effect = Assert.IsType<Effect.DeleteToRecycleBin>(Assert.Single(effects));
        Assert.True(effect.Permanent);
    }

    [Fact]
    public void DeleteMarked_ignores_marked_drives()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive with { IsMarked = true }], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.DeleteMarked(0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DeleteMarked_with_no_marks_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.DeleteMarked(0));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DeleteMarked_with_out_of_range_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.DeleteMarked(9));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void MarkRange_marks_every_entry_in_the_range_replacing_other_marks()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"),
            // Distinct names in sorted order: this test is about the range, and a fixture that the
            // sort would rearrange would be testing the sort instead.
            [Dir with { IsMarked = true }, File1, File2, new Entry("c.txt", EntryKind.File, IsMarked: true)],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.MarkRange(0, 1, 2, Additive: false));

        Assert.False(next.Columns[0].Entries[0].IsMarked);
        Assert.True(next.Columns[0].Entries[1].IsMarked);
        Assert.True(next.Columns[0].Entries[2].IsMarked);
        Assert.False(next.Columns[0].Entries[3].IsMarked);
        Assert.Equal(2, next.Columns[0].Cursor);
        // The cursor lands on File2 (entries[2]), triggering a preview load.
        Assert.Equal(@"C:\b.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void MarkRange_normalizes_a_reversed_from_to()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.MarkRange(0, FromIndex: 2, ToIndex: 0, Additive: false));

        Assert.All(next.Columns[0].Entries, e => Assert.True(e.IsMarked));
        Assert.Equal(0, next.Columns[0].Cursor);
    }

    [Fact]
    public void MarkRange_clamps_from_and_to_into_the_entries_range()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.MarkRange(0, FromIndex: -5, ToIndex: 50, Additive: false));

        Assert.All(next.Columns[0].Entries, e => Assert.True(e.IsMarked));
        Assert.Equal(2, next.Columns[0].Cursor);
    }

    [Fact]
    public void MarkRange_additive_keeps_existing_marks_outside_the_range()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir with { IsMarked = true }, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.MarkRange(0, 1, 2, Additive: true));

        Assert.True(next.Columns[0].Entries[0].IsMarked);
        Assert.True(next.Columns[0].Entries[1].IsMarked);
        Assert.True(next.Columns[0].Entries[2].IsMarked);
    }

    [Fact]
    public void MarkRange_non_additive_unmarks_entries_outside_the_range()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir with { IsMarked = true }, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.MarkRange(0, 1, 2, Additive: false));

        Assert.False(next.Columns[0].Entries[0].IsMarked);
        Assert.True(next.Columns[0].Entries[1].IsMarked);
        Assert.True(next.Columns[0].Entries[2].IsMarked);
    }

    [Fact]
    public void MarkRange_sets_cursor_to_the_clamped_to_index()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.MarkRange(0, 0, 1, Additive: false));

        Assert.Equal(1, next.Columns[0].Cursor);
    }

    [Fact]
    public void MarkRange_on_empty_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.MarkRange(0, 0, 0, Additive: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void MarkRange_with_out_of_range_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.MarkRange(9, 0, 0, Additive: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DirectoryLoaded_carries_marks_over_by_exact_name_match()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [File1 with { IsMarked = true }, File2], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [File1, File2]));

        Assert.True(next.Columns[0].Entries[0].IsMarked);
        Assert.False(next.Columns[0].Entries[1].IsMarked);
    }

    [Fact]
    public void DirectoryLoaded_drops_marks_for_names_that_vanished()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [File1 with { IsMarked = true }], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [File2]));

        Assert.Single(next.Columns[0].Entries);
        Assert.False(next.Columns[0].Entries[0].IsMarked);
    }

    [Fact]
    public void ShellOpCompleted_refreshes_the_completing_column_and_any_column_showing_an_affected_dir()
    {
        // A move out of column 1 ("C:\sub", the source's parent) dropped onto column 0 ("C:\",
        // the destination) completes against column 0. Column 2 shows an unrelated directory and
        // must be left alone.
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loading),
            new Column(new Location.RealDirectory(@"C:\sub"), [File2], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"D:\unrelated"), [], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(
            state, new Msg.ShellOpCompleted(0, new Location.RealDirectory(@"C:\"), [@"C:\", @"C:\sub"]));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        Assert.Equal(LoadState.Loaded, next.Columns[2].Load);
        Assert.Equal(2, effects.Count);
        var first = Assert.IsType<Effect.ReadDirectory>(effects[0]);
        Assert.Equal(0, first.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\"), first.Location);
        var second = Assert.IsType<Effect.ReadDirectory>(effects[1]);
        Assert.Equal(1, second.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), second.Location);
    }

    [Fact]
    public void ShellOpCompleted_with_no_affected_dirs_refreshes_only_the_completing_column()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loading),
            new Column(new Location.RealDirectory(@"C:\sub"), [File2], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ShellOpCompleted(0, new Location.RealDirectory(@"C:\"), []));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(LoadState.Loaded, next.Columns[1].Load);
        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(0, effect.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\"), effect.Location);
    }

    [Fact]
    public void ShellOpCompleted_for_a_delete_refreshes_the_deleted_targets_parent_column()
    {
        // Deleting a file from column 1 ("C:\sub") completes against that same column, and its
        // parent (also "C:\sub", the deleted target's parent) is itself the affected dir.
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File2], Cursor: 0, Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(
            state, new Msg.ShellOpCompleted(1, new Location.RealDirectory(@"C:\sub"), [@"C:\sub"]));

        Assert.Equal(LoadState.Loaded, next.Columns[0].Load);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(1, effect.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), effect.Location);
    }

    [Fact]
    public void ShellOpCompleted_with_wrong_path_is_ignored_as_stale()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.ShellOpCompleted(0, new Location.RealDirectory(@"C:\stale"), []));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ShellOpCompleted_with_out_of_range_column_is_ignored_as_stale()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loading));

        var (next, effects) = Transition.Apply(state, new Msg.ShellOpCompleted(9, new Location.RealDirectory(@"C:\"), []));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ShellOpFailed_sets_error_and_keeps_old_entries()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.ShellOpFailed(0, new Location.RealDirectory(@"C:\"), "access denied"));

        Assert.Equal(LoadState.Error, next.Columns[0].Load);
        Assert.Equal("access denied", next.Columns[0].ErrorMessage);
        Assert.Single(next.Columns[0].Entries);
        Assert.Empty(effects);
    }

    [Fact]
    public void ShellOpFailed_with_wrong_path_is_ignored_as_stale()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.ShellOpFailed(0, new Location.RealDirectory(@"C:\stale"), "oops"));

        Assert.Equal(state, next);
    }

    [Fact]
    public void DropFiles_onto_column_background_marks_column_loading_and_emits_ShellCopyOrMove()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\src\a.txt", @"C:\src\b.txt"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(2, next.Columns[0].Entries.Length);

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.Equal(0, effect.ColumnIndex);
        Assert.Equal(new Location.RealDirectory(@"C:\dest"), effect.ColumnLocation);
        Assert.Equal(@"C:\dest", effect.DestPath);
        Assert.Equal(paths, effect.Paths);
        Assert.True(effect.IsMove); // same volume (C:) by default
    }

    [Fact]
    public void DropFiles_onto_a_directory_row_resolves_dest_to_its_child_path()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\src\a.txt"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: 0, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.Equal(new Location.RealDirectory(@"C:\dest"), effect.ColumnLocation);
        Assert.Equal(@"C:\dest\sub", effect.DestPath);
        Assert.Equal(paths, effect.Paths);
    }

    [Fact]
    public void DropFiles_onto_a_file_row_falls_back_to_the_columns_own_path()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: 1, paths, ShiftHeld: false, CtrlHeld: false));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.Equal(@"C:\dest", effect.DestPath);
    }

    [Fact]
    public void DropFiles_onto_a_drive_row_on_the_root_column_resolves_dest_to_the_drive()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive], Cursor: 0, Load: LoadState.Loaded));
        ImmutableArray<string> paths = [@"D:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: 0, paths, ShiftHeld: false, CtrlHeld: false));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.Equal(@"C:\", effect.DestPath);
    }

    /// <summary>
    /// Dropping into the drive pane adds places, rather than doing nothing.
    /// </summary>
    /// <remarks>
    /// Reversed deliberately: this used to assert the drop was ignored, which is what made dragging
    /// a folder onto the お気に入り header appear broken. There is nowhere in this pane to copy
    /// *to*, so the only thing a drop can sensibly mean is "keep this here".
    /// </remarks>
    [Fact]
    public void DropFiles_onto_the_drive_pane_adds_the_dropped_places()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive], Cursor: 0, Load: LoadState.Loaded));
        ImmutableArray<string> paths = [@"C:\src", @"\\srv\share"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(state, next);
        Assert.Collection(
            effects,
            e => Assert.Equal(new Effect.SetPinned(@"C:\src", Pin: true), e),
            e => Assert.Equal(new Effect.SetPinned(@"\\srv\share", Pin: true), e));
    }

    [Fact]
    public void DropFiles_onto_a_header_adds_the_dropped_places_too()
    {
        var state = StateWithColumns(DrivePaneColumn());
        ImmutableArray<string> paths = [@"C:\src"];

        // Index 0 is a header - a label, not a place to copy into.
        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: 0, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(new Effect.SetPinned(@"C:\src", Pin: true), Assert.Single(effects));
    }

    [Fact]
    public void DropFiles_onto_a_row_inside_the_drive_pane_still_copies_into_it()
    {
        // Only the pane itself and its headers mean "keep this here"; a folder row is a destination
        // like any other.
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("お気に入り", EntryKind.Header, Group: EntryGroups.Favorites),
                new Entry(@"C:\Users\user", EntryKind.Directory, Group: EntryGroups.Favorites,
                    Target: new Location.RealDirectory(@"C:\Users\user"), DisplayName: "ホーム"),
            ],
            Cursor: 1,
            Load: LoadState.Loaded);
        ImmutableArray<string> paths = [@"D:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            StateWithColumns(column), new Msg.DropFiles(0, TargetEntryIndex: 1, paths, ShiftHeld: false, CtrlHeld: false));

        var copy = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.Equal(@"C:\Users\user", copy.DestPath);
    }

    [Fact]
    public void A_UNC_path_is_a_network_place_and_anything_else_is_a_favorite()
    {
        // The rule one gesture relies on: the section follows the path, not the key that added it.
        Assert.Equal(EntryGroups.Pinned, EntryGroups.ForPath(@"\\srv\share"));
        Assert.Equal(EntryGroups.Pinned, EntryGroups.ForPath(@"\\srv\share\deep\folder"));
        Assert.Equal(EntryGroups.Favorites, EntryGroups.ForPath(@"C:\work"));
        Assert.Equal(EntryGroups.Favorites, EntryGroups.ForPath(@"D:\"));
    }

    [Fact]
    public void DropFiles_with_empty_paths_is_ignored()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, [], ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DropFiles_with_out_of_range_column_is_ignored()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded));
        ImmutableArray<string> paths = [@"C:\src\a.txt"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(9, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DropFiles_silently_ignores_a_source_already_located_at_dest()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\dest\already-here.txt"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DropFiles_silently_ignores_a_directory_dropped_onto_itself()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\dest"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DropFiles_silently_ignores_a_source_whose_subtree_contains_dest()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest\child"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\dest"];

        var (next, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void DropFiles_filters_out_only_the_no_op_sources_keeping_the_rest()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\dest\already-here.txt", @"C:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.Equal([@"C:\src\a.txt"], effect.Paths);
    }

    [Fact]
    public void DropFiles_with_shift_held_forces_move_even_across_volumes()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"D:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: true, CtrlHeld: false));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.True(effect.IsMove);
    }

    [Fact]
    public void DropFiles_with_ctrl_held_forces_copy_even_on_the_same_volume()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: true));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.False(effect.IsMove);
    }

    [Fact]
    public void DropFiles_shift_wins_when_both_shift_and_ctrl_are_held()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: true, CtrlHeld: true));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.True(effect.IsMove);
    }

    [Fact]
    public void DropFiles_defaults_to_move_when_source_and_dest_share_a_volume()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"C:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.True(effect.IsMove);
    }

    [Fact]
    public void DropFiles_defaults_to_copy_when_source_and_dest_are_on_different_volumes()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> paths = [@"D:\src\a.txt"];

        var (_, effects) = Transition.Apply(
            state, new Msg.DropFiles(0, TargetEntryIndex: -1, paths, ShiftHeld: false, CtrlHeld: false));

        var effect = Assert.IsType<Effect.ShellCopyOrMove>(Assert.Single(effects));
        Assert.False(effect.IsMove);
    }

    [Fact]
    public void PasteRequested_appends_queued_job_and_emits_RunFileJob()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> sources = [@"C:\src\a.txt", @"C:\src\b.txt"];

        var (next, effects) = Transition.Apply(state, new Msg.PasteRequested(0, sources, IsMove: false));

        var job = Assert.Single(next.Jobs);
        Assert.Equal(1, job.JobId);
        Assert.Equal(JobKind.Copy, job.Kind);
        Assert.Equal(sources, job.Sources);
        Assert.Equal(@"C:\dest", job.DestDir);
        Assert.Equal(JobStatus.Queued, job.Status);
        Assert.Equal(2, next.NextJobId);

        var effect = Assert.IsType<Effect.RunFileJob>(Assert.Single(effects));
        Assert.Equal(1, effect.JobId);
        Assert.Equal(JobKind.Copy, effect.Kind);
        Assert.Equal(sources, effect.Sources);
        Assert.Equal(@"C:\dest", effect.DestDir);
    }

    [Fact]
    public void PasteRequested_clears_CutPending()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column) with { CutPending = [@"C:\src\a.txt"] };

        var (next, _) = Transition.Apply(state, new Msg.PasteRequested(0, [@"C:\src\a.txt"], IsMove: false));

        Assert.Empty(next.CutPending);
    }

    [Fact]
    public void PasteRequested_that_is_a_complete_no_op_leaves_CutPending_untouched()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded))
            with
        { CutPending = [@"C:\src\a.txt"] };

        var (next, _) = Transition.Apply(
            state, new Msg.PasteRequested(0, [@"C:\dest\already-here.txt"], IsMove: false));

        Assert.Equal([@"C:\src\a.txt"], next.CutPending);
    }

    [Fact]
    public void PasteRequested_with_IsMove_true_creates_a_Move_job()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        ImmutableArray<string> sources = [@"C:\src\a.txt"];

        var (next, effects) = Transition.Apply(state, new Msg.PasteRequested(0, sources, IsMove: true));

        Assert.Equal(JobKind.Move, next.Jobs[0].Kind);
        var effect = Assert.IsType<Effect.RunFileJob>(Assert.Single(effects));
        Assert.Equal(JobKind.Move, effect.Kind);
    }

    [Fact]
    public void PasteRequested_increments_NextJobId_across_multiple_pastes()
    {
        var column = new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (once, _) = Transition.Apply(state, new Msg.PasteRequested(0, [@"C:\src\a.txt"], IsMove: false));
        var (twice, _) = Transition.Apply(once, new Msg.PasteRequested(0, [@"C:\src\b.txt"], IsMove: false));

        Assert.Equal(2, twice.Jobs.Length);
        Assert.Equal(1, twice.Jobs[0].JobId);
        Assert.Equal(2, twice.Jobs[1].JobId);
        Assert.Equal(3, twice.NextJobId);
    }

    [Fact]
    public void PasteRequested_onto_root_column_is_ignored()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [Drive], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(
            state, new Msg.PasteRequested(0, [@"C:\src\a.txt"], IsMove: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
        Assert.Empty(next.Jobs);
    }

    [Fact]
    public void PasteRequested_with_empty_sources_is_ignored()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.PasteRequested(0, [], IsMove: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void PasteRequested_with_out_of_range_column_is_ignored()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(
            state, new Msg.PasteRequested(9, [@"C:\src\a.txt"], IsMove: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void PasteRequested_filters_no_op_sources_like_DropFiles_and_is_a_complete_no_op_if_all_filtered()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(
            state, new Msg.PasteRequested(0, [@"C:\dest\already-here.txt"], IsMove: false));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void PasteRequested_filters_only_the_no_op_sources_keeping_the_rest()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [], Load: LoadState.Loaded));
        ImmutableArray<string> sources = [@"C:\dest\already-here.txt", @"C:\src\a.txt"];

        var (next, effects) = Transition.Apply(state, new Msg.PasteRequested(0, sources, IsMove: false));

        var effect = Assert.IsType<Effect.RunFileJob>(Assert.Single(effects));
        Assert.Equal([@"C:\src\a.txt"], effect.Sources);
        Assert.Equal([@"C:\src\a.txt"], next.Jobs[0].Sources);
    }

    [Fact]
    public void JobProgress_updates_matching_job_and_sets_status_running()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest");
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(
            state, new Msg.JobProgress(1, DoneFiles: 2, TotalFiles: 5, DoneBytes: 200, TotalBytes: 500, CurrentFile: "a.txt"));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.Running, updated.Status);
        Assert.Equal(2, updated.DoneFiles);
        Assert.Equal(5, updated.TotalFiles);
        Assert.Equal(200, updated.DoneBytes);
        Assert.Equal(500, updated.TotalBytes);
        Assert.Equal("a.txt", updated.CurrentFile);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobProgress_with_unknown_JobId_is_ignored()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest");
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(
            state, new Msg.JobProgress(99, 0, 0, 0, 0, null));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobCompleted_marks_job_completed_and_fills_done_from_total()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: JobStatus.Running, TotalFiles: 3, CurrentFile: "a.txt");
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobCompleted(1, SkippedFiles: 1, AffectedDirs: []));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.Completed, updated.Status);
        Assert.Equal(3, updated.DoneFiles);
        Assert.Null(updated.CurrentFile);
        Assert.Equal(1, updated.SkippedFiles);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobCompleted_without_skips_removes_the_job_so_the_strip_auto_clears()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: JobStatus.Running, TotalFiles: 3, CurrentFile: "a.txt");
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobCompleted(1, SkippedFiles: 0, AffectedDirs: []));

        Assert.Empty(next.Jobs);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobCompleted_with_skips_keeps_the_job_until_dismissed()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: JobStatus.Running, TotalFiles: 3, CurrentFile: "a.txt");
        var state = AppState.Initial with { Jobs = [job] };

        var (next, _) = Transition.Apply(state, new Msg.JobCompleted(1, SkippedFiles: 1, AffectedDirs: []));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.Completed, updated.Status);
        Assert.Equal(1, updated.SkippedFiles);
    }

    [Fact]
    public void JobCompleted_refreshes_columns_matching_AffectedDirs()
    {
        var job = new Job(1, JobKind.Move, [@"C:\sub\a.txt"], @"C:\dest");
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\dest"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File1], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"D:\unrelated"), [], Cursor: 0, Load: LoadState.Loaded)) with
        { Jobs = [job] };

        var (next, effects) = Transition.Apply(
            state, new Msg.JobCompleted(1, SkippedFiles: 0, AffectedDirs: [@"C:\dest", @"C:\sub"]));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
        Assert.Equal(LoadState.Loaded, next.Columns[2].Load);
        Assert.Equal(2, effects.Count);
    }

    [Fact]
    public void JobCompleted_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobCompleted(1, 0, []));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobFailed_marks_job_failed_with_error()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: JobStatus.Running, CurrentFile: "a.txt");
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobFailed(1, "disk full"));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.Failed, updated.Status);
        Assert.Equal("disk full", updated.Error);
        Assert.Null(updated.CurrentFile);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobFailed_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobFailed(1, "oops"));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobCancelled_marks_job_cancelled_and_refreshes_affected_dirs()
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: JobStatus.Running, DoneFiles: 1, CurrentFile: "a.txt");
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [Dir], Cursor: 0, Load: LoadState.Loaded)) with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobCancelled(1, [@"C:\dest"]));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.Cancelled, updated.Status);
        Assert.Equal(1, updated.DoneFiles); // partial progress preserved, not forced to TotalFiles
        Assert.Null(updated.CurrentFile);
        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Single(effects);
    }

    [Fact]
    public void JobCancelled_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobCancelled(1, []));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    public void JobCancelRequested_emits_CancelJob_when_queued_or_running(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobCancelRequested(1));

        Assert.Equal(state, next);
        var effect = Assert.IsType<Effect.CancelJob>(Assert.Single(effects));
        Assert.Equal(1, effect.JobId);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void JobCancelRequested_is_a_no_op_for_finished_jobs(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobCancelRequested(1));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobCancelRequested_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobCancelRequested(1));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void JobDismissed_removes_finished_job(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobDismissed(1));

        Assert.Empty(next.Jobs);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    public void JobDismissed_is_a_no_op_for_unfinished_jobs(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobDismissed(1));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobDismissed_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobDismissed(1));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    public void JobConflictsFound_moves_queued_or_running_job_to_WaitingConflict(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobConflictsFound(1, 3));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.WaitingConflict, updated.Status);
        Assert.Equal(3, updated.ConflictCount);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(JobStatus.WaitingConflict)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void JobConflictsFound_is_a_no_op_for_jobs_not_queued_or_running(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobConflictsFound(1, 3));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobConflictsFound_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobConflictsFound(1, 3));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Theory]
    [InlineData(ConflictDecision.Overwrite)]
    [InlineData(ConflictDecision.Skip)]
    [InlineData(ConflictDecision.Cancel)]
    public void JobConflictResolved_moves_WaitingConflict_job_back_to_Running_and_emits_ResolveJobConflict(
        ConflictDecision decision)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: JobStatus.WaitingConflict, ConflictCount: 2);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobConflictResolved(1, decision));

        var updated = Assert.Single(next.Jobs);
        Assert.Equal(JobStatus.Running, updated.Status);
        var effect = Assert.IsType<Effect.ResolveJobConflict>(Assert.Single(effects));
        Assert.Equal(1, effect.JobId);
        Assert.Equal(decision, effect.Decision);
    }

    [Theory]
    [InlineData(JobStatus.Queued)]
    [InlineData(JobStatus.Running)]
    [InlineData(JobStatus.Completed)]
    [InlineData(JobStatus.Failed)]
    [InlineData(JobStatus.Cancelled)]
    public void JobConflictResolved_is_a_no_op_when_not_WaitingConflict(JobStatus status)
    {
        var job = new Job(1, JobKind.Copy, [@"C:\src\a.txt"], @"C:\dest", Status: status);
        var state = AppState.Initial with { Jobs = [job] };

        var (next, effects) = Transition.Apply(state, new Msg.JobConflictResolved(1, ConflictDecision.Overwrite));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void JobConflictResolved_with_unknown_JobId_is_ignored()
    {
        var state = AppState.Initial;

        var (next, effects) = Transition.Apply(state, new Msg.JobConflictResolved(1, ConflictDecision.Overwrite));

        Assert.Equal(state, next);
        Assert.Empty(effects);
    }

    [Fact]
    public void ExternalDirectoryChanged_refreshes_matching_columns()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\dest"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"D:\unrelated"), [], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged(@"C:\dest"));

        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(LoadState.Loaded, next.Columns[1].Load);
        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(0, effect.ColumnIndex);
    }

    [Fact]
    public void ExternalDirectoryChanged_with_no_matching_column_is_a_no_op()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\dest"), [Dir], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged(@"D:\elsewhere"));

        Assert.Equal(LoadState.Loaded, next.Columns[0].Load);
        Assert.Empty(effects);
    }

    [Fact]
    public void CursorMove_onto_a_file_emits_LoadPreview_with_incremented_generation()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        Assert.Equal(0, state.Preview.Generation);

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.Equal(PreviewKind.Loading, next.Preview.Kind);
        Assert.Equal(@"C:\a.txt", next.Preview.Path);
        Assert.Equal(1, next.Preview.Generation);
        var effect = Assert.IsType<Effect.LoadPreview>(Assert.Single(effects));
        Assert.Equal(1, effect.Generation);
        Assert.Equal(@"C:\a.txt", effect.Path);
    }

    /// <summary>
    /// A file in the recycle bin is read from where the bin keeps it, but described by where it came
    /// from - the only readable thing about it, since the bin's own name for it is $R00L0W8.txt.
    /// </summary>
    [Fact]
    public void CursorMove_onto_a_deleted_file_carries_where_it_came_from_into_the_preview()
    {
        var deleted = new Entry(
            @"C:\$Recycle.Bin\S-1-5-21-1\$R00L0W8.txt",
            EntryKind.File,
            DisplayName: "notes.txt",
            OriginalPath: @"D:\projects\notes.txt");
        var column = new Column(
            Location.RecycleBin.Instance,
            [Dir, deleted],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.Equal(@"C:\$Recycle.Bin\S-1-5-21-1\$R00L0W8.txt", next.Preview.Path);
        Assert.Equal(@"D:\projects\notes.txt", next.Preview.OriginalPath);
        Assert.Equal(@"C:\$Recycle.Bin\S-1-5-21-1\$R00L0W8.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void An_ordinary_file_has_no_original_path_in_the_preview()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);

        var (next, _) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        Assert.Null(next.Preview.OriginalPath);
    }

    [Fact]
    public void A_drive_row_asks_for_how_full_it_is_rather_than_its_contents()
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives),
                new Entry(@"C:\", EntryKind.Drive, Group: EntryGroups.Drives, DisplayName: "Windows (C:)"),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (next, effects) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        // Two effects: what to say about the drive, and what to show in the column beside it.
        var effect = Assert.Single(effects.OfType<Effect.LoadPreview>());
        Assert.Equal(PreviewTarget.Volume, effect.Target);
        Assert.Equal(@"C:\", effect.Path);
        Assert.Equal(PreviewKind.Loading, next.Preview.Kind);
    }

    /// <summary>
    /// A drive's path is also a valid directory path, so the effect has to say which question is
    /// being asked - "read this" and "how full is this" cannot be told apart from the path alone.
    /// </summary>
    [Fact]
    public void An_ordinary_file_still_asks_for_its_contents()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        Assert.Equal(PreviewTarget.File, Assert.IsType<Effect.LoadPreview>(Assert.Single(effects)).Target);
    }

    [Fact]
    public void The_bin_row_asks_for_how_much_is_in_it()
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("ゴミ箱", EntryKind.Header, Group: EntryGroups.Trash),
                new Entry(
                    "::recyclebin",
                    EntryKind.Directory,
                    Group: EntryGroups.Trash,
                    Target: Location.RecycleBin.Instance,
                    DisplayName: "ゴミ箱 (413)"),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        var effect = Assert.Single(effects.OfType<Effect.LoadPreview>());
        Assert.Equal(PreviewTarget.RecycleBin, effect.Target);
    }

    /// <summary>
    /// The share itself is a volume; a folder inside it is just a folder, and previewing it as one
    /// would put a capacity bar under every directory a user pins.
    /// </summary>
    [Theory]
    [InlineData(@"\\srv\share", true)]
    [InlineData(@"\\srv\share\", true)]
    [InlineData(@"\\srv\share\sub", false)]
    [InlineData(@"C:\Users\someone", false)]
    public void Only_a_share_root_gets_a_capacity_preview(string path, bool expectsCapacity)
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("ネットワーク", EntryKind.Header, Group: EntryGroups.Pinned),
                new Entry(
                    path,
                    EntryKind.Directory,
                    Group: EntryGroups.Pinned,
                    Target: new Location.RealDirectory(path),
                    IsRemovable: true),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        // Either way the column beside it fills in - it is a directory. The question here is only
        // whether the pane on the right also describes it as a volume.
        if (expectsCapacity)
        {
            Assert.Equal(PreviewTarget.Volume, Assert.Single(effects.OfType<Effect.LoadPreview>()).Target);
        }
        else
        {
            Assert.Empty(effects.OfType<Effect.LoadPreview>());
        }
    }

    [Fact]
    public void A_capacity_result_for_the_current_generation_becomes_the_preview()
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives),
                new Entry(@"C:\", EntryKind.Drive, Group: EntryGroups.Drives),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);
        var (loading, _) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        var capacity = new PreviewCapacity("Windows (C:)", "NTFS · 固定ドライブ", UsedBytes: 750, TotalBytes: 1000);
        var (next, effects) = Transition.Apply(
            loading, new Msg.PreviewCapacityLoaded(loading.Preview.Generation, capacity));

        Assert.Equal(PreviewKind.Capacity, next.Preview.Kind);
        Assert.Equal(capacity, next.Preview.Capacity);
        Assert.Equal(250, next.Preview.Capacity!.FreeBytes);
        Assert.Equal(0.75, next.Preview.Capacity!.UsedFraction);
        Assert.Empty(effects);
    }

    [Fact]
    public void A_capacity_result_from_a_stale_generation_is_ignored()
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives),
                new Entry(@"C:\", EntryKind.Drive, Group: EntryGroups.Drives),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);
        var (loading, _) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));

        var (next, _) = Transition.Apply(
            loading,
            new Msg.PreviewCapacityLoaded(
                loading.Preview.Generation - 1, new PreviewCapacity("stale", "stale", 1, 2)));

        Assert.Equal(PreviewKind.Loading, next.Preview.Kind);
        Assert.Null(next.Preview.Capacity);
    }

    /// <summary>The bin has a size but no size limit, so there is nothing for a bar to be full of.</summary>
    [Fact]
    public void A_capacity_without_a_total_has_no_fraction_and_no_free_space()
    {
        var bin = new PreviewCapacity("ゴミ箱", "ゴミ箱", UsedBytes: 12345, TotalBytes: null, ItemCount: 413);

        Assert.Null(bin.UsedFraction);
        Assert.Null(bin.FreeBytes);
    }

    [Fact]
    public void CursorMove_onto_a_directory_clears_the_preview_and_shows_the_directory_instead()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column) with
        {
            Preview = new PreviewState(3, @"C:\a.txt", PreviewKind.Text, "hi", [], null),
        };

        var (next, effects) = Transition.Apply(state, new Msg.CursorUp(0));

        Assert.Equal(PreviewKind.None, next.Preview.Kind);
        Assert.Null(next.Preview.Path);
        Assert.Equal(4, next.Preview.Generation);

        // A folder's preview is the column beside it, not the pane on the right.
        Assert.Equal(
            new Effect.ReadDirectory(1, new Location.RealDirectory(@"C:\sub"), Speculative: true),
            Assert.Single(effects));
        Assert.Equal(2, next.Columns.Length);
        Assert.Equal(LoadState.Loading, next.Columns[1].Load);
    }

    private static Column DrivePaneColumn(int cursor = 0) => new(
        Location.Drives.Instance,
        [
            new Entry("ドライブ", EntryKind.Header, Group: "drives"),
            new Entry(@"C:\", EntryKind.Drive, Group: "drives", DisplayName: "Windows (C:)"),
            new Entry(@"D:\", EntryKind.Drive, Group: "drives", DisplayName: "Data (D:)"),
            new Entry("ゴミ箱", EntryKind.Header, Group: "trash"),
            new Entry("::trash", EntryKind.Directory, Group: "trash", DisplayName: "ゴミ箱"),
        ],
        Cursor: cursor,
        Load: LoadState.Loaded);

    [Fact]
    public void Space_on_a_header_collapses_its_section_and_leaves_the_header()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 0));

        var (next, _) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));

        var column = next.Columns[0];
        Assert.Equal(3, column.Entries.Length);
        Assert.Equal("ドライブ", column.Entries[0].Name);
        Assert.DoesNotContain(column.Entries, e => e.Kind == EntryKind.Drive);

        // Nothing was read again - the rows are still there, just not shown.
        Assert.Equal(5, column.AllEntries.Length);

        // The cursor stays on the header you pressed, so pressing again puts them back.
        Assert.Equal(0, column.Cursor);
    }

    [Fact]
    public void Space_on_a_header_a_second_time_expands_it_again()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 0));

        var (collapsed, _) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));
        var (expanded, _) = Transition.Apply(collapsed, new Msg.ToggleMarkAtCursor(0));

        Assert.Equal(5, expanded.Columns[0].Entries.Length);
        Assert.Empty(expanded.Columns[0].CollapsedGroups);
    }

    [Fact]
    public void Collapsing_a_section_asks_for_the_whole_set_to_be_remembered()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 0));

        var (collapsed, effects) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));

        var effect = Assert.IsType<Effect.SetCollapsedGroups>(Assert.Single(effects));
        Assert.Equal(["drives"], effect.Groups);

        // Expanding it again must store the empty set, not simply stop mentioning it - otherwise the
        // section would come back collapsed forever.
        var (expanded, expandEffects) = Transition.Apply(collapsed, new Msg.ToggleMarkAtCursor(0));
        Assert.Empty(Assert.IsType<Effect.SetCollapsedGroups>(Assert.Single(expandEffects)).Groups);
        Assert.Empty(expanded.Columns[0].CollapsedGroups);
    }

    [Fact]
    public void Clicking_a_section_toggle_persists_it_the_same_way_as_the_key_does()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 4));

        var (_, effects) = Transition.Apply(state, new Msg.ToggleSection(0, 0));

        Assert.Equal(["drives"], Assert.IsType<Effect.SetCollapsedGroups>(Assert.Single(effects)).Groups);
    }

    [Fact]
    public void Restoring_collapsed_groups_hides_those_sections_without_a_re_read()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 0));

        var (next, effects) = Transition.Apply(state, new Msg.CollapsedGroupsRestored(0, ["drives"]));

        var column = next.Columns[0];
        Assert.Equal(3, column.Entries.Length);
        Assert.Equal(5, column.AllEntries.Length);
        Assert.Equal(["drives"], column.CollapsedGroups);
        Assert.Empty(effects);
        Assert.Empty(next.CheckInvariants());
    }

    /// <summary>
    /// A stored name is a wish, not a claim about what exists - a section that has since been removed
    /// or renamed must not break the pane.
    /// </summary>
    [Fact]
    public void Restoring_a_section_that_no_longer_exists_hides_nothing()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 0));

        var (next, _) = Transition.Apply(state, new Msg.CollapsedGroupsRestored(0, ["gone"]));

        Assert.Equal(5, next.Columns[0].Entries.Length);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Restoring_collapsed_groups_into_an_ordinary_directory_does_nothing()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CollapsedGroupsRestored(0, ["drives"]));

        Assert.Empty(next.Columns[0].CollapsedGroups);
        Assert.Equal(3, next.Columns[0].Entries.Length);
    }

    [Fact]
    public void A_collapsed_section_keeps_the_cursor_on_a_row_that_is_still_shown()
    {
        // Cursor on D:\, then the section it lives in is collapsed from elsewhere.
        var column = DrivePaneColumn(cursor: 2);
        var state = StateWithColumns(column);

        var (next, _) = Transition.Apply(state, new Msg.CursorHome(0));
        var (collapsed, _) = Transition.Apply(next, new Msg.ToggleMarkAtCursor(0));

        var result = collapsed.Columns[0];
        Assert.InRange(result.Cursor, 0, result.Entries.Length - 1);
        Assert.Empty(collapsed.CheckInvariants());
    }

    [Fact]
    public void Space_in_the_drive_pane_never_marks()
    {
        // Cursor on a drive row, not a header.
        var state = StateWithColumns(DrivePaneColumn(cursor: 1));

        var (next, _) = Transition.Apply(state, new Msg.ToggleMarkAtCursor(0));

        Assert.DoesNotContain(next.Columns[0].AllEntries, e => e.IsMarked);
    }

    [Fact]
    public void A_header_is_not_a_delete_target_and_cannot_be_entered()
    {
        var state = StateWithColumns(DrivePaneColumn(cursor: 0));

        var (afterDelete, deleteEffects) = Transition.Apply(state, new Msg.DeleteEntry(0, 0));
        var (afterEnter, enterEffects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Empty(deleteEffects);
        Assert.Equal(LoadState.Loaded, afterDelete.Columns[0].Load);
        Assert.Empty(enterEffects);
        Assert.Single(afterEnter.Columns);
    }

    [Fact]
    public void DirectoryLoaded_keeps_the_cursor_on_the_same_entry_when_one_above_it_disappears()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 2, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        // Dir was deleted externally. Index 2 now names nothing; the cursor should stay on File2.
        var (next, _) = Transition.Apply(
            state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [File1, File2]));

        Assert.Equal(1, next.Columns[0].Cursor);
        Assert.Equal(File2.Name, next.Columns[0].Entries[next.Columns[0].Cursor].Name);
    }

    [Fact]
    public void DirectoryLoaded_falls_back_to_the_index_when_the_cursor_entry_is_gone()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        // File1 itself was deleted - there is no name to follow, so the cursor holds its position.
        var (next, _) = Transition.Apply(
            state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [Dir, File2]));

        Assert.Equal(1, next.Columns[0].Cursor);
        Assert.Equal(File2.Name, next.Columns[0].Entries[1].Name);
    }

    [Fact]
    public void A_column_built_from_one_list_treats_all_of_it_as_visible()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);

        Assert.Equal(column.Entries, column.AllEntries);
        Assert.Empty(new AppState { Columns = [column], FocusedColumn = 0 }.CheckInvariants());
    }

    [Fact]
    public void A_visible_list_that_is_not_drawn_from_AllEntries_is_an_invariant_violation()
    {
        // Guards the rule the derived view rests on: a visible row must have come from a read.
        var broken = new Column(
            new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded)
        {
            AllEntries = [Dir],
        };

        var violations = new AppState { Columns = [broken], FocusedColumn = 0 }.CheckInvariants();

        Assert.Contains(violations, v => v.Contains("not in AllEntries", StringComparison.Ordinal));
    }

    /// <summary>
    /// The other half of the rule, and the half that survived sorting: reordering the view is fine,
    /// showing the same row twice is not.
    /// </summary>
    [Fact]
    public void A_visible_row_shown_twice_is_an_invariant_violation()
    {
        var broken = new Column(
            new Location.RealDirectory(@"C:\"), [Dir, Dir], Cursor: 0, Load: LoadState.Loaded)
        {
            AllEntries = [Dir, File1],
        };

        var violations = new AppState { Columns = [broken], FocusedColumn = 0 }.CheckInvariants();

        Assert.Contains(violations, v => v.Contains("not in AllEntries", StringComparison.Ordinal));
    }

    /// <summary>A reordered view is legitimate now that sorting exists, and must not be a violation.</summary>
    [Fact]
    public void A_reordered_visible_list_is_fine()
    {
        var reordered = new Column(
            new Location.RealDirectory(@"C:\"), [File1, Dir], Cursor: 0, Load: LoadState.Loaded)
        {
            AllEntries = [Dir, File1],
        };

        Assert.Empty(new AppState { Columns = [reordered], FocusedColumn = 0 }.CheckInvariants());
    }

    [Fact]
    public void A_mark_on_a_hidden_entry_survives_and_is_not_acted_on_while_hidden()
    {
        // Hand-built narrowed view: File1 is marked but not currently shown.
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [Dir, File2], Cursor: 0, Load: LoadState.Loaded)
        {
            AllEntries = [Dir, File1 with { IsMarked = true }, File2],
        };
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DeleteMarked(0, Permanent: false));

        // Nothing visible is marked, so there is nothing to delete - the hidden mark is not acted on.
        Assert.Empty(effects);
        Assert.True(next.Columns[0].AllEntries[1].IsMarked);
    }

    [Fact]
    public void CursorMove_off_a_still_loading_preview_cancels_the_abandoned_generation()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column) with
        {
            Preview = new PreviewState(3, @"C:\a.txt", PreviewKind.Loading, null, [], null),
        };

        var (next, effects) = Transition.Apply(state, new Msg.CursorDown(0)); // File1 -> File2

        Assert.Equal(4, next.Preview.Generation);
        var cancel = Assert.IsType<Effect.CancelPreview>(effects[0]);
        Assert.Equal(3, cancel.Generation);
        var load = Assert.IsType<Effect.LoadPreview>(effects[1]);
        Assert.Equal(4, load.Generation);
        Assert.Equal(2, effects.Count);
    }

    [Fact]
    public void CursorMove_off_a_completed_preview_does_not_cancel_anything()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column) with
        {
            // Already delivered - there is no outstanding work to call off.
            Preview = new PreviewState(3, @"C:\a.txt", PreviewKind.Text, "hi", [], null),
        };

        var (_, effects) = Transition.Apply(state, new Msg.CursorDown(0));

        Assert.IsType<Effect.LoadPreview>(Assert.Single(effects));
    }

    [Fact]
    public void CursorMove_from_a_loading_preview_onto_a_directory_cancels_without_reloading()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 1, Load: LoadState.Loaded);
        var state = StateWithColumns(column) with
        {
            Preview = new PreviewState(3, @"C:\a.txt", PreviewKind.Loading, null, [], null),
        };

        var (next, effects) = Transition.Apply(state, new Msg.CursorUp(0)); // File1 -> Dir

        Assert.Equal(PreviewKind.None, next.Preview.Kind);
        var cancel = Assert.IsType<Effect.CancelPreview>(Assert.Single(effects.OfType<Effect.CancelPreview>()));
        Assert.Equal(3, cancel.Generation);
        Assert.Empty(effects.OfType<Effect.LoadPreview>());
    }

    [Fact]
    public void Repeated_Apply_with_the_same_cursor_target_does_not_re_emit_LoadPreview()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1, File2], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (afterMove, moveEffects) = Transition.Apply(state, new Msg.CursorDown(0)); // lands on File1
        Assert.Single(moveEffects); // sanity: the move itself does trigger a load

        var (again, againEffects) = Transition.Apply(afterMove, new Msg.CursorTo(0, 1)); // same index - no-op

        Assert.Empty(againEffects);
        Assert.Equal(afterMove.Preview, again.Preview);
    }

    [Fact]
    public void DirectoryLoaded_that_lands_the_cursor_on_a_file_triggers_a_preview_load()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loading);
        var state = StateWithColumns(column);

        var (next, effects) = Transition.Apply(state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [File1]));

        Assert.Equal(PreviewKind.Loading, next.Preview.Kind);
        Assert.Equal(@"C:\a.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    /// <summary>
    /// Starting point shared by the <c>Msg.PreviewLoaded</c>/<c>Msg.PreviewFailed</c> tests below:
    /// a column whose cursor moves from a directory onto a file, so the move itself (via
    /// <c>Transition.ReconcilePreview</c>) is what produces the live generation these tests react
    /// against - constructing a state with the cursor pre-positioned on a file bypasses that
    /// reconciliation entirely and leaves <see cref="AppState.Preview"/> stuck at
    /// <see cref="PreviewKind.None"/> generation 0.
    /// </summary>
    private static AppState StateWithLoadingPreview()
    {
        var column = new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);
        var (afterMove, _) = Transition.Apply(StateWithColumns(column), new Msg.CursorDown(0));
        Assert.Equal(PreviewKind.Loading, afterMove.Preview.Kind); // sanity
        return afterMove;
    }

    [Fact]
    public void PreviewLoaded_with_matching_generation_fills_in_image_bytes()
    {
        var afterCursor = StateWithLoadingPreview();
        var generation = afterCursor.Preview.Generation;
        ImmutableArray<byte> bytes = [1, 2, 3, 4];

        var (next, effects) = Transition.Apply(
            afterCursor, new Msg.PreviewLoaded(generation, PreviewKind.Image, null, bytes, null));

        Assert.Equal(PreviewKind.Image, next.Preview.Kind);
        Assert.Equal(bytes, next.Preview.ImageBytes);
        Assert.Null(next.Preview.Text);
        Assert.Empty(effects);
    }

    [Fact]
    public void PreviewLoaded_binary_stores_the_label_in_Text()
    {
        var afterCursor = StateWithLoadingPreview();
        var generation = afterCursor.Preview.Generation;

        var (next, _) = Transition.Apply(
            afterCursor, new Msg.PreviewLoaded(generation, PreviewKind.Binary, null, [], "PE Executable"));

        Assert.Equal(PreviewKind.Binary, next.Preview.Kind);
        Assert.Equal("PE Executable", next.Preview.Text);
    }

    /// <summary>
    /// The hex-dump view (Phase 5 fix) depends on <c>ImageBytes</c> surviving a Binary result -
    /// unlike the label (which lands in <c>Text</c>), it must not be dropped just because the kind
    /// is not <see cref="PreviewKind.Image"/>. See <see cref="Msg.PreviewLoaded"/>'s remarks.
    /// </summary>
    [Fact]
    public void PreviewLoaded_binary_retains_the_carried_head_bytes()
    {
        var afterCursor = StateWithLoadingPreview();
        var generation = afterCursor.Preview.Generation;
        ImmutableArray<byte> head = [0x4D, 0x5A, 0x90, 0x00];

        var (next, _) = Transition.Apply(
            afterCursor, new Msg.PreviewLoaded(generation, PreviewKind.Binary, null, head, "PE Executable"));

        Assert.Equal(PreviewKind.Binary, next.Preview.Kind);
        Assert.Equal(head, next.Preview.ImageBytes);
    }

    [Fact]
    public void PreviewLoaded_with_stale_generation_is_ignored()
    {
        var afterCursor = StateWithLoadingPreview();

        var (next, effects) = Transition.Apply(
            afterCursor, new Msg.PreviewLoaded(afterCursor.Preview.Generation - 1, PreviewKind.Text, "stale", [], null));

        Assert.Equal(afterCursor.Preview, next.Preview);
        Assert.Empty(effects);
    }

    [Fact]
    public void PreviewLoaded_stores_the_carried_metadata()
    {
        var afterCursor = StateWithLoadingPreview();
        var generation = afterCursor.Preview.Generation;
        var metadata = new PreviewMetadata(
            "file1", 1234, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2), PixelWidth: 640, PixelHeight: 480, BitsPerPixel: 24);

        var (next, _) = Transition.Apply(
            afterCursor, new Msg.PreviewLoaded(generation, PreviewKind.Image, null, [1, 2], null, metadata));

        Assert.Equal(metadata, next.Preview.Metadata);
    }

    [Fact]
    public void PreviewLoaded_without_metadata_defaults_to_null()
    {
        var afterCursor = StateWithLoadingPreview();
        var generation = afterCursor.Preview.Generation;

        var (next, _) = Transition.Apply(
            afterCursor, new Msg.PreviewLoaded(generation, PreviewKind.Text, "hi", [], null));

        Assert.Null(next.Preview.Metadata);
    }

    [Fact]
    public void PreviewFailed_with_matching_generation_sets_error_and_clears_kind()
    {
        var afterCursor = StateWithLoadingPreview();

        var (next, effects) = Transition.Apply(
            afterCursor, new Msg.PreviewFailed(afterCursor.Preview.Generation, "denied"));

        Assert.Equal(PreviewKind.None, next.Preview.Kind);
        Assert.Equal("denied", next.Preview.Error);
        Assert.Empty(effects);
    }

    [Fact]
    public void PreviewFailed_with_stale_generation_is_ignored()
    {
        var afterCursor = StateWithLoadingPreview();

        var (next, effects) = Transition.Apply(
            afterCursor, new Msg.PreviewFailed(afterCursor.Preview.Generation - 1, "denied"));

        Assert.Equal(afterCursor.Preview, next.Preview);
        Assert.Empty(effects);
    }

    [Fact]
    public void FocusColumn_change_to_a_column_whose_cursor_is_on_a_file_triggers_a_preview_load()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"C:\sub"), [File1], Cursor: 0, Load: LoadState.Loaded));

        var (next, effects) = Transition.Apply(state, new Msg.FocusColumn(1));

        Assert.Equal(PreviewKind.Loading, next.Preview.Kind);
        Assert.Equal(@"C:\sub\a.txt", Assert.Single(effects.OfType<Effect.LoadPreview>()).Path);
    }

    [Fact]
    public void EnterDirectory_into_a_child_column_appends_a_LoadPreview_effect_alongside_ReadDirectory_when_relevant()
    {
        // The parent column's cursor lands on the directory it just entered (not a file), and the
        // new child column starts with cursor -1 - so no preview effect should be appended, just
        // the usual ReadDirectory.
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded));

        var (_, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        var effect = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(new Location.RealDirectory(@"C:\sub"), effect.Location);
    }

    [Fact]
    public void SetCutPending_replaces_CutPending_with_the_given_paths()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded));
        ImmutableArray<string> paths = [@"C:\a.txt", @"C:\b.txt"];

        var (next, effects) = Transition.Apply(state, new Msg.SetCutPending(paths));

        Assert.Equal(paths, next.CutPending);
        Assert.Empty(effects);
    }

    [Fact]
    public void SetCutPending_with_an_empty_array_clears_CutPending()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded))
            with
        { CutPending = [@"C:\a.txt"] };

        var (next, _) = Transition.Apply(state, new Msg.SetCutPending([]));

        Assert.Empty(next.CutPending);
    }

    [Fact]
    public void SetCutPending_overwrites_a_previous_pending_cut()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [Dir], Cursor: 0, Load: LoadState.Loaded))
            with
        { CutPending = [@"C:\old.txt"] };

        var (next, _) = Transition.Apply(state, new Msg.SetCutPending([@"C:\new.txt"]));

        Assert.Equal([@"C:\new.txt"], next.CutPending);
    }
    [Fact]
    public void Ctrl_D_pins_the_location_the_focused_column_is_showing()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\work"), [File1], Cursor: 0, Load: LoadState.Loaded));

        var (_, effects) = Transition.Apply(state, new Msg.PinFocusedLocation(0));

        var pin = Assert.IsType<Effect.SetPinned>(Assert.Single(effects));
        Assert.Equal(@"C:\work", pin.Path);
        Assert.True(pin.Pin);
    }

    [Fact]
    public void Ctrl_B_adds_the_folder_under_the_cursor_without_entering_it()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\work"), [Dir, File1], Cursor: 0, Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(StateWithColumns(column), new Msg.PinEntryAtCursor(0));

        var add = Assert.IsType<Effect.SetPinned>(Assert.Single(effects));

        // The folder pointed at, resolved against the column it sits in - not the column itself.
        Assert.Equal(@"C:\work\sub", add.Path);
        Assert.True(add.Pin);
    }

    [Fact]
    public void Ctrl_B_on_a_file_adds_nothing()
    {
        // A file is not a place. Only a row you could open is.
        var column = new Column(
            new Location.RealDirectory(@"C:\work"), [Dir, File1], Cursor: 1, Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(StateWithColumns(column), new Msg.PinEntryAtCursor(0));

        Assert.Empty(effects);
    }

    [Fact]
    public void Ctrl_B_on_a_header_adds_nothing()
    {
        var (_, effects) = Transition.Apply(
            StateWithColumns(DrivePaneColumn(cursor: 0)), new Msg.PinEntryAtCursor(0));

        Assert.Empty(effects);
    }

    [Fact]
    public void Ctrl_B_on_a_drive_row_adds_that_drive()
    {
        var (_, effects) = Transition.Apply(
            StateWithColumns(DrivePaneColumn(cursor: 1)), new Msg.PinEntryAtCursor(0));

        var add = Assert.IsType<Effect.SetPinned>(Assert.Single(effects));
        Assert.Equal(@"C:\", add.Path);
    }

    [Fact]
    public void The_drive_pane_cannot_pin_itself()
    {
        // It has no filesystem path, so there is nothing to pin.
        var state = StateWithColumns(DrivePaneColumn());

        var (_, effects) = Transition.Apply(state, new Msg.PinFocusedLocation(0));

        Assert.Empty(effects);
    }

    [Fact]
    public void Delete_on_a_pinned_row_unpins_it_rather_than_touching_the_folder()
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("ネットワーク", EntryKind.Header, Group: EntryGroups.Pinned),
                new Entry(@"\\srv\share", EntryKind.Directory, Group: EntryGroups.Pinned,
                    Target: new Location.RealDirectory(@"\\srv\share"), DisplayName: "share",
                    IsRemovable: true),
            ],
            Cursor: 1,
            Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (_, effects) = Transition.Apply(state, new Msg.DeleteEntry(0, 1));

        var unpin = Assert.IsType<Effect.SetPinned>(Assert.Single(effects));
        Assert.Equal(@"\\srv\share", unpin.Path);
        Assert.False(unpin.Pin);
    }

    [Fact]
    public void Delete_on_a_favorite_removes_nothing_at_all()
    {
        // Only a pinned place is the user's to remove; a known folder is simply there.
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("お気に入り", EntryKind.Header, Group: EntryGroups.Favorites),
                new Entry(@"C:\Users\user", EntryKind.Directory, Group: EntryGroups.Favorites,
                    Target: new Location.RealDirectory(@"C:\Users\user"), DisplayName: "ホーム"),
            ],
            Cursor: 1,
            Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(StateWithColumns(column), new Msg.DeleteEntry(0, 1));

        Assert.Empty(effects);
    }

    [Fact]
    public void PlacesChanged_re_reads_only_the_drive_panes()
    {
        var state = new AppState
        {
            Columns =
            [
                DrivePaneColumn(),
                new Column(new Location.RealDirectory(@"C:\work"), [File1], Cursor: 0, Load: LoadState.Loaded),
            ],
            FocusedColumn = 0,
        };

        var (next, effects) = Transition.Apply(state, new Msg.PlacesChanged());

        var read = Assert.IsType<Effect.ReadDirectory>(Assert.Single(effects));
        Assert.Equal(0, read.ColumnIndex);
        Assert.Equal(Location.Drives.Instance, read.Location);
        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(LoadState.Loaded, next.Columns[1].Load);
    }

    /// <summary>
    /// Delete in the drive pane must never reach the recycle bin.
    /// </summary>
    /// <remarks>
    /// This was real for one commit. Favorites arrived as <see cref="EntryKind.Directory"/> rows,
    /// and <c>DeleteEntry</c> only refused <c>Drive</c> and <c>Header</c> — so Delete on ホーム
    /// resolved to the profile path and sent <c>C:\Users\user</c> to the recycle bin. The pane holds
    /// places, not files; nothing in it is a deletion target.
    /// </remarks>
    [Fact]
    public void Delete_in_the_drive_pane_never_recycles_anything()
    {
        var column = new Column(
            Location.Drives.Instance,
            [
                new Entry("お気に入り", EntryKind.Header, Group: "favorites"),
                new Entry(@"C:\Users\user", EntryKind.Directory, Group: "favorites",
                    Target: new Location.RealDirectory(@"C:\Users\user"), DisplayName: "ホーム"),
                new Entry(@"C:\", EntryKind.Drive, Group: "drives", DisplayName: "Windows (C:)"),
            ],
            Cursor: 1,
            Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var (afterEntry, entryEffects) = Transition.Apply(state, new Msg.DeleteEntry(0, 1));
        var (afterMarked, markedEffects) = Transition.Apply(state, new Msg.DeleteMarked(0, Permanent: false));
        var (_, permanentEffects) = Transition.Apply(state, new Msg.DeleteEntry(0, 1, Permanent: true));

        Assert.Empty(entryEffects);
        Assert.Empty(markedEffects);
        Assert.Empty(permanentEffects);
        Assert.Equal(LoadState.Loaded, afterEntry.Columns[0].Load);
        Assert.Equal(LoadState.Loaded, afterMarked.Columns[0].Load);
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

    private static Gen<ImmutableArray<string>> GenPaths =>
        GenPath.List[0, 3].Select(list => list.ToImmutableArray());

    /// <summary>
    /// Locations a column or a column-keyed result can refer to. Covers both cases of the union -
    /// the drive list has no filesystem path, which is exactly the branch that used to be the
    /// empty-string sentinel - plus enough distinct real directories that staleness checks
    /// (a result arriving for a location the column no longer shows) are actually exercised.
    /// </summary>
    private static Gen<Location> GenLocation => Gen.OneOfConst<Location>(
        Location.Drives.Instance,
        new Location.RealDirectory(@"C:\"),
        new Location.RealDirectory(@"D:\"),
        new Location.RealDirectory(@"C:\Users"),
        new Location.RealDirectory(@"C:\Users\someone"),
        new Location.RealDirectory(@"C:\stale"));

    private static Gen<bool> GenIsMove => Gen.OneOfConst(true, false);

    private static Gen<int> GenJobId => Gen.Int[-2, 4];

    private static Gen<ConflictDecision> GenConflictDecision => Gen.OneOfConst(
        ConflictDecision.Overwrite, ConflictDecision.Skip, ConflictDecision.Cancel);

    private static Gen<PreviewKind> GenPreviewKind => Gen.OneOfConst(
        PreviewKind.None, PreviewKind.Loading, PreviewKind.Image, PreviewKind.Text, PreviewKind.Binary);

    private static Gen<ImmutableArray<byte>> GenImageBytes => Gen.OneOfConst(
        ImmutableArray<byte>.Empty, ImmutableArray.Create<byte>(1, 2, 3));

    private static Gen<PreviewMetadata?> GenPreviewMetadata => Gen.OneOfConst<PreviewMetadata?>(
        null,
        new PreviewMetadata("file.txt", 1024, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2)),
        new PreviewMetadata(
            "pic.png", 2048, new DateTime(2024, 1, 1), new DateTime(2024, 1, 2), PixelWidth: 100, PixelHeight: 50, BitsPerPixel: 24));

    private static Gen<EntryKind> GenEntryKind => Gen.OneOfConst(
        EntryKind.Drive,
        EntryKind.Directory,
        EntryKind.File,
        EntryKind.Header);

    /// <summary>
    /// Entries carry a group so that collapsing one actually narrows the view - a generated header
    /// with no group would toggle nothing, and the subsequence invariant would never be tested
    /// against a list that is genuinely shorter than the one it came from.
    /// </summary>
    private static Gen<Entry> GenEntry =>
        Gen.Select(
                Gen.OneOfConst("a", "b", "c"),
                GenEntryKind,
                Gen.OneOfConst("g1", "g2"),
                Gen.OneOfConst<string?>(null, @"D:\somewhere"))
            .Select(t => new Entry(t.Item1, t.Item2, Group: t.Item3, OriginalPath: t.Item4));

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
        Gen.Select(Gen.Select(GenColumnIndex, GenEntryIndex), GenIsMove)
            .Select(t => (Msg)new Msg.EnterDirectory(t.Item1.Item1, t.Item1.Item2, t.Item2)),
        GenColumnIndex.Select(i => (Msg)new Msg.GoToParent(i)),
        Gen.Const<Msg>(new Msg.Refresh()),
        Gen.Select(GenColumnIndex, GenLocation, GenEntries).Select(t => (Msg)new Msg.DirectoryLoaded(t.Item1, t.Item2, t.Item3)),
        Gen.Select(GenColumnIndex, GenLocation).Select(t => (Msg)new Msg.DirectoryLoadFailed(t.Item1, t.Item2, "error")),
        Gen.Select(Gen.Select(GenColumnIndex, GenEntryIndex), GenIsMove)
            .Select(t => (Msg)new Msg.DeleteEntry(t.Item1.Item1, t.Item1.Item2, t.Item2)),
        Gen.Select(
            Gen.Select(GenColumnIndex, GenEntryIndex),
            Gen.Select(GenPaths, GenIsMove, GenIsMove))
            .Select(t => (Msg)new Msg.DropFiles(t.Item1.Item1, t.Item1.Item2, t.Item2.Item1, t.Item2.Item2, t.Item2.Item3)),
        Gen.Select(Gen.Select(GenColumnIndex, GenLocation), GenPaths)
            .Select(t => (Msg)new Msg.ShellOpCompleted(t.Item1.Item1, t.Item1.Item2, t.Item2)),
        Gen.Select(GenColumnIndex, GenLocation).Select(t => (Msg)new Msg.ShellOpFailed(t.Item1, t.Item2, "error")),
        Gen.Select(GenColumnIndex, GenEntryIndex).Select(t => (Msg)new Msg.ToggleMark(t.Item1, t.Item2)),
        GenColumnIndex.Select(i => (Msg)new Msg.ToggleMarkAtCursor(i)),
        GenColumnIndex.Select(i => (Msg)new Msg.ClearMarks(i)),
        Gen.Select(GenColumnIndex, GenIsMove).Select(t => (Msg)new Msg.DeleteMarked(t.Item1, t.Item2)),
        Gen.Select(
            Gen.Select(GenColumnIndex, GenEntryIndex),
            Gen.Select(GenEntryIndex, GenIsMove))
            .Select(t => (Msg)new Msg.MarkRange(t.Item1.Item1, t.Item1.Item2, t.Item2.Item1, t.Item2.Item2)),
        Gen.Select(GenColumnIndex, GenPaths, GenIsMove)
            .Select(t => (Msg)new Msg.PasteRequested(t.Item1, t.Item2, t.Item3)),
        Gen.Select(GenJobId, Gen.Select(GenColumnIndex, GenColumnIndex))
            .Select(t => (Msg)new Msg.JobProgress(t.Item1, t.Item2.Item1, t.Item2.Item2, t.Item2.Item1, t.Item2.Item2, null)),
        Gen.Select(GenJobId, GenPaths).Select(t => (Msg)new Msg.JobCompleted(t.Item1, 0, t.Item2)),
        GenJobId.Select(i => (Msg)new Msg.JobFailed(i, "error")),
        Gen.Select(GenJobId, GenPaths).Select(t => (Msg)new Msg.JobCancelled(t.Item1, t.Item2)),
        GenJobId.Select(i => (Msg)new Msg.JobCancelRequested(i)),
        GenJobId.Select(i => (Msg)new Msg.JobDismissed(i)),
        Gen.Select(Gen.Select(GenJobId, GenPreviewKind), Gen.Select(GenImageBytes, GenPreviewMetadata))
            .Select(t => (Msg)new Msg.PreviewLoaded(t.Item1.Item1, t.Item1.Item2, "text", t.Item2.Item1, "label", t.Item2.Item2)),
        GenJobId.Select(i => (Msg)new Msg.PreviewFailed(i, "error")),
        GenPaths.Select(paths => (Msg)new Msg.SetCutPending(paths)),
        Gen.Select(GenJobId, Gen.Int[0, 5]).Select(t => (Msg)new Msg.JobConflictsFound(t.Item1, t.Item2)),
        Gen.Select(GenJobId, GenConflictDecision).Select(t => (Msg)new Msg.JobConflictResolved(t.Item1, t.Item2)),
        GenPath.Select(p => (Msg)new Msg.ExternalDirectoryChanged(p)),
        GenColumnIndex.Select(i => (Msg)new Msg.PinFocusedLocation(i)),
        GenColumnIndex.Select(i => (Msg)new Msg.PinEntryAtCursor(i)),
        Gen.Select(GenColumnIndex, GenEntryIndex).Select(t => (Msg)new Msg.ToggleSection(t.Item1, t.Item2)),
        Gen.Select(GenColumnIndex, Gen.OneOfConst("g1", "g2").List[0, 2])
            .Select(t => (Msg)new Msg.CollapsedGroupsRestored(t.Item1, [.. t.Item2])),
        Gen.OneOfConst(@"E:\", @"Z:\").Select(d => (Msg)new Msg.ImageMounted(d)),
        Gen.OneOfConst("ok", "failed").Select(m => (Msg)new Msg.NoticeRaised(m)),
        Gen.OneOfConst(SortMode.Name, SortMode.Extension, SortMode.Size, SortMode.Modified)
            .Select(m => (Msg)new Msg.SetSortMode(m)),
        Gen.Const<Msg>(new Msg.ToggleDirectoriesFirst()),
        Gen.Const<Msg>(new Msg.ToggleHiddenFiles()),
        Gen.Const<Msg>(new Msg.EnterSortMode()),
        Gen.Const<Msg>(new Msg.ExitSortMode()),
        Gen.OneOfConst(SortMode.Name, SortMode.Extension, SortMode.Size, SortMode.Modified)
            .Select(m => (Msg)new Msg.SetSortDescending(m)),
        GenColumnIndex.Select(i => (Msg)new Msg.CreateFolderRequested(i)),
        GenColumnIndex.Select(i => (Msg)new Msg.RenameRequested(i)),
        Gen.Const<Msg>(new Msg.RenameCancelled()),
        Gen.OneOfConst("a", "", "bad:name").Select(n => (Msg)new Msg.RenameSubmitted(n)),
        Gen.Select(GenColumnIndex, GenLocation).Select(t => (Msg)new Msg.RenameCompleted(t.Item1, t.Item2, "renamed")),
        Gen.Const<Msg>(new Msg.RenameFailed("nope")),
        Gen.Select(GenColumnIndex, GenLocation).Select(t => (Msg)new Msg.FolderCreated(t.Item1, t.Item2, "new")),
        Gen.Select(
                Gen.OneOfConst(SortMode.Name, SortMode.Size),
                Gen.OneOfConst(true, false),
                Gen.OneOfConst(true, false))
            .Select(t => (Msg)new Msg.ViewRestored(new ViewOptions(new SortOrder(t.Item1, t.Item2, t.Item3)))),
        GenJobId.Select(g => (Msg)new Msg.PreviewCapacityLoaded(
            g, new PreviewCapacity("vol", "kind", UsedBytes: 1, TotalBytes: 2))),
        Gen.Const<Msg>(new Msg.PlacesChanged()));

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
                // Preview.Generation only ever increases - PreviewLoaded/PreviewFailed never bump
                // it themselves, and ReconcilePreview only bumps forward.
                Assert.True(next.Preview.Generation >= state.Preview.Generation);
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
        Assert.Equal(expected.NextJobId, actual.NextJobId);
        Assert.Equal(expected.Jobs, actual.Jobs);
        Assert.Equal(expected.Preview.Generation, actual.Preview.Generation);
        Assert.Equal(expected.Preview.Path, actual.Preview.Path);
        Assert.Equal(expected.Preview.Kind, actual.Preview.Kind);
        Assert.Equal(expected.Preview.Text, actual.Preview.Text);
        Assert.Equal(expected.Preview.ImageBytes, actual.Preview.ImageBytes);
        Assert.Equal(expected.Preview.Error, actual.Preview.Error);
        Assert.Equal(expected.CutPending, actual.CutPending);
        Assert.Equal(expected.Columns.Length, actual.Columns.Length);
        for (var i = 0; i < expected.Columns.Length; i++)
        {
            var e = expected.Columns[i];
            var a = actual.Columns[i];
            Assert.Equal(e.Location, a.Location);
            Assert.Equal(e.Cursor, a.Cursor);
            Assert.Equal(e.ScrollOffset, a.ScrollOffset);
            Assert.Equal(e.Load, a.Load);
            Assert.Equal(e.ErrorMessage, a.ErrorMessage);
            Assert.Equal(e.Entries, a.Entries);
        }
    }
}
