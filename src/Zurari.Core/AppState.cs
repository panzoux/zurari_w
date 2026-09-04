using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>What an <see cref="Entry"/> represents in a <see cref="Column"/>.</summary>
public enum EntryKind
{
    /// <summary>A drive root, only ever found in the virtual root column (<see cref="Location.Drives"/>).</summary>
    Drive,

    /// <summary>A directory that can be entered to extend the browser one column to the right.</summary>
    Directory,

    /// <summary>A regular file.</summary>
    File,

    /// <summary>
    /// A section label in the drive pane - お気に入り, ドライブ, and so on. Not a place: it has no
    /// target, no size and no operations.
    /// </summary>
    /// <remarks>
    /// It is nonetheless a real entry rather than something the view invents, because the cursor
    /// lands on it and <c>Space</c> collapses the section beneath it. <see cref="Column.Cursor"/>
    /// is an index into <see cref="Column.Entries"/>, so anything the cursor can reach has to live
    /// there; a header synthesized during projection would put the control's indices and Core's out
    /// of step. Search skips these - narrowing a list to "ドライブ" means nothing.
    /// </remarks>
    Header,
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
/// <param name="Name">
/// What identifies this entry, and the path segment used to build a child column's path. In a
/// directory listing it is the file's name. In the drive pane it is the full target path, because
/// that pane is the one place two rows can share a display label - two pinned folders both called
/// <c>fol1</c>, from different roots - and the cursor follows entries by <see cref="Name"/>.
/// </param>
/// <param name="DisplayName">
/// What the user reads, when that differs from <see cref="Name"/>: <c>Windows (C:)</c> for a drive,
/// or <c>fol1 (D:\test\fol1)</c> for a favorite whose bare name would be ambiguous. <c>null</c> - the
/// normal case - means show <see cref="Name"/>.
/// </param>
/// <param name="Group">
/// Which section of the drive pane this row belongs to, or <c>null</c> outside that pane. Data
/// rather than presentation: a section-aware operation needs it, and the header rows themselves
/// carry it so a collapse knows what it is hiding.
/// </param>
/// <param name="OriginalPath">
/// Where a deleted item used to live, for rows in the recycle bin. The row is named by where the
/// item is *now* - the shell's parsing name, which is what any operation on it needs - so this is
/// the only place the thing a person recognises survives.
/// </param>
/// <param name="IsRemovable">
/// Whether the user put this row here and can take it away again. True for anything they added to
/// the drive pane, false for what is simply there - a drive, or a known folder like Downloads.
/// Independent of <see cref="Group"/>: an added folder sits in お気に入り alongside the known ones
/// and is still removable, while they are not.
/// </param>
/// <param name="Kind">What kind of item this is.</param>
/// <param name="SizeBytes">Size in bytes, or -1 when unknown/not applicable (directories, drives).</param>
/// <param name="Modified">Last-modified timestamp, or <c>default(DateTime)</c> when unknown.</param>
/// <param name="IsMarked">Whether the user has marked/selected this entry for a bulk operation.</param>
/// <param name="Target">
/// Where opening this row leads, when that is not simply "underneath the column it sits in".
/// <c>null</c> - the overwhelmingly common case - means derive it from the parent via
/// <see cref="Location.Child"/>, which costs nothing per entry. A row that points elsewhere (a
/// favorite, a pinned network share, the recycle bin) sets it explicitly.
/// </param>
public sealed record Entry(
    string Name,
    EntryKind Kind,
    long SizeBytes = -1,
    DateTime Modified = default,
    bool IsMarked = false,
    Location? Target = null,
    string? DisplayName = null,
    string? Group = null,
    bool IsRemovable = false,
    string? OriginalPath = null)
{
    /// <summary>What to render for this entry - <see cref="DisplayName"/> when it has one.</summary>
    public string Label => DisplayName ?? Name;
}

/// <summary>
/// One column of the miller-columns browser: the listing at <see cref="Location"/> plus its
/// cursor/scroll/load state.
/// </summary>
/// <param name="Location">Where this column points - see <see cref="Zurari.Core.Location"/>.</param>
/// <param name="Entries">The items currently known for this column.</param>
/// <param name="Cursor">Index of the highlighted entry in <see cref="Entries"/>, or -1 when empty.</param>
/// <param name="ScrollOffset">Index of the first visible entry, for virtualized rendering.</param>
/// <param name="Load">Whether <see cref="Entries"/> reflects a completed read, is loading, or errored.</param>
/// <param name="ErrorMessage">Set only when <see cref="Load"/> is <see cref="LoadState.Error"/>.</param>
public sealed record Column(
    Location Location,
    ImmutableArray<Entry> Entries,
    int Cursor = -1,
    int ScrollOffset = 0,
    LoadState Load = LoadState.Loading,
    string? ErrorMessage = null)
{
    private readonly ImmutableArray<Entry>? allEntries;

    /// <summary>
    /// Everything the last read returned. <see cref="Entries"/> is derived from this - the subset
    /// currently on screen, in the order it is shown.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two are the same array until something narrows or reorders the view. Keeping the full
    /// list means a view change - collapsing a section, revealing hidden files, changing the sort -
    /// is a pure transformation of data already in hand, rather than a trip back to the disk. That
    /// matters most exactly where a re-read hurts: a network share, an optical drive, a directory
    /// with a hundred thousand files in it.
    /// </para>
    /// <para>
    /// Marks live on the <see cref="Entry"/>, so they are recorded here rather than on the visible
    /// list: a mark must survive its row being hidden and come back when it is shown again, instead
    /// of being quietly dropped. Operations still act on <see cref="Entries"/> alone, so a mark you
    /// cannot see is never acted on.
    /// </para>
    /// <para>
    /// Defaults to <see cref="Entries"/> when never set, so constructing a column from a single list
    /// means "all of it is visible".
    /// </para>
    /// </remarks>
    public ImmutableArray<Entry> AllEntries
    {
        get => allEntries ?? Entries;
        init => allEntries = value;
    }

    /// <summary>
    /// Names of the sections whose rows are hidden. The header itself always stays - it is what you
    /// press to bring the section back.
    /// </summary>
    public ImmutableHashSet<string> CollapsedGroups { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>
    /// Collapses or expands <paramref name="group"/>, re-deriving the visible list and keeping the
    /// cursor on the same entry - which, for the header you just pressed, means it stays put.
    /// </summary>
    public Column ToggleGroup(string group, SortOrder sort)
    {
        // Remove hands back the very same set when the group was not in it, which is precisely
        // "this section was expanded" - so one call answers the question and does the work.
        var afterRemove = CollapsedGroups.Remove(group);
        var collapsed = ReferenceEquals(afterRemove, CollapsedGroups) ? CollapsedGroups.Add(group) : afterRemove;
        return WithView(AllEntries, collapsed, sort);
    }

    /// <summary>Name of the entry under the cursor, or <c>null</c> when there is none.</summary>
    public string? CursorName =>
        Cursor >= 0 && Cursor < Entries.Length ? Entries[Cursor].Name : null;

    /// <summary>
    /// Replaces what the last read returned and re-derives the visible list, keeping the cursor on
    /// the same <em>entry</em> rather than the same index.
    /// </summary>
    /// <remarks>
    /// Following the index means a file appearing or disappearing above the cursor silently moves it
    /// onto a different file - which then becomes what the next Delete or Enter acts on. Following
    /// the name is the same principle the cursor memory uses, and re-deriving will need it anyway:
    /// re-sorting moves every row.
    /// </remarks>
    public Column WithAllEntries(ImmutableArray<Entry> all, SortOrder sort) =>
        WithView(all, CollapsedGroups, sort);

    /// <summary>
    /// Replaces the whole set of collapsed sections and re-derives the visible list. Used to restore
    /// what the user had collapsed when the app last ran; <see cref="ToggleGroup"/> is the
    /// one-section-at-a-time version.
    /// </summary>
    public Column WithCollapsedGroups(ImmutableHashSet<string> collapsedGroups, SortOrder sort) =>
        WithView(AllEntries, collapsedGroups, sort);

    /// <summary>Re-derives the visible list under a different order, keeping the cursor on its entry.</summary>
    public Column WithSort(SortOrder sort) => WithView(AllEntries, CollapsedGroups, sort);

    /// <summary>
    /// The single place that writes <see cref="AllEntries"/>, <see cref="Entries"/> and
    /// <see cref="Cursor"/>, so they cannot drift apart.
    /// </summary>
    /// <remarks>
    /// Both must always be assigned together. <see cref="AllEntries"/> falls back to
    /// <see cref="Entries"/> when its backing field was never set - which is what lets a column be
    /// built from a single list - so a bare <c>with { Entries = … }</c> on such a column silently
    /// redefines what "everything" means, and the full list is lost. Going through here instead
    /// makes that unrepresentable.
    /// </remarks>
    private Column WithView(ImmutableArray<Entry> all, ImmutableHashSet<string> collapsedGroups, SortOrder sort)
    {
        ArgumentNullException.ThrowIfNull(sort);
        var previousName = CursorName;
        var visible = Derive(all, collapsedGroups, sort, IsSortable);
        return this with
        {
            AllEntries = all,
            Entries = visible,
            CollapsedGroups = collapsedGroups,
            Cursor = ResolveCursor(visible, previousName, Cursor),
        };
    }

    /// <summary>
    /// Whether the user's sort order applies to this column at all.
    /// </summary>
    /// <remarks>
    /// The drive pane is curated: sections in a fixed order, each with fixed contents, and header
    /// rows that only mean anything where they are. Sorting it by size would scatter the headers
    /// through the drives. Hidden files will join this as a second filter, not a second exception.
    /// </remarks>
    public bool IsSortable => Location is not Location.Drives;

    /// <summary>
    /// The visible list: everything except the rows of collapsed sections, in the order asked for.
    /// Hidden files will join this.
    /// </summary>
    private static ImmutableArray<Entry> Derive(
        ImmutableArray<Entry> all,
        ImmutableHashSet<string> collapsedGroups,
        SortOrder sort,
        bool sortable)
    {
        if (collapsedGroups.IsEmpty && !sortable)
        {
            // Returning the same instance matters beyond saving a copy: the projection reuses its
            // row array when Entries is reference-equal, which is what keeps cursor movement cheap.
            return all;
        }

        if (collapsedGroups.IsEmpty)
        {
            return Sort(all, sort);
        }

        var builder = ImmutableArray.CreateBuilder<Entry>(all.Length);
        foreach (var entry in all)
        {
            // A header survives its own section being collapsed - otherwise there would be nothing
            // left to press to bring it back.
            if (entry.Kind == EntryKind.Header || entry.Group is not { } group || !collapsedGroups.Contains(group))
            {
                builder.Add(entry);
            }
        }

        if (sortable)
        {
            return Sort(builder.ToImmutable(), sort);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// Orders a listing. Made stable explicitly - see the comment on the index array - so two rows
    /// the mode cannot tell apart keep the order they arrived in, which for a directory listing is
    /// the Runtime's own name order.
    /// </summary>
    /// <remarks>
    /// Only <see cref="SortOrder.Mode"/> reverses. Folders stay above files, and the name that
    /// breaks a tie stays ascending: a reversed tiebreak produces an order that looks arbitrary
    /// rather than reversed, which is worse than either direction.
    /// </remarks>
    private static ImmutableArray<Entry> Sort(ImmutableArray<Entry> entries, SortOrder sort)
    {
        if (entries.Length < 2)
        {
            return entries;
        }

        var direction = sort.Descending ? -1 : 1;

        // Indices, so the original position can break the last tie. Array.Sort is an introsort and
        // is NOT stable: without this, two rows the mode and the name cannot tell apart - two files
        // called the same thing in a set of search results, say - would come out in an order that
        // varied with the size of the list.
        var order = new int[entries.Length];
        for (var i = 0; i < order.Length; i++)
        {
            order[i] = i;
        }

        Array.Sort(order, (left, right) =>
        {
            var a = entries[left];
            var b = entries[right];

            if (sort.DirectoriesFirst)
            {
                var byKind = IsContainer(b).CompareTo(IsContainer(a));
                if (byKind != 0)
                {
                    return byKind;
                }
            }

            var byMode = direction * CompareBy(sort.Mode, a, b);
            if (byMode != 0)
            {
                return byMode;
            }

            var byName = NaturalComparer.Instance.Compare(a.Label, b.Label);
            return byName != 0 ? byName : left.CompareTo(right);
        });

        var sorted = ImmutableArray.CreateBuilder<Entry>(entries.Length);
        foreach (var index in order)
        {
            sorted.Add(entries[index]);
        }

        return sorted.MoveToImmutable();
    }

    private static bool IsContainer(Entry entry) => entry.Kind is EntryKind.Directory or EntryKind.Drive;

    private static int CompareBy(SortMode mode, Entry a, Entry b) => mode switch
    {
        SortMode.Extension => NaturalComparer.CompareExtensions(a.Label, b.Label),
        SortMode.Size => a.SizeBytes.CompareTo(b.SizeBytes),
        SortMode.Modified => a.Modified.CompareTo(b.Modified),
        _ => NaturalComparer.Instance.Compare(a.Label, b.Label),
    };

    private static int ResolveCursor(ImmutableArray<Entry> visible, string? previousName, int previousIndex)
    {
        if (visible.IsEmpty)
        {
            return -1;
        }

        if (previousName is not null)
        {
            for (var i = 0; i < visible.Length; i++)
            {
                if (string.Equals(visible[i].Name, previousName, StringComparison.Ordinal))
                {
                    return i;
                }
            }
        }

        // The entry is gone (deleted, renamed, filtered away). Falling back to the index keeps the
        // cursor where it was on screen, which is what the user is looking at.
        return Math.Clamp(previousIndex, 0, visible.Length - 1);
    }
}

/// <summary>
/// What the row under the cursor is, for the purposes of previewing it - which is not the same
/// question as <see cref="EntryKind"/>.
/// </summary>
/// <remarks>
/// A drive's path is also a perfectly good directory path, so the path alone cannot say whether the
/// answer wanted is "read this file" or "how full is this volume". The row knows; the path does not.
/// </remarks>
public enum PreviewTarget
{
    /// <summary>An ordinary file: read it and show its contents.</summary>
    File,

    /// <summary>A volume - a drive or a network share - shown as how much of it is in use.</summary>
    Volume,

    /// <summary>The recycle bin: how many items it holds and what they occupy.</summary>
    RecycleBin,
}

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

    /// <summary>
    /// How full something is, rather than what is in it - see <see cref="PreviewState.Capacity"/>.
    /// A drive, a network share, or the recycle bin.
    /// </summary>
    Capacity,
}

/// <summary>
/// How much a volume holds and how much of that is in use - the preview for a drive, a network
/// share, or the recycle bin.
/// </summary>
/// <param name="Title">What the thing is called: <c>Windows (C:)</c>, <c>ゴミ箱</c>.</param>
/// <param name="TypeName">A short description of its kind: <c>NTFS · 固定ドライブ</c>, <c>光学ドライブ</c>.</param>
/// <param name="UsedBytes">
/// What is occupied, or <c>null</c> when there is nothing to measure - an empty optical drive knows
/// what it is without knowing how full it is.
/// </param>
/// <param name="TotalBytes">
/// Capacity, or <c>null</c> when the thing has none to speak of. The recycle bin has a size but no
/// size limit, so it gets a figure and no bar.
/// </param>
/// <param name="ItemCount">How many things are in it, when that is a meaningful count - the bin.</param>
/// <param name="Status">
/// Why there are no figures, when there are none: 準備できていません for a drive with no disc in it.
/// <c>null</c> when the numbers speak for themselves.
/// </param>
/// <remarks>
/// A drive that cannot answer is still worth describing. Reporting it as a failed preview said only
/// that something went wrong, when the useful thing to say is what the drive <em>is</em> - an optical
/// drive with nothing in it is not an error, it is an empty drive.
/// </remarks>
public sealed record PreviewCapacity(
    string Title,
    string TypeName,
    long? UsedBytes = null,
    long? TotalBytes = null,
    long? ItemCount = null,
    string? Status = null)
{
    /// <summary>What is left, or <c>null</c> when there is no capacity to subtract from.</summary>
    public long? FreeBytes =>
        TotalBytes is { } total && UsedBytes is { } used ? Math.Max(0, total - used) : null;

    /// <summary>How full, from 0 to 1, or <c>null</c> when there is nothing to be full of.</summary>
    public double? UsedFraction =>
        TotalBytes is { } total && total > 0 && UsedBytes is { } used
            ? Math.Clamp(used / (double)total, 0, 1)
            : null;
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
    /// <summary>
    /// Where the previewed file used to live, before it was deleted - set only for rows in the
    /// recycle bin, <c>null</c> everywhere else.
    /// </summary>
    /// <remarks>
    /// <see cref="Path"/> for such a row is the bin's own storage name
    /// (<c>C:\$Recycle.Bin\S-1-5-...\$R...</c>), which is what has to be read but says nothing about
    /// what the file is. This is the only thing that does, so the pane leads with it.
    /// </remarks>
    public string? OriginalPath { get; init; }

    /// <summary>
    /// How full the thing under the cursor is, when <see cref="Kind"/> is
    /// <see cref="PreviewKind.Capacity"/>. <c>null</c> for every other kind.
    /// </summary>
    public PreviewCapacity? Capacity { get; init; }

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
    /// How listings are ordered. Applies to every column that can be sorted at all - see
    /// <see cref="Column.IsSortable"/>.
    /// </summary>
    public SortOrder Sort { get; init; } = SortOrder.Default;

    /// <summary>
    /// A short message for the status bar - the result of something the user asked for that has no
    /// other visible outcome. <c>null</c> when there is nothing to say.
    /// </summary>
    /// <remarks>
    /// Cleared as soon as the user moves on (see <c>Transition.ReconcilePreview</c>), so it reads as
    /// a reply to what was just done rather than a message that lingers. Eject is what this is for:
    /// a drive that will not eject otherwise does nothing at all, which is indistinguishable from a
    /// key that is not wired up.
    /// </remarks>
    public string? Notice { get; init; }

    /// <summary>
    /// An entry name to put the cursor on as soon as a listing containing it arrives, or
    /// <c>null</c>. Consumed and cleared by the first <see cref="Msg.DirectoryLoaded"/> that has it.
    /// </summary>
    /// <remarks>
    /// Mounting an image is the first use: the new drive does not exist until the listing is re-read,
    /// so "put the cursor on it" cannot be done at the moment the request is made. The same shape is
    /// what "the cursor lands on the folder you just created" will need.
    /// </remarks>
    public string? RevealTarget { get; init; }

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
        Columns = [new Column(Location.Drives.Instance, Entries: [])],
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

            // Entries is a view onto AllEntries: the same objects, none invented, none shown twice.
            //
            // This used to say "subsequence", which additionally required the visible rows to keep
            // the order the read produced. Sorting is precisely the act of not doing that, so the
            // property could not survive 6e.1. What it was actually guarding - a visible row that no
            // read ever produced, or one row appearing twice - is guarded by the subset check, and
            // that part still holds.
            if (!IsSubsetOfAll(column))
            {
                violations.Add($"Columns[{i}].Entries contains rows that are not in AllEntries.");
            }

            // There is deliberately no parent/child relationship checked between adjacent columns.
            // The old invariant required Columns[i].Path to start with Columns[i-1].Path, which was
            // only ever true because child locations were built by concatenating onto the parent.
            // Now a row carries its own Entry.Target, so a column can legitimately point somewhere
            // unrelated to the one on its left - that is exactly what a favorite, a pinned network
            // share, or the recycle bin does. String containment would reject all of them.
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

    /// <summary>
    /// Whether every visible row came from the read, and none appears twice.
    /// </summary>
    /// <remarks>
    /// By instance, not by name: deriving the view carries the same <see cref="Entry"/> objects
    /// across, so reference identity is exact and assumes nothing about names being unique - which
    /// holds for a directory listing but would not for search results gathered from several places.
    /// </remarks>
    private static bool IsSubsetOfAll(Column column)
    {
        var all = column.AllEntries;
        var visible = column.Entries;
        if (visible.Length > all.Length)
        {
            return false;
        }

        // The overwhelmingly common case: nothing narrows or reorders the view, so both are the
        // same array.
        if (all == visible)
        {
            return true;
        }

        var known = new HashSet<Entry>(all.Length, ReferenceEqualityComparer.Instance as IEqualityComparer<Entry>);
        foreach (var entry in all)
        {
            known.Add(entry);
        }

        var seen = new HashSet<Entry>(visible.Length, ReferenceEqualityComparer.Instance as IEqualityComparer<Entry>);
        foreach (var entry in visible)
        {
            if (!known.Contains(entry) || !seen.Add(entry))
            {
                return false;
            }
        }

        return true;
    }
}
