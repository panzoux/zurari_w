using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Zurari.Core;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.Core.Tests;

/// <summary>Leap (7b.4): narrow the focused column as you type, walk into folders.</summary>
public class LeapTests
{
    private static readonly Location Root = new Location.RealDirectory(@"C:\w");

    private static Entry Dir(string name) => new(name, EntryKind.Directory);

    private static Entry File(string name) => new(name, EntryKind.File);

    private static Column Listing(Location location, int cursor, params Entry[] entries) =>
        new Column(location, [], Cursor: -1, Load: LoadState.Loaded).WithAllEntries([.. entries], ViewOptions.Default) with
        {
            Cursor = entries.Length == 0 ? -1 : cursor,
        };

    private static AppState Browsing(int cursor = 0) => AppState.SinglePane(
    [
        Listing(Root, cursor, Dir("mapmodels"), Dir("maps"), File("map2tex.txt"), File("readme.md"), File("tex2map.txt"), File("日本.txt")),
    ]);

    private static AppState Apply(AppState state, params Msg[] msgs)
    {
        foreach (var msg in msgs)
        {
            state = Transition.Apply(state, msg).State;
        }

        return state;
    }

    private static List<string> Shown(AppState state, int column = -1) =>
        TestEntries.Names(state.Focused.Columns[column < 0 ? state.Focused.FocusedColumn : column].Entries);

    private static string? CursorName(AppState state) => state.Focused.FocusedColumnOrNull!.CursorName;

    private static AppState Loaded(AppState state, Location location, params Entry[] entries) =>
        Apply(state, new Msg.DirectoryLoaded(state.Focused.FocusedRef, location, [.. entries]));

    [Fact]
    public void Typing_hides_what_does_not_match_and_keeps_the_listing_order()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("map tex"));

        Assert.Equal(["map2tex.txt", "tex2map.txt"], Shown(state));
        Assert.Equal(6, state.Focused.Columns[0].AllEntries.Length);
    }

    [Fact]
    public void The_cursor_goes_to_the_first_prefix_match_before_any_substring_match()
    {
        var state = Apply(Browsing(cursor: 3), new Msg.BeginLeap(), new Msg.LeapTextChanged("tex"));

        Assert.Equal(["map2tex.txt", "tex2map.txt"], Shown(state));
        Assert.Equal("tex2map.txt", CursorName(state));
    }

    [Fact]
    public void Migemo_only_matches_show_only_when_the_letters_match_nothing()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("nihon"));
        Assert.Empty(Shown(state));

        state = Apply(state, new Msg.MigemoExpanded("nihon", new Regex("日本", NameQuery.PatternOptions)));
        Assert.Equal(["日本.txt"], Shown(state));

        // "re" matches readme.md by its letters, so a pattern that would also match 日本 adds nothing.
        state = Apply(state, new Msg.LeapTextChanged("re"), new Msg.MigemoExpanded("re", new Regex("日本|re", NameQuery.PatternOptions)));
        Assert.Equal(["readme.md"], Shown(state));
    }

    [Fact]
    public void The_preview_is_empty_while_leaping_and_comes_back_after()
    {
        var onFile = Apply(Browsing(cursor: 2), new Msg.CursorDown(Col(0)));
        Assert.Equal(PreviewKind.Loading, onFile.Focused.Preview.Kind);

        var leaping = Apply(onFile, new Msg.BeginLeap());
        Assert.Equal(PreviewKind.None, leaping.Focused.Preview.Kind);

        var (ended, effects) = Transition.Apply(leaping, new Msg.EndLeap(Restore: false));
        Assert.Equal(PreviewKind.Loading, ended.Focused.Preview.Kind);
        Assert.Single(TestEffects.Of<Effect.LoadPreview>(effects));
    }

    [Fact]
    public void A_lone_folder_match_is_walked_into_once_it_has_loaded()
    {
        var (typed, effects) = Transition.Apply(Apply(Browsing(), new Msg.BeginLeap()), new Msg.LeapTextChanged("mapm"));

        Assert.Equal(1, typed.Focused.FocusedColumn);
        Assert.Single(typed.Focused.Leap!.Trail);
        Assert.Equal("mapm", typed.Focused.Leap!.Trail[0].Text);
        Assert.Contains(TestEffects.Of<Effect.ReadDirectory>(effects), r => r.At == Col(1));
        Assert.True(typed.Focused.Leap!.AutoEnterArmed);
    }

    [Fact]
    public void A_chain_of_single_folders_unrolls_as_each_one_loads()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("mapm"));
        var models = Root.Child("mapmodels");

        state = Loaded(state, models, Dir("only"));
        Assert.Equal(2, state.Focused.FocusedColumn);

        state = Loaded(state, models.Child("only"), File("a.txt"), File("b.txt"));
        Assert.Equal(2, state.Focused.FocusedColumn);
        Assert.False(state.Focused.Leap!.AutoEnterArmed);
        Assert.Equal(2, state.Focused.Leap!.Trail.Length);
    }

    [Fact]
    public void Starting_a_Leap_does_not_walk_into_a_lone_folder()
    {
        var single = AppState.SinglePane([Listing(Root, 0, Dir("only"))]);

        var state = Apply(single, new Msg.BeginLeap());

        Assert.Equal(0, state.Focused.FocusedColumn);
    }

    [Fact]
    public void Stepping_out_puts_back_the_filter_that_led_in()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("mapm"));
        state = Loaded(state, Root.Child("mapmodels"), File("we.txt"), File("x.txt"));
        state = Apply(state, new Msg.LeapTextChanged("we"));
        Assert.Equal(["we.txt"], Shown(state));

        state = Apply(state, new Msg.LeapStepOut());

        Assert.Equal(0, state.Focused.FocusedColumn);
        Assert.Equal("mapm", state.Focused.Columns[0].Filter!.Text);
        Assert.Equal(["mapmodels"], Shown(state));
        Assert.Null(state.Focused.Columns[1].Filter);
        Assert.Equal(2, state.Focused.Columns[1].Entries.Length);
        Assert.False(state.Focused.Leap!.AutoEnterArmed);
    }

    [Fact]
    public void Stepping_out_of_the_starting_column_does_nothing()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("ma"));

        Assert.Same(state, Apply(state, new Msg.LeapStepOut()));
    }

    [Fact]
    public void Stepping_in_on_a_file_does_nothing()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("readme"));

        Assert.Same(state, Apply(state, new Msg.LeapStepIn()));
    }

    [Fact]
    public void Back_to_the_root_clears_every_filter()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("mapm"));
        state = Loaded(state, Root.Child("mapmodels"), File("we.txt"), File("x.txt"));
        state = Apply(state, new Msg.LeapTextChanged("we"), new Msg.LeapToRoot());

        Assert.Equal(0, state.Focused.FocusedColumn);
        Assert.Empty(state.Focused.Leap!.Trail);
        Assert.Equal(6, state.Focused.Columns[0].Entries.Length);
        Assert.Null(state.Focused.Columns[1].Filter);
    }

    [Fact]
    public void Esc_goes_back_to_where_the_Leap_started()
    {
        var state = Apply(Browsing(cursor: 3), new Msg.BeginLeap(), new Msg.LeapTextChanged("mapm"));
        state = Loaded(state, Root.Child("mapmodels"), File("we.txt"), File("x.txt"));

        state = Apply(state, new Msg.EndLeap(Restore: true));

        Assert.Null(state.Focused.Leap);
        Assert.Equal(0, state.Focused.FocusedColumn);
        Assert.Equal("readme.md", CursorName(state));
        Assert.Equal(6, state.Focused.Columns[0].Entries.Length);
    }

    [Fact]
    public void Enter_stays_where_the_Leap_took_it_and_shows_everything_again()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("tex2"));

        state = Apply(state, new Msg.EndLeap(Restore: false));

        Assert.Null(state.Focused.Leap);
        Assert.Equal("tex2map.txt", CursorName(state));
        Assert.Equal(6, state.Focused.Columns[0].Entries.Length);
    }

    [Fact]
    public void Marks_on_rows_the_filter_hides_survive()
    {
        var state = Apply(Browsing(cursor: 3), new Msg.ToggleMarkAtCursor(Col(0)), new Msg.BeginLeap(), new Msg.LeapTextChanged("tex"));
        state = Apply(state, new Msg.EndLeap(Restore: false));

        Assert.Contains(state.Focused.Columns[0].Entries, e => e.Name == "readme.md" && e.IsMarked);
    }

    [Fact]
    public void Clicking_another_column_confirms_the_Leap()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("mapm"));
        state = Loaded(state, Root.Child("mapmodels"), File("we.txt"), File("x.txt"));

        state = Apply(state, new Msg.FocusColumn(Col(0)));

        Assert.Null(state.Focused.Leap);
        Assert.Null(state.Focused.Columns[0].Filter);
    }

    [Fact]
    public void A_reread_while_leaping_waits_for_the_Leap_to_close()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("ma"));

        var (changed, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged(@"C:\w"));
        Assert.Empty(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.True(changed.Focused.NeedsRefresh);

        var (_, closeEffects) = Transition.Apply(changed, new Msg.EndLeap(Restore: true));
        Assert.NotEmpty(TestEffects.Of<Effect.ReadDirectory>(closeEffects));
    }

    [Fact]
    public void A_listing_that_lands_while_leaping_is_filtered_too()
    {
        var loading = AppState.SinglePane([new Column(Root, [], Cursor: -1, Load: LoadState.Loading)]);
        var state = Apply(loading, new Msg.BeginLeap(), new Msg.LeapTextChanged("tex"));

        state = Loaded(state, Root, File("tex.txt"), File("other.txt"));

        Assert.Equal(["tex.txt"], Shown(state));
    }

    [Fact]
    public void Headers_are_never_matched()
    {
        var drives = AppState.SinglePane(
        [
            new Column(Location.Drives.Instance, [new Entry("ドライブ", EntryKind.Header, Group: "ドライブ"), new Entry(@"C:\", EntryKind.Drive, DisplayName: "Windows (C:)", Group: "ドライブ")], Cursor: 1, Load: LoadState.Loaded),
        ]);

        var state = Apply(drives, new Msg.BeginLeap(), new Msg.LeapTextChanged("ド"));

        Assert.Empty(Shown(state));
    }
}
