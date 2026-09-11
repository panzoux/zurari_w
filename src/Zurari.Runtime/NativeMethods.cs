using System.Runtime.InteropServices;

namespace Zurari.Runtime;

/// <summary>
/// The one P/Invoke this layer needs. Kernel32 only: this is filesystem I/O, which is Runtime's
/// job, and nothing here touches the shell - that lives in <c>Zurari.Shell</c>, which Runtime is
/// forbidden to reference.
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// Free and total bytes for a directory, a drive root, or a UNC share.
    /// </summary>
    /// <remarks>
    /// Here because <c>DriveInfo</c> throws on a UNC path, so a pinned share's capacity has no
    /// managed route. It is what <c>DriveInfo</c> calls for a local volume anyway.
    /// </remarks>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetDiskFreeSpaceExW(
        string lpDirectoryName,
        out long lpFreeBytesAvailableToCaller,
        out long lpTotalNumberOfBytes,
        out long lpTotalNumberOfFreeBytes);

    /// <summary>
    /// The path a handle really refers to, after every link, junction, <c>subst</c> and mapped drive
    /// has been followed.
    /// </summary>
    /// <remarks>
    /// Here for the thumbnail guard: <c>C:\link\video.mp4</c> is a local-looking path whose bytes may
    /// live on a share, and only the handle knows. The preview has already opened that handle to read
    /// the file's head, so asking costs nothing.
    /// </remarks>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint GetFinalPathNameByHandleW(
        Microsoft.Win32.SafeHandles.SafeFileHandle hFile,
        [Out] char[] lpszFilePath,
        uint cchFilePath,
        uint dwFlags);
}
