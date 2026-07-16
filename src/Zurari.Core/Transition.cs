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
            Msg.EnterDirectory m => EnterDirectory(state, m.ColumnIndex, m.EntryIndex),
            Msg.GoToParent m => (GoToParent(state, m.ColumnIndex), NoEffects),
            Msg.Refresh => Refresh(state),
            Msg.DirectoryLoaded m => (DirectoryLoaded(state, m.ColumnIndex, m.Path, m.Entries), NoEffects),
            Msg.DirectoryLoadFailed m => (DirectoryLoadFailed(state, m.ColumnIndex, m.Path, m.Error), NoEffects),
            Msg.DeleteEntry m => DeleteEntry(state, m.ColumnIndex, m.EntryIndex),
            Msg.DropFiles m => DropFiles(state, m.ColumnIndex, m.TargetEntryIndex, m.Paths, m.ShiftHeld, m.CtrlHeld),
            Msg.ShellOpCompleted m => ShellOpCompleted(state, m.ColumnIndex, m.Path, m.AffectedDirs),
            Msg.ShellOpFailed m => (ShellOpFailed(state, m.ColumnIndex, m.Path, m.Error), NoEffects),
            Msg.ToggleMark m => (ToggleMark(state, m.ColumnIndex, m.EntryIndex), NoEffects),
            Msg.ToggleMarkAtCursor m => (ToggleMarkAtCursor(state, m.ColumnIndex), NoEffects),
            Msg.ClearMarks m => (ClearMarks(state, m.ColumnIndex), NoEffects),
            Msg.DeleteMarked m => DeleteMarked(state, m.ColumnIndex),
            Msg.MarkRange m => (MarkRange(state, m.ColumnIndex, m.FromIndex, m.ToIndex, m.Additive), NoEffects),
            _ => (state, NoEffects),
        };
    }

    private static bool InRange(AppState state, int columnIndex) =>
        columnIndex >= 0 && columnIndex < state.Columns.Length;

    private static AppState WithColumn(AppState state, int columnIndex, Column column) =>
        state with { Columns = state.Columns.SetItem(columnIndex, column) };

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

    private static (AppState, IReadOnlyList<Effect>) EnterDirectory(AppState state, int columnIndex, int entryIndex)
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
        if (entry.Kind == EntryKind.File)
        {
            var fileTruncated = state.Columns.Take(columnIndex + 1).ToImmutableArray();
            fileTruncated = fileTruncated.SetItem(columnIndex, column with { Cursor = entryIndex });
            var fileState = state with { Columns = fileTruncated, FocusedColumn = columnIndex };
            return (fileState, NoEffects);
        }

        var childPath = column.Path.Length == 0
            ? entry.Name
            : System.IO.Path.Combine(column.Path, entry.Name);

        var truncated = state.Columns.Take(columnIndex + 1).ToImmutableArray();
        truncated = truncated.SetItem(columnIndex, column with { Cursor = entryIndex });

        var newColumnIndex = truncated.Length;
        var newColumn = new Column(Path: childPath, Entries: [], Cursor: -1, Load: LoadState.Loading);
        var newColumns = truncated.Add(newColumn);

        var newState = state with { Columns = newColumns, FocusedColumn = newColumnIndex };
        return (newState, [new Effect.ReadDirectory(newColumnIndex, childPath)]);
    }

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
            effects[i] = new Effect.ReadDirectory(i, columns[i].Path);
        }

        return (state with { Columns = newColumns.MoveToImmutable() }, effects);
    }

    private static AppState DirectoryLoaded(AppState state, int columnIndex, string path, ImmutableArray<Entry> entries)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Path != path)
        {
            return state;
        }

        var carried = CarryMarks(column.Entries, entries);
        var cursor = carried.Length == 0 ? -1 : Math.Clamp(column.Cursor, 0, carried.Length - 1);
        var updated = column with
        {
            Entries = carried,
            Cursor = cursor,
            Load = LoadState.Loaded,
            ErrorMessage = null,
        };
        return WithColumn(state, columnIndex, updated);
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

    private static AppState DirectoryLoadFailed(AppState state, int columnIndex, string path, string error)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Path != path)
        {
            return state;
        }

        var updated = column with { Load = LoadState.Error, ErrorMessage = error };
        return WithColumn(state, columnIndex, updated);
    }

    private static (AppState, IReadOnlyList<Effect>) DeleteEntry(AppState state, int columnIndex, int entryIndex)
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
        if (entry.Kind == EntryKind.Drive)
        {
            return (state, NoEffects);
        }

        var targetFullPath = column.Path.Length == 0
            ? entry.Name
            : System.IO.Path.Combine(column.Path, entry.Name);

        var newState = WithColumn(state, columnIndex, column with { Load = LoadState.Loading });
        return (newState, [new Effect.DeleteToRecycleBin(columnIndex, column.Path, [targetFullPath])]);
    }

    private static AppState ToggleMark(AppState state, int columnIndex, int entryIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (entryIndex < 0 || entryIndex >= column.Entries.Length)
        {
            return state;
        }

        var entry = column.Entries[entryIndex];
        var newEntries = column.Entries.SetItem(entryIndex, entry with { IsMarked = !entry.IsMarked });
        return WithColumn(state, columnIndex, column with { Entries = newEntries, Cursor = entryIndex });
    }

    private static AppState ToggleMarkAtCursor(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Cursor < 0 || column.Cursor >= column.Entries.Length)
        {
            return state;
        }

        var entry = column.Entries[column.Cursor];
        var newEntries = column.Entries.SetItem(column.Cursor, entry with { IsMarked = !entry.IsMarked });
        var nextCursor = Math.Clamp(column.Cursor + 1, 0, newEntries.Length - 1);
        return WithColumn(state, columnIndex, column with { Entries = newEntries, Cursor = nextCursor });
    }

    private static AppState ClearMarks(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (!column.Entries.Any(e => e.IsMarked))
        {
            return state;
        }

        var newEntries = column.Entries.Select(e => e.IsMarked ? e with { IsMarked = false } : e).ToImmutableArray();
        return WithColumn(state, columnIndex, column with { Entries = newEntries });
    }

    private static (AppState, IReadOnlyList<Effect>) DeleteMarked(AppState state, int columnIndex)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        var targets = ImmutableArray.CreateBuilder<string>();
        foreach (var entry in column.Entries)
        {
            if (!entry.IsMarked || entry.Kind == EntryKind.Drive)
            {
                continue;
            }

            targets.Add(column.Path.Length == 0 ? entry.Name : System.IO.Path.Combine(column.Path, entry.Name));
        }

        if (targets.Count == 0)
        {
            return (state, NoEffects);
        }

        var newState = WithColumn(state, columnIndex, column with { Load = LoadState.Loading });
        return (newState, [new Effect.DeleteToRecycleBin(columnIndex, column.Path, targets.ToImmutable())]);
    }

    private static AppState MarkRange(AppState state, int columnIndex, int fromIndex, int toIndex, bool additive)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Entries.Length == 0)
        {
            return state;
        }

        var lastIndex = column.Entries.Length - 1;
        var from = Math.Clamp(Math.Min(fromIndex, toIndex), 0, lastIndex);
        var to = Math.Clamp(Math.Max(fromIndex, toIndex), 0, lastIndex);
        var clampedTo = Math.Clamp(toIndex, 0, lastIndex);

        var newEntries = column.Entries.Select((e, i) =>
        {
            var inRange = i >= from && i <= to;
            if (inRange)
            {
                return e.IsMarked ? e : e with { IsMarked = true };
            }

            return additive || !e.IsMarked ? e : e with { IsMarked = false };
        }).ToImmutableArray();

        return WithColumn(state, columnIndex, column with { Entries = newEntries, Cursor = clampedTo });
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
        return (newState, [new Effect.ShellCopyOrMove(columnIndex, column.Path, dest, filtered, isMove)]);
    }

    /// <summary>
    /// Resolves where a drop onto <paramref name="column"/> should land: the child path of
    /// <paramref name="targetEntryIndex"/> when it names a Directory or Drive row, otherwise the
    /// column's own path - unless that is the virtual root's empty path with no container row
    /// targeted, which means nothing (returns null).
    /// </summary>
    private static string? ResolveDropDest(Column column, int targetEntryIndex)
    {
        if (targetEntryIndex >= 0 && targetEntryIndex < column.Entries.Length)
        {
            var entry = column.Entries[targetEntryIndex];
            if (entry.Kind is EntryKind.Directory or EntryKind.Drive)
            {
                return column.Path.Length == 0
                    ? entry.Name
                    : System.IO.Path.Combine(column.Path, entry.Name);
            }
        }

        return column.Path.Length == 0 ? null : column.Path;
    }

    /// <summary>
    /// Drops any source that would be a no-op relative to <paramref name="dest"/>: already located
    /// there (its parent equals <paramref name="dest"/>), the destination itself, or an ancestor of
    /// the destination (which would make the transfer recursive). Comparisons are
    /// case-insensitive and ignore a trailing path separator.
    /// </summary>
    private static ImmutableArray<string> FilterDropSources(string dest, ImmutableArray<string> paths)
    {
        var normalizedDest = NormalizePath(dest);
        var builder = ImmutableArray.CreateBuilder<string>(paths.Length);

        foreach (var source in paths)
        {
            if (string.IsNullOrEmpty(source))
            {
                continue;
            }

            var normalizedSource = NormalizePath(source);

            if (string.Equals(normalizedSource, normalizedDest, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parent = TryGetDirectoryName(source);
            if (parent is not null
                && string.Equals(NormalizePath(parent), normalizedDest, StringComparison.OrdinalIgnoreCase))
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

    private static string NormalizePath(string path) => path.TrimEnd('\\', '/');

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
        AppState state, int columnIndex, string path, ImmutableArray<string> affectedDirs)
    {
        if (!InRange(state, columnIndex))
        {
            return (state, NoEffects);
        }

        var column = state.Columns[columnIndex];
        if (column.Path != path)
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
            if (i != columnIndex && !IsAffectedDirectory(columns[i].Path, affectedDirs))
            {
                continue;
            }

            newColumns = newColumns.SetItem(i, columns[i] with { Load = LoadState.Loading });
            effects.Add(new Effect.ReadDirectory(i, columns[i].Path));
        }

        return (state with { Columns = newColumns }, effects);
    }

    /// <summary>
    /// Whether <paramref name="columnPath"/> matches one of <paramref name="affectedDirs"/>,
    /// case-insensitively and ignoring a trailing path separator (so e.g. <c>C:\</c> and <c>C:</c>
    /// style roots still compare equal).
    /// </summary>
    private static bool IsAffectedDirectory(string columnPath, ImmutableArray<string> affectedDirs)
    {
        if (affectedDirs.IsDefaultOrEmpty)
        {
            return false;
        }

        var normalizedColumn = NormalizePath(columnPath);
        foreach (var dir in affectedDirs)
        {
            if (string.Equals(normalizedColumn, NormalizePath(dir), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static AppState ShellOpFailed(AppState state, int columnIndex, string path, string error)
    {
        if (!InRange(state, columnIndex))
        {
            return state;
        }

        var column = state.Columns[columnIndex];
        if (column.Path != path)
        {
            return state;
        }

        var updated = column with { Load = LoadState.Error, ErrorMessage = error };
        return WithColumn(state, columnIndex, updated);
    }
}
