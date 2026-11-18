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

    private static Column Mixed(string path, int cursor) => new(
        new Location.RealDirectory(path),
        [new Entry("sub", EntryKind.Directory), new Entry("f.txt", EntryKind.File)],
        Cursor: cursor, Load: LoadState.Loaded);

    [Fact]
    public void Copies_the_cursor_entry_into_the_folder_under_the_other_cursor()
    {
        var state = SplitWith(Mixed(@"C:\src", cursor: 1), Mixed(@"D:\dst", cursor: 0));

        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        var job = Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.Equal(@"D:\dst\sub", job.DestDir);
        Assert.Equal([@"C:\src\f.txt"], job.Sources);
        Assert.Equal(JobKind.Copy, job.Kind);
        Assert.Single(next.Jobs);
    }

    [Fact]
    public void Uses_the_other_columns_own_location_when_its_cursor_is_on_a_file()
    {
        var state = SplitWith(Mixed(@"C:\src", cursor: 1), Mixed(@"D:\dst", cursor: 1));

        var (_, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: true));

        var job = Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.Equal(@"D:\dst", job.DestDir);
        Assert.Equal(JobKind.Move, job.Kind);
    }

    [Fact]
    public void Marked_entries_are_copied_instead_of_the_cursor_entry()
    {
        var source = Mixed(@"C:\src", cursor: 1);
        source = source.WithAllEntries(
            [new Entry("sub", EntryKind.Directory, IsMarked: true), new Entry("f.txt", EntryKind.File)],
            ViewOptions.Default);
        var state = SplitWith(source, Mixed(@"D:\dst", cursor: 1));

        var (_, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        Assert.Equal([@"C:\src\sub"], Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects)).Sources);
    }

    [Fact]
    public void Refuses_with_a_notice_when_not_split()
    {
        var (next, effects) = Transition.Apply(Browsing(), new Msg.CopyToOtherPane(IsMove: false));

        Assert.Empty(effects);
        Assert.Contains("分割していません", next.Notice);
    }

    [Fact]
    public void Refuses_the_drive_list_as_a_destination()
    {
        // With the cursor on a drive row the folder under it (the drive) is a fine destination; an
        // empty drive list leaves only the drive list itself, which is not a directory.
        var emptyDrives = new Column(Location.Drives.Instance, [], Cursor: -1, Load: LoadState.Loaded);
        var state = SplitWith(Mixed(@"C:\src", cursor: 1), emptyDrives);

        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        Assert.Empty(effects);
        Assert.Contains("ここには", next.Notice);
    }

    [Fact]
    public void Refuses_copying_a_folder_into_itself()
    {
        var source = Mixed(@"C:\src", cursor: 0);                  // cursor on C:\src\sub
        var inside = Mixed(@"C:\src\sub", cursor: 1);               // other pane is inside it
        var state = SplitWith(source, inside);

        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: true));

        Assert.Empty(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.Contains("それ自身", next.Notice);
    }

    [Fact]
    public void Refuses_copying_a_folder_into_itself_when_the_other_pane_is_written_with_forward_slashes()
    {
        var state = SplitWith(Mixed(@"C:\src", cursor: 0), Mixed("C:/src/sub", cursor: 1));

        AssertRefused(state, "それ自身");
    }

    [Fact]
    public void Refuses_copying_into_the_same_folder_when_one_pane_is_written_with_forward_slashes()
    {
        AssertRefused(SplitWith(Mixed(@"C:\src", cursor: 1), Mixed("C:/src/", cursor: 1)), "同じフォルダー");
    }

    [Fact]
    public void An_external_change_matches_a_column_whatever_separator_the_path_uses()
    {
        var state = SplitWith(Files(@"C:\a", "x"), Files(@"D:\b", "y"));

        var (_, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged("C:/a/"));

        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(new ColumnRef(new PaneId(0), 0), read.At);
    }

    private static Column InBin(params Entry[] entries) => new(
        Location.RecycleBin.Instance, [.. entries], Cursor: entries.Length > 0 ? 0 : -1, Load: LoadState.Loaded);

    private static void AssertRefused(AppState state, string noticePart)
    {
        var (next, effects) = Transition.Apply(state, new Msg.CopyToOtherPane(IsMove: false));

        Assert.Empty(effects);
        Assert.Empty(next.Jobs);
        Assert.Contains(noticePart, next.Notice);
    }

    [Fact]
    public void Refuses_when_the_focused_column_is_the_recycle_bin()
    {
        var bin = InBin(new Entry(@"C:\$Recycle.Bin\S-1\$R1", EntryKind.File));

        AssertRefused(SplitWith(bin, Mixed(@"D:\dst", cursor: 1)), "ここには");
    }

    [Fact]
    public void Refuses_when_the_other_pane_is_in_the_bin_on_a_deleted_folder()
    {
        var bin = InBin(new Entry(@"C:\$Recycle.Bin\S-1\$R2", EntryKind.Directory));

        AssertRefused(SplitWith(Mixed(@"C:\src", cursor: 1), bin), "ここには");
    }

    [Fact]
    public void Refuses_when_the_other_pane_is_in_an_empty_bin()
    {
        AssertRefused(SplitWith(Mixed(@"C:\src", cursor: 1), InBin()), "ここには");
    }

    [Fact]
    public void Refuses_when_the_other_pane_is_the_drive_list_on_a_drive_row()
    {
        var drives = new Column(
            Location.Drives.Instance, [new Entry(@"C:", EntryKind.Drive)], Cursor: 0, Load: LoadState.Loaded);

        AssertRefused(SplitWith(Mixed(@"C:\src", cursor: 1), drives), "ここには");
    }

    [Fact]
    public void A_mark_hidden_by_the_view_is_not_copied()
    {
        var source = Mixed(@"C:\src", cursor: 1).WithAllEntries(
            [new Entry("sub", EntryKind.Directory, IsMarked: true, IsHidden: true), new Entry("f.txt", EntryKind.File)],
            ViewOptions.Default);

        var (_, effects) = Transition.Apply(
            SplitWith(source, Mixed(@"D:\dst", cursor: 1)), new Msg.CopyToOtherPane(IsMove: false));

        Assert.Equal([@"C:\src\f.txt"], Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects)).Sources);
    }

    [Fact]
    public void The_other_column_loading_with_no_entries_gives_its_own_location()
    {
        var loading = new Column(new Location.RealDirectory(@"D:\dst"), [], Cursor: -1, Load: LoadState.Loading);

        var (_, effects) = Transition.Apply(
            SplitWith(Mixed(@"C:\src", cursor: 1), loading), new Msg.CopyToOtherPane(IsMove: false));

        Assert.Equal(@"D:\dst", Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects)).DestDir);
    }

    [Theory]
    [InlineData(@"C:\src\sub")]
    [InlineData(@"C:\SRC\SUB")]
    [InlineData(@"C:\src\sub\deeper")]
    public void Refuses_a_destination_that_is_a_source_or_inside_it_however_it_is_spelled(string dest)
    {
        var other = new Column(new Location.RealDirectory(dest), [], Cursor: -1, Load: LoadState.Loaded);

        AssertRefused(SplitWith(Mixed(@"C:\src", cursor: 0), other), "それ自身");
    }

    [Fact]
    public void A_drive_root_is_a_fine_destination()
    {
        var root = new Column(new Location.RealDirectory(@"C:"), [], Cursor: -1, Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(
            SplitWith(Mixed(@"C:\a", cursor: 1), root), new Msg.CopyToOtherPane(IsMove: false));

        var job = Assert.Single(TestEffects.Of<Effect.RunFileJob>(effects));
        Assert.Equal(@"C:", job.DestDir);
        Assert.Equal([@"C:\a\f.txt"], job.Sources);
    }

    [Fact]
    public void Says_so_when_everything_already_lives_in_the_destination()
    {
        AssertRefused(SplitWith(Mixed(@"C:\src", cursor: 1), Mixed(@"C:\src", cursor: 1)), "同じフォルダー");
    }

    [Fact]
    public void PathsToActOn_prefers_marks_then_the_cursor_entry()
    {
        var cursorOnly = Mixed(@"C:\src", cursor: 1);
        Assert.Equal([@"C:\src\f.txt"], cursorOnly.PathsToActOn());

        var marked = cursorOnly.WithAllEntries(
            [new Entry("sub", EntryKind.Directory, IsMarked: true), new Entry("f.txt", EntryKind.File, IsMarked: true)],
            ViewOptions.Default);
        Assert.Equal([@"C:\src\sub", @"C:\src\f.txt"], marked.PathsToActOn());
    }

    [Fact]
    public void PathsToActOn_ignores_marks_in_a_collapsed_section_and_falls_back_to_the_cursor()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\src"),
            [
                new Entry("sub", EntryKind.Directory, IsMarked: true, Group: "g"),
                new Entry("f.txt", EntryKind.File),
            ],
            Cursor: 1,
            Load: LoadState.Loaded).WithCollapsedGroups(["g"], ViewOptions.Default);

        Assert.Equal([@"C:\src\f.txt"], column.PathsToActOn());
    }

    [Fact]
    public void PathsToActOn_excludes_marked_headers_and_drives()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\src"),
            [
                new Entry("hdr", EntryKind.Header, IsMarked: true),
                new Entry(@"D:", EntryKind.Drive, IsMarked: true),
                new Entry("f.txt", EntryKind.File),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);

        Assert.Empty(column.PathsToActOn());
    }

    [Fact]
    public void PathsToActOn_is_empty_for_the_recycle_bin_and_the_drive_list()
    {
        var bin = InBin(new Entry(@"C:\$Recycle.Bin\S-1\$R1", EntryKind.File, IsMarked: true));
        var drives = new Column(
            Location.Drives.Instance, [new Entry(@"C:", EntryKind.Drive)], Cursor: 0, Load: LoadState.Loaded);

        Assert.Empty(bin.PathsToActOn());
        Assert.Empty(drives.PathsToActOn());
    }

    [Fact]
    public void A_rename_reveal_lands_in_the_pane_that_renamed_even_if_the_other_loads_first()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(shown, shown);
        var mine = new ColumnRef(new PaneId(0), 0);
        var theirs = new ColumnRef(new PaneId(1), 0);
        var (renamed, _) = Transition.Apply(state, new Msg.RenameCompleted(mine, dir, "new"));

        var listing = ImmutableArray.Create(new Entry("a", EntryKind.File), new Entry("new", EntryKind.File));
        var (otherFirst, _) = Transition.Apply(renamed, new Msg.DirectoryLoaded(theirs, dir, listing));
        var (both, _) = Transition.Apply(otherFirst, new Msg.DirectoryLoaded(mine, dir, listing));

        Assert.Equal("new", both.Panes[0].Columns[0].CursorName);
        Assert.Equal("a", both.Panes[1].Columns[0].CursorName);   // kept its index, not pulled to "new"
        Assert.Null(both.RevealTarget);
    }

    [Fact]
    public void A_mounted_image_is_revealed_by_whichever_drive_list_loads_first()
    {
        var drives = new Column(Location.Drives.Instance, [new Entry(@"C:\", EntryKind.Drive)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(drives, drives);
        var (mounted, _) = Transition.Apply(state, new Msg.ImageMounted(@"E:\"));

        var listing = ImmutableArray.Create(new Entry(@"C:\", EntryKind.Drive), new Entry(@"E:\", EntryKind.Drive));
        var (next, _) = Transition.Apply(
            mounted, new Msg.DirectoryLoaded(new ColumnRef(new PaneId(1), 0), Location.Drives.Instance, listing));

        Assert.Equal(@"E:\", next.Panes[1].Columns[0].CursorName);
    }

    [Fact]
    public void A_reveal_for_a_pane_closed_during_the_reread_does_not_jump_a_later_listing()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(shown, shown);
        var bottom = new ColumnRef(new PaneId(1), 0);
        var top = new ColumnRef(new PaneId(0), 0);
        var (renamed, _) = Transition.Apply(state, new Msg.RenameCompleted(bottom, dir, "new"));
        var (closed, _) = Transition.Apply(renamed, new Msg.ToggleSplit());
        Assert.Single(closed.Panes);
        Assert.Equal(new PaneId(0), closed.Panes[0].Id);

        var listing = ImmutableArray.Create(new Entry("a", EntryKind.File), new Entry("new", EntryKind.File));
        var (loaded, _) = Transition.Apply(closed, new Msg.DirectoryLoaded(top, dir, listing));

        Assert.Equal("a", loaded.Panes[0].Columns[0].CursorName);
        Assert.Null(loaded.RevealTarget);
    }

    [Fact]
    public void A_finished_shell_op_rereads_affected_folders_even_when_its_column_is_gone()
    {
        var src = new Location.RealDirectory(@"C:\src");
        var state = SplitWith(
            new Column(src, [new Entry("f", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"D:\dst"), [], Load: LoadState.Loaded));
        var closedPane = new ColumnRef(new PaneId(9), 0);

        var (_, effects) = Transition.Apply(
            state, new Msg.ShellOpCompleted(closedPane, new Location.RealDirectory(@"D:\dst"), [@"C:\src"]));

        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(new ColumnRef(new PaneId(0), 0), read.At);
    }

    private static AppState TwoDriveLists()
    {
        var header = new Entry("net", EntryKind.Header, Group: "net");
        var row = new Entry(@"\\s\x", EntryKind.Directory, Group: "net");
        var drives = new Column(Location.Drives.Instance, [header, row], Cursor: 0, Load: LoadState.Loaded);
        return SplitWith(drives, drives);
    }

    [Fact]
    public void Collapsing_a_section_collapses_it_in_every_drive_list()
    {
        var state = TwoDriveLists();

        var (next, effects) = Transition.Apply(state, new Msg.ToggleSection(new ColumnRef(new PaneId(0), 0), 0));

        Assert.Single(next.Panes[0].Columns[0].Entries);
        Assert.Single(next.Panes[1].Columns[0].Entries);
        Assert.Single(TestEffects.Of<Effect.SetCollapsedGroups>(effects));
    }

    [Fact]
    public void Restored_collapsed_sections_apply_to_every_drive_list()
    {
        var state = TwoDriveLists();

        var (next, _) = Transition.Apply(state, new Msg.CollapsedGroupsRestored(new ColumnRef(new PaneId(1), 0), ["net"]));

        Assert.Single(next.Panes[0].Columns[0].Entries);
        Assert.Single(next.Panes[1].Columns[0].Entries);
    }

    private static Column TwoSectionDrives() => new(
        Location.Drives.Instance,
        [
            new Entry("net", EntryKind.Header, Group: "net"),
            new Entry(@"\\s\x", EntryKind.Directory, Group: "net"),
            new Entry("fav", EntryKind.Header, Group: "fav"),
            new Entry(@"C:\work", EntryKind.Directory, Group: "fav"),
        ],
        Cursor: 0,
        Load: LoadState.Loaded);

    [Fact]
    public void A_new_pane_shares_the_collapsed_sections_and_a_toggle_there_keeps_the_other_ones()
    {
        var drives = TwoSectionDrives();
        var first = new ColumnRef(new PaneId(0), 0);
        var second = new ColumnRef(new PaneId(1), 0);
        var (collapsed, _) = Transition.Apply(AppState.SinglePane([drives]), new Msg.ToggleSection(first, 0));
        var (split, _) = Transition.Apply(collapsed, new Msg.ToggleSplit());

        var seeded = split.Panes[1].Columns[0];
        Assert.True(seeded.CollapsedGroups.SetEquals(["net"]));
        Assert.Empty(split.CheckInvariants());

        var (loaded, _) = Transition.Apply(
            split, new Msg.DirectoryLoaded(second, Location.Drives.Instance, drives.AllEntries));
        var fav = loaded.Panes[1].Columns[0].Entries.IndexOf(drives.AllEntries[2]);
        var (both, effects) = Transition.Apply(loaded, new Msg.ToggleSection(second, fav));

        Assert.True(both.Panes[0].Columns[0].CollapsedGroups.SetEquals(["net", "fav"]));
        Assert.True(both.Panes[1].Columns[0].CollapsedGroups.SetEquals(["net", "fav"]));
        var saved = Assert.Single(TestEffects.Of<Effect.SetCollapsedGroups>(effects));
        Assert.Equal(2, saved.Groups.Length);
        Assert.Empty(both.CheckInvariants());
    }

    [Fact]
    public void A_reveal_does_not_jump_a_later_listing_of_another_folder_at_the_same_ref()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var other = new Location.RealDirectory(@"C:\y");
        var at = new ColumnRef(new PaneId(0), 0);
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var (renamed, _) = Transition.Apply(AppState.SinglePane([shown]), new Msg.RenameCompleted(at, dir, "new"));
        var moved = renamed.WithColumn(
            at, new Column(other, [new Entry("a", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded));

        var listing = ImmutableArray.Create(new Entry("a", EntryKind.File), new Entry("new", EntryKind.File));
        var (dropped, _) = Transition.Apply(moved, new Msg.DirectoryLoaded(at, dir, listing));
        var (later, _) = Transition.Apply(dropped, new Msg.DirectoryLoaded(at, other, listing));

        Assert.Equal("a", later.Panes[0].Columns[0].CursorName);
    }

    [Fact]
    public void A_failed_reread_clears_the_reveal_waiting_for_it()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var at = new ColumnRef(new PaneId(0), 0);
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var (renamed, _) = Transition.Apply(AppState.SinglePane([shown]), new Msg.RenameCompleted(at, dir, "new"));
        Assert.NotNull(renamed.RevealTarget);

        var (failed, _) = Transition.Apply(renamed, new Msg.DirectoryLoadFailed(at, dir, "denied"));

        Assert.Null(failed.RevealTarget);
    }

    [Fact]
    public void Closing_the_split_clears_a_reveal_scoped_to_the_dropped_pane_only()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(shown, shown);

        var (bottomReveal, _) = Transition.Apply(state, new Msg.RenameCompleted(new ColumnRef(new PaneId(1), 0), dir, "new"));
        var (closedKeepingTop, _) = Transition.Apply(bottomReveal, new Msg.ToggleSplit());
        Assert.Null(closedKeepingTop.RevealTarget);
        Assert.Empty(closedKeepingTop.CheckInvariants());

        var (topReveal, _) = Transition.Apply(state, new Msg.RenameCompleted(new ColumnRef(new PaneId(0), 0), dir, "new"));
        var (stillThere, _) = Transition.Apply(topReveal, new Msg.ToggleSplit());
        Assert.NotNull(stillThere.RevealTarget);
    }

    [Fact]
    public void A_shell_op_does_not_reread_its_column_when_it_shows_another_folder_but_does_reread_affected_ones()
    {
        var state = SplitWith(
            new Column(new Location.RealDirectory(@"C:\a"), [], Load: LoadState.Loaded),
            new Column(new Location.RealDirectory(@"D:\b"), [], Load: LoadState.Loaded));

        var (_, effects) = Transition.Apply(
            state,
            new Msg.ShellOpCompleted(
                new ColumnRef(new PaneId(0), 0), new Location.RealDirectory(@"C:\old"), [@"D:\b"]));

        var read = Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.Equal(new ColumnRef(new PaneId(1), 0), read.At);
    }

    [Fact]
    public void A_rename_rereads_the_same_folder_in_the_other_pane_too()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(shown, shown);
        var mine = new ColumnRef(new PaneId(0), 0);

        var (next, effects) = Transition.Apply(state, new Msg.RenameCompleted(mine, dir, "new"));

        var reads = TestEffects.Of<Effect.ReadDirectory>(effects);
        Assert.Equal(2, reads.Count);
        Assert.Contains(new Effect.ReadDirectory(mine, dir), reads);
        Assert.Contains(new Effect.ReadDirectory(new ColumnRef(new PaneId(1), 0), dir), reads);
        Assert.Equal(LoadState.Loading, next.Panes[1].Columns[0].Load);
        Assert.Equal(mine, next.RevealTarget!.At);
    }

    [Fact]
    public void A_scoped_reveal_is_ignored_by_another_pane_and_kept_for_its_own()
    {
        var dir = new Location.RealDirectory(@"C:\x");
        var shown = new Column(dir, [new Entry("old", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = SplitWith(shown, shown) with { RevealTarget = new Reveal("new", new ColumnRef(new PaneId(0), 0), dir) };

        var listing = ImmutableArray.Create(new Entry("a", EntryKind.File), new Entry("new", EntryKind.File));
        var (next, _) = Transition.Apply(state, new Msg.DirectoryLoaded(new ColumnRef(new PaneId(1), 0), dir, listing));

        Assert.Equal("a", next.Panes[1].Columns[0].CursorName);
        Assert.Equal(new Reveal("new", new ColumnRef(new PaneId(0), 0), dir), next.RevealTarget);
    }
}
