namespace Zurari.Core;

/// <summary>What a listing is ordered by.</summary>
/// <remarks>
/// Only the modes the data already supports. "Created" needs a field <see cref="Entry"/> does not
/// have, "deleted on" exists only inside the recycle bin, and sorting by the shell's type name means
/// asking the shell per extension - each is a separate piece of work rather than a member here that
/// would silently order by nothing.
/// </remarks>
public enum SortMode
{
    /// <summary>By name, comparing runs of digits as numbers - see <see cref="NaturalComparer"/>.</summary>
    Name,

    /// <summary>By extension, then by name within it.</summary>
    Extension,

    /// <summary>By size. Directories have none and are grouped together - see <see cref="SortOrder"/>.</summary>
    Size,

    /// <summary>By last-modified time, then by name for anything written in the same instant.</summary>
    Modified,
}

/// <summary>
/// How a listing is ordered: by what, which way round, and whether folders are kept above files.
/// </summary>
/// <param name="Mode">What to compare.</param>
/// <param name="Descending">Reverses <paramref name="Mode"/> only - see the remarks.</param>
/// <param name="DirectoriesFirst">
/// Whether folders are grouped above files. A separate switch rather than a mode of its own, because
/// it composes with every mode: "biggest first, but folders at the top" is a normal thing to want.
/// </param>
/// <remarks>
/// <para>
/// <paramref name="Descending"/> does not reverse the whole listing. Folders stay above files when
/// <paramref name="DirectoriesFirst"/> is set, and the name used to break ties stays ascending -
/// reversing a tiebreak makes an order that looks arbitrary rather than reversed.
/// </para>
/// <para>
/// One order applies wherever sorting applies at all, rather than one per kind of location. The plan
/// called for per-kind, and that becomes real as soon as a mode exists that only one kind supports -
/// deleted-on for the recycle bin, relevance for search results. None does yet, so a map keyed by
/// location kind would be a lookup that always returns the same answer.
/// </para>
/// </remarks>
public sealed record SortOrder(
    SortMode Mode = SortMode.Name,
    bool Descending = false,
    bool DirectoriesFirst = true)
{
    /// <summary>Name, ascending, folders first - what a file manager does before being asked.</summary>
    public static SortOrder Default { get; } = new();

    /// <summary>
    /// The order after asking for <paramref name="mode"/>: the same mode again means "the other way
    /// round", a different one means that mode, ascending.
    /// </summary>
    /// <remarks>
    /// The convention every column header in every file manager uses. It lives here rather than in
    /// the App so that "what does pressing this twice do" is answered by something with tests.
    /// </remarks>
    public SortOrder Select(SortMode mode) =>
        mode == Mode ? this with { Descending = !Descending } : this with { Mode = mode, Descending = false };
}

/// <summary>
/// Everything that turns what a read returned into what is on screen: what is hidden, and in what
/// order.
/// </summary>
/// <param name="Sort">How the visible rows are ordered.</param>
/// <param name="ShowHidden">
/// Whether entries Windows marks hidden or system are shown. Off by default, as in Explorer.
/// </param>
/// <remarks>
/// One record rather than a parameter each, because every one of these is an input to the same
/// derivation and the list only grows - a filter box and a "folders only" mode are both foreseeable.
/// Passing them individually meant every new one touched every call site.
/// </remarks>
public sealed record ViewOptions(SortOrder Sort, bool ShowHidden = false)
{
    /// <summary>Name order, folders first, hidden files out of sight.</summary>
    public static ViewOptions Default { get; } = new(SortOrder.Default);
}
