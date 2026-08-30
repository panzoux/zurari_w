using Zurari.Core;

namespace Zurari.Core.Tests;

public class LocationTests
{
    [Fact]
    public void A_real_directory_is_known_to_the_shell_by_its_path()
    {
        var location = new Location.RealDirectory(@"C:\Users\someone");

        Assert.Equal(@"C:\Users\someone", location.FilesystemPath);
        Assert.Equal(@"C:\Users\someone", location.ShellParsingName);
    }

    /// <summary>
    /// The recycle bin is the reason the two are separate properties: it has no path at all, but the
    /// shell names it perfectly well - which is what makes "ゴミ箱を空にする" reachable from the row's
    /// own context menu.
    /// </summary>
    [Fact]
    public void The_recycle_bin_has_no_path_but_the_shell_still_names_it()
    {
        var location = Location.RecycleBin.Instance;

        Assert.Null(location.FilesystemPath);
        Assert.Equal("::{645FF040-5081-101B-9F08-00AA002F954E}", location.ShellParsingName);
    }

    [Fact]
    public void The_drive_list_is_ours_alone_and_the_shell_has_no_name_for_it()
    {
        Assert.Null(Location.Drives.Instance.FilesystemPath);
        Assert.Null(Location.Drives.Instance.ShellParsingName);
    }
}
