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
        Assert.Equal(new Msg.EnterSortMode(), KeyMap.ResolveNormal(Key.S, ModifierKeys.None));
    }

    [Fact]
    public void Ctrl_shift_period_toggles_hidden_files()
    {
        Assert.Equal(
            new Msg.ToggleHiddenFiles(),
            KeyMap.ResolveNormal(Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift));
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
        Assert.Null(KeyMap.ResolveNormal(key, ModifierKeys.None));
    }

    [Fact]
    public void S_with_a_modifier_does_not_open_sort_mode()
    {
        Assert.Null(KeyMap.ResolveNormal(Key.S, ModifierKeys.Control));
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
