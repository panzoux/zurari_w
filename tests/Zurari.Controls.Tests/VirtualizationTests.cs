using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Zurari.Controls.Tests;

public class VirtualizationTests
{
    internal static IReadOnlyList<ColumnVm> MakeColumns(int columnCount, int entryCount, int focused = 0)
    {
        var columns = new ColumnVm[columnCount];
        for (var c = 0; c < columnCount; c++)
        {
            var entries = new EntryVm[entryCount];
            for (var i = 0; i < entryCount; i++)
            {
                entries[i] = new EntryVm(
                    $"entry-{c}-{i}",
                    i % 5 == 0 ? EntryKind.Directory : EntryKind.File,
                    IsMarked: false,
                    SizeText: "1.2 KB",
                    DateText: "2026-07-08 12:00");
            }

            columns[c] = new ColumnVm($"col {c}", entries, CursorIndex: 0, IsFocused: c == focused);
        }

        return columns;
    }

    internal static int CountVisualChildren<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T)
            {
                count++;
            }

            count += CountVisualChildren<T>(child);
        }

        return count;
    }

    [StaFact]
    public void Three_columns_of_100k_entries_realize_only_viewport_sized_container_counts()
    {
        var browser = new ColumnBrowser { Columns = MakeColumns(columnCount: 3, entryCount: 100_000) };
        using var host = new TestWindow(browser);

        var realized = CountVisualChildren<ListBoxItem>(browser);

        // 3 columns, each realizing roughly a viewport (+ virtualization buffer) of rows.
        Assert.True(realized > 0, "no containers realized - virtualization host is not set up");
        Assert.True(realized < 500, $"virtualization is broken: {realized} containers realized for 300k entries");
    }

    [StaFact]
    public void Columns_are_laid_out_horizontally_in_vm_order()
    {
        var browser = new ColumnBrowser { Columns = MakeColumns(columnCount: 3, entryCount: 10) };
        using var host = new TestWindow(browser);

        var headers = new List<string>();
        CollectTexts(browser, headers);

        Assert.Contains("col 0", headers);
        Assert.Contains("col 1", headers);
        Assert.Contains("col 2", headers);
    }

    private static void CollectTexts(DependencyObject root, List<string> texts)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBlock tb && tb.Text.Length > 0)
            {
                texts.Add(tb.Text);
            }

            CollectTexts(child, texts);
        }
    }
}
