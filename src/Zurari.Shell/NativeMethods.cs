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
    public const int SW_SHOWNORMAL = 1;

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

    public const int CSIDL_DESKTOP = 0;

    public const int SHCNRF_InterruptLevel = 0x0001;
    public const int SHCNRF_ShellLevel = 0x0002;

    /// <summary>Watch beneath the registered folder, not just the folder itself.</summary>
    public const int SHCNRF_RecursiveInterrupt = 0x1000;

    /// <summary>
    /// Deliver notifications as a shared-memory handle to be opened with
    /// <see cref="SHChangeNotification_Lock"/>, rather than as raw pointers valid only inside the
    /// message handler.
    /// </summary>
    /// <remarks>
    /// 0x8000, not 0x1000 - that is <see cref="SHCNRF_RecursiveInterrupt"/>. Getting it wrong is
    /// silent: the registration succeeds, notifications arrive, and every one of them fails to lock
    /// because they are being delivered in the old form.
    /// </remarks>
    public const int SHCNRF_NewDelivery = 0x8000;

    public const int SHCNE_MEDIAINSERTED = 0x00000020;
    public const int SHCNE_MEDIAREMOVED = 0x00000040;
    public const int SHCNE_DRIVEREMOVED = 0x00000080;
    public const int SHCNE_DRIVEADD = 0x00000100;
    public const int SHCNE_NETSHARE = 0x00000200;
    public const int SHCNE_NETUNSHARE = 0x00000400;
    public const int SHCNE_DRIVEADDGUI = 0x00010000;

    public const uint SHCNF_PATHW = 0x0005;

    /// <summary>
    /// The shell events that change what the drive pane should show: a volume arriving or leaving, a
    /// disc going in or coming out, a network share appearing or disappearing.
    /// </summary>
    /// <remarks>
    /// Deliberately narrow. Registering for everything would wake the app on every file written
    /// anywhere on the machine, to re-read a listing that had not changed.
    /// </remarks>
    public const int DriveEvents =
        SHCNE_DRIVEADD | SHCNE_DRIVEREMOVED | SHCNE_MEDIAINSERTED | SHCNE_MEDIAREMOVED
        | SHCNE_NETSHARE | SHCNE_NETUNSHARE | SHCNE_DRIVEADDGUI;

    /// <summary>One place in the shell namespace to watch, plus whether to watch beneath it.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SHChangeNotifyEntry
    {
        public IntPtr pidl;

        [MarshalAs(UnmanagedType.Bool)]
        public bool fRecursive;
    }

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern uint SHChangeNotifyRegister(
        IntPtr hwnd, int fSources, int fEvents, int wMsg, int cEntries, ref SHChangeNotifyEntry pshcne);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SHChangeNotifyDeregister(uint ulId);

    /// <summary>
    /// Opens one delivered notification. The returned handle must be passed to
    /// <see cref="SHChangeNotification_Unlock"/>.
    /// </summary>
    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr SHChangeNotification_Lock(
        IntPtr hChange, int dwProcessId, out IntPtr ppidl, out int plEvent);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SHChangeNotification_Unlock(IntPtr hLock);

    /// <summary>Broadcasts a shell change - how a test delivers a real event to a real watcher.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern void SHChangeNotify(int wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHGetSpecialFolderLocation(IntPtr hwnd, int csidl, out IntPtr ppidl);

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
