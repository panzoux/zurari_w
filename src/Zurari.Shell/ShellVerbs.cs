using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>
/// Invoking a registered shell verb on a path - the same mechanism as picking the entry from
/// Explorer's context menu, and so subject to the same permissions rather than needing more.
/// </summary>
/// <remarks>
/// Mounting a disc image and ejecting a drive are both verbs rather than APIs of their own.
/// <c>Windows.IsoFile</c> registers <c>mount</c> for .iso, and every removable drive registers
/// <c>Eject</c>; neither needs elevation, which is exactly why this route was chosen over
/// <c>AttachVirtualDisk</c> (administrator) and raw <c>IOCTL_STORAGE_EJECT_MEDIA</c> (a volume
/// handle, and no shell notification).
/// </remarks>
internal static class ShellVerbs
{
    /// <summary>
    /// Runs <paramref name="verb"/> on <paramref name="path"/>. Returns the Win32 error when the
    /// shell refuses, or <c>null</c> when it accepted.
    /// </summary>
    /// <remarks>
    /// <c>SEE_MASK_NOASYNC</c> because this runs on the executor's own STA thread, which does not
    /// outlive the call the way a message-pumping window does; without it the shell may return
    /// before it has finished with the arguments. <c>SEE_MASK_FLAG_NO_UI</c> suppresses the shell's
    /// own error box - the refusal comes back here instead, so it can be said in the status bar.
    /// </remarks>
    public static Win32Exception? Invoke(string verb, string path)
    {
        var info = new NativeMethods.SHELLEXECUTEINFOW
        {
            cbSize = Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFOW>(),
            fMask = NativeMethods.SEE_MASK_FLAG_NO_UI | NativeMethods.SEE_MASK_NOASYNC
                | NativeMethods.SEE_MASK_INVOKEIDLIST,
            lpVerb = verb,
            lpFile = path,
            nShow = NativeMethods.SW_HIDE,
        };

        return NativeMethods.ShellExecuteExW(ref info) ? null : new Win32Exception(Marshal.GetLastWin32Error());
    }
}
