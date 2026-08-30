using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Zurari.Core;

namespace Zurari.Shell;

/// <summary>
/// Resolves small (16x16) shell icons for entries shown in a column. Lookups never touch the
/// disk (<c>SHGFI_USEFILEATTRIBUTES</c>) and are cached by a coarse key — directory, drive, or
/// file extension — since the shell's icon for those is identical regardless of the concrete
/// path. Every failure mode (missing file, invalid name, COM error) degrades to <c>null</c>:
/// an icon is cosmetic, never load-bearing, so this type never throws.
/// </summary>
public sealed class ShellIconCache
{
    private const string DirectoryKey = "dir";
    private const string DriveKey = "drive";
    private const string FileKeyPrefix = "file:";
    private const string NamespaceKeyPrefix = "shell:";

    private readonly ConcurrentDictionary<string, ImageSource?> cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the icon for an entry of the given <paramref name="kind"/>. <paramref name="name"/>
    /// is only consulted for <see cref="EntryKind.File"/> (to derive the extension); it may be
    /// empty, extension-less, or contain characters that are not valid in a path — this never
    /// throws, it returns <c>null</c> on any failure instead.
    /// </summary>
    public ImageSource? GetIcon(EntryKind kind, string name)
    {
        // A drive resolves from its own path rather than from attributes. Asking for "a directory"
        // returns the generic folder icon for every drive alike - which is what the pane showed:
        // C:, D: and an optical drive all as folders. The shell has a distinct icon per drive type
        // (and per volume, for one with its own icon), and only the real path gets it.
        if (kind == EntryKind.Drive)
        {
            return string.IsNullOrEmpty(name)
                ? cache.GetOrAdd(DriveKey, _ => Resolve(kind, string.Empty))
                : cache.GetOrAdd(DriveKey + name, _ => ResolveForPath(name));
        }

        var extension = kind == EntryKind.File ? SafeExtension(name) : string.Empty;
        var key = kind switch
        {
            EntryKind.Directory => DirectoryKey,
            _ => FileKeyPrefix + extension,
        };

        return cache.GetOrAdd(key, _ => Resolve(kind, extension));
    }

    /// <summary>
    /// The icon the shell draws for a namespace item that has no filesystem path - the recycle bin
    /// being the one this exists for. <paramref name="parsingName"/> is the shell's own name for it
    /// (see <c>Location.ShellParsingName</c>).
    /// </summary>
    /// <param name="parsingName">What the shell calls the item, e.g. the recycle bin's CLSID.</param>
    /// <param name="variant">
    /// Anything about the item that changes its icon without changing its name - for the bin, whether
    /// it currently holds anything. Part of the cache key and nothing else: the icon itself always
    /// comes from asking the shell about the real item, so it is whatever Explorer is showing. Pass a
    /// value that changes when the icon should be looked up again.
    /// </param>
    /// <remarks>
    /// Asking about the item beats picking a stock icon that looks similar. The stock set is
    /// addressed by bare integers, and being one off in that list is silent - it hands back a
    /// perfectly valid icon of something else entirely, which is exactly what happened here (the bin
    /// rendered as a folder with a badge on it). This route cannot be off by one, and it follows the
    /// user's theme and icon-pack choices for free.
    /// </remarks>
    public ImageSource? GetShellNamespaceIcon(string parsingName, string variant) =>
        cache.GetOrAdd(
            NamespaceKeyPrefix + parsingName + "#" + variant,
            _ => ResolveForShellName(parsingName));

    private static string SafeExtension(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return string.Empty;
        }

        try
        {
            // CA1308 prefers ToUpperInvariant for normalization; case doesn't matter here since
            // it's only used as a cache key and in a probe path SHGetFileInfoW matches
            // case-insensitively.
            return Path.GetExtension(name).ToUpperInvariant();
        }
        catch (ArgumentException)
        {
            return string.Empty;
        }
    }

    private static BitmapSource? Resolve(EntryKind kind, string extension)
    {
        try
        {
            var isDirectoryLike = kind is EntryKind.Directory or EntryKind.Drive;
            var attributes = isDirectoryLike ? NativeMethods.FILE_ATTRIBUTE_DIRECTORY : NativeMethods.FILE_ATTRIBUTE_NORMAL;
            // SHGFI_USEFILEATTRIBUTES means the shell resolves the icon from the attributes/name
            // alone and never touches disk, so a placeholder probe path is fine here.
            var probePath = isDirectoryLike ? "dummy" : "dummy" + extension;

            var info = default(NativeMethods.SHFILEINFOW);
            var handle = NativeMethods.SHGetFileInfoW(
                probePath,
                attributes,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFOW>(),
                NativeMethods.SHGFI_USEFILEATTRIBUTES | NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

            if (handle == IntPtr.Zero || info.hIcon == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var source = Imaging.CreateBitmapSourceFromHIcon(
                    info.hIcon,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                source.Freeze();
                return source;
            }
            finally
            {
                NativeMethods.DestroyIcon(info.hIcon);
            }
        }
        catch (Exception)
        {
            // Icons are cosmetic, never load-bearing: any interop failure (invalid probe path,
            // COM error, out-of-resources) degrades to "no icon" rather than propagating.
            return null;
        }
    }

    /// <summary>
    /// The icon the shell shows for a real path, looked up from the item itself rather than from
    /// assumed attributes. Used for drives, where the type of volume is the whole point.
    /// </summary>
    private static BitmapSource? ResolveForPath(string path)
    {
        try
        {
            var info = default(NativeMethods.SHFILEINFOW);
            var handle = NativeMethods.SHGetFileInfoW(
                path,
                0,
                ref info,
                (uint)Marshal.SizeOf<NativeMethods.SHFILEINFOW>(),
                NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

            return handle == IntPtr.Zero ? null : FromHIcon(info.hIcon);
        }
        catch (Exception)
        {
            // A drive that went away between listing and drawing is not worth an error.
            return null;
        }
    }

    /// <summary>
    /// Looks up the real shell item and takes its icon. Returns <c>null</c> if the name does not
    /// resolve - an icon is cosmetic, so nothing here is worth an exception.
    /// </summary>
    private static BitmapSource? ResolveForShellName(string parsingName)
    {
        try
        {
            var hr = ShellContextMenuInterop.SHParseDisplayName(parsingName, IntPtr.Zero, out var pidl, 0, out _);
            if (hr != 0 || pidl == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var info = default(NativeMethods.SHFILEINFOW);
                var handle = NativeMethods.SHGetFileInfoPidl(
                    pidl,
                    0,
                    ref info,
                    (uint)Marshal.SizeOf<NativeMethods.SHFILEINFOW>(),
                    NativeMethods.SHGFI_PIDL | NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);
                return handle == IntPtr.Zero ? null : FromHIcon(info.hIcon);
            }
            finally
            {
                ShellContextMenuInterop.CoTaskMemFree(pidl);
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Converts a shell icon handle into a frozen bitmap and releases the handle.</summary>
    private static BitmapSource? FromHIcon(IntPtr hIcon)
    {
        if (hIcon == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var source = Imaging.CreateBitmapSourceFromHIcon(
                hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            NativeMethods.DestroyIcon(hIcon);
        }
    }
}
