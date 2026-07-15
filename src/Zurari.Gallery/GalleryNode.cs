using Zurari.Controls;

namespace Zurari.Gallery;

/// <summary>
/// One node of the fake in-memory directory tree used by the Gallery harness.
/// Immutable and generated deterministically (see <see cref="GalleryDatasets"/>);
/// there is no file I/O anywhere in this project (enforced by BannedSymbols.txt).
/// </summary>
public sealed record GalleryNode(
    string Name,
    EntryKind Kind,
    IReadOnlyList<GalleryNode> Children,
    long? SizeBytes,
    DateTime? Modified)
{
    /// <summary>Creates a directory node. Directories have no size/date of their own.</summary>
    public static GalleryNode Directory(string name, IReadOnlyList<GalleryNode> children) =>
        new(name, EntryKind.Directory, children, null, null);

    /// <summary>Creates a leaf file node. Files never have children.</summary>
    public static GalleryNode File(string name, long sizeBytes, DateTime modified) =>
        new(name, EntryKind.File, [], sizeBytes, modified);
}
