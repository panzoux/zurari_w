using Zurari.App;
using Zurari.Core;
using static Zurari.TestSupport.TestPanes;

namespace Zurari.App.Tests;

/// <summary>
/// The path bar and status bar: the full path of what the cursor is on, key hints for the mode the
/// app is in, and the counts that used to share one line with the path.
/// </summary>
public class StatusBarProjectionTests
{
    private static AppState StateWith(Column column) => AppState.SinglePane([column]);

    private static Column Folder(params Entry[] entries) =>
        new(new Location.RealDirectory(@"C:\x"), [.. entries], Cursor: 0, Load: LoadState.Loaded);

    [Fact]
    public void The_path_bar_shows_the_full_path_of_the_file_under_the_cursor()
    {
        var state = StateWith(Folder(new Entry("b.txt", EntryKind.File)));

        Assert.Equal(@"C:\x\b.txt", StateProjection.ProjectStatusBar(state).Path);
    }

    [Fact]
    public void The_path_bar_shows_a_folder_row_too()
    {
        var state = StateWith(Folder(new Entry("sub", EntryKind.Directory)));

        Assert.Equal(@"C:\x\sub", StateProjection.ProjectStatusBar(state).Path);
    }

    [Fact]
    public void A_drive_row_shows_its_root()
    {
        var drives = new Column(
            Location.Drives.Instance, [new Entry(@"C:\", EntryKind.Drive)], Cursor: 0, Load: LoadState.Loaded);

        Assert.Equal(@"C:\", StateProjection.ProjectStatusBar(StateWith(drives)).Path);
    }

    /// <summary>
    /// A deleted file sits in the bin under a meaningless name; where it came from is the path worth
    /// showing.
    /// </summary>
    [Fact]
    public void A_deleted_file_shows_where_it_came_from()
    {
        var bin = new Column(
            Location.RecycleBin.Instance,
            [new Entry("$R1.txt", EntryKind.File, OriginalPath: @"C:\docs\old.txt")],
            Cursor: 0,
            Load: LoadState.Loaded);

        Assert.Equal(@"C:\docs\old.txt", StateProjection.ProjectStatusBar(StateWith(bin)).Path);
    }

    [Fact]
    public void On_a_section_header_the_path_bar_names_the_pane()
    {
        var drives = new Column(
            Location.Drives.Instance,
            [new Entry("ドライブ", EntryKind.Header, Group: EntryGroups.Drives)],
            Cursor: 0,
            Load: LoadState.Loaded);

        Assert.Equal("ドライブ", StateProjection.ProjectStatusBar(StateWith(drives)).Path);
    }

    [Fact]
    public void In_normal_mode_the_hints_offer_sorting_and_hidden_files()
    {
        var hints = StateProjection.ProjectStatusBar(StateWith(Folder(new Entry("b.txt", EntryKind.File)))).Hints;

        Assert.Contains("S 並べ替え", hints);
        Assert.Contains("Ctrl+Shift+. 隠しファイル", hints);
    }

    [Fact]
    public void The_path_bar_shows_the_focused_bottom_panes_path()
    {
        var top = Folder(new Entry("t.txt", EntryKind.File));
        var bottom = new Column(
            new Location.RealDirectory(@"C:\other"), [new Entry("b.txt", EntryKind.File)], Cursor: 0, Load: LoadState.Loaded);
        var state = TwoPanes(top, bottom, focused: 1);

        Assert.Equal(@"C:\other\b.txt", StateProjection.ProjectStatusBar(state).Path);
    }

    /// <summary>Sort mode has to show itself: otherwise it is open and nothing on screen says so.</summary>
    [Fact]
    public void In_sort_mode_the_hints_show_the_current_order_and_the_field_keys()
    {
        var state = StateWith(Folder(new Entry("b.txt", EntryKind.File)))
            .WithFocused(p => p with { View = new ViewOptions(new SortOrder(SortMode.Size, Descending: true)) }) with
        {
            InputMode = InputMode.Sort,
        };

        var hints = StateProjection.ProjectStatusBar(state).Hints;

        Assert.Contains("サイズ ↓", hints);
        Assert.Contains("N 名前", hints);
        Assert.Contains("Esc", hints);
    }

    /// <summary>Enter and Esc in a rename box need no explaining, and nothing else applies while typing.</summary>
    [Fact]
    public void While_renaming_there_are_no_hints()
    {
        var (renaming, _) = Transition.Apply(
            StateWith(Folder(new Entry("b.txt", EntryKind.File))), new Msg.RenameRequested(Col(0)));
        Assert.NotNull(renaming.Rename);

        Assert.Equal(string.Empty, StateProjection.ProjectStatusBar(renaming).Hints);
    }

    [Fact]
    public void The_summary_keeps_the_count_marks_and_notice()
    {
        var column = Folder(new Entry("a.txt", EntryKind.File, IsMarked: true), new Entry("b.txt", EntryKind.File));
        var state = StateWith(column) with { Notice = "取り出せません" };

        Assert.Equal("2 件 | マーク: 1 | 取り出せません", StateProjection.ProjectStatusBar(state).Summary);
    }
}
