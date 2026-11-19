using System.Text.RegularExpressions;
using System.Windows.Input;
using Zurari.App;
using Zurari.Core;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.App.Tests;

/// <summary>The search and Leap keys and the bar they are typed into (7b.6).</summary>
public class ModeBarTests
{
    private static Column Listing(params Entry[] entries) =>
        new(new Location.RealDirectory(@"C:\w"), [.. entries], Cursor: entries.Length == 0 ? -1 : 0, Load: LoadState.Loaded);

    private static AppState Browsing() => AppState.SinglePane(
        [Listing(new Entry("docs", EntryKind.Directory), new Entry("alpha.txt", EntryKind.File), new Entry("beta.txt", EntryKind.File))]);

    private static AppState Apply(AppState state, params Msg[] msgs)
    {
        foreach (var msg in msgs)
        {
            state = Transition.Apply(state, msg).State;
        }

        return state;
    }

    [Theory]
    [InlineData(Key.OemQuestion)]
    [InlineData(Key.Divide)]
    public void Slash_opens_a_search(Key key)
    {
        Assert.Equal(new Msg.BeginSearch(), KeyMap.ResolveNormal(key, ModifierKeys.None, AppState.Initial));
    }

    [Fact]
    public void F3_opens_a_Leap()
    {
        Assert.Equal(new Msg.BeginLeap(), KeyMap.ResolveNormal(Key.F3, ModifierKeys.None, AppState.Initial));
    }

    [Theory]
    [InlineData(Key.Down, ModifierKeys.None, 1)]
    [InlineData(Key.N, ModifierKeys.Control, 1)]
    [InlineData(Key.Up, ModifierKeys.None, -1)]
    [InlineData(Key.P, ModifierKeys.Control, -1)]
    public void Search_arrows_step_between_matches(Key key, ModifierKeys modifiers, int delta)
    {
        Assert.Equal(new Msg.SearchStep(delta), KeyMap.ResolveSearchKey(key, modifiers));
    }

    [Fact]
    public void Search_owns_only_its_keys()
    {
        Assert.Equal(new Msg.ToggleSearchRegex(), KeyMap.ResolveSearchKey(Key.R, ModifierKeys.Control));
        Assert.Equal(new Msg.EndSearch(Restore: false), KeyMap.ResolveSearchKey(Key.Enter, ModifierKeys.None));
        Assert.Equal(new Msg.EndSearch(Restore: true), KeyMap.ResolveSearchKey(Key.Escape, ModifierKeys.None));
        Assert.Null(KeyMap.ResolveSearchKey(Key.A, ModifierKeys.None));
        Assert.Null(KeyMap.ResolveSearchKey(Key.Back, ModifierKeys.None));
        Assert.Null(KeyMap.ResolveSearchKey(Key.Left, ModifierKeys.None));
    }

    [Fact]
    public void Right_walks_into_a_folder_and_confirms_on_a_file()
    {
        var onFolder = Apply(Browsing(), new Msg.BeginLeap());
        Assert.Equal(new Msg.LeapStepIn(), KeyMap.ResolveLeapKey(Key.Right, ModifierKeys.None, onFolder, boxIsEmpty: true));

        var onFile = Apply(onFolder, new Msg.CursorDown(Col(0)));
        Assert.Equal(new Msg.EndLeap(Restore: false), KeyMap.ResolveLeapKey(Key.Right, ModifierKeys.None, onFile, boxIsEmpty: true));
    }

    [Fact]
    public void Backspace_walks_out_only_once_the_box_is_empty()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap());

        Assert.Equal(new Msg.LeapStepOut(), KeyMap.ResolveLeapKey(Key.Back, ModifierKeys.None, state, boxIsEmpty: true));
        Assert.Null(KeyMap.ResolveLeapKey(Key.Back, ModifierKeys.None, state, boxIsEmpty: false));
    }

    [Fact]
    public void Leap_keys()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap());

        Assert.Equal(new Msg.CursorDown(Col(0)), KeyMap.ResolveLeapKey(Key.Down, ModifierKeys.None, state, false));
        Assert.Equal(new Msg.CursorUp(Col(0)), KeyMap.ResolveLeapKey(Key.P, ModifierKeys.Control, state, false));
        Assert.Equal(new Msg.LeapStepIn(), KeyMap.ResolveLeapKey(Key.OemQuestion, ModifierKeys.None, state, false));
        Assert.Equal(new Msg.LeapStepOut(), KeyMap.ResolveLeapKey(Key.Left, ModifierKeys.None, state, false));
        Assert.Equal(new Msg.LeapToRoot(), KeyMap.ResolveLeapKey(Key.K, ModifierKeys.Control, state, false));
        Assert.Equal(new Msg.LeapTextChanged(string.Empty), KeyMap.ResolveLeapKey(Key.U, ModifierKeys.Control, state, false));
        Assert.Equal(new Msg.EndLeap(Restore: false), KeyMap.ResolveLeapKey(Key.F3, ModifierKeys.None, state, false));
        Assert.Equal(new Msg.EndLeap(Restore: false), KeyMap.ResolveLeapKey(Key.Enter, ModifierKeys.None, state, false));
        Assert.Equal(new Msg.EndLeap(Restore: true), KeyMap.ResolveLeapKey(Key.Escape, ModifierKeys.None, state, false));
        Assert.Null(KeyMap.ResolveLeapKey(Key.A, ModifierKeys.None, state, false));
    }

    [Fact]
    public void No_mode_no_bar()
    {
        Assert.Equal(StateProjection.ModeBarKind.Hidden, StateProjection.ProjectModeBar(Browsing()).Kind);
    }

    [Fact]
    public void The_search_bar_counts_matches()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("txt"),
            new Msg.MigemoExpanded("txt", null), new Msg.SearchStep(1));

        var bar = StateProjection.ProjectModeBar(state);

        Assert.Equal(StateProjection.ModeBarKind.Search, bar.Kind);
        Assert.Equal("/", bar.Prefix);
        Assert.Equal("txt", bar.Text);
        Assert.Equal("(2/2)", bar.Status);
        Assert.False(bar.IsError);
    }

    [Fact]
    public void A_search_waiting_for_migemo_is_not_yet_a_failure()
    {
        var waiting = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("nihon"));
        Assert.False(StateProjection.ProjectModeBar(waiting).IsError);

        var answered = Apply(waiting, new Msg.MigemoExpanded("nihon", new Regex("日本", NameQuery.PatternOptions)));
        var bar = StateProjection.ProjectModeBar(answered);
        Assert.Equal("(0)", bar.Status);
        Assert.True(bar.IsError);
    }

    [Fact]
    public void A_regular_expression_says_so()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.ToggleSearchRegex(), new Msg.SearchTextChanged("("));

        var bar = StateProjection.ProjectModeBar(state);

        Assert.Equal("[regex] (0)", bar.Status);
        Assert.True(bar.IsError);
    }

    [Fact]
    public void The_Leap_bar_shows_the_trail_and_no_match()
    {
        var state = Apply(Browsing(), new Msg.BeginLeap(), new Msg.LeapTextChanged("doc"));
        state = Apply(state, new Msg.DirectoryLoaded(Col(1), new Location.RealDirectory(@"C:\w\docs"), [new Entry("a.txt", EntryKind.File)]));
        state = Apply(state, new Msg.LeapTextChanged("zz"), new Msg.MigemoExpanded("zz", null));

        var bar = StateProjection.ProjectModeBar(state);

        Assert.Equal(StateProjection.ModeBarKind.Leap, bar.Kind);
        Assert.Equal("LEAP  doc/", bar.Prefix);
        Assert.Equal("zz", bar.Text);
        Assert.Equal("(no match)", bar.Status);
    }

    [Fact]
    public void Matches_are_highlighted_in_the_searched_column_only()
    {
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));

        var rows = StateProjection.Project(state, state.Focused)[0].Entries;

        Assert.False(rows[0].IsSearchMatch);
        Assert.False(rows[1].IsSearchMatch);
        Assert.True(rows[2].IsSearchMatch);
    }

    [Fact]
    public void A_cached_projection_is_not_reused_once_the_matches_change()
    {
        var cache = new ProjectionCache();
        var state = Apply(Browsing(), new Msg.BeginSearch(), new Msg.SearchTextChanged("beta"));
        _ = StateProjection.Project(state, state.Focused, cache: cache);

        var cleared = Apply(state, new Msg.SearchTextChanged("zzz"));
        var rows = StateProjection.Project(cleared, cleared.Focused, cache: cache)[0].Entries;

        Assert.False(rows[2].IsSearchMatch);
        Assert.NotEqual(StateProjection.RenderKey(state, state.Focused), StateProjection.RenderKey(cleared, cleared.Focused));
    }
}
