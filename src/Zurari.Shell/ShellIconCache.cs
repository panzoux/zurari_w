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

    private readonly ConcurrentDictionary<string, ImageSource?> cache = new(StringComparer.Ordinal);

    /// <summary>
    /// Returns the icon for an entry of the given <paramref name="kind"/>. <paramref name="name"/>
    /// is only consulted for <see cref="EntryKind.File"/> (to derive the extension); it may be
    /// empty, extension-less, or contain characters that are not valid in a path — this never
    /// throws, it returns <c>null</c> on any failure instead.
    /// </summary>
    public ImageSource? GetIcon(EntryKind kind, string name)
    {
        var extension = kind == EntryKind.File ? SafeExtension(name) : string.Empty;
        var key = kind switch
        {
            EntryKind.Directory => DirectoryKey,
            EntryKind.Drive => DriveKey,
            _ => FileKeyPrefix + extension,
        };

        return cache.GetOrAdd(key, _ => Resolve(kind, extension));
    }

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
}
