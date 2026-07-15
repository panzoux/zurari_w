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
}
