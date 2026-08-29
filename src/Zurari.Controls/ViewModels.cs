using System.Windows.Media;

namespace Zurari.Controls;

/// <summary>Kind of an entry shown in a column. Only affects visuals.</summary>
public enum EntryKind
{
    /// <summary>A drive root (leftmost column in real use).</summary>
    Drive,

    /// <summary>A directory; activating it opens a new column to the right.</summary>
    Directory,

    /// <summary>A regular file.</summary>
    File,

    /// <summary>
    /// A section label in the drive pane. Rendered as a label rather than a row: no icon, no size,
    /// no chevron, and none of the row-state fills.
    /// </summary>
    Header,
}

/// <summary>
/// One entry row. Immutable snapshot: the control never mutates it, the host
/// replaces whole <see cref="ColumnVm"/> lists to change what is displayed.
/// </summary>
public sealed record EntryVm(
    string Name,
    EntryKind Kind,
    bool IsMarked,
    string? SizeText,
    string? DateText,
    ImageSource? Icon = null,
    bool IsCut = false);

/// <summary>
/// One column (the contents of one directory). Immutable snapshot.
/// <see cref="CursorIndex"/> is the highlighted row (-1 for none, e.g. empty dir);
/// <see cref="IsFocused"/> marks the column that receives keyboard input.
/// </summary>
public sealed record ColumnVm(
    string Title,
    IReadOnlyList<EntryVm> Entries,
    int CursorIndex,
    bool IsFocused);
