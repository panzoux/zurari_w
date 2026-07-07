namespace Zurari.Core;

/// <summary>
/// The single entry point for state changes. Pure: no I/O, no threads, no
/// clocks — given the same state and message it always returns the same result.
/// </summary>
public static class Transition
{
    public static (AppState State, IReadOnlyList<Effect> Effects) Apply(AppState state, Msg msg)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(msg);

        return msg switch
        {
            Msg.Noop => (state, []),
            _ => (state, []),
        };
    }
}
