using System.Collections.Immutable;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// Remembers the <see cref="Controls.EntryVm"/> array projected for each column, so a render that
/// did not change a column's contents can hand back the very same array instance.
/// </summary>
/// <remarks>
/// <para>
/// This exists because of how the row list is bound. <c>Generic.xaml</c> binds the ListBox's
/// <c>ItemsSource</c> to <c>Column.Entries</c>, and WPF compares that by reference: give it a new
/// array and it tears down every realized container, re-runs virtualization and re-measures. Give it
/// the array it already has and the assignment is a no-op.
/// </para>
/// <para>
/// A cursor move changes one integer and nothing else, yet it used to rebuild every row in the
/// column - one <see cref="Controls.EntryVm"/> and one icon lookup per entry - purely to produce an
/// array WPF would then treat as brand new. Measured at 0.89 ms for 1,000 entries, 17 ms for 10,000
/// and 84 ms for 50,000, per keystroke, before WPF did any work of its own.
/// </para>
/// <para>
/// Reuse is keyed on everything an <see cref="Controls.EntryVm"/> is built from: the
/// <see cref="Column.Entries"/> instance (which covers names, kinds, sizes, dates and marks, since
/// any change to those produces a new array), the column's <see cref="Column.Location"/> and the
/// cut-pending set (both feed the per-row full path behind <c>IsCut</c>). The comparison is by
/// reference, so it is conservative: an equal-but-rebuilt list simply misses and re-projects.
/// </para>
/// </remarks>
public sealed class ProjectionCache
{
    private Entry[] memo = [];

    private readonly record struct Entry(
        ImmutableArray<Core.Entry> Entries,
        Location Location,
        ImmutableArray<string> CutPending,
        ImmutableHashSet<string> CollapsedGroups,
        Controls.EntryVm[] Vms);

    /// <summary>
    /// The array previously projected for <paramref name="index"/>, if it was built from exactly
    /// these inputs; otherwise <c>null</c>.
    /// </summary>
    internal Controls.EntryVm[]? TryReuse(int index, Column column, ImmutableArray<string> cutPending)
    {
        if (index < 0 || index >= memo.Length)
        {
            return null;
        }

        var cached = memo[index];
        return cached.Vms is not null
            && cached.Entries == column.Entries
            && cached.CutPending == cutPending
            && ReferenceEquals(cached.CollapsedGroups, column.CollapsedGroups)
            && cached.Location == column.Location
            ? cached.Vms
            : null;
    }

    /// <summary>Records what <paramref name="index"/> was projected from, for the next render.</summary>
    internal void Store(int index, Column column, ImmutableArray<string> cutPending, Controls.EntryVm[] vms)
    {
        if (index < 0)
        {
            return;
        }

        if (index >= memo.Length)
        {
            System.Array.Resize(ref memo, index + 1);
        }

        memo[index] = new Entry(column.Entries, column.Location, cutPending, column.CollapsedGroups, vms);
    }

    /// <summary>
    /// Drops memo slots for columns that no longer exist, so closing a deep chain does not pin
    /// their row arrays in memory.
    /// </summary>
    internal void Trim(int columnCount)
    {
        if (columnCount < memo.Length)
        {
            System.Array.Resize(ref memo, System.Math.Max(columnCount, 0));
        }
    }
}
