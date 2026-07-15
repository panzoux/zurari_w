using Zurari.Core;

namespace Zurari.Runtime;

/// <summary>
/// Minimal headless wiring harness: owns the current <see cref="AppState"/>, applies
/// <see cref="Transition.Apply"/> to incoming <see cref="Msg"/>s, and runs the resulting
/// <see cref="Effect"/>s through a caller-supplied delegate (typically
/// <see cref="WorkerRuntime.Submit"/>).
/// </summary>
/// <remarks>
/// Not thread-safe by design: <see cref="Dispatch"/> must always be called from the same
/// thread — the UI thread in the real app. <see cref="WorkerRuntime"/> workers report results
/// by invoking the <c>post</c> delegate from arbitrary worker threads; the owner is responsible
/// for marshalling each posted <see cref="Msg"/> onto the loop's thread before calling
/// <see cref="Dispatch"/> with it (e.g. WPF <c>Dispatcher.Invoke</c> in the real app, or draining
/// a <see cref="System.Collections.Concurrent.ConcurrentQueue{T}"/> on the test thread in
/// headless tests).
/// </remarks>
public sealed class MessageLoop
{
    private readonly Action<Effect> _runEffect;
    private readonly Action<AppState>? _onStateChanged;

    /// <summary>
    /// Creates a loop starting at <paramref name="initial"/>. Effects returned by
    /// <see cref="Transition.Apply"/> are handed to <paramref name="runEffect"/> (composition
    /// with <see cref="WorkerRuntime"/>: pass <c>runtime.Submit</c>). <paramref name="onStateChanged"/>,
    /// if given, is invoked with the new state after every <see cref="Dispatch"/> call.
    /// </summary>
    public MessageLoop(AppState initial, Action<Effect> runEffect, Action<AppState>? onStateChanged = null)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(runEffect);

        State = initial;
        _runEffect = runEffect;
        _onStateChanged = onStateChanged;
    }

    /// <summary>The current application state, as of the last <see cref="Dispatch"/> call.</summary>
    public AppState State { get; private set; }

    /// <summary>
    /// Applies <paramref name="msg"/> to <see cref="State"/> via <see cref="Transition.Apply"/>,
    /// runs every returned effect through the <c>runEffect</c> delegate supplied at construction,
    /// then notifies <c>onStateChanged</c>. Must be called from a single, consistent thread — see
    /// the type-level remarks for how Runtime results are expected to flow back in.
    /// </summary>
    public void Dispatch(Msg msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        var (state, effects) = Transition.Apply(State, msg);
        State = state;
        foreach (var effect in effects)
        {
            _runEffect(effect);
        }

        _onStateChanged?.Invoke(State);
    }
}
