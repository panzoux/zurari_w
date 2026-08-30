using Zurari.Core;

namespace Zurari.App;

/// <summary>Which executor is responsible for an <see cref="Effect"/>.</summary>
public enum EffectTarget
{
    /// <summary>The worker pool: directory reads, pinned places.</summary>
    Runtime,

    /// <summary>The preview slot, via the App's debounce gate.</summary>
    Preview,

    /// <summary>Shell interop: recycle bin, IFileOperation copy/move.</summary>
    Shell,

    /// <summary>The internal job engine.</summary>
    Jobs,
}

/// <summary>
/// Decides which executor each <see cref="Effect"/> goes to.
/// </summary>
/// <remarks>
/// Extracted from the composition root so it can be tested. It was a <c>switch</c> statement whose
/// missing cases fell through and did nothing: <see cref="Effect.SetPinned"/> and
/// <see cref="Effect.CancelPreview"/> were both silently dropped, so every gesture that added or
/// removed a place did nothing at all, and preview cancellation never reached the runtime. Nothing
/// caught it, because tests submit effects straight to their executor and never come through here.
///
/// Now a switch <em>expression</em> with no discard arm, so an unrouted effect throws instead of
/// vanishing - and <c>EffectRoutingTests</c> walks every <see cref="Effect"/> subtype to make sure
/// none does.
/// </remarks>
public static class EffectRouting
{
    /// <summary>Where <paramref name="effect"/> must be sent.</summary>
    /// <exception cref="NotSupportedException">
    /// The effect has no route. Adding an <see cref="Effect"/> means adding it here - and the
    /// throw is the point: the previous silent fall-through is what let two of them do nothing at
    /// all for several commits.
    /// </exception>
    public static EffectTarget For(Effect effect)
    {
        ArgumentNullException.ThrowIfNull(effect);
        return effect switch
        {
            // Routed by where the read points, not just by the effect. Most listings are directories
            // and belong to the Runtime; the recycle bin is a shell namespace folder with no path to
            // enumerate, so only Shell can read it.
            Effect.ReadDirectory { Location: Location.RecycleBin } => EffectTarget.Shell,
            Effect.ReadDirectory => EffectTarget.Runtime,
            Effect.SetPinned => EffectTarget.Runtime,
            Effect.LoadPreview => EffectTarget.Preview,
            Effect.CancelPreview => EffectTarget.Preview,
            Effect.DeleteToRecycleBin => EffectTarget.Shell,
            Effect.ShellCopyOrMove => EffectTarget.Shell,
            Effect.RunFileJob => EffectTarget.Jobs,
            Effect.CancelJob => EffectTarget.Jobs,
            Effect.ResolveJobConflict => EffectTarget.Jobs,
            _ => throw new NotSupportedException($"No executor is wired for {effect.GetType().Name}."),
        };
    }
}
