using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>Opening a file with its default app (6e.6) and making a folder (6e.5).</summary>
public class OpenAndCreateTests
{
    private static readonly Location Here = new Location.RealDirectory(@"C:\here");

    private static AppState With(Location location, params Entry[] entries) =>
        new()
        {
            Columns = [new Column(location, [.. entries], Cursor: 0, Load: LoadState.Loaded)],
            FocusedColumn = 0,
        };

    [Fact]
    public void Opening_a_file_hands_it_to_whatever_windows_opens_it_with()
    {
        var state = With(Here, new Entry("notes.txt", EntryKind.File));

        var (_, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Equal(
            new Effect.OpenWithDefaultApp(@"C:\here\notes.txt"),
            Assert.Single(effects.OfType<Effect.OpenWithDefaultApp>()));
    }

    /// <summary>A disc image is opened by becoming a drive, not by launching something.</summary>
    [Fact]
    public void Opening_a_disc_image_mounts_it_instead()
    {
        var state = With(Here, new Entry("ubuntu.iso", EntryKind.File));

        var (_, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Single(effects.OfType<Effect.MountImage>());
        Assert.Empty(effects.OfType<Effect.OpenWithDefaultApp>());
    }

    [Fact]
    public void Opening_a_folder_still_enters_it()
    {
        var state = With(Here, new Entry("sub", EntryKind.Directory));

        var (next, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Empty(effects.OfType<Effect.OpenWithDefaultApp>());
        Assert.Equal(2, next.Columns.Length);
    }

    [Fact]
    public void A_header_opens_nothing()
    {
        var state = With(Location.Drives.Instance, new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives));

        var (_, effects) = Transition.Apply(state, new Msg.EnterDirectory(0, 0));

        Assert.Empty(effects);
    }

    [Fact]
    public void Asking_for_a_new_folder_asks_the_runtime_to_make_one()
    {
        var state = With(Here, new Entry("a.txt", EntryKind.File));

        var (_, effects) = Transition.Apply(state, new Msg.CreateFolderRequested(0));

        Assert.Equal(new Effect.CreateFolder(0, Here), Assert.Single(effects.OfType<Effect.CreateFolder>()));
    }

    /// <summary>
    /// The drive pane holds places rather than files, and the recycle bin is not somewhere to put
    /// things. Both say so instead of failing silently.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Somewhere_that_cannot_hold_a_folder_says_so(bool bin)
    {
        var state = With(bin ? Location.RecycleBin.Instance : Location.Drives.Instance);

        var (next, effects) = Transition.Apply(state, new Msg.CreateFolderRequested(0));

        Assert.Empty(effects.OfType<Effect.CreateFolder>());
        Assert.NotNull(next.Notice);
    }

    [Fact]
    public void A_created_folder_is_read_back_and_the_cursor_lands_on_it()
    {
        var state = With(Here, new Entry("a.txt", EntryKind.File));

        var (asked, effects) = Transition.Apply(state, new Msg.FolderCreated(0, Here, "新しいフォルダー"));

        Assert.Equal("新しいフォルダー", asked.RevealTarget);
        Assert.Equal(LoadState.Loading, asked.Columns[0].Load);
        Assert.Equal(new Effect.ReadDirectory(0, Here), Assert.Single(effects.OfType<Effect.ReadDirectory>()));

        var (loaded, _) = Transition.Apply(
            asked,
            new Msg.DirectoryLoaded(
                0, Here, [new Entry("新しいフォルダー", EntryKind.Directory), new Entry("a.txt", EntryKind.File)]));

        Assert.Equal("新しいフォルダー", loaded.Columns[0].Entries[loaded.Columns[0].Cursor].Name);
        Assert.Null(loaded.RevealTarget);
    }

    /// <summary>
    /// A report about a column that has since gone somewhere else is stale, and acting on it would
    /// yank the user back to where they no longer are.
    /// </summary>
    [Fact]
    public void A_report_for_a_column_that_moved_on_is_ignored()
    {
        var state = With(new Location.RealDirectory(@"C:\elsewhere"), new Entry("a.txt", EntryKind.File));

        var (next, effects) = Transition.Apply(state, new Msg.FolderCreated(0, Here, "新しいフォルダー"));

        Assert.Empty(effects);
        Assert.Null(next.RevealTarget);
    }
}
