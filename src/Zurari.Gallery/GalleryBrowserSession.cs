using System.Globalization;
using System.Windows.Input;
using Zurari.Controls;

namespace Zurari.Gallery;

/// <summary>
/// Reference reducer for the Gallery harness: turns ColumnBrowser's request events into
/// a fresh <see cref="Columns"/> snapshot. This is the wiring pattern Phase 3 will follow
/// for the real State-&gt;VM adapter in Zurari.App, proven here over fake data first.
/// Deliberately free of WPF UI types (the modifier/button enums on the event args are
/// plain value types) so it is unit-testable without an STA thread.
/// </summary>
public sealed class GalleryBrowserSession
{
    private readonly List<GalleryNode> chain = [];
    private readonly List<int> cursorIndices = [];
    private readonly HashSet<GalleryNode> marked = new(ReferenceEqualityComparer.Instance);

    private int focusedColumn;

    public GalleryBrowserSession(GalleryNode root, int initialDepth = 1)
    {
        Reset(root, initialDepth);
    }

    /// <summary>Current column snapshot. Assign this to the host's ColumnBrowser.Columns after every handler call.</summary>
    public IReadOnlyList<ColumnVm> Columns { get; private set; } = [];

    /// <summary>
    /// Rebuilds the session around a new tree (a dataset switch). Clears marks and cursors.
    /// <paramref name="initialDepth"/> pre-expands the chain by following each column's first
    /// entry as long as it is a directory (used by the "Deep" dataset to show 8 columns immediately).
    /// </summary>
    public void Reset(GalleryNode root, int initialDepth = 1)
    {
        ArgumentNullException.ThrowIfNull(root);

        chain.Clear();
        cursorIndices.Clear();
        marked.Clear();

        chain.Add(root);
        cursorIndices.Add(root.Children.Count > 0 ? 0 : -1);

        while (chain.Count < initialDepth)
        {
            var cursor = cursorIndices[^1];
            var current = chain[^1];
            if (cursor < 0 || cursor >= current.Children.Count)
            {
                break;
            }

            var child = current.Children[cursor];
            if (child.Kind != EntryKind.Directory)
            {
                break;
            }

            chain.Add(child);
            cursorIndices.Add(child.Children.Count > 0 ? 0 : -1);
        }

        focusedColumn = chain.Count - 1;
        Rebuild();
    }

    /// <summary>Up/Down/PageUp/PageDown/Home/End: moves the cursor within the target column, clamped to bounds.</summary>
    public void HandleCursorMoveRequested(CursorMoveRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ColumnIndex < 0 || e.ColumnIndex >= chain.Count)
        {
            return;
        }

        var count = chain[e.ColumnIndex].Children.Count;
        if (count == 0)
        {
            cursorIndices[e.ColumnIndex] = -1;
            Rebuild();
            return;
        }

        var current = Math.Max(0, cursorIndices[e.ColumnIndex]);
        var step = Math.Max(1, e.VisibleRowCount);
        var next = e.Move switch
        {
            CursorMove.Up => Math.Max(0, current - 1),
            CursorMove.Down => Math.Min(count - 1, current + 1),
            CursorMove.PageUp => Math.Max(0, current - step),
            CursorMove.PageDown => Math.Min(count - 1, current + step),
            CursorMove.Home => 0,
            CursorMove.End => count - 1,
            _ => current,
        };

        cursorIndices[e.ColumnIndex] = next;
        Rebuild();
    }

    /// <summary>Changes which column is focused (click on a non-focused column).</summary>
    public void HandleColumnFocusRequested(ColumnFocusRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ColumnIndex < 0 || e.ColumnIndex >= chain.Count)
        {
            return;
        }

        focusedColumn = e.ColumnIndex;
        Rebuild();
    }

    /// <summary>
    /// Enter / double-click / right-arrow on an entry. Moves that column's cursor to the entry;
    /// if it is a directory, columns to the right of it are rebuilt to show the new column, focused.
    /// </summary>
    public void HandleEntryActivated(EntryActivatedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ColumnIndex < 0 || e.ColumnIndex >= chain.Count)
        {
            return;
        }

        var column = chain[e.ColumnIndex];
        if (e.EntryIndex < 0 || e.EntryIndex >= column.Children.Count)
        {
            return;
        }

        cursorIndices[e.ColumnIndex] = e.EntryIndex;

        var entry = column.Children[e.EntryIndex];
        if (entry.Kind != EntryKind.Directory)
        {
            Rebuild();
            return;
        }

        while (chain.Count > e.ColumnIndex + 1)
        {
            chain.RemoveAt(chain.Count - 1);
            cursorIndices.RemoveAt(cursorIndices.Count - 1);
        }

        chain.Add(entry);
        cursorIndices.Add(entry.Children.Count > 0 ? 0 : -1);
        focusedColumn = chain.Count - 1;
        Rebuild();
    }

    /// <summary>
    /// Backspace / left-arrow. Finder semantics: only the focus moves one column left - the
    /// trailing columns (path history) stay visible until something new is activated.
    /// </summary>
    public void HandleNavigateUpRequested(NavigateUpRequestedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ColumnIndex <= 0 || e.ColumnIndex >= chain.Count)
        {
            return;
        }

        focusedColumn = e.ColumnIndex - 1;
        Rebuild();
    }

    /// <summary>A pointer press on a row: moves that column's cursor there; Ctrl+click toggles the mark.</summary>
    public void HandleEntryPointerPressed(EntryPointerPressedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (e.ColumnIndex < 0 || e.ColumnIndex >= chain.Count)
        {
            return;
        }

        var column = chain[e.ColumnIndex];
        if (e.EntryIndex < 0 || e.EntryIndex >= column.Children.Count)
        {
            return;
        }

        cursorIndices[e.ColumnIndex] = e.EntryIndex;

        if ((e.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            var entry = column.Children[e.EntryIndex];
            if (!marked.Add(entry))
            {
                marked.Remove(entry);
            }
        }

        Rebuild();
    }

    private void Rebuild()
    {
        var columns = new ColumnVm[chain.Count];
        for (var i = 0; i < chain.Count; i++)
        {
            var node = chain[i];
            var entries = new EntryVm[node.Children.Count];
            for (var j = 0; j < node.Children.Count; j++)
            {
                var child = node.Children[j];
                entries[j] = new EntryVm(
                    child.Name,
                    child.Kind,
                    marked.Contains(child),
                    child.Kind == EntryKind.File ? FormatSize(child.SizeBytes) : null,
                    child.Kind == EntryKind.File ? FormatDate(child.Modified) : null);
            }

            columns[i] = new ColumnVm(node.Name, entries, cursorIndices[i], i == focusedColumn);
        }

        Columns = columns;
    }

    private static string? FormatSize(long? sizeBytes) =>
        sizeBytes is { } size ? size.ToString("N0", CultureInfo.InvariantCulture) + " B" : null;

    private static string? FormatDate(DateTime? modified) =>
        modified is { } date ? date.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) : null;
}
