using System.Windows.Input;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// What the sort and hidden-file keys mean. Pure so the whole table is tested directly; which
/// window event asks it is the business of <see cref="MainWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sort is a mode rather than a set of chords: <c>S</c> opens it, and the next key decides - a
/// field (choosing the one in use again flips it, so <c>S N</c> twice reverses name order), Shift
/// with a field for descending, or <c>D</c> for folders first. Any of those closes the mode
/// (Transition does that, not this map). Esc closes it too, and so does any other key, which then
/// goes where it would have gone.
/// </para>
/// <para>
/// The plain letters claimed in normal mode are <c>S</c> (sort), and <c>o</c>, <c>c</c> and <c>m</c>
/// (the split: sync to, copy to and move to the other pane). Incremental search opens on <c>/</c>, as
/// in zrr, and letters that mean nothing yet are left free. Ctrl+C / Ctrl+X / Ctrl+V are the
/// clipboard's and are not touched by <c>c</c>.
/// </para>
/// </remarks>
internal static class KeyMap
{
    /// <summary>
    /// The message for a normal-mode key this map owns, or <c>null</c> for any other key. Tab is
    /// owned only while split (<paramref name="state"/> says so): with one pane it is left to WPF.
    /// </summary>
    public static Msg? ResolveNormal(Key key, ModifierKeys modifiers, AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return (key, modifiers) switch
        {
            (Key.S, ModifierKeys.None) => new Msg.EnterSortMode(),
            (Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift) => new Msg.ToggleHiddenFiles(),
            (Key.W, ModifierKeys.Control) => new Msg.ToggleSplit(),
            (Key.Tab, ModifierKeys.None) when state.Panes.Length == 2 =>
                new Msg.FocusPane(state.Panes[1 - state.FocusedPane].Id),
            (Key.O, ModifierKeys.Shift) => new Msg.SwapPanes(),
            (Key.O, ModifierKeys.None) => new Msg.SyncToOtherPane(),
            (Key.C, ModifierKeys.None) => new Msg.CopyToOtherPane(IsMove: false),
            (Key.M, ModifierKeys.None) => new Msg.CopyToOtherPane(IsMove: true),
            _ => null,
        };
    }

    /// <summary>What a key means while sort mode is open.</summary>
    public static SortKeyOutcome ResolveSortMode(Key key, ModifierKeys modifiers)
    {
        // Shift is pressed before its letter and arrives as a key of its own. Closing the mode on it
        // would make Shift+N impossible to type.
        if (IsModifierKey(key))
        {
            return new SortKeyOutcome.Ignore();
        }

        if (FieldFor(key) is { } field && modifiers is ModifierKeys.None or ModifierKeys.Shift)
        {
            return new SortKeyOutcome.Act(
                modifiers == ModifierKeys.Shift ? new Msg.SetSortDescending(field) : new Msg.SetSortMode(field));
        }

        return (key, modifiers) switch
        {
            (Key.D, ModifierKeys.None) => new SortKeyOutcome.Act(new Msg.ToggleDirectoriesFirst()),
            (Key.Escape, ModifierKeys.None) => new SortKeyOutcome.Act(new Msg.ExitSortMode()),
            _ => new SortKeyOutcome.Close(),
        };
    }

    private static SortMode? FieldFor(Key key) => key switch
    {
        Key.N => SortMode.Name,
        Key.E => SortMode.Extension,
        Key.S => SortMode.Size,
        Key.M => SortMode.Modified,
        _ => null,
    };

    private static bool IsModifierKey(Key key) =>
        key is Key.LeftShift or Key.RightShift or Key.LeftCtrl or Key.RightCtrl
            or Key.LeftAlt or Key.RightAlt or Key.System or Key.LWin or Key.RWin;

    /// <summary>What a key does to an open sort mode.</summary>
    internal abstract record SortKeyOutcome
    {
        /// <summary>A sort key: dispatch <paramref name="Msg"/>, which also closes the mode.</summary>
        public sealed record Act(Msg Msg) : SortKeyOutcome;

        /// <summary>A modifier on its own, still waiting for the key it modifies.</summary>
        public sealed record Ignore : SortKeyOutcome;

        /// <summary>Not a sort key: close the mode, then let the key go where it would have gone.</summary>
        public sealed record Close : SortKeyOutcome;
    }
}
