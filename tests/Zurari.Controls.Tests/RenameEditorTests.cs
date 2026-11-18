using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Zurari.Controls;

namespace Zurari.Controls.Tests;

/// <summary>
/// The rename editor really appears over the row (6e.4). A mistake in the adorner plumbing produces
/// no build error and fails nothing else - the editor simply never shows.
/// </summary>
public class RenameEditorTests
{
    private static ColumnVm Column(params string[] names)
    {
        var entries = new EntryVm[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            entries[i] = new EntryVm(names[i], EntryKind.File, false, "1 KB", null);
        }

        return new ColumnVm("test", entries, CursorIndex: 0, IsFocused: true);
    }

    private static int CountTextBoxes(DependencyObject root)
    {
        var found = 0;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox)
            {
                found++;
            }

            found += CountTextBoxes(child);
        }

        return found;
    }

    [StaFact]
    public void The_editor_appears_over_the_row_and_goes_away_again()
    {
        var browser = new ColumnBrowser { Columns = [Column("a.txt", "b.txt")] };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        browser.SyncRenameEditor(0, 1, "b.txt", null);
        TestWindow.DoEvents();
        Assert.Equal(1, CountTextBoxes(browser));

        browser.SyncRenameEditor(-1, -1, null, null);
        TestWindow.DoEvents();
        Assert.Equal(0, CountTextBoxes(browser));
    }

    /// <summary>
    /// A second call with a complaint must update the editor rather than stack another on top -
    /// which would also throw away what the user had typed.
    /// </summary>
    [StaFact]
    public void Showing_it_twice_updates_rather_than_stacking()
    {
        var browser = new ColumnBrowser { Columns = [Column("a.txt")] };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        browser.SyncRenameEditor(0, 0, "a.txt", null);
        TestWindow.DoEvents();
        browser.SyncRenameEditor(0, 0, "a.txt", "既にあります");
        TestWindow.DoEvents();

        Assert.Equal(1, CountTextBoxes(browser));
    }

    private static TextBox? FindTextBox(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox box)
            {
                return box;
            }

            if (FindTextBox(child) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    /// <summary>
    /// Taking the editor away from code moves keyboard focus off it; that must not read as the user
    /// giving up, or every rename the state ends would cancel itself back through the state.
    /// </summary>
    [StaFact]
    public void Hiding_a_focused_editor_from_code_does_not_raise_cancelled()
    {
        var browser = new ColumnBrowser { Columns = [Column("a.txt")] };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();
        var cancelled = 0;
        browser.RenameCancelled += (_, _) => cancelled++;

        browser.SyncRenameEditor(0, 0, "a.txt", null);
        TestWindow.DoEvents();
        var box = FindTextBox(browser);
        Assert.NotNull(box);
        box.Focus();
        Keyboard.Focus(box);
        TestWindow.DoEvents();

        browser.SyncRenameEditor(-1, -1, null, null);
        TestWindow.DoEvents();

        Assert.Equal(0, CountTextBoxes(browser));
        Assert.Equal(0, cancelled);
    }

    [StaFact]
    public void Escape_in_the_editor_still_raises_cancelled()
    {
        var browser = new ColumnBrowser { Columns = [Column("a.txt")] };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();
        var cancelled = 0;
        browser.RenameCancelled += (_, _) => cancelled++;

        browser.SyncRenameEditor(0, 0, "a.txt", null);
        TestWindow.DoEvents();
        var box = FindTextBox(browser);
        Assert.NotNull(box);

        box.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(box), 0, Key.Escape)
        {
            RoutedEvent = UIElement.PreviewKeyDownEvent,
        });

        Assert.Equal(1, cancelled);
    }

    [StaFact]
    public void Only_the_column_being_renamed_gets_an_editor()
    {
        var browser = new ColumnBrowser { Columns = [Column("a.txt"), Column("b.txt")] };
        using var host = new TestWindow(browser);
        TestWindow.DoEvents();

        browser.SyncRenameEditor(1, 0, "b.txt", null);
        TestWindow.DoEvents();

        Assert.Equal(1, CountTextBoxes(browser));
    }
}
