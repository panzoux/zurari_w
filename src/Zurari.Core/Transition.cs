using System.Collections.Immutable;

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
            Msg.DeleteCompleted m => DeleteCompleted(state, m.ColumnIndex, m.Path),
            Msg.DeleteFailed m => (DeleteFailed(state, m.ColumnIndex, m.Path, m.Error), NoEffects),
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

        var cursor = entries.Length == 0 ? -1 : Math.Clamp(column.Cursor, 0, entries.Length - 1);
        var updated = column with
        {
            Entries = entries,
            Cursor = cursor,
            Load = LoadState.Loaded,
            ErrorMessage = null,
        };
        return WithColumn(state, columnIndex, updated);
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
        return (newState, [new Effect.DeleteToRecycleBin(columnIndex, column.Path, targetFullPath)]);
    }

    private static (AppState, IReadOnlyList<Effect>) DeleteCompleted(AppState state, int columnIndex, string path)
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

        return (state, [new Effect.ReadDirectory(columnIndex, path)]);
    }

    private static AppState DeleteFailed(AppState state, int columnIndex, string path, string error)
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
