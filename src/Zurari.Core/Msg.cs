using System.Collections.Immutable;

namespace Zurari.Core;

/// <summary>
/// Everything that can happen, expressed as data: user input translated by the
/// App layer, and results/progress/errors coming back from the Runtime workers.
/// </summary>
public abstract record Msg
{
    private Msg()
    {
    }

    /// <summary>Does nothing. Exists so the transition harness is testable from day zero.</summary>
    public sealed record Noop : Msg;

    /// <summary>Move the cursor of <paramref name="ColumnIndex"/> up by one entry.</summary>
    public sealed record CursorUp(int ColumnIndex) : Msg;

    /// <summary>Move the cursor of <paramref name="ColumnIndex"/> down by one entry.</summary>
    public sealed record CursorDown(int ColumnIndex) : Msg;

    /// <summary>Move the cursor of <paramref name="ColumnIndex"/> up by <paramref name="PageSize"/> entries.</summary>
    public sealed record CursorPageUp(int ColumnIndex, int PageSize) : Msg;

    /// <summary>Move the cursor of <paramref name="ColumnIndex"/> down by <paramref name="PageSize"/> entries.</summary>
    public sealed record CursorPageDown(int ColumnIndex, int PageSize) : Msg;

    /// <summary>Move the cursor of <paramref name="ColumnIndex"/> to its first entry.</summary>
    public sealed record CursorHome(int ColumnIndex) : Msg;

    /// <summary>Move the cursor of <paramref name="ColumnIndex"/> to its last entry.</summary>
    public sealed record CursorEnd(int ColumnIndex) : Msg;

    /// <summary>
    /// Move the cursor of <paramref name="ColumnIndex"/> directly to <paramref name="EntryIndex"/>
    /// (clamped into range). Used for pointer clicks, which pick an absolute row rather than a delta.
    /// </summary>
    public sealed record CursorTo(int ColumnIndex, int EntryIndex) : Msg;

    /// <summary>Give keyboard focus to <paramref name="ColumnIndex"/>.</summary>
    public sealed record FocusColumn(int ColumnIndex) : Msg;

    /// <summary>
    /// Activate the entry at <paramref name="EntryIndex"/> in <paramref name="ColumnIndex"/>. For a
    /// directory or drive, extends the browser one column to the right and requests its listing;
    /// <paramref name="FocusChild"/> (default <c>true</c>) decides whether keyboard focus follows
    /// into that new child column (Enter / → / double-click) or stays on <paramref name="ColumnIndex"/>
    /// itself (a plain click on the directory - the child pane still opens, but the strong
    /// cursor-row highlight and title-bar path stay put). For a file, selects it and moves focus to
    /// <paramref name="ColumnIndex"/> regardless of <paramref name="FocusChild"/> - there is no
    /// child column to focus.
    /// </summary>
    public sealed record EnterDirectory(int ColumnIndex, int EntryIndex, bool FocusChild = true) : Msg;

    /// <summary>Move focus from <paramref name="ColumnIndex"/> to the column to its left, Finder style.</summary>
    public sealed record GoToParent(int ColumnIndex) : Msg;

    /// <summary>Re-request the listing of every column, keeping current entries visible while loading.</summary>
    public sealed record Refresh : Msg;

    /// <summary>
    /// A <c>ReadDirectory</c> effect succeeded. Race-safe: ignored unless
    /// <paramref name="ColumnIndex"/> is still in range and that column's location still equals
    /// <paramref name="Location"/> (otherwise it is a stale result from a superseded read).
    /// </summary>
    public sealed record DirectoryLoaded(int ColumnIndex, Location Location, ImmutableArray<Entry> Entries) : Msg;

    /// <summary>
    /// A <c>ReadDirectory</c> effect failed. Subject to the same staleness check as
    /// <see cref="DirectoryLoaded"/>.
    /// </summary>
    public sealed record DirectoryLoadFailed(int ColumnIndex, Location Location, string Error) : Msg;

    /// <summary>
    /// Delete the entry at <paramref name="EntryIndex"/> in <paramref name="ColumnIndex"/> to the
    /// recycle bin, or - when <paramref name="Permanent"/> is <c>true</c> (Shift+Delete) - bypassing
    /// it entirely. Drive entries are ignored (there is nothing to delete). Marks the column
    /// <see cref="LoadState.Loading"/> (keeping its entries visible) and emits
    /// <see cref="Effect.DeleteToRecycleBin"/>.
    /// </summary>
    public sealed record DeleteEntry(int ColumnIndex, int EntryIndex, bool Permanent = false) : Msg;

    /// <summary>
    /// Drop <paramref name="Paths"/> onto <paramref name="ColumnIndex"/>, to be copied or moved
    /// there. When <paramref name="TargetEntryIndex"/> names a Directory or Drive row in that
    /// column, the destination is that entry's child path; otherwise (background drop, or a row
    /// that is not a container) the destination is the column's own path. Ignored if the column is
    /// out of range, the resolved destination is empty (dropping onto the virtual root's
    /// background), or <paramref name="Paths"/> is empty. Sources that would be no-ops relative to
    /// the destination (already there, the destination itself, or an ancestor of the destination)
    /// are silently filtered out; if nothing remains, the drop is a complete no-op (no effect, no
    /// error). <paramref name="ShiftHeld"/> forces a move, <paramref name="CtrlHeld"/> forces a
    /// copy (Shift wins if both are held); otherwise the transfer moves when source and destination
    /// share a volume root and copies otherwise (Explorer's default). Marks the column
    /// <see cref="LoadState.Loading"/> (keeping its entries visible) and emits
    /// <see cref="Effect.ShellCopyOrMove"/>.
    /// </summary>
    public sealed record DropFiles(
        int ColumnIndex, int TargetEntryIndex, ImmutableArray<string> Paths, bool ShiftHeld, bool CtrlHeld) : Msg;

    /// <summary>
    /// A shell operation (<see cref="Effect.DeleteToRecycleBin"/> or
    /// <see cref="Effect.ShellCopyOrMove"/>) succeeded. Subject to the same staleness check as
    /// <see cref="DirectoryLoaded"/> (<paramref name="ColumnIndex"/> in range and that column's
    /// location still equals <paramref name="ColumnLocation"/>). <paramref name="AffectedDirs"/> lists every
    /// directory whose contents actually changed (e.g. a move's destination and, for a move only,
    /// the distinct parents of its sources; a delete's targets' distinct parents) - computed by the
    /// Shell executor, which has the effect at hand. Re-emits <see cref="Effect.ReadDirectory"/> for
    /// <paramref name="ColumnIndex"/> and for every other column whose path matches one of
    /// <paramref name="AffectedDirs"/>; those columns stay <see cref="LoadState.Loading"/> until
    /// their read completes. Columns showing an unrelated directory are left untouched.
    /// </summary>
    public sealed record ShellOpCompleted(
        int ColumnIndex, Location ColumnLocation, ImmutableArray<string> AffectedDirs) : Msg;

    /// <summary>
    /// A shell operation (<see cref="Effect.DeleteToRecycleBin"/> or
    /// <see cref="Effect.ShellCopyOrMove"/>) failed. Subject to the same staleness check as
    /// <see cref="ShellOpCompleted"/>.
    /// </summary>
    public sealed record ShellOpFailed(int ColumnIndex, Location ColumnLocation, string Error) : Msg;

    /// <summary>
    /// Flip the mark of the entry at <paramref name="EntryIndex"/> in <paramref name="ColumnIndex"/>
    /// and move that column's cursor to it. Out-of-range column or entry index is a no-op. Drive
    /// entries can be marked like any other entry (harmless, since <see cref="DeleteMarked"/>
    /// ignores marked drives).
    /// </summary>
    public sealed record ToggleMark(int ColumnIndex, int EntryIndex) : Msg;

    /// <summary>
    /// Flip the mark of the entry currently under <paramref name="ColumnIndex"/>'s cursor, then
    /// advance the cursor by one (clamped to the last entry) - the classic filer Space behavior.
    /// No-op when the column is out of range, empty, or its cursor is -1.
    /// </summary>
    public sealed record ToggleMarkAtCursor(int ColumnIndex) : Msg;

    /// <summary>Unmark every entry in <paramref name="ColumnIndex"/>. Out-of-range column is a no-op.</summary>
    public sealed record ClearMarks(int ColumnIndex) : Msg;

    /// <summary>
    /// Delete every marked Directory/File entry (marked Drives are ignored) in
    /// <paramref name="ColumnIndex"/> to the recycle bin as a single batch, or - when
    /// <paramref name="Permanent"/> is <c>true</c> (Shift+Delete) - bypassing it entirely. No-op if
    /// the column has no such marked entry (or is out of range) - the App layer is expected to fall
    /// back to <see cref="DeleteEntry"/> for the cursor entry in that case. Marks the column
    /// <see cref="LoadState.Loading"/> (keeping its entries visible) and emits one
    /// <see cref="Effect.DeleteToRecycleBin"/> carrying all marked full paths.
    /// </summary>
    public sealed record DeleteMarked(int ColumnIndex, bool Permanent = false) : Msg;

    /// <summary>
    /// Marks every entry in <paramref name="ColumnIndex"/> between <paramref name="FromIndex"/> and
    /// <paramref name="ToIndex"/> (either order; both clamped into the column's entries range), then
    /// moves the cursor to the clamped <paramref name="ToIndex"/>. When <paramref name="Additive"/>
    /// is <c>false</c>, every entry outside the range is unmarked (replaces the selection); when
    /// <c>true</c>, existing marks outside the range are preserved. Used by rubber-band (rectangle)
    /// multi-select. Out-of-range column or an empty column is a no-op.
    /// </summary>
    public sealed record MarkRange(int ColumnIndex, int FromIndex, int ToIndex, bool Additive) : Msg;

    /// <summary>
    /// Paste <paramref name="Sources"/> into <paramref name="ColumnIndex"/>'s directory, copying
    /// when <paramref name="IsMove"/> is <c>false</c> or moving when it is <c>true</c>. Ignored if
    /// the column is out of range, its Path is empty (the virtual root has nothing to paste into),
    /// or <paramref name="Sources"/> is empty. Subject to the same no-op filtering as
    /// <see cref="DropFiles"/> (already-there / dest-itself / dest-inside-source sources are
    /// dropped first; if nothing remains, this is a complete no-op). Otherwise appends a new
    /// <see cref="Job"/> (id <see cref="AppState.NextJobId"/>, status Queued) to
    /// <see cref="AppState.Jobs"/>, increments <see cref="AppState.NextJobId"/>, and emits
    /// <see cref="Effect.RunFileJob"/>.
    /// </summary>
    public sealed record PasteRequested(int ColumnIndex, ImmutableArray<string> Sources, bool IsMove) : Msg;

    /// <summary>
    /// Progress report for the job engine's <paramref name="JobId"/>: marks it Running and updates
    /// its counters/current file. Ignored (no-op) if no job with that id is tracked in
    /// <see cref="AppState.Jobs"/> - it either finished already or belongs to a state this Msg is
    /// stale against.
    /// </summary>
    public sealed record JobProgress(
        int JobId, int DoneFiles, int TotalFiles, long DoneBytes, long TotalBytes, string? CurrentFile) : Msg;

    /// <summary>
    /// <paramref name="JobId"/> finished successfully. Marks it Completed (DoneFiles = TotalFiles,
    /// CurrentFile cleared, <see cref="Job.SkippedFiles"/> set to <paramref name="SkippedFiles"/>)
    /// and re-requests every column whose Path matches one of <paramref name="AffectedDirs"/> -
    /// same mechanism as <see cref="ShellOpCompleted"/> (marks the column Loading and emits
    /// <see cref="Effect.ReadDirectory"/>). Ignored if no job with that id is tracked.
    /// </summary>
    public sealed record JobCompleted(int JobId, int SkippedFiles, ImmutableArray<string> AffectedDirs) : Msg;

    /// <summary>
    /// <paramref name="JobId"/> failed unexpectedly. Marks it Failed with <paramref name="Error"/>.
    /// Ignored if no job with that id is tracked.
    /// </summary>
    public sealed record JobFailed(int JobId, string Error) : Msg;

    /// <summary>
    /// <paramref name="JobId"/> was cancelled (in response to <see cref="JobCancelRequested"/>).
    /// Marks it Cancelled and, since partial work may have happened, refreshes columns exactly like
    /// <see cref="JobCompleted"/> does via <paramref name="AffectedDirs"/>. Ignored if no job with
    /// that id is tracked.
    /// </summary>
    public sealed record JobCancelled(int JobId, ImmutableArray<string> AffectedDirs) : Msg;

    /// <summary>
    /// Requests cancellation of <paramref name="JobId"/>. Emits <see cref="Effect.CancelJob"/> when
    /// a job with that id is tracked and still Queued or Running; state is otherwise unchanged
    /// (the engine confirms completion of the cancellation via <see cref="JobCancelled"/>). No-op
    /// if the job is unknown or already finished (Completed/Failed/Cancelled).
    /// </summary>
    public sealed record JobCancelRequested(int JobId) : Msg;

    /// <summary>
    /// Removes <paramref name="JobId"/> from <see cref="AppState.Jobs"/>, but only when it has
    /// finished (Completed, Failed, or Cancelled) - used to dismiss a job's row from the UI. No-op
    /// if the job is unknown or still Queued/Running.
    /// </summary>
    public sealed record JobDismissed(int JobId) : Msg;

    /// <summary>
    /// An <see cref="Effect.LoadPreview"/> succeeded. Ignored unless <paramref name="Generation"/>
    /// still matches <see cref="AppState.Preview"/>'s current generation (otherwise this is a
    /// stale result from a target the cursor has since moved past). <paramref name="BinaryLabel"/>
    /// is only meaningful when <paramref name="Kind"/> is <see cref="PreviewKind.Binary"/> - see
    /// <see cref="PreviewState.Text"/>'s remarks for why it lands in that same field rather than a
    /// dedicated one. <paramref name="ImageBytes"/> is likewise reused for <see cref="PreviewKind.Binary"/>:
    /// rather than add a dedicated field, it carries the file's head (up to a few KB - the Runtime's
    /// job to cap) so the App layer's hex-dump view (<see cref="HexDump.Format"/>) has bytes to
    /// format; for <see cref="PreviewKind.Image"/> it is the whole file, and for every other kind
    /// it is empty. <paramref name="Metadata"/> carries Finder-style inspector data (size/dates,
    /// plus pixel dimensions/depth for images) gathered alongside the load - present for every
    /// kind, not just images; see <see cref="PreviewState.Metadata"/>.
    /// </summary>
    public sealed record PreviewLoaded(
        int Generation,
        PreviewKind Kind,
        string? Text,
        ImmutableArray<byte> ImageBytes,
        string? BinaryLabel,
        PreviewMetadata? Metadata = null) : Msg;

    /// <summary>
    /// An <see cref="Effect.LoadPreview"/> failed. Subject to the same staleness check as
    /// <see cref="PreviewLoaded"/>.
    /// </summary>
    public sealed record PreviewFailed(int Generation, string Error) : Msg;

    /// <summary>
    /// An <see cref="Effect.LoadPreview"/> for a volume or the recycle bin succeeded. Subject to the
    /// same staleness check as <see cref="PreviewLoaded"/>.
    /// </summary>
    /// <remarks>
    /// Separate from <see cref="PreviewLoaded"/> rather than another nullable field on it: that
    /// record already carries three fields that only apply to some kinds, and a capacity shares none
    /// of them - no bytes, no text, no file metadata.
    /// </remarks>
    public sealed record PreviewCapacityLoaded(int Generation, PreviewCapacity Capacity) : Msg;

    /// <summary>
    /// Replaces <see cref="AppState.CutPending"/> with <paramref name="Paths"/> (an empty array
    /// clears it) - the Explorer-style "dim the cut rows" feedback for Ctrl+X. Dispatched by the App
    /// layer alongside its own clipboard write; Ctrl+C dispatches this with an empty array (copying
    /// clears any pending-cut look). Also cleared automatically by <see cref="PasteRequested"/>.
    /// </summary>
    public sealed record SetCutPending(ImmutableArray<string> Paths) : Msg;

    /// <summary>
    /// The job engine's pre-scan for <paramref name="JobId"/> found <paramref name="ConflictCount"/>
    /// destination entries with the same name as a source. A job tracked as
    /// <see cref="JobStatus.Queued"/> or <see cref="JobStatus.Running"/> moves to
    /// <see cref="JobStatus.WaitingConflict"/> with <see cref="Job.ConflictCount"/> set; the engine
    /// is blocked waiting for <see cref="Msg.JobConflictResolved"/>. Ignored if the job is unknown
    /// or already in some other state.
    /// </summary>
    public sealed record JobConflictsFound(int JobId, int ConflictCount) : Msg;

    /// <summary>
    /// The user resolved the conflict prompt for <paramref name="JobId"/> (see
    /// <see cref="JobConflictsFound"/>) with <paramref name="Decision"/>. A job tracked as
    /// <see cref="JobStatus.WaitingConflict"/> moves back to <see cref="JobStatus.Running"/>
    /// immediately (the engine's next progress report will confirm it, but flipping the status here
    /// keeps the strip from looking stuck) and emits <see cref="Effect.ResolveJobConflict"/> to wake
    /// the blocked worker. Ignored if the job is unknown or not currently
    /// <see cref="JobStatus.WaitingConflict"/>.
    /// </summary>
    public sealed record JobConflictResolved(int JobId, ConflictDecision Decision) : Msg;

    /// <summary>
    /// A <see cref="Zurari.Runtime.DirectoryWatcher"/> observed a filesystem change under
    /// <paramref name="Path"/> made outside zurari (e.g. a file pasted via Explorer). Marks
    /// <see cref="LoadState.Loading"/> and emits <see cref="Effect.ReadDirectory"/> for every column
    /// whose path matches <paramref name="Path"/> (same normalization as
    /// <see cref="ShellOpCompleted"/>'s <c>AffectedDirs</c> matching); no matching column is a no-op.
    /// Note: our own jobs and shell operations also trigger watcher events for directories they just
    /// touched - harmless, since re-reading an already-fresh column is a no-op in effect, so this is
    /// not special-cased away.
    /// </summary>
    public sealed record ExternalDirectoryChanged(string Path) : Msg;

    /// <summary>
    /// Pin the location <paramref name="ColumnIndex"/> is showing, so it appears in the drive pane's
    /// network section. Ignored for a column with no filesystem path of its own - the drive pane
    /// cannot pin itself.
    /// </summary>
    public sealed record PinFocusedLocation(int ColumnIndex) : Msg;

    /// <summary>
    /// Add the folder under <paramref name="ColumnIndex"/>'s cursor to the drive pane, without
    /// having to go into it first. Ignored unless the cursor is on something you can open.
    /// </summary>
    /// <remarks>
    /// The gesture that matters in practice. Reaching the drive pane by dragging is not an option
    /// once a few columns are open - it is off-screen to the left, and there is no drag-scroll -
    /// so adding a place has to work from the keyboard, pointing at a folder rather than standing
    /// in it.
    /// </remarks>
    public sealed record PinEntryAtCursor(int ColumnIndex) : Msg;

    /// <summary>
    /// The pinned places changed, so any column showing the drive pane is stale. Re-reads every
    /// <see cref="Zurari.Core.Location.Drives"/> column; no such column is a no-op.
    /// </summary>
    public sealed record PlacesChanged : Msg;

    /// <summary>
    /// Collapse or expand the section headed by <paramref name="EntryIndex"/>, from the mouse rather
    /// than the cursor. Ignored unless that row is a header with a section to toggle.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="ToggleMarkAtCursor"/>, which acts wherever the cursor happens to be:
    /// clicking one section's toggle must not move the cursor out of another.
    /// </remarks>
    public sealed record ToggleSection(int ColumnIndex, int EntryIndex) : Msg;

    /// <summary>
    /// The sections that were collapsed when the app last ran, applied to a drive pane that has just
    /// finished loading. Ignored for any other column.
    /// </summary>
    /// <remarks>
    /// A separate message rather than a field on <see cref="DirectoryLoaded"/>: what is in a listing
    /// and how much of it is showing are different questions, and only the one pane that has sections
    /// has an answer to the second.
    /// </remarks>
    public sealed record CollapsedGroupsRestored(int ColumnIndex, ImmutableArray<string> Groups) : Msg;

    /// <summary>
    /// An <see cref="Effect.MountImage"/> succeeded and <paramref name="DriveRoot"/> appeared.
    /// Re-reads the drive pane and puts the cursor on the new drive once its row exists.
    /// </summary>
    public sealed record ImageMounted(string DriveRoot) : Msg;

    /// <summary>
    /// Something to tell the user in the status bar - see <see cref="AppState.Notice"/>. Replaces
    /// whatever was there.
    /// </summary>
    public sealed record NoticeRaised(string Message) : Msg;
}
