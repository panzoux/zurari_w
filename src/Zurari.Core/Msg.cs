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
    /// directory or drive, extends the browser one column to the right and requests its listing.
    /// For a file, currently a no-op.
    /// </summary>
    public sealed record EnterDirectory(int ColumnIndex, int EntryIndex) : Msg;

    /// <summary>Move focus from <paramref name="ColumnIndex"/> to the column to its left, Finder style.</summary>
    public sealed record GoToParent(int ColumnIndex) : Msg;

    /// <summary>Re-request the listing of every column, keeping current entries visible while loading.</summary>
    public sealed record Refresh : Msg;

    /// <summary>
    /// A <c>ReadDirectory</c> effect succeeded. Race-safe: ignored unless
    /// <paramref name="ColumnIndex"/> is still in range and that column's path still equals
    /// <paramref name="Path"/> (otherwise it is a stale result from a superseded read).
    /// </summary>
    public sealed record DirectoryLoaded(int ColumnIndex, string Path, ImmutableArray<Entry> Entries) : Msg;

    /// <summary>
    /// A <c>ReadDirectory</c> effect failed. Subject to the same staleness check as
    /// <see cref="DirectoryLoaded"/>.
    /// </summary>
    public sealed record DirectoryLoadFailed(int ColumnIndex, string Path, string Error) : Msg;

    /// <summary>
    /// Delete the entry at <paramref name="EntryIndex"/> in <paramref name="ColumnIndex"/> to the
    /// recycle bin. Drive entries are ignored (there is nothing to delete). Marks the column
    /// <see cref="LoadState.Loading"/> (keeping its entries visible) and emits
    /// <see cref="Effect.DeleteToRecycleBin"/>.
    /// </summary>
    public sealed record DeleteEntry(int ColumnIndex, int EntryIndex) : Msg;

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
    /// path still equals <paramref name="Path"/>). Re-emits <see cref="Effect.ReadDirectory"/> for
    /// the column; it stays <see cref="LoadState.Loading"/> until that completes.
    /// </summary>
    public sealed record ShellOpCompleted(int ColumnIndex, string Path) : Msg;

    /// <summary>
    /// A shell operation (<see cref="Effect.DeleteToRecycleBin"/> or
    /// <see cref="Effect.ShellCopyOrMove"/>) failed. Subject to the same staleness check as
    /// <see cref="ShellOpCompleted"/>.
    /// </summary>
    public sealed record ShellOpFailed(int ColumnIndex, string Path, string Error) : Msg;

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
    /// <paramref name="ColumnIndex"/> to the recycle bin as a single batch. No-op if the column has
    /// no such marked entry (or is out of range) - the App layer is expected to fall back to
    /// <see cref="DeleteEntry"/> for the cursor entry in that case. Marks the column
    /// <see cref="LoadState.Loading"/> (keeping its entries visible) and emits one
    /// <see cref="Effect.DeleteToRecycleBin"/> carrying all marked full paths.
    /// </summary>
    public sealed record DeleteMarked(int ColumnIndex) : Msg;

    /// <summary>
    /// Marks every entry in <paramref name="ColumnIndex"/> between <paramref name="FromIndex"/> and
    /// <paramref name="ToIndex"/> (either order; both clamped into the column's entries range), then
    /// moves the cursor to the clamped <paramref name="ToIndex"/>. When <paramref name="Additive"/>
    /// is <c>false</c>, every entry outside the range is unmarked (replaces the selection); when
    /// <c>true</c>, existing marks outside the range are preserved. Used by rubber-band (rectangle)
    /// multi-select. Out-of-range column or an empty column is a no-op.
    /// </summary>
    public sealed record MarkRange(int ColumnIndex, int FromIndex, int ToIndex, bool Additive) : Msg;
}
