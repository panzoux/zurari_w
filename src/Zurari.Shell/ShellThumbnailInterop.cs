using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>
/// The shell's thumbnail cache, declared from <c>thumbcache.h</c> (Windows SDK 10.0.26100.0).
/// </summary>
/// <remarks>
/// Every GUID, flag value and vtable order here was checked against that header rather than written
/// from memory. Three bare constants in this project were valid-but-wrong before this file existed
/// (a stock icon id one off, <c>SHCNRF_NewDelivery</c> given the value of RecursiveInterrupt), and
/// each failed silently - an interface with its methods in the wrong order fails worse.
/// </remarks>
internal static class ShellThumbnailInterop
{
    /// <summary>
    /// <c>WTS_EXTRACTDONOTCACHE</c>: extract, but do not write the result to any cache.
    /// </summary>
    /// <remarks>
    /// This is the flag the whole feature depends on. Without it, extracting a thumbnail for a file
    /// in a network folder writes a hidden <c>Thumbs.db</c> into that folder - which on Windows'
    /// default settings it does, and which then locks the folder against deletion. The result is
    /// cached by this app instead, under %LOCALAPPDATA%, where nothing else ever sees it.
    /// </remarks>
    public const int WTS_EXTRACTDONOTCACHE = 0x20;

    /// <summary><c>WTS_SCALETOREQUESTEDSIZE</c>: scale down to the size asked for.</summary>
    public const int WTS_SCALETOREQUESTEDSIZE = 0x40;

    [DllImport("gdi32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    /// <summary>CLSID_LocalThumbnailCache.</summary>
    [ComImport]
    [Guid("50EF4544-AC9F-4A8E-B21B-8A26180DB13F")]
    [ClassInterface(ClassInterfaceType.None)]
    public class LocalThumbnailCache
    {
    }

    /// <summary>IThumbnailCache - methods in header order.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("F676C15D-596A-4ce2-8234-33996F445DB1")]
    public interface IThumbnailCache
    {
        [PreserveSig]
        int GetThumbnail(
            FileOperationInterop.IShellItem pShellItem,
            uint cxyRequestedThumbSize,
            int flags,
            out ISharedBitmap? ppvThumb,
            out int pOutFlags,
            IntPtr pThumbnailID);

        [PreserveSig]
        int GetThumbnailByID(
            IntPtr thumbnailID,
            uint cxyRequestedThumbSize,
            out ISharedBitmap? ppvThumb,
            out int pOutFlags);
    }

    /// <summary>ISharedBitmap - methods in header order.</summary>
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("091162a4-bc96-411f-aae8-c5122cd03363")]
    public interface ISharedBitmap
    {
        [PreserveSig]
        int GetSharedBitmap(out IntPtr phbm);

        [PreserveSig]
        int GetSize(out Size pSize);

        [PreserveSig]
        int GetFormat(out int pat);

        [PreserveSig]
        int InitializeBitmap(IntPtr hbm, int wtsAT);

        /// <summary>Hands the bitmap over; the caller then owns it and must DeleteObject it.</summary>
        [PreserveSig]
        int Detach(out IntPtr phbm);
    }

    /// <summary>The Win32 SIZE structure.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Size
    {
        public int cx;
        public int cy;
    }
}
