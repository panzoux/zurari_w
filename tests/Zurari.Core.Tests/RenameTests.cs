using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>Renaming (6e.4): what Core decides before and after the filesystem is asked.</summary>
public class RenameTests
{
    private static readonly Location Here = new Location.RealDirectory(@"C:\here");

    private static AppState With(Location location, int cursor, params Entry[] entries) =>
        new()
        {
            Columns = [new Column(location, [.. entries], Cursor: cursor, Load: LoadState.Loaded)],
            FocusedColumn = 0,
        };

    private static AppState Editing(out Entry target)
    {
        target = new Entry("notes.txt", EntryKind.File);
        var state = With(Here, 0, target);
        var (editing, _) = Transition.Apply(state, new Msg.RenameRequested(0));
        return editing;
    }

    [Fact]
    public void F2_opens_the_editor_on_the_row_under_the_cursor()
    {
        var state = Editing(out _);

        Assert.NotNull(state.Rename);
        Assert.Equal("notes.txt", state.Rename!.EntryName);
        Assert.Null(state.Rename.Error);
    }

    /// <summary>
    /// The drive pane holds places, not files. Renaming a favorite would mean renaming the folder it
    /// points at, which is not what the row looks like it is offering.
    /// </summary>
    [Theory]
    [InlineData(EntryKind.Header)]
    [InlineData(EntryKind.Drive)]
    public void Rows_that_are_not_files_cannot_be_renamed(EntryKind kind)
    {
        var state = With(Location.Drives.Instance, 0, new Entry("x", kind, Group: EntryGroups.Drives));

        var (next, _) = Transition.Apply(state, new Msg.RenameRequested(0));

        Assert.Null(next.Rename);
    }

    [Fact]
    public void A_favorite_in_the_drive_pane_cannot_be_renamed_either()
    {
        var state = With(
            Location.Drives.Instance,
            0,
            new Entry(@"C:\work", EntryKind.Directory, Group: EntryGroups.Favorites, IsRemovable: true));

        var (next, _) = Transition.Apply(state, new Msg.RenameRequested(0));

        Assert.Null(next.Rename);
    }

    [Fact]
    public void Accepting_a_name_asks_the_runtime_to_rename()
    {
        var state = Editing(out _);

        var (next, effects) = Transition.Apply(state, new Msg.RenameSubmitted("readme.txt"));

        Assert.Equal(
            new Effect.RenameEntry(0, Here, "notes.txt", "readme.txt"),
            Assert.Single(effects.OfType<Effect.RenameEntry>()));

        // Still open: it closes when the rename actually happened, not when it was asked for.
        Assert.NotNull(next.Rename);
    }

    [Fact]
    public void Surrounding_spaces_are_trimmed()
    {
        var state = Editing(out _);

        var (_, effects) = Transition.Apply(state, new Msg.RenameSubmitted("  readme.txt  "));

        Assert.Equal("readme.txt", Assert.Single(effects.OfType<Effect.RenameEntry>()).NewName);
    }

    [Fact]
    public void The_same_name_just_closes_the_editor()
    {
        var state = Editing(out _);

        var (next, effects) = Transition.Apply(state, new Msg.RenameSubmitted("notes.txt"));

        Assert.Null(next.Rename);
        Assert.Empty(effects.OfType<Effect.RenameEntry>());
    }

    /// <summary>
    /// What can be answered without touching the disk is answered immediately, and the editor keeps
    /// the text - the thing that needs fixing is what is still on screen.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad:name.txt")]
    [InlineData("bad/name.txt")]
    [InlineData("bad?name.txt")]
    public void A_name_that_cannot_work_is_refused_without_asking_the_filesystem(string name)
    {
        var state = Editing(out _);

        var (next, effects) = Transition.Apply(state, new Msg.RenameSubmitted(name));

        Assert.Empty(effects.OfType<Effect.RenameEntry>());
        Assert.NotNull(next.Rename);
        Assert.NotNull(next.Rename!.Error);
    }

    [Fact]
    public void A_refusal_from_the_filesystem_keeps_the_editor_open_with_the_reason()
    {
        var state = Editing(out _);
        var (asked, _) = Transition.Apply(state, new Msg.RenameSubmitted("taken.txt"));

        var (next, _) = Transition.Apply(asked, new Msg.RenameFailed("taken.txt は既にあります"));

        Assert.NotNull(next.Rename);
        Assert.Equal("taken.txt は既にあります", next.Rename!.Error);
        Assert.Equal("notes.txt", next.Rename.EntryName);
    }

    [Fact]
    public void A_second_attempt_clears_the_previous_complaint()
    {
        var state = Editing(out _);
        var (asked, _) = Transition.Apply(state, new Msg.RenameSubmitted("taken.txt"));
        var (failed, _) = Transition.Apply(asked, new Msg.RenameFailed("taken.txt は既にあります"));

        var (retry, effects) = Transition.Apply(failed, new Msg.RenameSubmitted("free.txt"));

        Assert.Null(retry.Rename!.Error);
        Assert.Single(effects.OfType<Effect.RenameEntry>());
    }

    [Fact]
    public void A_successful_rename_closes_the_editor_and_lands_the_cursor_on_the_new_name()
    {
        var state = Editing(out _);
        var (asked, _) = Transition.Apply(state, new Msg.RenameSubmitted("readme.txt"));

        var (done, effects) = Transition.Apply(asked, new Msg.RenameCompleted(0, Here, "readme.txt"));

        Assert.Null(done.Rename);
        Assert.Equal("readme.txt", done.RevealTarget);
        Assert.Single(effects.OfType<Effect.ReadDirectory>());

        var (loaded, _) = Transition.Apply(
            done, new Msg.DirectoryLoaded(0, Here, [new Entry("readme.txt", EntryKind.File)]));

        Assert.Equal("readme.txt", loaded.Columns[0].Entries[loaded.Columns[0].Cursor].Name);
    }

    [Fact]
    public void Giving_up_changes_nothing()
    {
        var state = Editing(out _);

        var (next, effects) = Transition.Apply(state, new Msg.RenameCancelled());

        Assert.Null(next.Rename);
        Assert.Empty(effects.OfType<Effect.RenameEntry>());
    }

    [Fact]
    public void Submitting_with_no_editor_open_does_nothing()
    {
        var state = With(Here, 0, new Entry("notes.txt", EntryKind.File));

        var (next, effects) = Transition.Apply(state, new Msg.RenameSubmitted("x.txt"));

        Assert.Null(next.Rename);
        Assert.Empty(effects.OfType<Effect.RenameEntry>());
    }
}
