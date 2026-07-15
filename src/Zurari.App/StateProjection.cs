using System.Globalization;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// Pure mapping from <see cref="AppState"/> (Core) to the view-model shapes
/// <see cref="Zurari.Controls.ColumnBrowser"/> understands. No I/O, no decisions beyond display
/// formatting — everything here is a straight projection of already-decided state.
/// </summary>
public static class StateProjection
{
    private static readonly string[] SizeUnits = ["KB", "MB", "GB", "TB", "PB"];

    /// <summary>Projects every column of <paramref name="state"/> into a <see cref="Controls.ColumnVm"/>.</summary>
    public static IReadOnlyList<Controls.ColumnVm> Project(AppState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        var result = new Controls.ColumnVm[state.Columns.Length];
        for (var i = 0; i < state.Columns.Length; i++)
        {
            result[i] = ProjectColumn(state.Columns[i], isFocused: i == state.FocusedColumn);
        }

        return result;
    }

    private static Controls.ColumnVm ProjectColumn(Column column, bool isFocused)
    {
        var entries = new Controls.EntryVm[column.Entries.Length];
        for (var i = 0; i < column.Entries.Length; i++)
        {
            entries[i] = ProjectEntry(column.Entries[i]);
        }

        return new Controls.ColumnVm(
            Title: ProjectTitle(column),
            Entries: entries,
            CursorIndex: column.Cursor,
            IsFocused: isFocused);
    }

    private static string ProjectTitle(Column column)
    {
        var baseTitle = column.Path.Length == 0 ? "ドライブ" : LastPathSegment(column.Path);

        return column.Load switch
        {
            LoadState.Error => baseTitle + " (エラー)",
            LoadState.Loading when column.Entries.Length == 0 => baseTitle + " …",
            _ => baseTitle,
        };
    }

    private static string LastPathSegment(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            // A bare drive root like "C:\" trims to "C:".
            return path.TrimEnd('\\', '/');
        }

        var separatorIndex = trimmed.LastIndexOfAny(['\\', '/']);
        return separatorIndex < 0 ? trimmed : trimmed[(separatorIndex + 1)..];
    }

    private static Controls.EntryVm ProjectEntry(Entry entry) =>
        new(
            Name: entry.Name,
            Kind: ProjectKind(entry.Kind),
            IsMarked: entry.IsMarked,
            SizeText: ProjectSize(entry),
            DateText: ProjectDate(entry.Modified));

    private static Controls.EntryKind ProjectKind(EntryKind kind) => kind switch
    {
        EntryKind.Drive => Controls.EntryKind.Drive,
        EntryKind.Directory => Controls.EntryKind.Directory,
        _ => Controls.EntryKind.File,
    };

    private static string? ProjectSize(Entry entry)
    {
        if (entry.Kind != EntryKind.File || entry.SizeBytes < 0)
        {
            return null;
        }

        if (entry.SizeBytes < 1024)
        {
            return entry.SizeBytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        double size = entry.SizeBytes;
        var unitIndex = -1;
        while (size >= 1024 && unitIndex < SizeUnits.Length - 1)
        {
            size /= 1024;
            unitIndex++;
        }

        return size.ToString("F1", CultureInfo.InvariantCulture) + " " + SizeUnits[unitIndex];
    }

    private static string? ProjectDate(DateTime modified) =>
        modified == default ? null : modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
}
