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

    /// <summary>
    /// The hover control exists in the realized header, carries the right word, and reports its row.
    /// </summary>
    /// <remarks>
    /// It is wired through a Button inside a DataTemplate, caught by bubbling because a templated
    /// child cannot be named and hooked the way a template part can - so nothing here is checked by
    /// the compiler.
    /// </remarks>
    [StaFact]
    public void A_header_carries_a_hide_show_control_that_reports_its_row()
    {
        var columns = new[]
        {
            new ColumnVm(
                "ドライブ",
                [
                    new EntryVm("お気に入り", EntryKind.Header, false, null, null),
                    new EntryVm("ホーム", EntryKind.Directory, false, null, null),
                    new EntryVm("ドライブ", EntryKind.Header, false, null, null, IsSectionCollapsed: true),
                ],
                CursorIndex: 1,
                IsFocused: true),
        };

        var browser = new ColumnBrowser { Columns = columns };
        using var window = new TestWindow(browser);
        TestWindow.DoEvents();

        var list = FindVisualChild<ListBox>(browser)!;
        var expanded = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
        var collapsed = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(2);

        var expandedButton = FindVisualChild<Button>(expanded);
        var collapsedButton = FindVisualChild<Button>(collapsed);
        Assert.NotNull(expandedButton);
        Assert.NotNull(collapsedButton);
        Assert.Equal("非表示", expandedButton!.Content);
        Assert.Equal("表示", collapsedButton!.Content);

        // Ordinary rows have no such control.
        Assert.Null(FindVisualChild<Button>((ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(1)));

        int? reported = null;
        browser.SectionToggleRequested += (_, e) => reported = e.EntryIndex;
        collapsedButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));

        Assert.Equal(2, reported);
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
