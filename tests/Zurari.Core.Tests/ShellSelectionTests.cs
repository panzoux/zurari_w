using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>
/// <see cref="Column.ShellSelection"/> is the shell menu's rule, <see cref="Column.PathsToActOn"/> the
/// file operations' rule. They differ on purpose, and these tests pin where.
/// </summary>
public class ShellSelectionTests
{
    private static Column InBin(params Entry[] entries) =>
        new(Location.RecycleBin.Instance, [.. entries], Cursor: 0, Load: LoadState.Loaded);

    [Fact]
    public void A_recycle_bin_column_with_two_marked_rows_selects_both_for_the_shell_and_none_for_file_operations()
    {
        var bin = InBin(
            new Entry(@"C:\$Recycle.Bin\S-1\$R1", EntryKind.File, IsMarked: true),
            new Entry(@"C:\$Recycle.Bin\S-1\$R2", EntryKind.File, IsMarked: true),
            new Entry(@"C:\$Recycle.Bin\S-1\$R3", EntryKind.File));

        Assert.Equal([@"C:\$Recycle.Bin\S-1\$R1", @"C:\$Recycle.Bin\S-1\$R2"], bin.ShellSelection());
        Assert.Empty(bin.PathsToActOn());
    }

    /// <summary>
    /// The bin row sits in the drive list with no filesystem path, so <see cref="Column.PathOf"/>
    /// misses it; the shell knows it by its parsing name.
    /// </summary>
    [Fact]
    public void The_recycle_bin_row_in_the_drive_list_is_selected_by_its_parsing_name()
    {
        var drives = new Column(
            Location.Drives.Instance,
            [
                new Entry(@"C:\", EntryKind.Drive, IsMarked: true),
                new Entry("::recyclebin", EntryKind.Directory, IsMarked: true, Target: Location.RecycleBin.Instance),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);

        Assert.Equal([@"C:\", Location.RecycleBin.ParsingName], drives.ShellSelection());
        Assert.Empty(drives.PathsToActOn());
    }

    [Fact]
    public void A_favorite_is_selected_by_where_it_points_not_by_where_it_sits()
    {
        var drives = new Column(
            Location.Drives.Instance,
            [new Entry(@"D:\docs", EntryKind.Directory, IsMarked: true, Target: new Location.RealDirectory(@"D:\docs"))],
            Cursor: 0,
            Load: LoadState.Loaded);

        Assert.Equal([@"D:\docs"], drives.ShellSelection());
    }

    [Fact]
    public void A_marked_header_and_unmarked_rows_are_left_out()
    {
        var folder = new Column(
            new Location.RealDirectory(@"C:\x"),
            [
                new Entry("hdr", EntryKind.Header, IsMarked: true),
                new Entry("a.txt", EntryKind.File),
                new Entry("b.txt", EntryKind.File, IsMarked: true),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);

        Assert.Equal([@"C:\x\b.txt"], folder.ShellSelection());
    }

    [Fact]
    public void Nothing_marked_selects_nothing()
    {
        var folder = new Column(
            new Location.RealDirectory(@"C:\x"), [new Entry("a.txt", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);

        Assert.True(folder.ShellSelection().IsEmpty);
    }
}
