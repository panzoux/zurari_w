using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public void Directories_share_one_icon_and_each_drive_caches_its_own()
    {
        var cache = new ShellIconCache();

        var dir1 = cache.GetIcon(EntryKind.Directory, "sub1");
        var dir2 = cache.GetIcon(EntryKind.Directory, "sub2");
        var systemDrive = cache.GetIcon(EntryKind.Drive, "C:\\");

        Assert.NotNull(dir1);
        Assert.NotNull(systemDrive);

        // Every directory looks the same, so one cache entry serves them all. A drive does not:
        // it is cached per path, so asking twice is what returns the same instance.
        Assert.Same(dir1, dir2);
        Assert.Same(systemDrive, cache.GetIcon(EntryKind.Drive, "C:\\"));
    }

    /// <summary>
    /// The regression this exists for: drives were resolved as "something with the directory
    /// attribute", so every drive in the pane - fixed, optical, removable alike - drew the generic
    /// folder icon. Compares pixels rather than references, since two separate HICONs are never the
    /// same instance and a reference check would pass either way.
    /// </summary>
    [StaFact]
    public void Drive_does_not_draw_the_generic_folder_icon()
    {
        var cache = new ShellIconCache();

        var folder = cache.GetIcon(EntryKind.Directory, "sub");
        var systemDrive = cache.GetIcon(EntryKind.Drive, "C:\\");

        Assert.NotNull(folder);
        Assert.NotNull(systemDrive);
        Assert.NotEqual(Pixels((BitmapSource)folder!), Pixels((BitmapSource)systemDrive!));
    }

    /// <summary>
    /// The bin has no path, so its icon comes from looking the real namespace item up. Asserting it
    /// is the bin's icon and not a folder is the point: the first attempt used a stock icon chosen by
    /// a bare integer, was one off in that list, and silently returned a perfectly valid folder icon.
    /// </summary>
    [StaFact]
    public void The_recycle_bin_gets_the_shells_own_bin_icon()
    {
        var cache = new ShellIconCache();

        var bin = cache.GetShellNamespaceIcon(Location.RecycleBin.ParsingName, "empty");
        var folder = cache.GetIcon(EntryKind.Directory, "sub");

        Assert.NotNull(bin);
        Assert.NotNull(folder);
        Assert.NotEqual(Pixels((BitmapSource)folder!), Pixels((BitmapSource)bin!));
    }

    /// <summary>
    /// The variant is what makes emptying the bin change its icon: same name, different key, so the
    /// shell is asked again instead of the cache answering with the full bin forever.
    /// </summary>
    [StaFact]
    public void A_namespace_icon_is_cached_per_variant()
    {
        var cache = new ShellIconCache();

        var first = cache.GetShellNamespaceIcon(Location.RecycleBin.ParsingName, "full");

        Assert.NotNull(first);
        Assert.Same(first, cache.GetShellNamespaceIcon(Location.RecycleBin.ParsingName, "full"));
        Assert.NotSame(first, cache.GetShellNamespaceIcon(Location.RecycleBin.ParsingName, "empty"));
    }

    [StaFact]
    public void A_name_the_shell_cannot_resolve_yields_no_icon_rather_than_throwing()
    {
        var cache = new ShellIconCache();

        Assert.Null(cache.GetShellNamespaceIcon("::{00000000-0000-0000-0000-000000000000}", "x"));
    }

    /// <summary>Raw BGRA bytes of an icon, for comparing two icons by what they look like.</summary>
    private static byte[] Pixels(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
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
