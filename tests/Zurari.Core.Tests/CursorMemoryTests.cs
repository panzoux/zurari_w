using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>Coming back to a place lands on the entry you left it on (6e.3).</summary>
public class CursorMemoryTests
{
    private static Entry File(string name) => new(name, EntryKind.File);

    private static readonly Location Home = new Location.RealDirectory(@"C:\home");
    private static readonly Location Away = new Location.RealDirectory(@"C:\away");

    private static AppState At(Location location, params Entry[] entries) =>
        new()
        {
            Columns = [new Column(location, [.. entries], Cursor: entries.Length > 0 ? 0 : -1, Load: LoadState.Loaded)],
            FocusedColumn = 0,
        };

    [Fact]
    public void Moving_the_cursor_records_where_it_is()
    {
        var state = At(Home, File("a.txt"), File("b.txt"), File("c.txt"));

        var (moved, _) = Transition.Apply(state, new Msg.CursorTo(0, 2));

        Assert.Equal("c.txt", moved.RecallCursor(Home));
    }

    /// <summary>
    /// By name, never index: an index means something different after a sort or a delete, and the
    /// point is to come back to the file, not to the fourth row.
    /// </summary>
    [Fact]
    public void Coming_back_lands_on_the_entry_even_though_the_listing_changed_shape()
    {
        var state = At(Home, File("a.txt"), File("b.txt"), File("c.txt"));
        var (moved, _) = Transition.Apply(state, new Msg.CursorTo(0, 2));

        // Somewhere else, then back - and in the meantime a file appeared above the one we want.
        var elsewhere = moved with
        {
            Columns = [new Column(Home, [], Cursor: -1, Load: LoadState.Loading)],
        };
        var (back, _) = Transition.Apply(
            elsewhere,
            new Msg.DirectoryLoaded(0, Home, [File("aa.txt"), File("a.txt"), File("b.txt"), File("c.txt")]));

        Assert.Equal("c.txt", back.Columns[0].Entries[back.Columns[0].Cursor].Name);
    }

    [Fact]
    public void A_place_never_visited_starts_at_the_top()
    {
        var state = At(Home, File("a.txt"));
        var opening = state with { Columns = [new Column(Away, [], Cursor: -1, Load: LoadState.Loading)] };

        var (loaded, _) = Transition.Apply(
            opening, new Msg.DirectoryLoaded(0, Away, [File("x.txt"), File("y.txt")]));

        Assert.Equal(0, loaded.Columns[0].Cursor);
    }

    /// <summary>
    /// A remembered entry that has since been deleted must not leave the cursor nowhere - the
    /// listing simply starts at the top again.
    /// </summary>
    [Fact]
    public void A_remembered_entry_that_is_gone_falls_back_to_the_top()
    {
        var state = At(Home, File("a.txt"), File("gone.txt"));
        var (moved, _) = Transition.Apply(state, new Msg.CursorTo(0, 1));
        var reopening = moved with { Columns = [new Column(Home, [], Cursor: -1, Load: LoadState.Loading)] };

        var (loaded, _) = Transition.Apply(reopening, new Msg.DirectoryLoaded(0, Home, [File("a.txt")]));

        Assert.Equal(0, loaded.Columns[0].Cursor);
        Assert.Empty(loaded.CheckInvariants());
    }

    /// <summary>
    /// Re-reading a column the user is standing in must not move their cursor to where it was some
    /// time ago - <c>WithAllEntries</c> has already kept it where it is.
    /// </summary>
    [Fact]
    public void Refreshing_a_column_in_use_does_not_move_the_cursor_back()
    {
        var state = At(Home, File("a.txt"), File("b.txt"), File("c.txt"));
        var (onC, _) = Transition.Apply(state, new Msg.CursorTo(0, 2));
        var (onA, _) = Transition.Apply(onC, new Msg.CursorTo(0, 0));

        var (reloaded, _) = Transition.Apply(
            onA, new Msg.DirectoryLoaded(0, Home, [File("a.txt"), File("b.txt"), File("c.txt")]));

        Assert.Equal("a.txt", reloaded.Columns[0].Entries[reloaded.Columns[0].Cursor].Name);
    }

    /// <summary>
    /// An explicit reveal - the drive a disc image was just mounted as - is a thing the user asked
    /// for now, and outranks where they happened to be last time.
    /// </summary>
    [Fact]
    public void An_explicit_reveal_wins_over_the_memory()
    {
        var state = At(Home, File("a.txt"), File("b.txt"));
        var (moved, _) = Transition.Apply(state, new Msg.CursorTo(0, 1));
        var reopening = moved with
        {
            Columns = [new Column(Home, [], Cursor: -1, Load: LoadState.Loading)],
            RevealTarget = "a.txt",
        };

        var (loaded, _) = Transition.Apply(
            reopening, new Msg.DirectoryLoaded(0, Home, [File("a.txt"), File("b.txt")]));

        Assert.Equal("a.txt", loaded.Columns[0].Entries[loaded.Columns[0].Cursor].Name);
        Assert.Null(loaded.RevealTarget);
    }

    [Fact]
    public void Visiting_a_place_again_moves_it_to_the_front_rather_than_recording_it_twice()
    {
        var state = At(Home, File("a.txt"), File("b.txt"));

        var (first, _) = Transition.Apply(state, new Msg.CursorTo(0, 1));
        var (second, _) = Transition.Apply(first, new Msg.CursorTo(0, 0));

        Assert.Single(second.CursorMemory);
        Assert.Equal("a.txt", second.RecallCursor(Home));
        Assert.Empty(second.CheckInvariants());
    }

    /// <summary>
    /// The cap is what makes this a memory rather than a leak: without it the map grows for as long
    /// as the app runs, and the property tests would grow it without bound.
    /// </summary>
    [Fact]
    public void The_memory_is_capped_and_drops_the_oldest()
    {
        var state = new AppState
        {
            Columns = [new Column(Home, [File("a.txt")], Cursor: 0, Load: LoadState.Loaded)],
            FocusedColumn = 0,
        };

        for (var i = 0; i < AppState.MaxRememberedCursors + 20; i++)
        {
            state = state.RememberCursor(new Location.RealDirectory($@"C:\dir{i}"), $"file{i}.txt");
        }

        Assert.Equal(AppState.MaxRememberedCursors, state.CursorMemory.Length);
        Assert.Empty(state.CheckInvariants());

        // The most recent survived and the first one did not.
        Assert.NotNull(state.RecallCursor(new Location.RealDirectory($@"C:\dir{AppState.MaxRememberedCursors + 19}")));
        Assert.Null(state.RecallCursor(new Location.RealDirectory(@"C:\dir0")));
    }

    [Fact]
    public void An_over_full_memory_is_an_invariant_violation()
    {
        var overfull = ImmutableArray.CreateBuilder<RememberedCursor>();
        for (var i = 0; i <= AppState.MaxRememberedCursors; i++)
        {
            overfull.Add(new RememberedCursor(new Location.RealDirectory($@"C:\d{i}"), "x"));
        }

        var state = new AppState
        {
            Columns = [new Column(Home, [File("a.txt")], Cursor: 0, Load: LoadState.Loaded)],
            FocusedColumn = 0,
            CursorMemory = overfull.ToImmutable(),
        };

        Assert.Contains(state.CheckInvariants(), v => v.Contains("over the cap", StringComparison.Ordinal));
    }

    [Fact]
    public void The_same_place_twice_in_the_memory_is_an_invariant_violation()
    {
        var state = new AppState
        {
            Columns = [new Column(Home, [File("a.txt")], Cursor: 0, Load: LoadState.Loaded)],
            FocusedColumn = 0,
            CursorMemory = [new RememberedCursor(Home, "a.txt"), new RememberedCursor(Home, "b.txt")],
        };

        Assert.Contains(state.CheckInvariants(), v => v.Contains("more than once", StringComparison.Ordinal));
    }

    /// <summary>
    /// A column that opened beside the cursor has a cursor nobody chose. Recording it would overwrite
    /// where the user actually was the last time they went in.
    /// </summary>
    [Fact]
    public void A_column_that_merely_opened_beside_the_cursor_records_nothing()
    {
        var state = new AppState
        {
            Columns = [new Column(Home, [new Entry("sub", EntryKind.Directory)], Cursor: -1, Load: LoadState.Loading)],
            FocusedColumn = 0,
        };

        var (loaded, _) = Transition.Apply(
            state, new Msg.DirectoryLoaded(0, Home, [new Entry("sub", EntryKind.Directory)]));

        // The child column opened, but only the focused column's location was recorded.
        Assert.Equal(2, loaded.Columns.Length);
        Assert.Single(loaded.CursorMemory);
        Assert.Equal(Home, loaded.CursorMemory[0].Location);
    }
}
