namespace Zurari.Core;

/// <summary>
/// Immutable snapshot of the entire application. The UI is a projection of this
/// value; the only way it changes is <see cref="Transition.Apply"/>.
/// </summary>
public sealed record AppState
{
    public static AppState Initial { get; } = new();
}
