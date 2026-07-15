using System.Windows.Input;
using Zurari.Controls;

namespace Zurari.Gallery.Tests;

public class GalleryBrowserSessionTests
{
    [Fact]
    public void EntryActivated_on_a_directory_extends_the_chain_and_focuses_the_new_column()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleEntryActivated(new EntryActivatedEventArgs(0, 0)); // dirA

        Assert.Equal(2, session.Columns.Count);
        Assert.Equal("dirA", session.Columns[1].Title);
        Assert.Equal(3, session.Columns[1].Entries.Count);
        Assert.False(session.Columns[0].IsFocused);
        Assert.True(session.Columns[1].IsFocused);
    }

    [Fact]
    public void EntryActivated_on_a_file_does_not_extend_the_chain()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleEntryActivated(new EntryActivatedEventArgs(0, 2)); // r1.txt

        Assert.Single(session.Columns);
        Assert.Equal(2, session.Columns[0].CursorIndex);
    }

    [Fact]
    public void EntryActivated_on_an_earlier_column_rebuilds_columns_to_its_right()
    {
        var session = new GalleryBrowserSession(SampleTree());
        session.HandleEntryActivated(new EntryActivatedEventArgs(0, 0)); // -> root, dirA

        session.HandleEntryActivated(new EntryActivatedEventArgs(0, 1)); // activate dirB instead

        Assert.Equal(2, session.Columns.Count);
        Assert.Equal("dirB", session.Columns[1].Title);
    }

    [Fact]
    public void NavigateUpRequested_moves_focus_left_without_truncating_columns()
    {
        var session = new GalleryBrowserSession(SampleTree());
        session.HandleEntryActivated(new EntryActivatedEventArgs(0, 0)); // -> 2 columns, focus col 1

        session.HandleNavigateUpRequested(new NavigateUpRequestedEventArgs(1));

        Assert.Equal(2, session.Columns.Count);
        Assert.True(session.Columns[0].IsFocused);
        Assert.False(session.Columns[1].IsFocused);
    }

    [Fact]
    public void NavigateUpRequested_on_the_root_column_is_a_no_op()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleNavigateUpRequested(new NavigateUpRequestedEventArgs(0));

        Assert.True(session.Columns[0].IsFocused);
    }

    [Fact]
    public void CursorMoveRequested_up_at_index_zero_stays_clamped_at_zero()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleCursorMoveRequested(new CursorMoveRequestedEventArgs(0, CursorMove.Up, 0));

        Assert.Equal(0, session.Columns[0].CursorIndex);
    }

    [Fact]
    public void CursorMoveRequested_down_at_last_index_stays_clamped_at_last()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleCursorMoveRequested(new CursorMoveRequestedEventArgs(0, CursorMove.End, 0));
        Assert.Equal(2, session.Columns[0].CursorIndex);

        session.HandleCursorMoveRequested(new CursorMoveRequestedEventArgs(0, CursorMove.Down, 0));

        Assert.Equal(2, session.Columns[0].CursorIndex);
    }

    [Fact]
    public void CursorMoveRequested_page_down_advances_by_the_visible_row_count_hint()
    {
        var files = new GalleryNode[20];
        for (var i = 0; i < files.Length; i++)
        {
            files[i] = GalleryNode.File($"f{i}.txt", i, new DateTime(2020, 1, 1));
        }

        var session = new GalleryBrowserSession(GalleryNode.Directory("root", files));

        session.HandleCursorMoveRequested(new CursorMoveRequestedEventArgs(0, CursorMove.PageDown, 5));

        Assert.Equal(5, session.Columns[0].CursorIndex);
    }

    [Fact]
    public void CursorMoveRequested_page_down_clamps_to_the_last_index()
    {
        var files = new GalleryNode[5];
        for (var i = 0; i < files.Length; i++)
        {
            files[i] = GalleryNode.File($"f{i}.txt", i, new DateTime(2020, 1, 1));
        }

        var session = new GalleryBrowserSession(GalleryNode.Directory("root", files));

        session.HandleCursorMoveRequested(new CursorMoveRequestedEventArgs(0, CursorMove.PageDown, 100));

        Assert.Equal(4, session.Columns[0].CursorIndex);
    }

    [Fact]
    public void ColumnFocusRequested_changes_the_focused_column()
    {
        var session = new GalleryBrowserSession(SampleTree());
        session.HandleEntryActivated(new EntryActivatedEventArgs(0, 0)); // 2 columns, focus col 1

        session.HandleColumnFocusRequested(new ColumnFocusRequestedEventArgs(0));

        Assert.True(session.Columns[0].IsFocused);
        Assert.False(session.Columns[1].IsFocused);
    }

    [Fact]
    public void EntryPointerPressed_with_control_modifier_toggles_is_marked()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleEntryPointerPressed(
            new EntryPointerPressedEventArgs(0, 0, ModifierKeys.Control, MouseButton.Left));
        Assert.True(session.Columns[0].Entries[0].IsMarked);

        session.HandleEntryPointerPressed(
            new EntryPointerPressedEventArgs(0, 0, ModifierKeys.Control, MouseButton.Left));
        Assert.False(session.Columns[0].Entries[0].IsMarked);
    }

    [Fact]
    public void EntryPointerPressed_without_control_modifier_moves_cursor_but_does_not_mark()
    {
        var session = new GalleryBrowserSession(SampleTree());

        session.HandleEntryPointerPressed(
            new EntryPointerPressedEventArgs(0, 1, ModifierKeys.None, MouseButton.Left));

        Assert.Equal(1, session.Columns[0].CursorIndex);
        Assert.False(session.Columns[0].Entries[1].IsMarked);
    }

    [Fact]
    public void HandleEntryActivated_with_out_of_range_column_index_is_a_no_op()
    {
        var session = new GalleryBrowserSession(SampleTree());
        var before = session.Columns;

        session.HandleEntryActivated(new EntryActivatedEventArgs(5, 0));

        Assert.Same(before, session.Columns);
    }

    [Fact]
    public void Reset_with_initial_depth_pre_expands_by_following_first_directory_entries()
    {
        var session = new GalleryBrowserSession(GalleryDatasets.Deep(), initialDepth: 8);

        Assert.Equal(8, session.Columns.Count);
        Assert.True(session.Columns[7].IsFocused);
    }

    private static GalleryNode SampleTree()
    {
        var dirA = GalleryNode.Directory(
            "dirA",
            [
                GalleryNode.File("a1.txt", 10, new DateTime(2020, 1, 1)),
                GalleryNode.File("a2.txt", 20, new DateTime(2020, 1, 2)),
                GalleryNode.File("a3.txt", 30, new DateTime(2020, 1, 3)),
            ]);
        var dirB = GalleryNode.Directory("dirB", []);
        return GalleryNode.Directory("root", [dirA, dirB, GalleryNode.File("r1.txt", 5, new DateTime(2020, 1, 4))]);
    }
}
