using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>
/// Hand-written P/Invoke declarations for the small slice of the shell API this project
/// wraps (icon lookups). No source generator — kept close to the C++ reference implementation
/// at <c>win_ctxmenu/ctxmenu.cpp</c> so future Phase 4 tasks can extend it the same way.
/// </summary>
internal static class NativeMethods
{
    public const uint SHGFI_ICON = 0x000000100;
    public const uint SHGFI_SMALLICON = 0x000000001;
    public const uint SHGFI_USEFILEATTRIBUTES = 0x000000010;
    public const uint SHGFI_PIDL = 0x000000008;

    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;
    public const uint FILE_ATTRIBUTE_DIRECTORY = 0x00000010;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SHGetFileInfoW(
        string pszPath,
        uint dwFileAttributes,
        ref SHFILEINFOW psfi,
        uint cbFileInfo,
        uint uFlags);

    /// <summary>
    /// <see cref="SHGetFileInfoW"/> for an item named by PIDL rather than by path - the only way to
    /// ask about something in the shell namespace that has no path, such as the recycle bin.
    /// </summary>
    [DllImport("shell32.dll", EntryPoint = "SHGetFileInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SHGetFileInfoPidl(
        IntPtr pidl,
        uint dwFileAttributes,
        ref SHFILEINFOW psfi,
        uint cbFileInfo,
        uint uFlags);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyIcon(IntPtr hIcon);

    /// <summary>
    /// Resolves a known folder to a path. The only way to reach Downloads: unlike Desktop and
    /// Documents it has no <see cref="Environment.SpecialFolder"/> member, so the shell API is not
    /// a stylistic preference here but the only route.
    /// </summary>
    /// <remarks>
    /// The returned buffer is allocated by the shell and must be released with
    /// <see cref="CoTaskMemFree"/>.
    /// </remarks>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHGetKnownFolderPath(
        in Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);

    public const uint SEE_MASK_FLAG_NO_UI = 0x00000400;
    public const uint SEE_MASK_NOASYNC = 0x00000100;
    public const uint SEE_MASK_INVOKEIDLIST = 0x0000000C;
    public const int SW_HIDE = 0;

    /// <summary>Arguments for <see cref="ShellExecuteExW"/>.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct SHELLEXECUTEINFOW
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
        public int nShow;
        public IntPtr hInstApp;
        public IntPtr lpIDList;
        [MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
        public IntPtr hkeyClass;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr hProcess;
    }

    /// <summary>
    /// Invokes a registered verb on a file or drive - <c>mount</c> on a disc image, <c>Eject</c> on
    /// a drive. The same code path Explorer's own context menu takes, so it needs no elevation for
    /// anything Explorer can do unelevated.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShellExecuteExW(ref SHELLEXECUTEINFOW lpExecInfo);

    /// <summary>How much is in the recycle bin, without enumerating it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SHQUERYRBINFO
    {
        public int cbSize;
        public long i64Size;
        public long i64NumItems;
    }

    /// <summary>
    /// Totals for the recycle bin. <c>null</c> for the root path means every drive's bin at once,
    /// which is what the pane shows.
    /// </summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHQueryRecycleBinW(
        [MarshalAs(UnmanagedType.LPWStr)] string? pszRootPath, ref SHQUERYRBINFO pSHQueryRBInfo);

}
