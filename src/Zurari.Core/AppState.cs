using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>What an <see cref="Entry"/> represents in a <see cref="Column"/>.</summary>
public enum EntryKind
{
    /// <summary>A drive root, only ever found in the virtual root column (Path == "").</summary>
    Drive,

    /// <summary>A directory that can be entered to extend the browser one column to the right.</summary>
    Directory,

    /// <summary>A regular file.</summary>
    File,
}

/// <summary>Load state of a <see cref="Column"/>'s <see cref="Column.Entries"/>.</summary>
public enum LoadState
{
    /// <summary>A <c>ReadDirectory</c> effect is outstanding for this column.</summary>
    Loading,

    /// <summary>Entries reflect the last successful read.</summary>
    Loaded,

    /// <summary>The last read failed; see <see cref="Column.ErrorMessage"/>.</summary>
    Error,
}

/// <summary>
/// A single item shown in a column. Contains no I/O types (BannedSymbols enforces this) —
/// everything the Runtime learns about the filesystem must be reduced to this shape first.
/// </summary>
/// <param name="Name">Display name (also the path segment used to build a child column's path).</param>
/// <param name="Kind">What kind of item this is.</param>
/// <param name="SizeBytes">Size in bytes, or -1 when unknown/not applicable (directories, drives).</param>
/// <param name="Modified">Last-modified timestamp, or <c>default(DateTime)</c> when unknown.</param>
/// <param name="IsMarked">Whether the user has marked/selected this entry for a bulk operation.</param>
public sealed record Entry(
    string Name,
    EntryKind Kind,
    long SizeBytes = -1,
    DateTime Modified = default,
    bool IsMarked = false);

/// <summary>
/// One column of the miller-columns browser: the directory listing at <see cref="Path"/> plus
/// its cursor/scroll/load state.
/// </summary>
/// <param name="Path">
/// Normalized full path of the directory this column shows. Empty string means the virtual
/// root/drive-list column.
/// </param>
/// <param name="Entries">The items currently known for this column.</param>
/// <param name="Cursor">Index of the highlighted entry in <see cref="Entries"/>, or -1 when empty.</param>
/// <param name="ScrollOffset">Index of the first visible entry, for virtualized rendering.</param>
/// <param name="Load">Whether <see cref="Entries"/> reflects a completed read, is loading, or errored.</param>
/// <param name="ErrorMessage">Set only when <see cref="Load"/> is <see cref="LoadState.Error"/>.</param>
public sealed record Column(
    string Path,
    ImmutableArray<Entry> Entries,
    int Cursor = -1,
    int ScrollOffset = 0,
    LoadState Load = LoadState.Loading,
    string? ErrorMessage = null);

/// <summary>
/// Immutable snapshot of the entire application. The UI is a projection of this
/// value; the only way it changes is <see cref="Transition.Apply"/>.
/// </summary>
public sealed record AppState
{
    /// <summary>The miller columns currently shown, left to right.</summary>
    public required ImmutableArray<Column> Columns { get; init; }

    /// <summary>Index into <see cref="Columns"/> of the column that has keyboard focus.</summary>
    public required int FocusedColumn { get; init; }

    /// <summary>Starting state: a single, still-loading virtual root column (the drive list).</summary>
    public static AppState Initial { get; } = new()
    {
        Columns = [new Column(Path: "", Entries: [])],
        FocusedColumn = 0,
    };

    /// <summary>
    /// Checks the invariants that every reachable <see cref="AppState"/> must satisfy. Returns an
    /// empty list when the state is valid; otherwise one human-readable description per violation.
    /// Intended for tests (property-based tests call this after every transition), not production
    /// hot paths.
    /// </summary>
    public IReadOnlyList<string> CheckInvariants()
    {
        var violations = new List<string>();

        if (Columns.IsDefaultOrEmpty)
        {
            violations.Add("Columns must be non-empty.");
            return violations;
        }

        if (FocusedColumn < 0 || FocusedColumn >= Columns.Length)
        {
            violations.Add($"FocusedColumn {FocusedColumn} is out of range [0, {Columns.Length}).");
        }

        for (var i = 0; i < Columns.Length; i++)
        {
            var column = Columns[i];

            if (column.Entries.IsEmpty)
            {
                if (column.Cursor != -1)
                {
                    violations.Add($"Columns[{i}].Cursor is {column.Cursor} but Entries is empty (expected -1).");
                }
            }
            else if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
            {
                violations.Add(
                    $"Columns[{i}].Cursor {column.Cursor} is out of range [0, {column.Entries.Length}).");
            }

            if (column.Load == LoadState.Error && column.ErrorMessage is null)
            {
                violations.Add($"Columns[{i}].Load is Error but ErrorMessage is null.");
            }

            if (i > 0)
            {
                var parentPath = Columns[i - 1].Path;
                if (parentPath.Length == 0)
                {
                    if (column.Path.Length == 0)
                    {
                        violations.Add($"Columns[{i}].Path must be a drive root, not the virtual root.");
                    }
                }
                else if (!column.Path.StartsWith(parentPath, StringComparison.Ordinal))
                {
                    violations.Add(
                        $"Columns[{i}].Path \"{column.Path}\" is not under Columns[{i - 1}].Path \"{parentPath}\".");
                }
            }
        }

        return violations;
    }
}
