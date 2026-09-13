using System.Windows.Input;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// What the sort and hidden-file keys mean. Pure so the whole table is tested directly; which
/// window event asks it is the business of <see cref="MainWindow"/>.
/// </summary>
/// <remarks>
/// <para>
/// Sort is a mode rather than a set of chords: <c>S</c> opens it, and the keys after it choose
/// fields until it closes - so <c>S N N N</c> keeps flipping name order without pressing S again.
/// Shift with a field key goes straight to descending, and <c>D</c> toggles folders first. Esc
/// closes it, and so does any other key, which then goes where it would have gone.
/// </para>
/// <para>
/// No plain letter other than <c>S</c> is claimed in normal mode. Incremental search opens on
/// <c>/</c>, as in zrr, and letters that mean nothing yet are left free.
/// </para>
/// </remarks>
internal static class KeyMap
{
    /// <summary>The message for a normal-mode key this map owns, or <c>null</c> for any other key.</summary>
    public static Msg? ResolveNormal(Key key, ModifierKeys modifiers) => (key, modifiers) switch
    {
        (Key.S, ModifierKeys.None) => new Msg.EnterSortMode(),
        (Key.OemPeriod, ModifierKeys.Control | ModifierKeys.Shift) => new Msg.ToggleHiddenFiles(),
        _ => null,
    };

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
        /// <summary>A sort key: dispatch <paramref name="Msg"/>, and the mode stays open.</summary>
        public sealed record Act(Msg Msg) : SortKeyOutcome;

        /// <summary>A modifier on its own, still waiting for the key it modifies.</summary>
        public sealed record Ignore : SortKeyOutcome;

        /// <summary>Not a sort key: close the mode, then let the key go where it would have gone.</summary>
        public sealed record Close : SortKeyOutcome;
    }
}
