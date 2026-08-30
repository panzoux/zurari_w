using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>How much is in the recycle bin.</summary>
/// <param name="ItemCount">Number of deleted items across every drive.</param>
/// <param name="TotalBytes">What they occupy.</param>
public readonly record struct RecycleBinSummary(long ItemCount, long TotalBytes);

/// <summary>
/// Reads the recycle bin, which is a shell namespace folder rather than a directory.
/// </summary>
/// <remarks>
/// There is no path to enumerate. <c>C:\$Recycle.Bin</c> exists but is per-SID, access-restricted,
/// and holds the mangled on-disk form rather than the names a person deleted - so the only honest
/// route is the shell's own view of it. That is why this lives in Shell and why reading a
/// <see cref="Zurari.Core.Location.RecycleBin"/> is routed here instead of to the Runtime that
/// handles every other listing.
/// </remarks>
public static class RecycleBinFolder
{
    /// <summary>Parsing name for the bin, understood by <c>SHCreateItemFromParsingName</c>.</summary>
    private const string ParsingName = "shell:RecycleBinFolder";

    /// <summary>BHID_EnumItems - asks a shell item for an enumerator over its children.</summary>
    private static readonly Guid BhidEnumItems = new("94f60519-2850-4924-aa5a-d15e84868039");

    private static readonly Guid IidEnumShellItems = new("70629033-e363-4a28-a567-0db78006e6d7");

    /// <summary>SIGDN_NORMALDISPLAY - the name as Explorer shows it.</summary>
    private const uint SigdnNormalDisplay = 0;

    /// <summary>SFGAO_FOLDER - whether the item is a container.</summary>
    private const uint SfgaoFolder = 0x20000000;

    /// <summary>SIGDN_DESKTOPABSOLUTEPARSING - a name the shell can resolve back to the item.</summary>
    private const uint SigdnDesktopAbsoluteParsing = 0x80028000;

    /// <summary>
    /// Item count and total size. Cheap - the shell keeps these, so nothing is enumerated.
    /// </summary>
    public static RecycleBinSummary Query()
    {
        try
        {
            var info = new NativeMethods.SHQUERYRBINFO { cbSize = Marshal.SizeOf<NativeMethods.SHQUERYRBINFO>() };
            var hr = NativeMethods.SHQueryRecycleBinW(null, ref info);
            return hr == 0 ? new RecycleBinSummary(info.i64NumItems, info.i64Size) : default;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return default;
        }
    }

    /// <summary>
    /// The names of the items in the bin, in the order the shell reports them. Empty when the bin
    /// cannot be read at all.
    /// </summary>
    /// <remarks>
    /// Names only for now. Original location, deleted-on date and size each live behind
    /// <c>IShellItem2</c> property keys, which is a second slab of interop; a listing you can look
    /// at is worth more than nothing while that is outstanding.
    /// </remarks>
    public static IReadOnlyList<RecycleBinItem> EnumerateParsingNames() => Read();

    public static IReadOnlyList<string> Enumerate() => [.. Read().Select(i => i.Original)];

    private static List<RecycleBinItem> Read()
    {
        IEnumShellItems? enumerator = null;
        var handle = IntPtr.Zero;
        try
        {
            var hr = FileOperationInterop.SHCreateItemFromParsingName(
                ParsingName, IntPtr.Zero, FileOperationInterop.IidIShellItem, out var folder);
            if (hr != 0 || folder is null)
            {
                return [];
            }

            hr = folder.BindToHandler(IntPtr.Zero, BhidEnumItems, IidEnumShellItems, out handle);
            if (hr != 0 || handle == IntPtr.Zero)
            {
                return [];
            }

            enumerator = (IEnumShellItems)Marshal.GetObjectForIUnknown(handle);
            return ReadItems(enumerator);
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or DllNotFoundException)
        {
            // The bin is a convenience, never load-bearing: a shell that will not talk to us shows
            // an empty listing rather than taking the pane down.
            return [];
        }
        finally
        {
            if (enumerator is not null)
            {
                Marshal.ReleaseComObject(enumerator);
            }

            if (handle != IntPtr.Zero)
            {
                Marshal.Release(handle);
            }
        }
    }

    private static List<RecycleBinItem> ReadItems(IEnumShellItems enumerator)
    {
        var items = new List<RecycleBinItem>();
        while (enumerator.Next(1, out var item, out var fetched) == 0 && fetched == 1 && item is not null)
        {
            try
            {
                var original = DisplayName(item, SigdnNormalDisplay);
                var parsing = DisplayName(item, SigdnDesktopAbsoluteParsing);
                if (!string.IsNullOrEmpty(original) && !string.IsNullOrEmpty(parsing))
                {
                    _ = item.GetAttributes(SfgaoFolder, out var attributes);
                    items.Add(new RecycleBinItem(parsing, original, (attributes & SfgaoFolder) != 0));
                }
            }
            finally
            {
                Marshal.ReleaseComObject(item);
            }
        }

        return items;
    }

    private static string? DisplayName(FileOperationInterop.IShellItem item, uint kind)
    {
        var ptr = IntPtr.Zero;
        try
        {
            return item.GetDisplayName(kind, out ptr) == 0 && ptr != IntPtr.Zero
                ? Marshal.PtrToStringUni(ptr)
                : null;
        }
        finally
        {
            if (ptr != IntPtr.Zero)
            {
                ShellContextMenuInterop.CoTaskMemFree(ptr);
            }
        }
    }
}

/// <summary>One item in the recycle bin, under both the names the shell has for it.</summary>
/// <param name="Parsing">
/// A name the shell can resolve back to this item (<c>SIGDN_DESKTOPABSOLUTEPARSING</c>). What any
/// operation on it has to be given - the original path is gone, so it resolves to nothing.
/// </param>
/// <param name="Original">Where the item used to live, which is what a person recognises it by.</param>
/// <param name="IsFolder">Whether a folder was deleted rather than a file.</param>
public readonly record struct RecycleBinItem(string Parsing, string Original, bool IsFolder);

/// <summary>Enumerator over a shell folder's children, obtained via <c>BHID_EnumItems</c>.</summary>
[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("70629033-e363-4a28-a567-0db78006e6d7")]
internal interface IEnumShellItems
{
    [PreserveSig]
    int Next(uint celt, out FileOperationInterop.IShellItem? rgelt, out uint pceltFetched);

    [PreserveSig]
    int Skip(uint celt);

    [PreserveSig]
    int Reset();

    [PreserveSig]
    int Clone(out IEnumShellItems ppenum);
}
