using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>
/// Hand-written COM interop for the shell's <c>IFileOperation</c>, used to move items to the
/// recycle bin with the same undo-able semantics as Explorer's Delete. No source generator (see
/// <c>NativeMethods</c>): kept close to the Win32/COM reference material this project follows.
/// Only the members this project calls are declared, in their real vtable order — ComImport
/// dispatches by slot, so every interface below must list ALL members up to (and including) the
/// last one actually used, even ones this project never calls.
/// </summary>
internal static class FileOperationInterop
{
    /// <summary>CLSID_FileOperation — the coclass that implements <see cref="IFileOperation"/>.</summary>
    public static readonly Guid ClsidFileOperation = new("3ad05575-8857-4850-9277-11b85bdb8e09");

    public const uint FOF_ALLOWUNDO = 0x0040;
    public const uint FOF_NOCONFIRMATION = 0x0010;
    public const uint FOF_SILENT = 0x0004;
    public const uint FOF_NOERRORUI = 0x0400;

    [ComImport]
    [Guid("3ad05575-8857-4850-9277-11b85bdb8e09")]
    public class FileOperation
    {
    }

    /// <summary>
    /// <c>IFileOperation</c> (shobjidl.h). Members are declared up to <c>PerformOperations</c> and
    /// <c>GetAnyOperationsAborted</c> in their real vtable slots; earlier unused members are kept
    /// as placeholders so later slots line up.
    /// </summary>
    [ComImport]
    [Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IFileOperation
    {
        [PreserveSig]
        int Advise(IntPtr pfops, out uint pdwCookie); // IFileOperationProgressSink*, unused here

        [PreserveSig]
        int Unadvise(uint dwCookie);

        [PreserveSig]
        int SetOperationFlags(uint dwOperationFlags);

        [PreserveSig]
        int SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);

        [PreserveSig]
        int SetProgressDialog(IntPtr popd); // IOperationsProgressDialog*, unused here

        [PreserveSig]
        int SetProperties(IntPtr pproparray); // IPropertyChangeArray*, unused here

        [PreserveSig]
        int SetOwnerWindow(IntPtr hwndOwner);

        [PreserveSig]
        int ApplyPropertiesToItem(IShellItem psiItem);

        [PreserveSig]
        int ApplyPropertiesToItems(IntPtr punkItems);

        [PreserveSig]
        int RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IntPtr pfopsItem);

        [PreserveSig]
        int RenameItems(IntPtr pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);

        [PreserveSig]
        int MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszNewName, IntPtr pfopsItem);

        [PreserveSig]
        int MoveItems(IntPtr punkItems, IShellItem psiDestinationFolder);

        [PreserveSig]
        int CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string? pszCopyName, IntPtr pfopsItem);

        [PreserveSig]
        int CopyItems(IntPtr punkItems, IShellItem psiDestinationFolder);

        [PreserveSig]
        int DeleteItem(IShellItem psiItem, IntPtr pfopsItem);

        [PreserveSig]
        int DeleteItems(IntPtr punkItems);

        [PreserveSig]
        int NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string? pszTemplateName, IntPtr pfopsItem);

        [PreserveSig]
        int PerformOperations();

        [PreserveSig]
        int GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
    }

    [ComImport]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItem
    {
        [PreserveSig]
        int BindToHandler(IntPtr pbc, [MarshalAs(UnmanagedType.LPStruct)] Guid bhid, [MarshalAs(UnmanagedType.LPStruct)] Guid riid, out IntPtr ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(uint sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static extern int SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string pszPath,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        out IShellItem ppv);

    /// <summary>IID_IShellItem, used with <see cref="SHCreateItemFromParsingName"/>.</summary>
    public static readonly Guid IidIShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");
}
