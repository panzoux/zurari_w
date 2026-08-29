namespace Zurari.Runtime;

/// <summary>
/// A place the drive pane lists above the drives: a favorite folder, and later a pinned share.
/// </summary>
/// <param name="Path">Full path. Also the entry's identity, since two places can read alike.</param>
/// <param name="Label">What the user reads, before any disambiguation.</param>
/// <remarks>
/// Exists so the composition root can hand these to <see cref="WorkerRuntime"/> without Runtime
/// depending on <c>Zurari.Shell</c> - which the layering tests forbid, and which resolving a known
/// folder requires.
/// </remarks>
public sealed record RootPlace(string Path, string Label);
