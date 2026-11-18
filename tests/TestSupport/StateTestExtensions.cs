using Zurari.Core;

namespace Zurari.TestSupport;

/// <summary>Shorthand for the state edits tests make over and over.</summary>
public static class StateTestExtensions
{
    /// <summary>
    /// <paramref name="state"/> with its focused pane replaced by <paramref name="change"/> applied to it:
    /// the one-expression form of <c>state.WithPane(state.Focused with { ... })</c>.
    /// </summary>
    public static AppState WithFocused(this AppState state, Func<Pane, Pane> change)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(change);
        return state.WithPane(change(state.Focused));
    }
}
