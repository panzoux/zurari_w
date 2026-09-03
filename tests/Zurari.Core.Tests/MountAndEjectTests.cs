using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>
/// Opening a disc image mounts it; Ctrl+E takes it, or a disc, back out again.
/// </summary>
public class MountAndEjectTests
{
    private static AppState With(params Column[] columns) =>
        new() { Columns = [.. columns], FocusedColumn = 0 };

    private static Column DrivePane(int cursor, params Entry[] rows) =>
        new(Location.Drives.Instance, [.. rows], Cursor: cursor, Load: LoadState.Loaded);

    private static readonly Entry Optical =
        new(@"E:\", EntryKind.Drive, Group: EntryGroups.Drives, DisplayName: "E: (光学ドライブ)", IsEjectable: true);

    private static readonly Entry Fixed =
        new(@"C:\", EntryKind.Drive, Group: EntryGroups.Drives, DisplayName: "Windows (C:)");

    [Fact]
    public void Opening_an_iso_asks_the_shell_to_mount_it()
    {
        var column = new Column(
            new Location.RealDirectory(@"D:\images"),
            [new Entry("ubuntu.iso", EntryKind.File)],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(With(column), new Msg.EnterDirectory(0, 0));

        Assert.Equal(
            new Effect.MountImage(@"D:\images\ubuntu.iso"),
            Assert.Single(effects.OfType<Effect.MountImage>()));
    }

    /// <summary>
    /// Case-insensitively, because Windows filenames are - and an image named in capitals is common
    /// enough that missing it would look like the feature simply does not work.
    /// </summary>
    [Theory]
    [InlineData("UBUNTU.ISO", true)]
    [InlineData("disc.iso", true)]
    [InlineData("notes.txt", false)]
    [InlineData("disk.vhdx", false)]
    [InlineData("isometric.png", false)]
    public void Only_an_iso_mounts(string name, bool mounts)
    {
        var column = new Column(
            new Location.RealDirectory(@"D:\images"),
            [new Entry(name, EntryKind.File)],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (_, effects) = Transition.Apply(With(column), new Msg.EnterDirectory(0, 0));

        Assert.Equal(mounts, effects.OfType<Effect.MountImage>().Any());
    }

    [Fact]
    public void Ejecting_an_optical_drive_asks_the_shell_to_eject_it()
    {
        var (_, effects) = Transition.Apply(With(DrivePane(0, Optical)), new Msg.EjectAtCursor(0));

        Assert.Equal(new Effect.EjectDrive(@"E:\"), Assert.Single(effects));
    }

    /// <summary>
    /// A refusal has to be said out loud. A key that silently does nothing is indistinguishable from
    /// one that was never wired up - which is exactly how a real defect hid earlier in this phase.
    /// </summary>
    [Fact]
    public void Ejecting_a_fixed_disk_says_why_it_will_not()
    {
        var (next, effects) = Transition.Apply(With(DrivePane(0, Fixed)), new Msg.EjectAtCursor(0));

        Assert.Empty(effects);
        Assert.NotNull(next.Notice);
        Assert.Contains("Windows (C:)", next.Notice!, StringComparison.Ordinal);
    }

    [Fact]
    public void Ejecting_something_that_is_not_a_drive_says_so()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"),
            [new Entry("a.txt", EntryKind.File)],
            Cursor: 0,
            Load: LoadState.Loaded);

        var (next, effects) = Transition.Apply(With(column), new Msg.EjectAtCursor(0));

        Assert.Empty(effects);
        Assert.NotNull(next.Notice);
    }

    [Fact]
    public void A_notice_goes_away_once_the_cursor_moves_on()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"),
            [new Entry("a.txt", EntryKind.File), new Entry("b.txt", EntryKind.File)],
            Cursor: 0,
            Load: LoadState.Loaded);
        var (withNotice, _) = Transition.Apply(With(column), new Msg.NoticeRaised("何か"));
        Assert.Equal("何か", withNotice.Notice);

        var (moved, _) = Transition.Apply(withNotice, new Msg.CursorDown(0));

        Assert.Null(moved.Notice);
    }

    [Fact]
    public void A_mounted_image_re_reads_the_drive_pane_and_waits_to_reveal_the_new_drive()
    {
        var state = With(DrivePane(0, Fixed));

        var (next, effects) = Transition.Apply(state, new Msg.ImageMounted(@"E:\"));

        Assert.Equal(@"E:\", next.RevealTarget);
        Assert.Equal(LoadState.Loading, next.Columns[0].Load);
        Assert.Equal(
            new Effect.ReadDirectory(0, Location.Drives.Instance),
            Assert.Single(effects.OfType<Effect.ReadDirectory>()));
    }

    [Fact]
    public void The_cursor_lands_on_the_new_drive_when_its_row_arrives()
    {
        var (waiting, _) = Transition.Apply(With(DrivePane(0, Fixed)), new Msg.ImageMounted(@"E:\"));

        var (loaded, _) = Transition.Apply(
            waiting,
            new Msg.DirectoryLoaded(0, Location.Drives.Instance, [Fixed, Optical]));

        Assert.Equal(1, loaded.Columns[0].Cursor);
        Assert.Equal(@"E:\", loaded.Columns[0].Entries[loaded.Columns[0].Cursor].Name);

        // Consumed, so an unrelated listing arriving later does not move the cursor again.
        Assert.Null(loaded.RevealTarget);
    }

    /// <summary>Opening the drive is the user's next move, not ours - see the plan's 6d notes.</summary>
    [Fact]
    public void Mounting_does_not_descend_into_the_new_drive()
    {
        var (waiting, _) = Transition.Apply(With(DrivePane(0, Fixed)), new Msg.ImageMounted(@"E:\"));

        var (loaded, _) = Transition.Apply(
            waiting,
            new Msg.DirectoryLoaded(0, Location.Drives.Instance, [Fixed, Optical]));

        Assert.Equal(0, loaded.FocusedColumn);
    }

    [Fact]
    public void A_listing_without_the_awaited_row_leaves_the_target_waiting()
    {
        var (waiting, _) = Transition.Apply(With(DrivePane(0, Fixed)), new Msg.ImageMounted(@"E:\"));

        var (loaded, _) = Transition.Apply(
            waiting,
            new Msg.DirectoryLoaded(0, Location.Drives.Instance, [Fixed]));

        Assert.Equal(@"E:\", loaded.RevealTarget);
    }
}
