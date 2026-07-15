using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>
/// Hand-written COM/P/Invoke interop for hosting the Explorer <c>IContextMenu</c> for a set of
/// filesystem paths (see <see cref="ShellContextMenu"/>). Ported from the working C++ example at
/// <c>win_ctxmenu/ctxmenu.cpp</c>; kept close to its shape so future maintenance can compare the
/// two side by side. As with <see cref="FileOperationInterop"/>, ComImport interfaces declare
/// members up to (and including) the last one this project actually calls, in their real vtable
/// order - earlier unused members are still declared (with best-effort signatures) purely to keep
/// later slots aligned.
/// </summary>
internal static class ShellContextMenuInterop
{
    /// <summary>IID_IShellFolder.</summary>
    public static readonly Guid IidIShellFolder = new("000214E6-0000-0000-C000-000000000046");

    /// <summary>IID_IContextMenu.</summary>
    public static readonly Guid IidIContextMenu = new("000214e4-0000-0000-c000-000000000046");

    /// <summary>IID_IContextMenu2.</summary>
    public static readonly Guid IidIContextMenu2 = new("000214f4-0000-0000-c000-000000000046");

    /// <summary>IID_IContextMenu3.</summary>
    public static readonly Guid IidIContextMenu3 = new("bcfce0a0-ec17-11d0-8d10-00a0c90f2719");

    public const uint CMF_NORMAL = 0x00000000;
    public const uint CMF_EXTENDEDVERBS = 0x00000100;

    public const uint TPM_RIGHTBUTTON = 0x0002;
    public const uint TPM_RETURNCMD = 0x0100;

    public const uint CMIC_MASK_UNICODE = 0x4000000;
    public const int SW_SHOWNORMAL = 1;

    public const int WM_INITMENUPOPUP = 0x0117;
    public const int WM_DRAWITEM = 0x002B;
    public const int WM_MEASUREITEM = 0x002C;
    public const int WM_MENUCHAR = 0x0120;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// <c>CMINVOKECOMMANDINFOEX</c> (shobjidl_core.h). The verb fields are declared as
    /// <see cref="IntPtr"/> rather than marshaled strings: invoking by id uses the classic
    /// <c>MAKEINTRESOURCE</c> trick of stuffing a small integer into a pointer-shaped field
    /// instead of a real string pointer, which .NET string marshaling cannot express.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct CMINVOKECOMMANDINFOEX
    {
        public int cbSize;
        public uint fMask;
        public IntPtr hwnd;
        public IntPtr lpVerb;
        public IntPtr lpParameters;
        public IntPtr lpDirectory;
        public int nShow;
        public uint dwHotKey;
        public IntPtr hIcon;
        public IntPtr lpTitle;
        public IntPtr lpVerbW;
        public IntPtr lpParametersW;
        public IntPtr lpDirectoryW;
        public IntPtr lpTitleW;
        public POINT ptInvoke;
    }

    /// <summary>
    /// <c>IShellFolder</c> (shobjidl_core.h). Only the members up to <see cref="GetUIObjectOf"/>
    /// (the sole one this project calls) are declared, in their real vtable order.
    /// </summary>
    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellFolder
    {
        [PreserveSig]
        int ParseDisplayName(
            IntPtr hwnd,
            IntPtr pbc,
            [MarshalAs(UnmanagedType.LPWStr)] string pszDisplayName,
            IntPtr pchEaten,
            out IntPtr ppidl,
            IntPtr pdwAttributes);

        [PreserveSig]
        int EnumObjects(IntPtr hwnd, uint grfFlags, out IntPtr ppenumIDList);

        [PreserveSig]
        int BindToObject(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int BindToStorage(IntPtr pidl, IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);

        [PreserveSig]
        int CreateViewObject(IntPtr hwndOwner, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetAttributesOf(uint cidl, IntPtr apidl, IntPtr rgfInOut);

        [PreserveSig]
        int GetUIObjectOf(
            IntPtr hwndOwner,
            uint cidl,
            IntPtr apidl,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            IntPtr rgfReserved,
            out IContextMenu ppv);
    }

    /// <summary><c>IContextMenu</c> (shobjidl_core.h) - all three members are used.</summary>
    [ComImport]
    [Guid("000214e4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);
    }

    /// <summary>
    /// <c>IContextMenu2</c> - adds <see cref="HandleMenuMsg"/> for owner-drawn submenu forwarding
    /// (e.g. "Send to"). Declared independently of <see cref="IContextMenu"/> (COM interop
    /// dispatches by vtable slot, not by inheritance), so it repeats the first three members.
    /// </summary>
    [ComImport]
    [Guid("000214f4-0000-0000-c000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu2
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);
    }

    /// <summary>
    /// <c>IContextMenu3</c> - adds <see cref="HandleMenuMsg2"/>, which additionally returns an
    /// <c>LRESULT</c> (needed for e.g. <c>WM_MENUCHAR</c>). Preferred over
    /// <see cref="IContextMenu2"/> when both are available.
    /// </summary>
    [ComImport]
    [Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IContextMenu3
    {
        [PreserveSig]
        int QueryContextMenu(IntPtr hmenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint uFlags);

        [PreserveSig]
        int InvokeCommand(ref CMINVOKECOMMANDINFOEX pici);

        [PreserveSig]
        int GetCommandString(IntPtr idCmd, uint uType, IntPtr pReserved, IntPtr pszName, uint cchMax);

        [PreserveSig]
        int HandleMenuMsg(uint uMsg, IntPtr wParam, IntPtr lParam);

        [PreserveSig]
        int HandleMenuMsg2(uint uMsg, IntPtr wParam, IntPtr lParam, out IntPtr plResult);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHParseDisplayName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszName,
        IntPtr pbc,
        out IntPtr ppidl,
        uint sfgaoIn,
        out uint psfgaoOut);

    [DllImport("shell32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHBindToParent(
        IntPtr pidl,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellFolder ppv,
        out IntPtr ppidlLast);

    [DllImport("ole32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern void CoTaskMemFree(IntPtr pv);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll", SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int TrackPopupMenuEx(
        IntPtr hmenu, uint fuFlags, int x, int y, IntPtr hwnd, IntPtr lptpm);
}
