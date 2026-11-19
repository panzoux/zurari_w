using System.Collections.Immutable;
using System.Text.RegularExpressions;
using Zurari.Core;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.Core.Tests;

/// <summary><c>/</c> search (7b.3): highlight matches in the focused column, hide nothing.</summary>
public class SearchTests
{
    private static Column Files(string path, int cursor, params string[] names)
    {
        var entries = ImmutableArray.CreateBuilder<Entry>(names.Length);
        foreach (var name in names)
        {
            entries.Add(new Entry(name, EntryKind.File));
        }

        return new Column(new Location.RealDirectory(path), entries.MoveToImmutable(), Cursor: cursor, Load: LoadState.Loaded);
    }

    private static AppState Browsing(int cursor = 0) =>
        AppState.SinglePane([Files(@"C:\a", cursor, "alpha.txt", "beta.txt", "gamma.txt", "alphabet.txt", "日本.txt")]);

    private static AppState Apply(AppState state, params Msg[] msgs)
    {
        foreach (var msg in msgs)
        {
            state = Transition.Apply(state, msg).State;
        }

        return state;
    }

    private static Column Searched(AppState state) => state.Focused.Columns[0];

    [Fact]
    public void Typing_moves_the_cursor_to_the_first_match_after_where_the_search_started()
    {
        var state = Apply(Browsing(cursor: 1), new Msg.BeginSearch(), new Msg.SearchTextChanged("alpha"));

        var search = state.Focused.Search!;
        Assert.Equal([0, 3], search.Matches);
        Assert.Equal(1, search.MatchIndex);
        Assert.Equal(3, Searched(state).Cursor);
    }

    [Fact]
    public void Nothing_is_hidden()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));

        Assert.Equal(5, Searched(state).Entries.Length);
    }

    [Fact]
    public void Stepping_wraps_around_both_ways()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("alpha"));
        Assert.Equal(0, Searched(state).Cursor);

        state = Apply(state, new Msg.SearchStep(1));
        Assert.Equal(3, Searched(state).Cursor);

        state = Apply(state, new Msg.SearchStep(1));
        Assert.Equal(0, Searched(state).Cursor);

        state = Apply(state, new Msg.SearchStep(-1));
        Assert.Equal(3, Searched(state).Cursor);
    }

    [Fact]
    public void Esc_goes_back_to_where_the_search_started_and_Enter_stays()
    {
        var searching = Apply(Browsing(cursor: 2), new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));
        Assert.Equal(1, Searched(searching).Cursor);

        var cancelled = Apply(searching, new Msg.EndSearch(Restore: true));
        Assert.Null(cancelled.Focused.Search);
        Assert.Equal(2, Searched(cancelled).Cursor);

        var confirmed = Apply(searching, new Msg.EndSearch(Restore: false));
        Assert.Null(confirmed.Focused.Search);
        Assert.Equal(1, Searched(confirmed).Cursor);
    }

    [Fact]
    public void Clearing_the_text_goes_back_to_where_the_search_started()
    {
        var state = Apply(
            Browsing(cursor: 2), new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"), new Msg.SearchTextChanged(""));

        Assert.Equal(2, Searched(state).Cursor);
        Assert.Empty(state.Focused.Search!.Matches);
    }

    [Fact]
    public void Typing_asks_for_each_new_word_once()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch());

        var (first, effects) = Transition.Apply(state, new Msg.SearchTextChanged("niho"));
        Assert.Equal("niho", Assert.Single(TestEffects.Of<Effect.ExpandMigemo>(effects)).Word);

        var (_, again) = Transition.Apply(first, new Msg.SearchTextChanged("niho tx"));
        Assert.Equal("tx", Assert.Single(TestEffects.Of<Effect.ExpandMigemo>(again)).Word);
    }

    [Fact]
    public void A_migemo_pattern_adds_matches_when_it_arrives()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("nihon"));
        Assert.Empty(state.Focused.Search!.Matches);

        state = Apply(state, new Msg.MigemoExpanded("nihon", new Regex("日本", NameQuery.PatternOptions)));

        Assert.Equal([4], state.Focused.Search!.Matches);
        Assert.Equal(4, Searched(state).Cursor);
    }

    [Fact]
    public void A_pattern_for_text_since_edited_away_is_dropped()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("nihon"), new Msg.SearchTextChanged("be"));

        var after = Apply(state, new Msg.MigemoExpanded("nihon", new Regex("日本", NameQuery.PatternOptions)));

        Assert.Same(state, after);
    }

    [Fact]
    public void A_bad_regular_expression_matches_nothing()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.ToggleSearchRegex(), new Msg.SearchTextChanged("("));

        Assert.True(state.Focused.Search!.Query.IsInvalid);
        Assert.Empty(state.Focused.Search!.Matches);
    }

    [Fact]
    public void A_regular_expression_does_not_ask_for_migemo()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.ToggleSearchRegex());

        var (searched, effects) = Transition.Apply(state, new Msg.SearchTextChanged("^a.*t$"));

        Assert.Empty(TestEffects.Of<Effect.ExpandMigemo>(effects));
        Assert.Equal([0, 3], searched.Focused.Search!.Matches);
    }

    [Fact]
    public void Search_does_not_open_while_renaming_or_on_top_of_another_mode()
    {
        var renaming = Apply(Browsing(), new Msg.RenameRequested(Col(0)));
        Assert.Same(renaming, Apply(renaming, new Msg.BeginSearch()));

        var leaping = Apply(Browsing(), new Msg.BeginLeap());
        Assert.Same(leaping, Apply(leaping, new Msg.BeginSearch()));
    }

    [Fact]
    public void Opening_a_search_closes_sort_mode()
    {
        var state = Apply(Browsing(), new Msg.EnterSortMode(), new Msg.BeginSearch());

        Assert.Equal(InputMode.Normal, state.InputMode);
        Assert.NotNull(state.Focused.Search);
    }

    [Fact]
    public void Moving_to_another_column_confirms_the_search()
    {
        var two = AppState.SinglePane(
            [Files(@"C:\", 0, "a"), Files(@"C:\a", 0, "alpha.txt", "beta.txt")], focusedColumn: 1);
        var state = Apply(two, new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));

        state = Apply(state, new Msg.FocusColumn(Col(0)));

        Assert.Null(state.Focused.Search);
        Assert.Equal(1, state.Focused.Columns[1].Cursor);
    }

    [Fact]
    public void Focusing_the_other_pane_confirms_the_search()
    {
        var two = TwoPanes(Files(@"C:\a", 0, "alpha.txt", "beta.txt"), Files(@"C:\b", 0, "x"));
        var state = Apply(two, new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));

        state = Apply(state, new Msg.FocusPane(new PaneId(1)));

        Assert.Null(state.Panes[0].Search);
        Assert.Equal(1, state.Panes[0].Columns[0].Cursor);
    }

    [Fact]
    public void A_reread_while_searching_waits_for_the_search_to_close()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));

        var (changed, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged(@"C:\a"));
        Assert.Empty(TestEffects.Of<Effect.ReadDirectory>(effects));
        Assert.True(changed.Focused.NeedsRefresh);
        Assert.Equal(LoadState.Loaded, Searched(changed).Load);

        var (closed, closeEffects) = Transition.Apply(changed, new Msg.EndSearch(Restore: false));
        Assert.False(closed.Focused.NeedsRefresh);
        Assert.Equal(@"C:\a", Assert.Single(TestEffects.Of<Effect.ReadDirectory>(closeEffects)).Location.FilesystemPath);
    }

    [Fact]
    public void A_reread_in_the_other_pane_is_not_held_back()
    {
        var two = TwoPanes(Files(@"C:\a", 0, "alpha.txt"), Files(@"C:\a", 0, "alpha.txt"));
        var state = Apply(two, new Msg.BeginSearch());

        var (changed, effects) = Transition.Apply(state, new Msg.ExternalDirectoryChanged(@"C:\a"));

        Assert.Equal(new PaneId(1), Assert.Single(TestEffects.Of<Effect.ReadDirectory>(effects)).At.Pane);
        Assert.True(changed.Panes[0].NeedsRefresh);
    }

    [Fact]
    public void Matches_follow_a_listing_that_lands_while_searching()
    {
        var loading = AppState.SinglePane(
            [Files(@"C:\a", 0, "alpha.txt") with { Load = LoadState.Loading }]);
        var state = Apply(loading, new Msg.BeginSearch(), new Msg.SearchTextChanged("be"));
        Assert.Empty(state.Focused.Search!.Matches);

        state = Apply(
            state,
            new Msg.DirectoryLoaded(Col(0), new Location.RealDirectory(@"C:\a"), [new Entry("alpha.txt", EntryKind.File), new Entry("beta.txt", EntryKind.File)]));

        Assert.Equal([1], state.Focused.Search!.Matches);
    }
}
