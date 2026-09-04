using System.Collections.Immutable;
using System.Linq;

namespace Zurari.Core;

/// <summary>
/// The single entry point for state changes. Pure: no I/O, no threads, no
/// clocks — given the same state and message it always returns the same result.
/// </summary>
public static class Transition
{
    private static readonly Effect[] NoEffects = [];

    public static (AppState State, IReadOnlyList<Effect> Effects) Apply(AppState state, Msg msg)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(msg);

        var (applied, effects) = ApplyCore(state, msg);
        var (withChild, withChildEffects) = ReconcileChildColumn(state, applied, effects);
        return ReconcilePreview(state, withChild, withChildEffects);
    }

    /// <summary>
    /// Keeps the column immediately right of the focused one showing whatever the focused cursor is
    /// pointing at - the Finder/Explorer columns behaviour, where landing on a folder reveals its
    /// contents without entering it. A cursor on anything that is not a container leaves nothing to
    /// its right.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A common post-step of <see cref="Apply"/>, like <see cref="ReconcilePreview"/> and for the
    /// same reason: every message that can move a cursor or change focus has to be followed by it,
    /// and enumerating those in each branch is how one gets missed.
    /// </para>
    /// <para>
    /// <b>Bounded to exactly one column beyond the focus, which is what makes it safe.</b> The child
    /// loads asynchronously, and its <see cref="Msg.DirectoryLoaded"/> runs this again - so if the
    /// rule were "extend wherever a cursor sits on a folder", each load would trigger the next and a
    /// deep chain would unroll itself, forever down a junction that points at its own ancestor.
    /// Reading only the <em>focused</em> column means the grandchild's load extends nothing, because
    /// focus has not moved. Descending stays one keypress per column, exactly as it is today. This is
    /// why the reparse-point visited-set that zurari needs for its own auto-extend is not needed here
    /// (see the plan's deferred list).
    /// </para>
    /// <para>
    /// Does nothing when the column to the right already shows the right place, which is what lets
    /// <see cref="GoToParent"/> keep the chain: moving focus left puts the cursor on the folder the
    /// next column is already showing, so the columns beyond it survive untouched.
    /// </para>
    /// </remarks>
    private static (AppState, IReadOnlyList<Effect>) ReconcileChildColumn(
        AppState oldState, AppState state, IReadOnlyList<Effect> effects)
    {
        var focusedIndex = state.FocusedColumn;
        if (!InRange(state, focusedIndex))
        {
            return (state, effects);
        }

        var wanted = ChildOfCursor(state.Columns[focusedIndex]);

        // Nothing the message did changed where the cursor points, so leave the columns alone. This
        // is what keeps a message that was ignored actually ignored, rather than every Apply
        // rebuilding the pane underneath it.
        if (Equals(wanted, ChildOfCursor(FocusedColumnOrNull(oldState))))
        {
            return (state, effects);
        }

        var childIndex = focusedIndex + 1;
        var existing = childIndex < state.Columns.Length ? state.Columns[childIndex].Location : null;

        if (Equals(wanted, existing))
        {
            return (state, effects);
        }

        var truncated = state.Columns.Take(childIndex).ToImmutableArray();
        if (wanted is null)
        {
            return (state with { Columns = truncated }, effects);
        }

        var withChild = truncated.Add(new Column(wanted, Entries: [], Cursor: -1, Load: LoadState.Loading));
        return (
            state with { Columns = withChild },
            Append(effects, new Effect.ReadDirectory(childIndex, wanted, Speculative: true)));
    }

    /// <summary>
    /// Where the row under <paramref name="column"/>'s cursor leads, or <c>null</c> when it leads
    /// nowhere - an empty column, a file, or a section header.
    /// </summary>
    private static Column? FocusedColumnOrNull(AppState state) =>
        InRange(state, state.FocusedColumn) ? state.Columns[state.FocusedColumn] : null;

    private static Location? ChildOfCursor(Column? column)
    {
        if (column is null || column.Cursor < 0 || column.Cursor >= column.Entries.Length)
        {
            return null;
        }

        var entry = column.Entries[column.Cursor];
        return entry.Kind is EntryKind.File or EntryKind.Header ? null : ChildLocation(column, entry);
    }

    private static (AppState State, IReadOnlyList<Effect> Effects) ApplyCore(AppState state, Msg msg)
    {
        return msg switch
        {
            Msg.Noop => (state, NoEffects),
            Msg.CursorUp m => (MoveCursor(state, m.ColumnIndex, delta: -1), NoEffects),
            Msg.CursorDown m => (MoveCursor(state, m.ColumnIndex, delta: 1), NoEffects),
            Msg.CursorPageUp m => (MoveCursor(state, m.ColumnIndex, delta: -Math.Max(1, m.PageSize)), NoEffects),
            Msg.CursorPageDown m => (MoveCursor(state, m.ColumnIndex, delta: Math.Max(1, m.PageSize)), NoEffects),
            Msg.CursorHome m => (MoveCursorTo(state, m.ColumnIndex, index: 0), NoEffects),
            Msg.CursorEnd m => (MoveCursorTo(state, m.ColumnIndex, index: int.MaxValue), NoEffects),
            Msg.CursorTo m => (MoveCursorTo(state, m.ColumnIndex, m.EntryIndex), NoEffects),
            Msg.FocusColumn m => (FocusColumn(state, m.ColumnIndex), NoEffects),
            Msg.EnterDirectory m => EnterDirectory(state, m.ColumnIndex, m.EntryIndex, m.FocusChild),
            Msg.GoToParent m => (GoToParent(state, m.ColumnIndex), NoEffects),
            Msg.Refresh => Refresh(state),
            Msg.DirectoryLoaded m => (DirectoryLoaded(state, m.ColumnIndex, m.Location, m.Entries), NoEffects),
            Msg.DirectoryLoadFailed m => (DirectoryLoadFailed(state, m.ColumnIndex, m.Location, m.Error), NoEffects),
            Msg.DeleteEntry m => DeleteEntry(state, m.ColumnIndex, m.EntryIndex, m.Permanent),
            Msg.DropFiles m => DropFiles(state, m.ColumnIndex, m.TargetEntryIndex, m.Paths, m.ShiftHeld, m.CtrlHeld),
            Msg.ShellOpCompleted m => ShellOpCompleted(state, m.ColumnIndex, m.ColumnLocation, m.AffectedDirs),
            Msg.ShellOpFailed m => (ShellOpFailed(state, m.ColumnIndex, m.ColumnLocation, m.Error), NoEffects),
            Msg.ToggleMark m => (ToggleMark(state, m.ColumnIndex, m.EntryIndex), NoEffects),
            Msg.ToggleMarkAtCursor m => ToggleMarkAtCursor(state, m.ColumnIndex),
            Msg.ClearMarks m => (ClearMarks(state, m.ColumnIndex), NoEffects),
            Msg.DeleteMarked m => DeleteMarked(state, m.ColumnIndex, m.Permanent),
            Msg.MarkRange m => (MarkRange(state, m.ColumnIndex, m.FromIndex, m.ToIndex, m.Additive), NoEffects),
            Msg.PasteRequested m => PasteRequested(state, m.ColumnIndex, m.Sources, m.IsMove),
            Msg.JobProgress m =>
                (JobProgress(state, m.JobId, m.DoneFiles, m.TotalFiles, m.DoneBytes, m.TotalBytes, m.CurrentFile), NoEffects),
            Msg.JobCompleted m => JobFinished(
                state, m.JobId, JobStatus.Completed, error: null, m.SkippedFiles, m.AffectedDirs),
            Msg.JobFailed m => (JobFailed(state, m.JobId, m.Error), NoEffects),
            Msg.JobCancelled m => JobFinished(
                state, m.JobId, JobStatus.Cancelled, error: null, skippedFiles: null, m.AffectedDirs),
            Msg.JobCancelRequested m => JobCancelRequested(state, m.JobId),
            Msg.JobDismissed m => (JobDismissed(state, m.JobId), NoEffects),
            Msg.PreviewLoaded m =>
                (PreviewLoaded(state, m.Generation, m.Kind, m.Text, m.ImageBytes, m.BinaryLabel, m.Metadata), NoEffects),
            Msg.PreviewFailed m => (PreviewFailed(state, m.Generation, m.Error), NoEffects),
            Msg.PreviewCapacityLoaded m => (PreviewCapacityLoaded(state, m.Generation, m.Capacity), NoEffects),
            Msg.SetCutPending m => (state with { CutPending = m.Paths }, NoEffects),
            Msg.JobConflictsFound m => (JobConflictsFound(state, m.JobId, m.ConflictCount), NoEffects),
            Msg.JobConflictResolved m => JobConflictResolved(state, m.JobId, m.Decision),
            Msg.ExternalDirectoryChanged m => ExternalDirectoryChanged(state, m.Path),
            Msg.PinFocusedLocation m => PinFocusedLocation(state, m.ColumnIndex),
            Msg.PinEntryAtCursor m => PinEntryAtCursor(state, m.ColumnIndex),
            Msg.PlacesChanged => ReloadDrivePanes(state),
            Msg.ImageMounted m => ReloadDrivePanes(state with { RevealTarget = m.DriveRoot }),
            Msg.NoticeRaised m => (state with { Notice = m.Message }, NoEffects),
            Msg.ToggleSection m => ToggleSection(state, m.ColumnIndex, m.EntryIndex),
            Msg.CollapsedGroupsRestored m => (RestoreCollapsedGroups(state, m.ColumnIndex, m.Groups), NoEffects),
            _ => (state, NoEffects),
        };
    }

    private static bool InRange(AppState state, int columnIndex) =>
        columnIndex >= 0 && columnIndex < state.Columns.Length;

    private static AppState WithColumn(AppState state, int columnIndex, Column column) =>
        state with { Columns = state.Columns.SetItem(columnIndex, column) };

    /// <summary>
    /// Where opening <paramref name="entry"/> from <paramref name="column"/> leads: the row's own
    /// <see cref="Entry.Target"/> when it has one (a favorite or pinned share pointing outside the
    /// column), otherwise derived from the column's location.
    /// </summary>
    private static Location ChildLocation(Column column, Entry entry) =>
        entry.Target ?? column.Location.Child(entry.Name);

    /// <summary>
    /// The filesystem path of <paramref name="entry"/> as shown in <paramref name="column"/>, or
    /// <c>null</c> when it has none (a row inside a location that is not backed by the filesystem).
    /// </summary>
    private static string? EntryPath(Column column, Entry entry) =>
        entry.Target is { } target ? target.FilesystemPath : column.Location.ChildPath(entry.Name);

    private static AppState MoveCursor(AppState state, int columnIndex, int delta)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Cursor == -1)
        {
            return state;
        }

        var next = Math.Clamp(column.Cursor + delta, 0, column.Entries.Length - 1);
        return next == column.Cursor ? state : WithColumn(state, columnIndex, column with { Cursor = next });
    }

    private static AppState MoveCursorTo(AppState state, int columnIndex, int index)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Cursor == -1)
        {
            return state;
        }

        var next = Math.Clamp(index, 0, column.Entries.Length - 1);
        return next == column.Cursor ? state : WithColumn(state, columnIndex, column with { Cursor = next });
    }

    private static AppState FocusColumn(AppState state, int columnIndex) =>
        InRange(state, columnIndex) ? state with { FocusedColumn = columnIndex } : state;

    private static (AppState, IReadOnlyList<Effect>) EnterDirectory(
        AppState state, int columnIndex, int entryIndex, bool focusChild)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return (state, NoEffects);
        }

        var entry = column.Entries[entryIndex];
        if (entry.Kind is EntryKind.File or EntryKind.Header)
        {
            var fileTruncated = state.Columns.Take(columnIndex + 1).ToImmutableArray();
            fileTruncated = fileTruncated.SetItem(columnIndex, column with { Cursor = entryIndex });
            var fileState = state with { Columns = fileTruncated, FocusedColumn = columnIndex };

            // A disc image is a container the filesystem cannot open, so opening it means asking the
            // shell to make it one - after which it is an ordinary drive like any other.
            return entry.Kind == EntryKind.File && IsDiscImage(entry.Name) && EntryPath(column, entry) is { } imagePath
                ? (fileState, new Effect[] { new Effect.MountImage(imagePath) })
                : (fileState, NoEffects);
        }

        var childLocation = ChildLocation(column, entry);
        var childIndex = columnIndex + 1;

        // The column beside the cursor is usually already showing this - it opened the moment the
        // cursor landed here. Entering then means moving into it, not throwing away a loaded listing
        // and reading it again, which would flash 読み込み中… over contents already on screen.
        if (childIndex < state.Columns.Length && state.Columns[childIndex].Location == childLocation)
        {
            var reused = state.Columns.SetItem(columnIndex, column with { Cursor = entryIndex });
            return (
                state with { Columns = reused, FocusedColumn = focusChild ? childIndex : columnIndex },
                NoEffects);
        }

        var truncated = state.Columns.Take(columnIndex + 1).ToImmutableArray();
        truncated = truncated.SetItem(columnIndex, column with { Cursor = entryIndex });

        var newColumnIndex = truncated.Length;
        var newColumn = new Column(childLocation, Entries: [], Cursor: -1, Load: LoadState.Loading);
        var newColumns = truncated.Add(newColumn);

        var newState = state with { Columns = newColumns, FocusedColumn = focusChild ? newColumnIndex : columnIndex };
        return (newState, [new Effect.ReadDirectory(newColumnIndex, childLocation)]);
    }

    /// <summary>
    /// Whether <paramref name="name"/> is a disc image the shell can mount without elevation.
    /// </summary>
    /// <remarks>
    /// ISO only. <c>Windows.IsoFile</c> registers the <c>mount</c> verb for it; VHD and VHDX go
    /// through <c>AttachVirtualDisk</c>, which needs an administrator, so offering it here would
    /// produce a prompt or a silent failure rather than a mounted drive.
    /// </remarks>
    private static bool IsDiscImage(string name) =>
        name.EndsWith(".iso", StringComparison.OrdinalIgnoreCase);

    private static AppState GoToParent(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex) || columnIndex == 0)
        {
            return state;
        }

        return state with { FocusedColumn = columnIndex - 1 };
    }

    private static (AppState, IReadOnlyList<Effect>) Refresh(AppState state)
    {
        var columns = state.Columns;
        var effects = new Effect[columns.Length];
        var newColumns = ImmutableArray.CreateBuilder<Column>(columns.Length);
        for (var i = 0; i < columns.Length; i++)
        {
            newColumns.Add(columns[i] with { Load = LoadState.Loading });
            effects[i] = new Effect.ReadDirectory(i, columns[i].Location);
        }

        return (state with { Columns = newColumns.MoveToImmutable() }, effects);
    }

    private static AppState DirectoryLoaded(
        AppState state, int columnIndex, Location location, ImmutableArray<Entry> entries)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Location != location)
        {
            return state;
        }

        // Marks carry from everything the column knew, not just what was on screen, so a refresh
        // does not quietly drop the marks of rows the view happened to be hiding.
        var carried = CarryMarks(column.AllEntries, entries);
        var updated = column.WithAllEntries(carried) with
        {
            Load = LoadState.Loaded,
            ErrorMessage = null,
        };

        // Something was waiting for this listing to exist before it could be pointed at - the drive
        // a disc image was just mounted as. Now that its row is here, put the cursor on it.
        var revealed = state.RevealTarget is { } target ? IndexOfName(updated.Entries, target) : -1;

        var withColumn = WithColumn(state, columnIndex, revealed >= 0 ? updated with { Cursor = revealed } : updated);
        return revealed >= 0
            ? withColumn with { FocusedColumn = columnIndex, RevealTarget = null }
            : withColumn;
    }

    /// <summary>Index of the entry called <paramref name="name"/>, or -1.</summary>
    private static int IndexOfName(ImmutableArray<Entry> entries, string name)
    {
        for (var i = 0; i < entries.Length; i++)
        {
            if (string.Equals(entries[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Carries <see cref="Entry.IsMarked"/> from <paramref name="oldEntries"/> onto
    /// <paramref name="newEntries"/> by exact (ordinal) <see cref="Entry.Name"/> match, so a shell
    /// op's post-completion <see cref="Msg.Refresh"/> does not silently drop the user's marks.
    /// Entries whose name no longer exists simply drop their mark.
    /// </summary>
    private static ImmutableArray<Entry> CarryMarks(ImmutableArray<Entry> oldEntries, ImmutableArray<Entry> newEntries)
    {
        var markedNames = oldEntries.Where(e => e.IsMarked).Select(e => e.Name).ToHashSet(StringComparer.Ordinal);
        if (markedNames.Count == 0)
        {
            return newEntries;
        }

        return newEntries
            .Select(e => markedNames.Contains(e.Name) ? e with { IsMarked = true } : e)
            .ToImmutableArray();
    }

    private static AppState DirectoryLoadFailed(AppState state, int columnIndex, Location location, string error)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Location != location)
        {
            return state;
        }

        var updated = column with { Load = LoadState.Error, ErrorMessage = error };
        return WithColumn(state, columnIndex, updated);
    }

    private static (AppState, IReadOnlyList<Effect>) DeleteEntry(
        AppState state, int columnIndex, int entryIndex, bool permanent)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return (state, NoEffects);
        }

        var entry = column.Entries[entryIndex];

        // In the drive pane Delete removes the row, never what it points at. Only something the user
        // added is theirs to remove; a drive or a known folder is simply there.
        if (!AllowsDeletion(column))
        {
            return entry.IsRemovable
                ? (state, new Effect[] { new Effect.SetPinned(entry.Name, Pin: false) })
                : (state, NoEffects);
        }

        if (entry.Kind is EntryKind.Drive or EntryKind.Header)
        {
            return (state, NoEffects);
        }

        if (EntryPath(column, entry) is not { } targetFullPath)
        {
            return (state, NoEffects);
        }

        var newState = WithColumn(state, columnIndex, column with { Load = LoadState.Loading });
        return (newState, [new Effect.DeleteToRecycleBin(columnIndex, column.Location, [targetFullPath], permanent)]);
    }

    /// <summary>
    /// Applies <paramref name="change"/> to every entry the column knows about, visible or not, and
    /// re-derives the visible list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mark state is recorded on <see cref="Column.AllEntries"/> rather than on the visible list, so
    /// that a mark survives its row being hidden and returns when the row does. Writing it to the
    /// visible list instead would silently drop marks every time the view narrowed. The cost is the
    /// same single array copy the previous <c>SetItem</c> made.
    /// </para>
    /// <para>
    /// Callers identify their target by <em>instance</em>, not by name. Deriving the visible list
    /// carries the same <see cref="Entry"/> objects across, so reference identity is exact and needs
    /// no assumption that names are unique - which holds for a directory listing but would not for a
    /// set of search results gathered from several directories at once.
    /// </para>
    /// </remarks>
    private static Column WithMarkChange(Column column, Func<Entry, Entry> change)
    {
        var updated = column.AllEntries.Select(change).ToImmutableArray();
        return column.WithAllEntries(updated);
    }

    /// <summary>
    /// Whether rows in <paramref name="column"/> can be marked at all.
    /// </summary>
    /// <remarks>
    /// Marks exist to gather a set of files for one operation - copy, move, delete. The drive pane
    /// holds places rather than files, and none of those operations means anything applied to a
    /// drive or a favorite, so there is nothing for a mark there to feed. Refusing it also leaves
    /// Space free to mean the one thing it should mean in that pane: collapse the section.
    /// </remarks>
    private static bool AllowsMarks(Column column) => column.Location is not Location.Drives;

    /// <summary>
    /// Whether rows in <paramref name="column"/> can be deleted.
    /// </summary>
    /// <remarks>
    /// The drive pane holds places, not files. A favorite is a pointer to a folder, and Delete on it
    /// means "stop showing this here" - never "recycle what it points at". Leaving the ordinary path
    /// open was briefly real: favorites arrive as Directory rows, so Delete on ホーム resolved to
    /// the profile path and would have recycled the whole of <c>C:\Users\user</c>.
    /// </remarks>
    private static bool AllowsDeletion(Column column) => column.Location is not Location.Drives;

    /// <summary>
    /// Pins whatever <paramref name="columnIndex"/> is showing. A column with no filesystem path -
    /// the drive pane itself - has nothing to pin.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) PinFocusedLocation(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex) ||
            state.Columns[columnIndex].Location.FilesystemPath is not { } path)
        {
            return (state, NoEffects);
        }

        return (state, [new Effect.SetPinned(path, Pin: true)]);
    }

    /// <summary>
    /// Adds the folder under the cursor to the drive pane. Only a row you could open is a place -
    /// a file is not, and neither is a header.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) PinEntryAtCursor(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
        {
            return (state, NoEffects);
        }

        var entry = column.Entries[column.Cursor];
        if (entry.Kind is not (EntryKind.Directory or EntryKind.Drive) ||
            EntryPath(column, entry) is not { } path)
        {
            return (state, NoEffects);
        }

        return (state, [new Effect.SetPinned(path, Pin: true)]);
    }

    /// <summary>
    /// Collapses or expands the section headed by <paramref name="entryIndex"/>, leaving the cursor
    /// where it was.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) ToggleSection(AppState state, int columnIndex, int entryIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return (state, NoEffects);
        }

        var entry = column.Entries[entryIndex];
        return entry is { Kind: EntryKind.Header, Group: { } group }
            ? Collapse(state, columnIndex, column, group)
            : (state, NoEffects);
    }

    /// <summary>
    /// Applies a section toggle and asks for the result to be remembered, so the pane comes back the
    /// way it was left.
    /// </summary>
    /// <remarks>
    /// Only the drive pane is persisted. It is the one pane whose sections are the same every run;
    /// remembering a collapse in a listing that will not exist next time would be storage that can
    /// never be used.
    /// </remarks>
    private static (AppState, IReadOnlyList<Effect>) Collapse(
        AppState state, int columnIndex, Column column, string group)
    {
        var toggled = column.ToggleGroup(group);
        var next = WithColumn(state, columnIndex, toggled);
        return column.Location is Location.Drives
            ? (next, new Effect[] { new Effect.SetCollapsedGroups([.. toggled.CollapsedGroups]) })
            : (next, NoEffects);
    }

    /// <summary>
    /// Re-collapses the sections the user had collapsed last time, once the drive pane has loaded.
    /// </summary>
    /// <remarks>
    /// Silently ignores a group name that no longer matches any section - a stored setting is a
    /// wish, not a claim about what exists, and <see cref="Column.WithView"/> simply hides nothing
    /// for it. Restoring into a column that is not the drive pane would be meaningless, so it does
    /// not happen.
    /// </remarks>
    private static AppState RestoreCollapsedGroups(
        AppState state, int columnIndex, ImmutableArray<string> groups)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        return column.Location is Location.Drives
            ? WithColumn(state, columnIndex, column.WithCollapsedGroups([.. groups]))
            : state;
    }

    /// <summary>
    /// Re-reads every column showing the drive pane, after the pinned places changed underneath it.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) ReloadDrivePanes(AppState state)
    {
        var columns = state.Columns;
        var newColumns = columns;
        var effects = new List<Effect>();
        for (var i = 0; i < columns.Length; i++)
        {
            if (columns[i].Location is not Location.Drives)
            {
                continue;
            }

            newColumns = newColumns.SetItem(i, columns[i] with { Load = LoadState.Loading });
            effects.Add(new Effect.ReadDirectory(i, columns[i].Location));
        }

        return (state with { Columns = newColumns }, effects);
    }

    private static AppState ToggleMark(AppState state, int columnIndex, int entryIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length || !AllowsMarks(column))
        {
            return state;
        }

        var target = column.Entries[entryIndex];
        if (target.Kind == EntryKind.Header)
        {
            return state;
        }

        var updated = WithMarkChange(
            column, e => ReferenceEquals(e, target) ? e with { IsMarked = !e.IsMarked } : e);
        return WithColumn(state, columnIndex, updated with { Cursor = entryIndex });
    }

    /// <summary>
    /// Space: acts on the row under the cursor, which is a mark almost everywhere and a section
    /// collapse on a header.
    /// </summary>
    /// <remarks>
    /// The branch lives here rather than in the App so that the decision stays with the state that
    /// knows what the row is. The two meanings never compete: a header cannot be marked, and the one
    /// pane that has headers does not have marks either.
    /// </remarks>
    private static (AppState, IReadOnlyList<Effect>) ToggleMarkAtCursor(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
        {
            return (state, NoEffects);
        }

        var target = column.Entries[column.Cursor];
        if (target.Kind == EntryKind.Header)
        {
            return target.Group is { } group
                ? Collapse(state, columnIndex, column, group)
                : (state, NoEffects);
        }

        if (!AllowsMarks(column))
        {
            return (state, NoEffects);
        }

        var updated = WithMarkChange(
            column, e => ReferenceEquals(e, target) ? e with { IsMarked = !e.IsMarked } : e);

        // Advancing past the row you just marked is what makes marking a run of files one keypress
        // each. Skips a header, which is never a mark target.
        var nextCursor = AdvancePastHeaders(updated.Entries, column.Cursor + 1);
        return (WithColumn(state, columnIndex, updated with { Cursor = nextCursor }), NoEffects);
    }

    /// <summary>
    /// First markable row at or after <paramref name="from"/>, clamped into range.
    /// </summary>
    private static int AdvancePastHeaders(ImmutableArray<Entry> entries, int from)
    {
        var index = Math.Clamp(from, 0, entries.Length - 1);
        while (index < entries.Length - 1 && entries[index].Kind == EntryKind.Header)
        {
            index++;
        }

        return index;
    }

    private static AppState ClearMarks(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];

        // Clears hidden marks too: Esc means "nothing is marked", and leaving marks alive on rows
        // the user cannot see would make them reappear the next time the view widened.
        if (!column.AllEntries.Any(e => e.IsMarked))
        {
            return state;
        }

        var updated = WithMarkChange(column, e => e.IsMarked ? e with { IsMarked = false } : e);
        return WithColumn(state, columnIndex, updated);
    }

    private static (AppState, IReadOnlyList<Effect>) DeleteMarked(AppState state, int columnIndex, bool permanent)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (!AllowsDeletion(column))
        {
            return (state, NoEffects);
        }

        var targets = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in column.Entries)
        {
            if (!entry.IsMarked || entry.Kind is EntryKind.Drive or EntryKind.Header)
            {
                continue;
            }

            if (EntryPath(column, entry) is { } path)
            {
                targets.Add(path);
            }
        }

        if (targets.Count == 0)
        {
            return (state, NoEffects);
        }

        var newState = WithColumn(state, columnIndex, column with { Load = LoadState.Loading });
        return (
            newState,
            [new Effect.DeleteToRecycleBin(columnIndex, column.Location, targets.ToImmutable(), permanent)]);
    }

    private static AppState MarkRange(AppState state, int columnIndex, int fromIndex, int toIndex, bool additive)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Entries.Length == 0 || !AllowsMarks(column))
        {
            return state;
        }

        var lastIndex = column.Entries.Length - 1;
        var from = Math.Clamp(Math.Min(fromIndex, toIndex), 0, lastIndex);
        var to = Math.Clamp(Math.Max(fromIndex, toIndex), 0, lastIndex);
        var clampedTo = Math.Clamp(toIndex, 0, lastIndex);

        // The range is expressed in visible rows, so the rows it covers are collected here and
        // applied to every entry below. A non-additive drag clears marks outside the range, hidden
        // ones included - a rubber band means "these and only these".
        var inRange = new HashSet<object>(ReferenceEqualityComparer.Instance);
        for (var i = from; i <= to; i++)
        {
            if (column.Entries[i].Kind != EntryKind.Header)
            {
                inRange.Add(column.Entries[i]);
            }
        }

        var updated = WithMarkChange(column, e =>
        {
            if (inRange.Contains(e))
            {
                return e.IsMarked ? e : e with { IsMarked = true };
            }

            return additive || !e.IsMarked ? e : e with { IsMarked = false };
        });

        return WithColumn(state, columnIndex, updated with { Cursor = clampedTo });
    }

    private static (AppState, IReadOnlyList<Effect>) DropFiles(
        AppState state, int columnIndex, int targetEntryIndex, ImmutableArray<string> paths, bool shiftHeld, bool ctrlHeld)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (paths.IsDefaultOrEmpty)
        {
            return (state, NoEffects);
        }

        // Dropping into the drive pane adds places rather than copying files - there is nowhere in
        // it to copy *to*. Dropping onto a row inside it still means that row, so a folder dragged
        // onto a favorite is copied into it as anywhere else; only the pane itself and its headers
        // mean "put this here".
        if (column.Location is Location.Drives && IsPaneBackgroundOrHeader(column, targetEntryIndex))
        {
            return (state, [.. paths.Select(p => new Effect.SetPinned(p, Pin: true))]);
        }

        var dest = ResolveDropDest(column, targetEntryIndex);
        if (dest is null)
        {
            return (state, NoEffects);
        }

        var filtered = FilterDropSources(dest, paths);
        if (filtered.Length == 0)
        {
            return (state, NoEffects);
        }

        var isMove = shiftHeld || (!ctrlHeld && SameVolume(dest, filtered[0]));

        var newState = WithColumn(state, columnIndex, column with { Load = LoadState.Loading });
        return (newState, [new Effect.ShellCopyOrMove(columnIndex, column.Location, dest, filtered, isMove)]);
    }

    /// <summary>
    /// Resolves where a drop onto <paramref name="column"/> should land: the path of
    /// <paramref name="targetEntryIndex"/> when it names a Directory or Drive row, otherwise the
    /// column's own path - unless the column has no filesystem path of its own (the drive list)
    /// and no container row was targeted, which means nothing (returns null).
    /// </summary>
    /// <summary>
    /// Whether a drop at <paramref name="targetEntryIndex"/> landed on the pane itself rather than
    /// on one of its rows - the background, or a section header, which is a label and not a place.
    /// </summary>
    private static bool IsPaneBackgroundOrHeader(Column column, int targetEntryIndex) =>
        targetEntryIndex < 0
        || targetEntryIndex >= column.Entries.Length
        || column.Entries[targetEntryIndex].Kind == EntryKind.Header;

    private static string? ResolveDropDest(Column column, int targetEntryIndex)
    {
        if (targetEntryIndex >= 0 && targetEntryIndex < column.Entries.Length)
        {
            var entry = column.Entries[targetEntryIndex];
            if (entry.Kind is EntryKind.Directory or EntryKind.Drive)
            {
                return EntryPath(column, entry);
            }
        }

        return column.Location.FilesystemPath;
    }

    /// <summary>
    /// Drops any source that would be a no-op relative to <paramref name="dest"/>: already located
    /// there (its parent equals <paramref name="dest"/>), the destination itself, or an ancestor of
    /// the destination (which would make the transfer recursive). Comparisons are
    /// case-insensitive and ignore a trailing path separator.
    /// </summary>
    private static ImmutableArray<string> FilterDropSources(string dest, ImmutableArray<string> paths)
    {
        var normalizedDest = PathComparisonKey(dest);
        var builder = ImmutableArray.CreateBuilder<string>(paths.Length);

        foreach (var source in paths)
        {
            if (string.IsNullOrEmpty(source))
            {
                continue;
            }

            var normalizedSource = PathComparisonKey(source);

            if (string.Equals(normalizedSource, normalizedDest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = TryGetDirectoryName(source);
            if (parent is not null
                && string.Equals(PathComparisonKey(parent), normalizedDest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (normalizedDest.StartsWith(
                    normalizedSource + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Add(source);
        }

        return builder.ToImmutable();
    }

    /// <summary>
    /// A key for comparing two paths for "same directory", not a path.
    /// </summary>
    /// <remarks>
    /// Trailing separators are dropped so that <c>C:\Users\</c> and <c>C:\Users</c> compare equal.
    /// That also turns a bare drive root <c>C:\</c> into <c>C:</c>, which Windows would read as a
    /// *relative* path (the current directory on drive C) if it were ever used as one - so it must
    /// not be. Every caller compares the result and discards it; nothing builds a path from it.
    /// The name says so, to keep it that way.
    /// </remarks>
    private static string PathComparisonKey(string path) => path.TrimEnd('\\', '/');

    private static string? TryGetDirectoryName(string path)
    {
        try
        {
            return System.IO.Path.GetDirectoryName(path);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Explorer's default transfer kind: move within a volume, copy across volumes.</summary>
    private static bool SameVolume(string dest, string firstSource)
    {
        try
        {
            return string.Equals(
                System.IO.Path.GetPathRoot(dest),
                System.IO.Path.GetPathRoot(firstSource),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static (AppState, IReadOnlyList<Effect>) ShellOpCompleted(
        AppState state, int columnIndex, Location columnLocation, ImmutableArray<string> affectedDirs)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (column.Location != columnLocation)
        {
            return (state, NoEffects);
        }

        // A move/delete changes directories beyond the one the operation completed against
        // (its destination, and - for a move - its sources' parents), and any of those may be
        // visible as another column (e.g. dragging a file out of column 3 into column 2). Only
        // re-read columns actually affected, plus the completing column itself (belt and
        // braces); columns showing an unrelated directory are left alone. Marks survive via the
        // name-match carry-over in DirectoryLoaded.
        var columns = state.Columns;
        var newColumns = columns;
        var effects = new List<Effect>();
        for (var i = 0; i < columns.Length; i++)
        {
            if (i != columnIndex && !IsAffectedDirectory(columns[i].Location, affectedDirs))
            {
                continue;
            }

            newColumns = newColumns.SetItem(i, columns[i] with { Load = LoadState.Loading });
            effects.Add(new Effect.ReadDirectory(i, columns[i].Location));
        }

        return (state with { Columns = newColumns }, effects);
    }

    /// <summary>
    /// Whether <paramref name="columnLocation"/> matches one of <paramref name="affectedDirs"/>,
    /// case-insensitively and ignoring a trailing path separator (so e.g. <c>C:\</c> and <c>C:</c>
    /// style roots still compare equal). A location with no filesystem path of its own - the drive
    /// list - never matches, since these are filesystem directories that changed.
    /// </summary>
    private static bool IsAffectedDirectory(Location columnLocation, ImmutableArray<string> affectedDirs)
    {
        if (affectedDirs.IsDefaultOrEmpty || columnLocation.FilesystemPath is not { } columnPath)
        {
            return false;
        }

        var normalizedColumn = PathComparisonKey(columnPath);
        foreach (var dir in affectedDirs)
        {
            if (string.Equals(normalizedColumn, PathComparisonKey(dir), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static AppState ShellOpFailed(AppState state, int columnIndex, Location columnLocation, string error)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Location != columnLocation)
        {
            return state;
        }

        var updated = column with { Load = LoadState.Error, ErrorMessage = error };
        return WithColumn(state, columnIndex, updated);
    }

    private static (AppState, IReadOnlyList<Effect>) PasteRequested(
        AppState state, int columnIndex, ImmutableArray<string> sources, bool isMove)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (column.Location.FilesystemPath is not { } destDir || sources.IsDefaultOrEmpty)
        {
            return (state, NoEffects);
        }

        var filtered = FilterDropSources(destDir, sources);
        if (filtered.Length == 0)
        {
            return (state, NoEffects);
        }

        var jobId = state.NextJobId;
        var job = new Job(jobId, isMove ? JobKind.Move : JobKind.Copy, filtered, destDir);
        var newState = state with
        {
            Jobs = state.Jobs.Add(job),
            NextJobId = jobId + 1,
            // Any paste - whether or not it consumes the exact paths that were cut - consumes the
            // pending-cut look; keeping it simple rather than trying to diff which paths were pasted.
            CutPending = [],
        };
        return (newState, [new Effect.RunFileJob(jobId, job.Kind, filtered, destDir)]);
    }

    private static int FindJobIndex(ImmutableArray<Job> jobs, int jobId)
    {
        for (var i = 0; i < jobs.Length; i++)
        {
            if (jobs[i].JobId == jobId)
            {
                return i;
            }
        }

        return -1;
    }

    private static AppState JobProgress(
        AppState state, int jobId, int doneFiles, int totalFiles, long doneBytes, long totalBytes, string? currentFile)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return state;
        }

        var updated = state.Jobs[index] with
        {
            Status = JobStatus.Running,
            DoneFiles = doneFiles,
            TotalFiles = totalFiles,
            DoneBytes = doneBytes,
            TotalBytes = totalBytes,
            CurrentFile = currentFile,
        };
        return state with { Jobs = state.Jobs.SetItem(index, updated) };
    }

    private static AppState JobFailed(AppState state, int jobId, string error)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return state;
        }

        var updated = state.Jobs[index] with { Status = JobStatus.Failed, Error = error, CurrentFile = null };
        return state with { Jobs = state.Jobs.SetItem(index, updated) };
    }

    /// <summary>
    /// Shared handling for <see cref="Msg.JobCompleted"/> and <see cref="Msg.JobCancelled"/>: both
    /// finish a job and refresh every column matching <paramref name="affectedDirs"/>, following
    /// the same re-read mechanism as <see cref="ShellOpCompleted"/>. A job that completed cleanly
    /// (<see cref="JobStatus.Completed"/> with no skipped files) carries no information the user
    /// needs to review, so it is removed from <see cref="AppState.Jobs"/> immediately instead of
    /// lingering until <see cref="Msg.JobDismissed"/> - the strip then clears itself. Completed-
    /// with-skips, Failed, and Cancelled jobs stay put (they carry information the user may want to
    /// see) until explicitly dismissed.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) JobFinished(
        AppState state, int jobId, JobStatus status, string? error, int? skippedFiles, ImmutableArray<string> affectedDirs)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return (state, NoEffects);
        }

        var job = state.Jobs[index];
        var effectiveSkippedFiles = skippedFiles ?? job.SkippedFiles;
        var jobs = status == JobStatus.Completed && effectiveSkippedFiles == 0
            ? state.Jobs.RemoveAt(index)
            : state.Jobs.SetItem(index, job with
            {
                Status = status,
                CurrentFile = null,
                DoneFiles = status == JobStatus.Completed ? job.TotalFiles : job.DoneFiles,
                SkippedFiles = effectiveSkippedFiles,
                Error = error,
            });
        var stateWithJob = state with { Jobs = jobs };

        var columns = stateWithJob.Columns;
        var newColumns = columns;
        var effects = new List<Effect>();
        for (var i = 0; i < columns.Length; i++)
        {
            if (!IsAffectedDirectory(columns[i].Location, affectedDirs))
            {
                continue;
            }

            newColumns = newColumns.SetItem(i, columns[i] with { Load = LoadState.Loading });
            effects.Add(new Effect.ReadDirectory(i, columns[i].Location));
        }

        return (stateWithJob with { Columns = newColumns }, effects);
    }

    private static (AppState, IReadOnlyList<Effect>) JobCancelRequested(AppState state, int jobId)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return (state, NoEffects);
        }

        var job = state.Jobs[index];
        if (job.Status is not (JobStatus.Queued or JobStatus.Running))
        {
            return (state, NoEffects);
        }

        return (state, [new Effect.CancelJob(jobId)]);
    }

    private static AppState JobDismissed(AppState state, int jobId)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return state;
        }

        var job = state.Jobs[index];
        if (job.Status is not (JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled))
        {
            return state;
        }

        return state with { Jobs = state.Jobs.RemoveAt(index) };
    }

    private static AppState JobConflictsFound(AppState state, int jobId, int conflictCount)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return state;
        }

        var job = state.Jobs[index];
        if (job.Status is not (JobStatus.Queued or JobStatus.Running))
        {
            return state;
        }

        var updated = job with { Status = JobStatus.WaitingConflict, ConflictCount = conflictCount };
        return state with { Jobs = state.Jobs.SetItem(index, updated) };
    }

    private static (AppState, IReadOnlyList<Effect>) JobConflictResolved(
        AppState state, int jobId, ConflictDecision decision)
    {
        var index = FindJobIndex(state.Jobs, jobId);
        if (index < 0)
        {
            return (state, NoEffects);
        }

        var job = state.Jobs[index];
        if (job.Status != JobStatus.WaitingConflict)
        {
            return (state, NoEffects);
        }

        var updated = job with { Status = JobStatus.Running };
        var newState = state with { Jobs = state.Jobs.SetItem(index, updated) };
        return (newState, [new Effect.ResolveJobConflict(jobId, decision)]);
    }

    /// <summary>
    /// Marks every column whose path matches <paramref name="path"/> - same normalization as
    /// <see cref="IsAffectedDirectory"/> - <see cref="LoadState.Loading"/> and re-requests it. See
    /// <see cref="Msg.ExternalDirectoryChanged"/>'s remarks for why our own jobs/shell ops
    /// harmlessly triggering this too is not special-cased away.
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) ExternalDirectoryChanged(AppState state, string path)
    {
        var columns = state.Columns;
        var newColumns = columns;
        var effects = new List<Effect>();
        for (var i = 0; i < columns.Length; i++)
        {
            if (!IsAffectedDirectory(columns[i].Location, [path]))
            {
                continue;
            }

            newColumns = newColumns.SetItem(i, columns[i] with { Load = LoadState.Loading });
            effects.Add(new Effect.ReadDirectory(i, columns[i].Location));
        }

        return (state with { Columns = newColumns }, effects);
    }

    /// <summary>
    /// Common post-step run after every <see cref="Msg"/> (see <see cref="Apply"/>): compares the
    /// focused column's cursor target (the full path of a File-kind entry, or <c>null</c> for
    /// none/directory/drive/out-of-range) between <paramref name="oldState"/> and
    /// <paramref name="newState"/>. Unchanged - including both sides being <c>null</c> - leaves
    /// <see cref="AppState.Preview"/> untouched, which is what keeps
    /// <see cref="Msg.PreviewLoaded"/>/<see cref="Msg.PreviewFailed"/> (results, not cursor moves)
    /// from re-triggering themselves. A change to a file bumps <see cref="PreviewState.Generation"/>,
    /// sets <see cref="PreviewKind.Loading"/>, and appends <see cref="Effect.LoadPreview"/>; a
    /// change to none/directory/drive bumps the generation and resets to
    /// <see cref="PreviewKind.None"/> with no effect (any in-flight load for the old target is left
    /// to arrive and be discarded by the generation mismatch).
    /// </summary>
    private static (AppState, IReadOnlyList<Effect>) ReconcilePreview(
        AppState oldState, AppState newState, IReadOnlyList<Effect> effects)
    {
        var oldTarget = ResolveCursorFileTarget(oldState).Path;
        var (newTarget, originalPath, targetKind) = ResolveCursorFileTarget(newState);
        if (string.Equals(oldTarget, newTarget, StringComparison.Ordinal))
        {
            return (newState, effects);
        }

        var nextGeneration = newState.Preview.Generation + 1;

        // The cursor moved, so whatever the last notice was replying to is over.
        newState = newState with { Notice = null };

        // The target moved, so whatever was loading for the previous generation is now destined to
        // be discarded on arrival. Say so, rather than letting it run to completion unnoticed.
        var abandoned = oldState.Preview.Kind == PreviewKind.Loading
            ? new Effect.CancelPreview(oldState.Preview.Generation)
            : null;

        if (newTarget is null)
        {
            var cleared = newState with { Preview = PreviewState.Initial with { Generation = nextGeneration } };
            return (cleared, Append(effects, abandoned));
        }

        var loading = newState with
        {
            Preview = new PreviewState(
                nextGeneration, newTarget, PreviewKind.Loading, Text: null, ImageBytes: [], Error: null)
            {
                OriginalPath = originalPath,
            },
        };

        var withPreviewEffect = Append(
            effects, abandoned, new Effect.LoadPreview(nextGeneration, newTarget, targetKind));
        return (loading, withPreviewEffect);
    }

    /// <summary>Returns <paramref name="effects"/> with any non-null <paramref name="extra"/> appended.</summary>
    private static IReadOnlyList<Effect> Append(IReadOnlyList<Effect> effects, params Effect?[] extra)
    {
        if (Array.TrueForAll(extra, e => e is null))
        {
            return effects;
        }

        var combined = new List<Effect>(effects);
        foreach (var effect in extra)
        {
            if (effect is not null)
            {
                combined.Add(effect);
            }
        }

        return combined;
    }

    /// <summary>
    /// The File-kind entry under <paramref name="state"/>'s focused column's cursor: the full path
    /// to read, and <see cref="Entry.OriginalPath"/> when the row carries one. Both <c>null</c> when
    /// the focused column is out of range, empty, its cursor is on a Directory/Drive/Header, or its
    /// cursor is -1.
    /// </summary>
    private static CursorTarget ResolveCursorFileTarget(AppState state)
    {
        if (!InRange(state, state.FocusedColumn))
        {
            return default;
        }

        var column = state.Columns[state.FocusedColumn];
        if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
        {
            return default;
        }

        var entry = column.Entries[column.Cursor];

        if (entry.Target is Location.RecycleBin)
        {
            // The bin has no path. Its own name identifies it well enough for the one question being
            // asked of it, which is how much is in it.
            return new CursorTarget(Location.RecycleBin.ParsingName, null, PreviewTarget.RecycleBin);
        }

        if (entry.Kind == EntryKind.Drive)
        {
            return new CursorTarget(EntryPath(column, entry), null, PreviewTarget.Volume);
        }

        var path = EntryPath(column, entry);

        // A pinned share is a directory like any other to open, but "how full is it" is the useful
        // thing to say about the share itself - the same question a drive row answers.
        if (entry.Kind == EntryKind.Directory && column.Location is Location.Drives && IsShareRoot(path))
        {
            return new CursorTarget(path, null, PreviewTarget.Volume);
        }

        return entry.Kind == EntryKind.File
            ? new CursorTarget(path, entry.OriginalPath, PreviewTarget.File)
            : default;
    }

    /// <summary>
    /// Whether <paramref name="path"/> is a UNC share root - <c>\\server\share</c> and nothing
    /// deeper. A folder inside a share is just a folder; the share itself is a volume.
    /// </summary>
    private static bool IsShareRoot(string? path)
    {
        if (path is null || !path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return false;
        }

        var segments = path.TrimEnd('\\', '/').Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2;
    }

    /// <summary>
    /// What the cursor is pointing at, for the preview: what to load, how to load it, and - for a
    /// deleted file - where it came from.
    /// </summary>
    private readonly record struct CursorTarget(string? Path, string? OriginalPath, PreviewTarget Kind);

    private static AppState PreviewLoaded(
        AppState state,
        int generation,
        PreviewKind kind,
        string? text,
        ImmutableArray<byte> imageBytes,
        string? binaryLabel,
        PreviewMetadata? metadata)
    {
        if (generation != state.Preview.Generation)
        {
            return state;
        }

        // Binary results have no body to show, so the label doubles as PreviewState.Text - see its
        // remarks.
        var displayText = kind == PreviewKind.Binary ? binaryLabel : text;
        return state with
        {
            Preview = state.Preview with
            {
                Kind = kind,
                Text = displayText,
                ImageBytes = imageBytes,
                Error = null,
                Metadata = metadata,
            },
        };
    }

    private static AppState PreviewCapacityLoaded(AppState state, int generation, PreviewCapacity capacity)
    {
        if (generation != state.Preview.Generation)
        {
            return state;
        }

        return state with
        {
            Preview = state.Preview with
            {
                Kind = PreviewKind.Capacity,
                Text = null,
                ImageBytes = [],
                Error = null,
                Metadata = null,
                Capacity = capacity,
            },
        };
    }

    private static AppState PreviewFailed(AppState state, int generation, string error)
    {
        if (generation != state.Preview.Generation)
        {
            return state;
        }

        return state with { Preview = state.Preview with { Kind = PreviewKind.None, Error = error } };
    }
}
