using System.Windows.Input;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

/// <summary>
/// The format of the <c>--debug-input</c> key and state lines. Pinned because the lines exist to be
/// read when something does not work, and a log that silently loses a field is worse than none.
/// </summary>
public class InputTraceTests
{
    [Fact]
    public void A_key_line_says_what_arrived_and_whether_it_was_already_handled()
    {
        var line = InputTrace.KeyEvent(
            "key", Key.S, Key.S, ModifierKeys.None, Core.InputMode.Normal, "ListBox", editing: false, handled: true);

        Assert.Equal("[app] key key=S mods=None mode=Normal focus=ListBox editing=False handled=True", line);
    }

    /// <summary>With an IME on, the raw key is ImeProcessed; the line has to show the real one too.</summary>
    [Fact]
    public void A_key_line_shows_the_effective_key_only_when_it_differs()
    {
        var line = InputTrace.KeyEvent(
            "previewKey", Key.ImeProcessed, Key.N, ModifierKeys.Shift, Core.InputMode.Sort, null, editing: false, handled: false);

        Assert.Equal(
            "[app] previewKey key=ImeProcessed effective=N mods=Shift mode=Sort focus=(none) editing=False handled=False",
            line);
    }

    [Fact]
    public void A_state_line_shows_the_order_and_whether_the_focused_column_can_be_sorted()
    {
        var drives = new Column(
            Location.Drives.Instance,
            [new Entry("ドライブ", EntryKind.Header), new Entry(@"C:\", EntryKind.Drive), new Entry(@"D:\", EntryKind.Drive), new Entry(@"E:\", EntryKind.Drive)],
            Cursor: 0,
            Load: LoadState.Loaded);
        var state = new AppState
        {
            Columns = [drives],
            FocusedColumn = 0,
            InputMode = Core.InputMode.Sort,
            View = new ViewOptions(new SortOrder(SortMode.Name, Descending: true)),
        };

        Assert.Equal(
            @"[app] state mode=Sort sort=Name/desc/dirsFirst hidden=False focused=0 loc=Drives sortable=False first=[ドライブ, C:\, D:\]",
            InputTrace.State(state));
    }

    [Fact]
    public void A_state_line_names_a_real_directory_by_its_path()
    {
        var folder = new Column(
            new Location.RealDirectory(@"C:\x"), [new Entry("b.txt", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = new AppState { Columns = [folder], FocusedColumn = 0 };

        Assert.Equal(
            @"[app] state mode=Normal sort=Name/asc/dirsFirst hidden=False focused=0 loc=C:\x sortable=True first=[b.txt]",
            InputTrace.State(state));
    }
}
