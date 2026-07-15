using Zurari.Core;

namespace Zurari.Shell.Tests;

public class ShellIconCacheTests
{
    [StaFact]
    public void File_extension_returns_frozen_icon()
    {
        var cache = new ShellIconCache();

        var icon = cache.GetIcon(EntryKind.File, "a.txt");

        Assert.NotNull(icon);
        Assert.True(icon!.IsFrozen);
    }

    [StaFact]
    public void Same_extension_returns_cached_instance()
    {
        var cache = new ShellIconCache();

        var first = cache.GetIcon(EntryKind.File, "a.txt");
        var second = cache.GetIcon(EntryKind.File, "b.txt");

        Assert.Same(first, second);
    }

    [StaFact]
    public void Different_extensions_are_cached_separately()
    {
        var cache = new ShellIconCache();

        var txt = cache.GetIcon(EntryKind.File, "a.txt");
        var exe = cache.GetIcon(EntryKind.File, "a.exe");

        // Not asserting they differ (both may legitimately resolve to the same generic icon on
        // some systems) - only that each lookup succeeds and is independently cached.
        Assert.NotNull(txt);
        Assert.NotNull(exe);
        Assert.Same(exe, cache.GetIcon(EntryKind.File, "b.exe"));
    }

    [StaFact]
    public void Directory_and_drive_return_non_null_and_are_cached()
    {
        var cache = new ShellIconCache();

        var dir1 = cache.GetIcon(EntryKind.Directory, "sub1");
        var dir2 = cache.GetIcon(EntryKind.Directory, "sub2");
        var drive1 = cache.GetIcon(EntryKind.Drive, "C:\\");
        var drive2 = cache.GetIcon(EntryKind.Drive, "D:\\");

        Assert.NotNull(dir1);
        Assert.NotNull(drive1);
        Assert.Same(dir1, dir2);
        Assert.Same(drive1, drive2);
    }

    [StaTheory]
    [InlineData("noext")]
    [InlineData("")]
    [InlineData("a|b?<>.txt")]
    public void Weird_names_do_not_throw(string name)
    {
        var cache = new ShellIconCache();

        var exception = Record.Exception(() => cache.GetIcon(EntryKind.File, name));

        Assert.Null(exception);
    }

    [StaFact]
    public void Very_long_name_does_not_throw()
    {
        var cache = new ShellIconCache();
        var longName = new string('a', 5000) + ".txt";

        var exception = Record.Exception(() => cache.GetIcon(EntryKind.File, longName));

        Assert.Null(exception);
    }
}
