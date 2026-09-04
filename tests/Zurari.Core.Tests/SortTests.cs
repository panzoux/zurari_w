using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>Ordering a listing (6e.1): modes, direction, and folders-first.</summary>
public class SortTests
{
    private static Entry File(string name, long size = 0, int day = 1) =>
        new(name, EntryKind.File, SizeBytes: size, Modified: new DateTime(2026, 1, day));

    private static Entry Dir(string name) => new(name, EntryKind.Directory, SizeBytes: -1);

    private static AppState With(SortOrder sort, params Entry[] entries) =>
        new()
        {
            Columns = [new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1, Load: LoadState.Loaded)],
            FocusedColumn = 0,
            Sort = sort,
        };

    /// <summary>Runs a listing through the real path a listing takes: a load, then the derived view.</summary>
    private static IReadOnlyList<string> Order(SortOrder sort, params Entry[] entries)
    {
        var state = With(sort, entries);
        var (loaded, _) = Transition.Apply(
            state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [.. entries]));
        return [.. loaded.Columns[0].Entries.Select(e => e.Name)];
    }

    /// <summary>
    /// The reason natural ordering is not optional: by character, <c>file10</c> comes before
    /// <c>file2</c>, which is right about text and wrong about what a numbered name means.
    /// </summary>
    [Fact]
    public void Numbers_in_names_are_compared_as_numbers()
    {
        Assert.Equal(
            ["file1.txt", "file2.txt", "file10.txt", "file20.txt"],
            Order(SortOrder.Default, File("file10.txt"), File("file2.txt"), File("file20.txt"), File("file1.txt")));
    }

    [Fact]
    public void Leading_zeros_do_not_change_the_number()
    {
        Assert.Equal(
            ["a2.txt", "a007.txt", "a10.txt"],
            Order(SortOrder.Default, File("a10.txt"), File("a007.txt"), File("a2.txt")));
    }

    [Fact]
    public void Numbers_longer_than_any_integer_still_compare()
    {
        var big = new string('9', 40);
        var bigger = "1" + new string('0', 40);

        Assert.Equal(
            [$"n{big}.txt", $"n{bigger}.txt"],
            Order(SortOrder.Default, File($"n{bigger}.txt"), File($"n{big}.txt")));
    }

    [Fact]
    public void Folders_come_first_and_are_sorted_among_themselves()
    {
        Assert.Equal(
            ["alpha", "beta", "a.txt", "z.txt"],
            Order(SortOrder.Default, File("z.txt"), Dir("beta"), File("a.txt"), Dir("alpha")));
    }

    [Fact]
    public void Folders_first_can_be_turned_off()
    {
        Assert.Equal(
            ["a.txt", "alpha", "beta", "z.txt"],
            Order(
                SortOrder.Default with { DirectoriesFirst = false },
                File("z.txt"), Dir("beta"), File("a.txt"), Dir("alpha")));
    }

    /// <summary>
    /// Reversing reverses the mode, not the listing. Folders stay on top - "biggest first, folders
    /// above" is a normal thing to want, and a fully reversed list is not.
    /// </summary>
    [Fact]
    public void Descending_reverses_the_mode_but_not_the_folders_first_rule()
    {
        Assert.Equal(
            ["beta", "alpha", "z.txt", "a.txt"],
            Order(
                SortOrder.Default with { Descending = true },
                File("a.txt"), Dir("alpha"), File("z.txt"), Dir("beta")));
    }

    [Fact]
    public void Sorting_by_size_puts_the_biggest_last_and_reversed_puts_it_first()
    {
        var entries = new[] { File("m.txt", 50), File("s.txt", 5), File("l.txt", 500) };

        Assert.Equal(["s.txt", "m.txt", "l.txt"], Order(new SortOrder(SortMode.Size), entries));
        Assert.Equal(
            ["l.txt", "m.txt", "s.txt"],
            Order(new SortOrder(SortMode.Size, Descending: true), entries));
    }

    [Fact]
    public void Sorting_by_modified_orders_oldest_first()
    {
        Assert.Equal(
            ["old.txt", "mid.txt", "new.txt"],
            Order(new SortOrder(SortMode.Modified), File("new.txt", day: 20), File("old.txt", day: 1), File("mid.txt", day: 10)));
    }

    [Fact]
    public void Sorting_by_extension_groups_by_it_and_orders_by_name_within()
    {
        Assert.Equal(
            ["b.md", "a.txt", "b.txt"],
            Order(new SortOrder(SortMode.Extension), File("b.txt"), File("a.txt"), File("b.md")));
    }

    /// <summary>
    /// A leading dot is a name, not an extension - <c>.gitignore</c> is a file called that, and
    /// filing it under <c>.g</c> would put it among the gzip files.
    /// </summary>
    [Fact]
    public void A_dotfile_has_no_extension()
    {
        Assert.Equal(string.Empty, NaturalComparer.ExtensionOf(".gitignore"));
        Assert.Equal(".TXT", NaturalComparer.ExtensionOf("a.txt"));
        Assert.Equal(string.Empty, NaturalComparer.ExtensionOf("README"));
    }

    /// <summary>
    /// Two rows the mode and the name cannot tell apart must not swap places depending on how long
    /// the list is - Array.Sort is an introsort, and is not stable on its own.
    /// </summary>
    [Fact]
    public void Rows_that_compare_equal_keep_the_order_they_arrived_in()
    {
        var first = File("same.txt", 1);
        var second = File("same.txt", 1);
        var state = With(new SortOrder(SortMode.Size), first, second);

        var (loaded, _) = Transition.Apply(
            state, new Msg.DirectoryLoaded(0, new Location.RealDirectory(@"C:\"), [first, second]));

        Assert.Same(first, loaded.Columns[0].Entries[0]);
        Assert.Same(second, loaded.Columns[0].Entries[1]);
    }

    [Fact]
    public void Case_does_not_split_the_alphabet()
    {
        Assert.Equal(
            ["Alpha.txt", "alpha2.txt", "Beta.txt"],
            Order(SortOrder.Default, File("Beta.txt"), File("alpha2.txt"), File("Alpha.txt")));
    }

    /// <summary>The drive pane is curated - sorting it would scatter the headers through the drives.</summary>
    [Fact]
    public void The_drive_pane_is_never_reordered()
    {
        var pane = new Column(
            Location.Drives.Instance,
            [
                new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives),
                new Entry(@"Z:\", EntryKind.Drive, Group: EntryGroups.Drives),
                new Entry(@"C:\", EntryKind.Drive, Group: EntryGroups.Drives),
            ],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = new AppState { Columns = [pane], FocusedColumn = 0 };

        var (next, _) = Transition.Apply(state, new Msg.SetSortMode(SortMode.Name));

        Assert.Equal(["ドライブ", @"Z:\", @"C:\"], next.Columns[0].Entries.Select(e => e.Name));
    }

    [Fact]
    public void Choosing_the_mode_already_in_use_reverses_it_instead()
    {
        var order = SortOrder.Default;

        var again = order.Select(SortMode.Name);
        Assert.True(again.Descending);

        var third = again.Select(SortMode.Name);
        Assert.False(third.Descending);

        // A different mode starts ascending rather than inheriting the reversal.
        Assert.False(again.Select(SortMode.Size).Descending);
        Assert.Equal(SortMode.Size, again.Select(SortMode.Size).Mode);
    }

    [Fact]
    public void Changing_the_order_re_sorts_every_open_column_and_remembers_it()
    {
        var left = new Column(
            new Location.RealDirectory(@"C:\"), [File("b.txt", 1), File("a.txt", 9)], Cursor: 0, Load: LoadState.Loaded);
        var right = new Column(
            new Location.RealDirectory(@"C:\x"), [File("d.txt", 1), File("c.txt", 9)], Cursor: 0, Load: LoadState.Loaded);
        var state = new AppState { Columns = [left, right], FocusedColumn = 0 };

        var (next, effects) = Transition.Apply(state, new Msg.SetSortMode(SortMode.Size));

        Assert.Equal(["b.txt", "a.txt"], next.Columns[0].Entries.Select(e => e.Name));
        Assert.Equal(["d.txt", "c.txt"], next.Columns[1].Entries.Select(e => e.Name));
        Assert.Equal(
            new Effect.SetSortOrder(new SortOrder(SortMode.Size)),
            Assert.Single(effects.OfType<Effect.SetSortOrder>()));
    }

    /// <summary>
    /// Re-sorting moves every row, so following the index would land the cursor on a different file -
    /// which is then what the next Delete or Enter acts on.
    /// </summary>
    [Fact]
    public void The_cursor_stays_on_its_entry_when_the_order_changes()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"),
            [File("a.txt", 100), File("b.txt", 10), File("c.txt", 1)],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = new AppState { Columns = [column], FocusedColumn = 0 };

        var (next, _) = Transition.Apply(state, new Msg.SetSortMode(SortMode.Size));

        Assert.Equal(["c.txt", "b.txt", "a.txt"], next.Columns[0].Entries.Select(e => e.Name));
        Assert.Equal("a.txt", next.Columns[0].Entries[next.Columns[0].Cursor].Name);
    }

    [Fact]
    public void Restoring_a_stored_order_applies_it_without_writing_it_back()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"), [File("a.txt", 9), File("b.txt", 1)], Cursor: 0, Load: LoadState.Loaded);
        var state = new AppState { Columns = [column], FocusedColumn = 0 };

        var (next, effects) = Transition.Apply(
            state, new Msg.SortOrderRestored(new SortOrder(SortMode.Size)));

        Assert.Equal(["b.txt", "a.txt"], next.Columns[0].Entries.Select(e => e.Name));
        Assert.Equal(new SortOrder(SortMode.Size), next.Sort);
        Assert.Empty(effects.OfType<Effect.SetSortOrder>());
    }

    [Fact]
    public void A_marked_row_keeps_its_mark_across_a_re_sort()
    {
        var column = new Column(
            new Location.RealDirectory(@"C:\"),
            [File("a.txt", 100) with { IsMarked = true }, File("b.txt", 1)],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = new AppState { Columns = [column], FocusedColumn = 0 };

        var (next, _) = Transition.Apply(state, new Msg.SetSortMode(SortMode.Size));

        var moved = next.Columns[0].Entries.Single(e => e.Name == "a.txt");
        Assert.True(moved.IsMarked);
        Assert.Empty(next.CheckInvariants());
    }

    [Fact]
    public void Toggling_folders_first_keeps_the_mode()
    {
        var state = new AppState
        {
            Columns = [new Column(new Location.RealDirectory(@"C:\"), [], Cursor: -1, Load: LoadState.Loaded)],
            FocusedColumn = 0,
            Sort = new SortOrder(SortMode.Size, Descending: true),
        };

        var (next, _) = Transition.Apply(state, new Msg.ToggleDirectoriesFirst());

        Assert.Equal(SortMode.Size, next.Sort.Mode);
        Assert.True(next.Sort.Descending);
        Assert.False(next.Sort.DirectoriesFirst);
    }
}
