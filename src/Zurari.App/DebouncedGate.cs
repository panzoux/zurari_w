using System.Windows.Threading;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// A <see cref="PendingEffectGate{T}"/> with its own timer: holds the newest effect and submits it
/// once nothing newer has arrived for <c>interval</c>.
/// </summary>
/// <remarks>
/// One per pane and per effect kind, never shared. With a shared timer, steady cursor movement in
/// pane A restarts it on every key, and pane B's held preview or speculative read (from a job
/// reread, a watcher event, F5) would wait for as long as A stayed busy. The timer is created on
/// first use and reused. UI-thread only, like the gate it wraps.
/// </remarks>
/// <typeparam name="T">The kind of effect being coalesced.</typeparam>
internal sealed class DebouncedGate<T>
    where T : Effect
{
    private readonly PendingEffectGate<T> gate = new();
    private readonly TimeSpan interval;
    private readonly Action<T> submit;
    private DispatcherTimer? timer;

    /// <summary>Creates a gate that calls <paramref name="submit"/> with the newest held effect after <paramref name="interval"/> of quiet.</summary>
    public DebouncedGate(TimeSpan interval, Action<T> submit)
    {
        ArgumentNullException.ThrowIfNull(submit);
        this.interval = interval;
        this.submit = submit;
    }

    /// <summary>Holds <paramref name="effect"/> (replacing any pending one) and restarts the quiet period.</summary>
    public void Hold(T effect)
    {
        gate.Hold(effect);

        if (timer is null)
        {
            timer = new DispatcherTimer { Interval = interval };
            timer.Tick += OnTick;
        }

        timer.Stop();
        timer.Start();
    }

    /// <summary>
    /// Stops the timer and forgets what is held, for a pane that no longer exists. Nothing is
    /// submitted: there is nobody left to consume the result.
    /// </summary>
    public void Stop()
    {
        timer?.Stop();
        gate.Take();
    }

    private void OnTick(object? sender, EventArgs e)
    {
        timer?.Stop();
        if (gate.Take() is { } effect)
        {
            submit(effect);
        }
    }
}
