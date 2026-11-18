using System.Windows.Input;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

/// <summary>
/// What the sort and hidden-file keys mean. The table is pure, so it is checked here in full rather
/// than through a window.
/// </summary>
public class KeyMapTests
{
    [Fact]
    public void S_opens_sort_mode()
    {
        Assert.Equal(new Msg.EnterSortMode(), KeyMap.ResolveNormal(Key.S, ModifierKeys.None, AppState.Initial));
    }

    [Fact]
    public void Ctrl_shift_period_toggles_hidden_files()
    {
        Assert.Equal(
            new Msg.ToggleHiddenFiles(),
            KeyMap.ResolveNormal(Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift, AppState.Initial));
    }

    /// <summary>
    /// Plain letters stay unclaimed in normal mode. Incremental search will open on "/", not on a
    /// letter, but nothing is gained by taking letters that mean nothing yet.
    /// </summary>
    [Theory]
    [InlineData(Key.N)]
    [InlineData(Key.E)]
    [InlineData(Key.D)]
    public void Other_letters_mean_nothing_in_normal_mode(Key key)
    {
        Assert.Null(KeyMap.ResolveNormal(key, ModifierKeys.None, AppState.Initial));
    }

    [Fact]
    public void S_with_a_modifier_does_not_open_sort_mode()
    {
        Assert.Null(KeyMap.ResolveNormal(Key.S, ModifierKeys.Control, AppState.Initial));
    }

    [Theory]
    [InlineData(Key.W, ModifierKeys.Control, typeof(Msg.ToggleSplit))]
    [InlineData(Key.O, ModifierKeys.Shift, typeof(Msg.SwapPanes))]
    [InlineData(Key.O, ModifierKeys.None, typeof(Msg.SyncToOtherPane))]
    [InlineData(Key.C, ModifierKeys.None, typeof(Msg.CopyToOtherPane))]
    [InlineData(Key.M, ModifierKeys.None, typeof(Msg.CopyToOtherPane))]
    public void Split_keys_resolve(Key key, ModifierKeys modifiers, Type expected)
    {
        Assert.IsType(expected, KeyMap.ResolveNormal(key, modifiers, AppState.Initial));
    }

    [Fact]
    public void M_moves_and_C_copies()
    {
        Assert.Equal(new Msg.CopyToOtherPane(IsMove: true), KeyMap.ResolveNormal(Key.M, ModifierKeys.None, AppState.Initial));
        Assert.Equal(new Msg.CopyToOtherPane(IsMove: false), KeyMap.ResolveNormal(Key.C, ModifierKeys.None, AppState.Initial));
    }

    [Fact]
    public void Tab_focuses_the_other_pane_only_when_split()
    {
        var single = AppState.Initial;
        var (split, _) = Transition.Apply(single, new Msg.ToggleSplit());

        Assert.Null(KeyMap.ResolveNormal(Key.Tab, ModifierKeys.None, single));
        Assert.Equal(new Msg.FocusPane(split.Panes[1].Id), KeyMap.ResolveNormal(Key.Tab, ModifierKeys.None, split));
    }

    [Fact]
    public void Tab_from_the_bottom_pane_goes_back_to_the_top()
    {
        var (split, _) = Transition.Apply(AppState.Initial, new Msg.ToggleSplit());
        var (bottomFocused, _) = Transition.Apply(split, new Msg.FocusPane(split.Panes[1].Id));

        Assert.Equal(
            new Msg.FocusPane(bottomFocused.Panes[0].Id),
            KeyMap.ResolveNormal(Key.Tab, ModifierKeys.None, bottomFocused));
    }

    /// <summary>The clipboard chords are not the split's: Ctrl+C/X/V, Shift+Tab and Ctrl+O stay unmapped here.</summary>
    [Theory]
    [InlineData(Key.C, ModifierKeys.Control)]
    [InlineData(Key.X, ModifierKeys.Control)]
    [InlineData(Key.V, ModifierKeys.Control)]
    [InlineData(Key.Tab, ModifierKeys.Control)]
    [InlineData(Key.O, ModifierKeys.Control)]
    [InlineData(Key.W, ModifierKeys.None)]
    public void Chords_that_look_like_split_keys_are_left_alone(Key key, ModifierKeys modifiers)
    {
        var (split, _) = Transition.Apply(AppState.Initial, new Msg.ToggleSplit());

        Assert.Null(KeyMap.ResolveNormal(key, modifiers, split));
    }

    [Fact]
    public void Shift_Tab_focuses_the_other_pane_only_when_split()
    {
        var single = AppState.Initial;
        var (split, _) = Transition.Apply(single, new Msg.ToggleSplit());

        Assert.Null(KeyMap.ResolveNormal(Key.Tab, ModifierKeys.Shift, single));
        Assert.Equal(new Msg.FocusPane(split.Panes[1].Id), KeyMap.ResolveNormal(Key.Tab, ModifierKeys.Shift, split));
    }

    /// <summary>A guess among the sort letters must never start a job or delete.</summary>
    [Theory]
    [InlineData(Key.C, ModifierKeys.None)]
    [InlineData(Key.Delete, ModifierKeys.None)]
    [InlineData(Key.Delete, ModifierKeys.Shift)]
    [InlineData(Key.V, ModifierKeys.Control)]
    public void In_sort_mode_keys_that_start_a_job_or_delete_close_the_mode_and_do_nothing(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(new KeyMap.SortKeyOutcome.CloseAndSwallow(), KeyMap.ResolveSortMode(key, modifiers));
    }

    [Theory]
    [InlineData(Key.Down, ModifierKeys.None)]
    [InlineData(Key.O, ModifierKeys.None)]
    [InlineData(Key.Tab, ModifierKeys.None)]
    [InlineData(Key.W, ModifierKeys.Control)]
    public void In_sort_mode_other_keys_still_close_and_carry_on(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(new KeyMap.SortKeyOutcome.Close(), KeyMap.ResolveSortMode(key, modifiers));
    }

    [Theory]
    [InlineData(Key.N, SortMode.Name)]
    [InlineData(Key.E, SortMode.Extension)]
    [InlineData(Key.S, SortMode.Size)]
    [InlineData(Key.M, SortMode.Modified)]
    public void A_field_key_chooses_that_field(Key key, SortMode mode)
    {
        Assert.Equal(
            new KeyMap.SortKeyOutcome.Act(new Msg.SetSortMode(mode)),
            KeyMap.ResolveSortMode(key, ModifierKeys.None));
    }

    [Theory]
    [InlineData(Key.N, SortMode.Name)]
    [InlineData(Key.E, SortMode.Extension)]
    [InlineData(Key.S, SortMode.Size)]
    [InlineData(Key.M, SortMode.Modified)]
    public void Shift_with_a_field_key_chooses_it_descending(Key key, SortMode mode)
    {
        Assert.Equal(
            new KeyMap.SortKeyOutcome.Act(new Msg.SetSortDescending(mode)),
            KeyMap.ResolveSortMode(key, ModifierKeys.Shift));
    }

    [Fact]
    public void D_toggles_folders_first()
    {
        Assert.Equal(
            new KeyMap.SortKeyOutcome.Act(new Msg.ToggleDirectoriesFirst()),
            KeyMap.ResolveSortMode(Key.D, ModifierKeys.None));
    }

    [Fact]
    public void Esc_closes_sort_mode()
    {
        Assert.Equal(
            new KeyMap.SortKeyOutcome.Act(new Msg.ExitSortMode()),
            KeyMap.ResolveSortMode(Key.Escape, ModifierKeys.None));
    }

    /// <summary>
    /// Shift is pressed before its letter and arrives as a key of its own. Closing the mode on it
    /// would make Shift+N impossible to type.
    /// </summary>
    [Theory]
    [InlineData(Key.LeftShift)]
    [InlineData(Key.RightShift)]
    [InlineData(Key.LeftCtrl)]
    [InlineData(Key.RightCtrl)]
    [InlineData(Key.LeftAlt)]
    [InlineData(Key.RightAlt)]
    [InlineData(Key.System)]
    public void A_modifier_on_its_own_waits_for_its_key(Key key)
    {
        Assert.Equal(new KeyMap.SortKeyOutcome.Ignore(), KeyMap.ResolveSortMode(key, ModifierKeys.Shift));
    }

    [Theory]
    [InlineData(Key.Down, ModifierKeys.None)]
    [InlineData(Key.Enter, ModifierKeys.None)]
    [InlineData(Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift)]
    public void Any_other_key_closes_the_mode_and_goes_where_it_would_have(Key key, ModifierKeys modifiers)
    {
        Assert.Equal(new KeyMap.SortKeyOutcome.Close(), KeyMap.ResolveSortMode(key, modifiers));
    }
}
