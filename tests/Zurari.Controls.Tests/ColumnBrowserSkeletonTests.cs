namespace Zurari.Controls.Tests;

public class ColumnBrowserSkeletonTests
{
    [StaFact]
    public void Template_applies_when_hosted_in_a_window()
    {
        var browser = new ColumnBrowser();
        using var host = new TestWindow(browser);

        Assert.True(browser.IsLoaded);
        Assert.NotNull(browser.Template);
    }

    [StaFact]
    public void Columns_snapshot_can_be_assigned_and_read_back()
    {
        var browser = new ColumnBrowser();
        var columns = new[]
        {
            new ColumnVm(
                "root",
                new[] { new EntryVm("a", EntryKind.Directory, false, null, null) },
                0,
                true),
        };

        browser.Columns = columns;

        Assert.Same(columns, browser.Columns);
    }
}
