namespace Zurari.Core;

/// <summary>
/// Section identifiers for the drive pane's <see cref="Entry.Group"/>.
/// </summary>
/// <remarks>
/// Stable keys, not labels: the visible header text is a header row's <see cref="Entry.Name"/>, and
/// changing it must not silently expand every section a user had collapsed. They live in Core rather
/// than in the Runtime that builds the listing because behaviour depends on them - Delete means
/// "unpin" on a pinned row and nothing at all on any other row in that pane.
/// </remarks>
public static class EntryGroups
{
    /// <summary>Known user folders: home, desktop, documents, downloads.</summary>
    public const string Favorites = "favorites";

    /// <summary>The machine's drives.</summary>
    public const string Drives = "drives";

    /// <summary>Network locations the user pinned. The only rows in this pane that can be removed.</summary>
    public const string Pinned = "pinned";
}
