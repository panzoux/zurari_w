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

    /// <summary>Network locations - shares, whether the user added them or not.</summary>
    public const string Pinned = "pinned";

    /// <summary>The recycle bin. Its own section, one row.</summary>
    public const string Trash = "trash";

    /// <summary>
    /// Which section a place the user added belongs to, decided by the path rather than by how it
    /// was added.
    /// </summary>
    /// <remarks>
    /// A UNC path is a network location wherever the user happened to be standing when they added
    /// it, and a local folder is a favorite. Deciding from the path means one gesture serves both
    /// sections and the headers cannot start lying - which a second "add to favorites" key would
    /// allow, by letting a share be filed under お気に入り.
    /// </remarks>
    public static string ForPath(string path) =>
        path is not null && path.StartsWith(@"\\", StringComparison.Ordinal) ? Pinned : Favorites;
}
