using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit;

namespace Zurari.Controls.Tests;

public class EntryTemplateSelectorTests
{
    private static EntryVm Row(EntryKind kind) =>
        new("row", kind, IsMarked: false, SizeText: null, DateText: null);

    [Fact]
    public void A_header_gets_the_header_template_and_everything_else_gets_the_entry_one()
    {
        var header = new DataTemplate();
        var entry = new DataTemplate();
        var selector = new EntryTemplateSelector { HeaderTemplate = header, EntryTemplate = entry };

        Assert.Same(header, selector.SelectTemplate(Row(EntryKind.Header), new DependencyObject()));
        Assert.Same(entry, selector.SelectTemplate(Row(EntryKind.Drive), new DependencyObject()));
        Assert.Same(entry, selector.SelectTemplate(Row(EntryKind.Directory), new DependencyObject()));
        Assert.Same(entry, selector.SelectTemplate(Row(EntryKind.File), new DependencyObject()));
    }

    /// <summary>
    /// The two templates are wired up in Generic.xaml, where a mistake produces no build error and
    /// would simply leave headers rendering as ordinary rows. This realizes a real column and looks
    /// at what actually came out.
    /// </summary>
    [StaFact]
    public void A_header_row_renders_without_the_icon_an_ordinary_row_has()
    {
        var columns = new[]
        {
            new ColumnVm(
                "ドライブ",
                [
                    new EntryVm("ドライブ", EntryKind.Header, false, null, null),
                    new EntryVm("Windows (C:)", EntryKind.Drive, false, null, null),
                ],
                CursorIndex: 0,
                IsFocused: true),
        };

        var browser = new ColumnBrowser { Columns = columns };
        using var window = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindVisualChild<ListBox>(browser);
        Assert.NotNull(list);

        var headerContainer = list!.ItemContainerGenerator.ContainerFromIndex(0) as ListBoxItem;
        var rowContainer = list.ItemContainerGenerator.ContainerFromIndex(1) as ListBoxItem;
        Assert.NotNull(headerContainer);
        Assert.NotNull(rowContainer);

        Assert.Equal(0, VirtualizationTests.CountVisualChildren<Image>(headerContainer!));
        Assert.Equal(1, VirtualizationTests.CountVisualChildren<Image>(rowContainer!));
    }

    private static T? FindVisualChild<T>(DependencyObject root)
        where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed)
            {
                return typed;
            }

            var nested = FindVisualChild<T>(child);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
