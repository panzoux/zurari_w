using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.App.Tests;

public class SplitLayoutTests
{
    private static readonly PaneId Top = new(0);

    private static PaneViewSettings Stored(string mode, bool descending, bool dirsFirst, bool hidden) =>
        new(mode, descending, dirsFirst, hidden);

    private static ViewOptions ViewRestoredFor(IReadOnlyList<Msg> msgs)
    {
        var restored = Assert.IsType<Msg.ViewRestored>(msgs[0]);
        Assert.Equal(Top, restored.Pane);
        return restored.View;
    }

    [Fact]
    public void A_legacy_file_restores_its_single_view_and_refreshes_without_a_split()
    {
        var settings = new UserSettings(SortMode: "Size", SortDescending: true, DirectoriesFirst: false, ShowHidden: true);

        var msgs = SplitLayout.StartupMsgs(settings, Top);

        Assert.Equal(2, msgs.Count);
        var view = ViewRestoredFor(msgs);
        Assert.Equal(new ViewOptions(new SortOrder(SortMode.Size, true, false), true), view);
        Assert.IsType<Msg.Refresh>(msgs[1]);
    }

    [Fact]
    public void A_default_file_restores_the_default_view()
    {
        var msgs = SplitLayout.StartupMsgs(new UserSettings(), Top);

        Assert.Equal(2, msgs.Count);
        Assert.Equal(ViewOptions.Default, ViewRestoredFor(msgs));
    }

    [Fact]
    public void An_open_split_with_one_stored_view_opens_the_bottom_pane_with_no_view_of_its_own()
    {
        var settings = new UserSettings(
            SplitOpen: true,
            PaneViews: [Stored("Modified", false, true, false)]);

        var msgs = SplitLayout.StartupMsgs(settings, Top);

        Assert.Equal(3, msgs.Count);
        Assert.Equal(SortMode.Modified, ViewRestoredFor(msgs).Sort.Mode);
        Assert.IsType<Msg.Refresh>(msgs[1]);
        var toggle = Assert.IsType<Msg.ToggleSplit>(msgs[2]);
        Assert.Null(toggle.NewPaneView);
    }

    [Fact]
    public void An_open_split_with_two_stored_views_gives_each_its_own()
    {
        var settings = new UserSettings(
            SplitOpen: true,
            PaneViews: [Stored("Size", true, true, false), Stored("Extension", false, false, true)]);

        var msgs = SplitLayout.StartupMsgs(settings, Top);

        Assert.Equal(3, msgs.Count);
        Assert.Equal(new ViewOptions(new SortOrder(SortMode.Size, true, true), false), ViewRestoredFor(msgs));
        var toggle = Assert.IsType<Msg.ToggleSplit>(msgs[2]);
        Assert.Equal(new ViewOptions(new SortOrder(SortMode.Extension, false, false), true), toggle.NewPaneView);
    }

    [Fact]
    public void Null_view_entries_read_as_default_views_instead_of_throwing()
    {
        var settings = new UserSettings(
            SplitOpen: true,
            PaneViews: [null!, null!]);

        var msgs = SplitLayout.StartupMsgs(settings, Top);

        Assert.Equal(3, msgs.Count);
        Assert.Equal(ViewOptions.Default, ViewRestoredFor(msgs));
        var toggle = Assert.IsType<Msg.ToggleSplit>(msgs[2]);
        Assert.Equal(ViewOptions.Default, toggle.NewPaneView);
    }

    [Fact]
    public void A_closed_split_is_not_reopened_even_when_two_views_are_stored()
    {
        var settings = new UserSettings(
            SplitOpen: false,
            PaneViews: [Stored("Name", false, true, false), Stored("Size", true, true, true)]);

        var msgs = SplitLayout.StartupMsgs(settings, Top);

        Assert.Equal(2, msgs.Count);
        Assert.IsType<Msg.ViewRestored>(msgs[0]);
        Assert.IsType<Msg.Refresh>(msgs[1]);
    }

    [Fact]
    public void A_window_too_small_for_both_panes_has_no_measured_ratio()
    {
        Assert.Null(SplitLayout.MeasuredRatio(100, 100, 120));
        Assert.Null(SplitLayout.MeasuredRatio(120, 120, 120));
        Assert.Null(SplitLayout.MeasuredRatio(0, 0, 120));
    }

    [Fact]
    public void A_zero_height_bottom_row_has_no_measured_ratio()
    {
        Assert.Null(SplitLayout.MeasuredRatio(800, 0, 120));
        Assert.Null(SplitLayout.MeasuredRatio(800, -1, 120));
    }

    [Fact]
    public void A_measured_ratio_is_the_top_rows_share()
    {
        Assert.Equal(0.5, SplitLayout.MeasuredRatio(300, 300, 120));
        Assert.Equal(0.6, SplitLayout.MeasuredRatio(360, 240, 120)!.Value, 10);
    }

    [Fact]
    public void A_measured_ratio_leaves_each_pane_its_minimum_height()
    {
        // 1000 total, min 120: the floor is 0.15 (150 > 120) and the ceiling 0.85.
        Assert.Equal(0.15, SplitLayout.MeasuredRatio(10, 990, 120));
        Assert.Equal(0.85, SplitLayout.MeasuredRatio(990, 10, 120));

        // 400 total, min 120: the minimum height (0.3) is the tighter bound, not 0.15.
        Assert.Equal(0.3, SplitLayout.MeasuredRatio(10, 390, 120)!.Value, 10);
        Assert.Equal(0.7, SplitLayout.MeasuredRatio(390, 10, 120)!.Value, 10);
    }

    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(0.15, 0.15)]
    [InlineData(0.85, 0.85)]
    [InlineData(0.0, 0.15)]
    [InlineData(-3.0, 0.15)]
    [InlineData(0.99, 0.85)]
    [InlineData(7.0, 0.85)]
    [InlineData(double.NaN, 0.5)]
    [InlineData(double.PositiveInfinity, 0.5)]
    [InlineData(double.NegativeInfinity, 0.5)]
    public void ClampRatio_keeps_a_ratio_within_bounds_and_replaces_a_non_finite_one(double input, double expected)
    {
        Assert.Equal(expected, SplitLayout.ClampRatio(input));
    }
}
