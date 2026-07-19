using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// Coalescing bookkeeping for <see cref="MainWindow"/>'s preview-load debounce (Phase 5 fix P1):
/// while the cursor is moving fast, <see cref="Effect.LoadPreview"/> arrives far more often than
/// a load is actually worth submitting. <see cref="Hold"/> keeps only the newest one; the caller
/// is expected to (re)start a short timer on every <see cref="Hold"/> and, when it finally fires
/// without being restarted again, call <see cref="Take"/> to submit whatever is still pending.
/// Older <c>LoadPreview</c>s are never sent at all - safe, because <see cref="AppState.Preview"/>'s
/// generation counter already makes a stale result harmless even if it somehow raced through.
/// Not thread-affine by itself; <see cref="MainWindow"/> only ever touches it from the UI thread.
/// </summary>
internal sealed class PendingPreviewGate
{
    private Effect.LoadPreview? pending;

    /// <summary>Replaces whatever was pending (if anything) with <paramref name="effect"/>.</summary>
    public void Hold(Effect.LoadPreview effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        pending = effect;
    }

    /// <summary>Returns and clears the pending effect, or <c>null</c> if nothing is held.</summary>
    public Effect.LoadPreview? Take()
    {
        var effect = pending;
        pending = null;
        return effect;
    }
}
