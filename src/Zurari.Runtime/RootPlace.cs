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

/// <summary>
/// The recycle bin row, as the composition root describes it.
/// </summary>
/// <param name="Label">What the row reads, including how much is in the bin.</param>
/// <param name="ItemCount">How many deleted items it holds.</param>
/// <param name="TotalBytes">What those items occupy.</param>
/// <remarks>
/// Supplied rather than computed here for the same reason favorites are: the totals come from
/// <c>SHQueryRecycleBin</c>, and Runtime is not allowed to depend on Shell.
/// </remarks>
public sealed record TrashPlace(string Label, long ItemCount = 0, long TotalBytes = 0);
