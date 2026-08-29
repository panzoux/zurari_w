using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

public class StateProjectionTests
{
    private static AppState StateWithColumns(params Column[] columns) =>
        new() { Columns = [.. columns], FocusedColumn = 0 };

    [Fact]
    public void Project_rejects_null_state()
    {
        Assert.Throws<ArgumentNullException>(() => StateProjection.Project(null!));
    }

    [Fact]
    public void Root_column_title_is_drive_list_label()
    {
        var state = StateWithColumns(new Column(Location.Drives.Instance, [], Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal("ドライブ", vms[0].Title);
    }

    [Fact]
    public void Normal_directory_title_is_last_path_segment()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\Users\someone"), [], Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal("someone", vms[0].Title);
    }

    [Fact]
    public void Drive_root_title_trims_trailing_separator()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal("C:", vms[0].Title);
    }

    [Fact]
    public void Error_load_state_suffixes_title()
    {
        var state = StateWithColumns(
            new Column(new Location.RealDirectory(@"C:\Users"), [], Load: LoadState.Error, ErrorMessage: "denied"));

        var vms = StateProjection.Project(state);

        Assert.Equal("Users (エラー)", vms[0].Title);
    }

    [Fact]
    public void Loading_with_no_entries_suffixes_title_with_ellipsis()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\Users"), [], Load: LoadState.Loading));

        var vms = StateProjection.Project(state);

        Assert.Equal("Users …", vms[0].Title);
    }

    [Fact]
    public void Loading_with_stale_entries_does_not_suffix_title()
    {
        var entry = new Entry("a.txt", EntryKind.File);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\Users"), [entry], Cursor: 0, Load: LoadState.Loading));

        var vms = StateProjection.Project(state);

        Assert.Equal("Users", vms[0].Title);
        // Stale entries stay visible while reloading.
        Assert.Single(vms[0].Entries);
    }

    [Theory]
    [InlineData(0, "0 B")]
    [InlineData(1023, "1023 B")]
    [InlineData(1024, "1.0 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    public void File_size_is_formatted_as_human_readable(long sizeBytes, string expected)
    {
        var entry = new Entry("f.bin", EntryKind.File, SizeBytes: sizeBytes);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal(expected, vms[0].Entries[0].SizeText);
    }

    [Fact]
    public void Unknown_file_size_projects_to_null()
    {
        var entry = new Entry("f.bin", EntryKind.File, SizeBytes: -1);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Null(vms[0].Entries[0].SizeText);
    }

    [Theory]
    [InlineData(Core.EntryKind.Drive)]
    [InlineData(Core.EntryKind.Directory)]
    public void Directory_and_drive_size_is_always_null(Core.EntryKind kind)
    {
        var entry = new Entry("sub", kind, SizeBytes: 123);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Null(vms[0].Entries[0].SizeText);
    }

    [Fact]
    public void Unknown_modified_date_projects_to_null()
    {
        var entry = new Entry("f.bin", EntryKind.File, Modified: default);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Null(vms[0].Entries[0].DateText);
    }

    [Fact]
    public void Known_modified_date_is_formatted_invariant()
    {
        var modified = new DateTime(2026, 7, 15, 9, 30, 0, DateTimeKind.Unspecified);
        var entry = new Entry("f.bin", EntryKind.File, Modified: modified);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal("2026-07-15 09:30", vms[0].Entries[0].DateText);
    }

    [Fact]
    public void Kind_is_mapped_one_to_one()
    {
        var drive = new Entry("C:\\", EntryKind.Drive);
        var dir = new Entry("sub", EntryKind.Directory);
        var file = new Entry("a.txt", EntryKind.File);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [drive, dir, file], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal(Controls.EntryKind.Drive, vms[0].Entries[0].Kind);
        Assert.Equal(Controls.EntryKind.Directory, vms[0].Entries[1].Kind);
        Assert.Equal(Controls.EntryKind.File, vms[0].Entries[2].Kind);
    }

    [Fact]
    public void Cursor_index_and_mark_are_passed_through()
    {
        var entry = new Entry("a.txt", EntryKind.File, IsMarked: true);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Equal(0, vms[0].CursorIndex);
        Assert.True(vms[0].Entries[0].IsMarked);
    }

    [Fact]
    public void Focused_column_is_flagged_and_others_are_not()
    {
        var state = new AppState
        {
            Columns =
            [
                new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loaded),
                new Column(new Location.RealDirectory(@"C:\sub"), [], Load: LoadState.Loaded),
            ],
            FocusedColumn = 1,
        };

        var vms = StateProjection.Project(state);

        Assert.False(vms[0].IsFocused);
        Assert.True(vms[1].IsFocused);
    }

    [Fact]
    public void Empty_column_has_no_entries()
    {
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [], Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.Empty(vms[0].Entries);
    }

    [Fact]
    public void Entry_whose_full_path_is_in_CutPending_projects_as_IsCut()
    {
        var cut = new Entry("cut.txt", EntryKind.File);
        var plain = new Entry("plain.txt", EntryKind.File);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [cut, plain], Cursor: 0, Load: LoadState.Loaded))
            with
        { CutPending = [@"C:\cut.txt"] };

        var vms = StateProjection.Project(state);

        Assert.True(vms[0].Entries[0].IsCut);
        Assert.False(vms[0].Entries[1].IsCut);
    }

    [Fact]
    public void CutPending_comparison_is_case_insensitive()
    {
        var entry = new Entry("Cut.TXT", EntryKind.File);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded))
            with
        { CutPending = [@"c:\cut.txt"] };

        var vms = StateProjection.Project(state);

        Assert.True(vms[0].Entries[0].IsCut);
    }

    [Fact]
    public void Empty_CutPending_projects_every_entry_as_not_cut()
    {
        var entry = new Entry("a.txt", EntryKind.File);
        var state = StateWithColumns(new Column(new Location.RealDirectory(@"C:\"), [entry], Cursor: 0, Load: LoadState.Loaded));

        var vms = StateProjection.Project(state);

        Assert.False(vms[0].Entries[0].IsCut);
    }

    [Fact]
    public void CutPending_only_marks_entries_in_the_matching_column()
    {
        var entryA = new Entry("same-name.txt", EntryKind.File);
        var entryB = new Entry("same-name.txt", EntryKind.File);
        var state = new AppState
        {
            Columns =
            [
                new Column(new Location.RealDirectory(@"C:\a"), [entryA], Cursor: 0, Load: LoadState.Loaded),
                new Column(new Location.RealDirectory(@"C:\b"), [entryB], Cursor: 0, Load: LoadState.Loaded),
            ],
            FocusedColumn = 0,
            CutPending = [@"C:\a\same-name.txt"],
        };

        var vms = StateProjection.Project(state);

        Assert.True(vms[0].Entries[0].IsCut);
        Assert.False(vms[1].Entries[0].IsCut);
    }

    /// <summary>
    /// The property the whole cache exists for. Generic.xaml binds the ListBox's ItemsSource to
    /// Column.Entries and WPF compares it by reference, so handing back the same array instance is
    /// what stops a cursor move from tearing down and regenerating every realized container.
    /// </summary>
    [Fact]
    public void A_cursor_move_reuses_the_projected_row_array()
    {
        var entries = new Entry[] { new("a.txt", EntryKind.File), new("b.txt", EntryKind.File) };
        var column = new Column(new Location.RealDirectory(@"C:\"), [.. entries], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);
        var cache = new ProjectionCache();

        var first = StateProjection.Project(state, null, cache);
        var second = StateProjection.Project(StateWithColumns(column with { Cursor = 1 }), null, cache);

        Assert.Same(first[0].Entries, second[0].Entries);

        // The ColumnVm around it is still rebuilt, because the cursor really did move.
        Assert.Equal(0, first[0].CursorIndex);
        Assert.Equal(1, second[0].CursorIndex);
    }

    [Fact]
    public void Changed_entries_do_not_reuse_the_projected_row_array()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [new Entry("a.txt", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var cache = new ProjectionCache();

        var first = StateProjection.Project(StateWithColumns(column), null, cache);

        // A mark lives on the Entry, so toggling one produces a new Entries array - and must produce
        // new rows, or the mark would not render.
        var marked = column with { Entries = [new Entry("a.txt", EntryKind.File, IsMarked: true)] };
        var second = StateProjection.Project(StateWithColumns(marked), null, cache);

        Assert.NotSame(first[0].Entries, second[0].Entries);
        Assert.True(second[0].Entries[0].IsMarked);
    }

    [Fact]
    public void A_changed_cut_set_does_not_reuse_the_projected_row_array()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [new Entry("a.txt", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var cache = new ProjectionCache();

        var first = StateProjection.Project(StateWithColumns(column), null, cache);

        // IsCut is derived from state.CutPending rather than from the Entry, so it is part of the
        // key too - otherwise Ctrl+X would not dim the rows until something else invalidated them.
        var cut = StateWithColumns(column) with { CutPending = [@"C:\a.txt"] };
        var second = StateProjection.Project(cut, null, cache);

        Assert.NotSame(first[0].Entries, second[0].Entries);
        Assert.True(second[0].Entries[0].IsCut);
    }

    [Fact]
    public void Projecting_without_a_cache_still_builds_fresh_rows()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [new Entry("a.txt", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = StateWithColumns(column);

        var first = StateProjection.Project(state);
        var second = StateProjection.Project(state);

        Assert.NotSame(first[0].Entries, second[0].Entries);
    }
}
