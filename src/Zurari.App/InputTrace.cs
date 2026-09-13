using System.Globalization;
using System.Windows.Input;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// The text of the <c>--debug-input</c> lines about keys and what they did to the state. Pure, so
/// the format is pinned by tests; whether and where a line is written is the business of
/// <see cref="MainWindow"/>.
/// </summary>
/// <remarks>
/// Written to find where a keypress stops having an effect. Each boundary gets its own line: the
/// window's preview and bubbling key handlers (did the key arrive, as what, was it already
/// handled, what did the key map make of it), the state after anything that changes the mode or
/// the view (did Core act), and the rows handed to the column browser (did the screen get it).
/// </remarks>
internal static class InputTrace
{
    /// <summary>One key event as a handler saw it.</summary>
    /// <param name="phase">Which handler: <c>previewKey</c> or <c>key</c>.</param>
    /// <param name="key">The raw <see cref="KeyEventArgs.Key"/>.</param>
    /// <param name="effective">The key after IME and Alt normalisation; shown only when it differs.</param>
    /// <param name="modifiers">Modifiers held.</param>
    /// <param name="mode">The input mode when the key arrived.</param>
    /// <param name="focus">Type name of the element with keyboard focus, or <c>null</c>.</param>
    /// <param name="editing">Whether an editable text box owns the keys.</param>
    /// <param name="handled">Whether something had already handled the event before this handler.</param>
    public static string KeyEvent(
        string phase,
        Key key,
        Key effective,
        ModifierKeys modifiers,
        Core.InputMode mode,
        string? focus,
        bool editing,
        bool handled)
    {
        var effectivePart = effective == key ? string.Empty : " effective=" + effective;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"[app] {phase} key={key}{effectivePart} mods={modifiers} mode={mode} focus={focus ?? "(none)"} editing={editing} handled={handled}");
    }

    /// <summary>What a key resolved to, and where.</summary>
    public static string Resolution(string phase, string outcome) =>
        string.Create(CultureInfo.InvariantCulture, $"[app] {phase} -> {outcome}");

    /// <summary>
    /// The parts of the state a key could have changed, plus enough of the focused column to tell
    /// whether its rows actually moved.
    /// </summary>
    public static string State(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var sort = state.View.Sort;
        var direction = sort.Descending ? "desc" : "asc";
        var folders = sort.DirectoriesFirst ? "/dirsFirst" : string.Empty;
        var line = string.Create(
            CultureInfo.InvariantCulture,
            $"[app] state mode={state.InputMode} sort={sort.Mode}/{direction}{folders} hidden={state.View.ShowHidden} focused={state.FocusedColumn}");

        if (state.FocusedColumn >= 0 && state.FocusedColumn < state.Columns.Length)
        {
            var column = state.Columns[state.FocusedColumn];
            line += string.Create(
                CultureInfo.InvariantCulture,
                $" loc={Describe(column.Location)} sortable={column.IsSortable} first=[{FirstNames(column.Entries.Select(e => e.Name))}]");
        }

        return line;
    }

    /// <summary>The first few names of a list, for a log line: enough to see an order change.</summary>
    public static string FirstNames(IEnumerable<string> names)
    {
        ArgumentNullException.ThrowIfNull(names);
        return string.Join(", ", names.Take(3));
    }

    private static string Describe(Location location) => location switch
    {
        Location.RealDirectory directory => directory.Path,
        Location.Drives => "Drives",
        Location.RecycleBin => "RecycleBin",
        _ => location.GetType().Name,
    };
}
