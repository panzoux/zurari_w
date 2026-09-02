using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// Coalescing bookkeeping for the effects a moving cursor emits far faster than they are worth
/// performing: <see cref="Effect.LoadPreview"/> (Phase 5 fix P1) and the speculative
/// <see cref="Effect.ReadDirectory"/> that fills the column beside the cursor.
/// </summary>
/// <remarks>
/// <see cref="Hold"/> keeps only the newest; the caller (re)starts a short timer on every
/// <see cref="Hold"/> and, when it finally fires without being restarted again, calls
/// <see cref="Take"/> to submit whatever is still pending. The ones in between are never sent at
/// all, which is safe for both: a stale preview is discarded by generation, and a stale listing by
/// the column's location (see <c>Transition.DirectoryLoaded</c>). Not thread-affine by itself;
/// <see cref="MainWindow"/> only ever touches it from the UI thread.
/// </remarks>
/// <typeparam name="T">The kind of effect being coalesced.</typeparam>
internal sealed class PendingEffectGate<T>
    where T : Effect
{
    private T? pending;

    /// <summary>Replaces whatever was pending (if anything) with <paramref name="effect"/>.</summary>
    public void Hold(T effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        pending = effect;
    }

    /// <summary>Returns and clears the pending effect, or <c>null</c> if nothing is held.</summary>
    public T? Take()
    {
        var effect = pending;
        pending = null;
        return effect;
    }
}
