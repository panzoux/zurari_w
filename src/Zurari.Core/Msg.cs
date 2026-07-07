namespace Zurari.Core;

/// <summary>
/// Everything that can happen, expressed as data: user input translated by the
/// App layer, and results/progress/errors coming back from the Runtime workers.
/// </summary>
public abstract record Msg
{
    private Msg()
    {
    }

    /// <summary>Does nothing. Exists so the transition harness is testable from day zero.</summary>
    public sealed record Noop : Msg;
}
