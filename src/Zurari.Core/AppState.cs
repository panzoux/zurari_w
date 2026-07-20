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

/// <summary>What the preview pane is currently showing.</summary>
public enum PreviewKind
{
    /// <summary>No file is under the focused column's cursor - the pane is empty.</summary>
    None,

    /// <summary>An <see cref="Effect.LoadPreview"/> is outstanding for the current file.</summary>
    Loading,

    /// <summary><see cref="PreviewState.ImageBytes"/> holds the whole file, ready to decode.</summary>
    Image,

    /// <summary><see cref="PreviewState.Text"/> holds the decoded head of the file.</summary>
    Text,

    /// <summary>
    /// Not renderable (or too big, for images). <see cref="PreviewState.Text"/> holds the
    /// <see cref="FileTypeDetector"/> label instead of file content in this case - see the type's
    /// remarks.
    /// </summary>
    Binary,
}

/// <summary>
/// Live state of the preview pane: a projection of whatever file sits under the focused column's
/// cursor. Reconciled after every <see cref="Msg"/> as a common post-step of
/// <see cref="Transition.Apply"/> (see <c>Transition.ReconcilePreview</c>) rather than per-branch,
/// since any cursor/focus-affecting message can change the target.
/// </summary>
/// <param name="Generation">
/// Bumped every time the target file changes. <see cref="Msg.PreviewLoaded"/>/
/// <see cref="Msg.PreviewFailed"/> carry the generation they were requested for and are ignored
/// unless it still matches - this is what makes fast cursor movement discard stale results instead
/// of flickering.
/// </param>
/// <param name="Path">Full path of the file this state describes, or <c>null</c> when <see cref="Kind"/> is <see cref="PreviewKind.None"/>.</param>
/// <param name="Kind">What is currently displayed.</param>
/// <param name="Text">
/// Decoded text content when <see cref="Kind"/> is <see cref="PreviewKind.Text"/>. Doubles as the
/// <see cref="FileTypeDetector"/> display label when <see cref="Kind"/> is
/// <see cref="PreviewKind.Binary"/> (chosen over a separate label field to keep this record small -
/// the two kinds never need both a body and a label at once). <c>null</c> otherwise.
/// </param>
/// <param name="ImageBytes">
/// The whole file's bytes when <see cref="Kind"/> is <see cref="PreviewKind.Image"/>; a capped
/// head of the file (for the hex-dump view, see <see cref="HexDump.Format"/>) when <see cref="Kind"/>
/// is <see cref="PreviewKind.Binary"/> - see <see cref="Msg.PreviewLoaded"/>'s remarks; empty
/// otherwise.
/// </param>
/// <param name="Error">Set only when a <see cref="Msg.PreviewFailed"/> was the most recent result for the current generation.</param>
/// <param name="Metadata">
/// Finder-style file metadata (size/dates, plus pixel dimensions and bit depth for images) from
/// the most recent <see cref="Msg.PreviewLoaded"/> for this generation, or <c>null</c> before any
/// result has arrived (or after a <see cref="Msg.PreviewFailed"/>, which does not carry one).
/// </param>
public sealed record PreviewState(
    int Generation,
    string? Path,
    PreviewKind Kind,
    string? Text,
    ImmutableArray<byte> ImageBytes,
    string? Error,
    PreviewMetadata? Metadata = null)
{
    /// <summary>Starting state: no file selected, generation 0.</summary>
    public static PreviewState Initial { get; } = new(
        Generation: 0, Path: null, Kind: PreviewKind.None, Text: null, ImageBytes: [], Error: null, Metadata: null);
}

/// <summary>
/// Finder-style inspector metadata for the file the preview pane currently shows - see
/// <see cref="PreviewState.Metadata"/> and <see cref="Msg.PreviewLoaded"/>. Gathered by
/// <c>Zurari.Runtime.WorkerRuntime.ExecuteLoadPreview</c> (I/O); <see cref="PixelWidth"/>,
/// <see cref="PixelHeight"/> and <see cref="BitsPerPixel"/> are only set for image kinds (parsed
/// by <see cref="ImageHeaderParser"/>) and stay <c>null</c> otherwise.
/// </summary>
/// <param name="FileName">The file's own name (no directory), e.g. <c>"photo.png"</c>.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="Created">File creation time.</param>
/// <param name="Modified">File last-write time.</param>
/// <param name="PixelWidth">Image width in pixels, when known.</param>
/// <param name="PixelHeight">Image height in pixels, when known.</param>
/// <param name="BitsPerPixel">Image bit depth (bits per pixel), when known.</param>
public sealed record PreviewMetadata(
    string FileName,
    long SizeBytes,
    DateTime Created,
    DateTime Modified,
    int? PixelWidth = null,
    int? PixelHeight = null,
    int? BitsPerPixel = null);

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

    /// <summary>Background copy/move jobs, in submission order.</summary>
    public ImmutableArray<Job> Jobs { get; init; } = [];

    /// <summary>The <see cref="Job.JobId"/> to assign to the next job created by <c>PasteRequested</c>.</summary>
    public int NextJobId { get; init; } = 1;

    /// <summary>What the preview pane currently shows - see <see cref="PreviewState"/>.</summary>
    public PreviewState Preview { get; init; } = PreviewState.Initial;

    /// <summary>
    /// Full paths of entries the user has cut (Ctrl+X) but not yet pasted - Explorer-style "dim the
    /// cut rows" feedback. Set wholesale by <see cref="Msg.SetCutPending"/> (replacing whatever was
    /// there); cleared automatically by <see cref="Msg.PasteRequested"/>, since any paste consumes
    /// the pending-cut look regardless of what was pasted. Empty by default.
    /// </summary>
    public ImmutableArray<string> CutPending { get; init; } = [];

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

        if (!Jobs.IsDefaultOrEmpty)
        {
            var seenJobIds = new HashSet<int>();
            foreach (var job in Jobs)
            {
                if (!seenJobIds.Add(job.JobId))
                {
                    violations.Add($"Jobs contains duplicate JobId {job.JobId}.");
                }
            }
        }

        return violations;
    }
}
