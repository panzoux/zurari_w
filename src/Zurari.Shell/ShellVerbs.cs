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
    public static Win32Exception? Invoke(string verb, string path) => Invoke(verb, path, suppressUi: true);

    /// <summary>
    /// As <see cref="Invoke(string, string)"/>, with <paramref name="suppressUi"/> controlling
    /// whether the shell may put its own dialogs on screen.
    /// </summary>
    /// <remarks>
    /// Opening a file is the one case that wants them: a file with no association is supposed to
    /// raise "how do you want to open this?", and suppressing that turns a normal prompt into a
    /// keypress that appears to do nothing.
    /// </remarks>
    public static Win32Exception? Invoke(string verb, string path, bool suppressUi)
    {
        var info = new NativeMethods.SHELLEXECUTEINFOW
        {
            cbSize = Marshal.SizeOf<NativeMethods.SHELLEXECUTEINFOW>(),
            fMask = NativeMethods.SEE_MASK_NOASYNC | NativeMethods.SEE_MASK_INVOKEIDLIST
                | (suppressUi ? NativeMethods.SEE_MASK_FLAG_NO_UI : 0),
            lpVerb = verb,
            lpFile = path,
            nShow = suppressUi ? NativeMethods.SW_HIDE : NativeMethods.SW_SHOWNORMAL,
        };

        return NativeMethods.ShellExecuteExW(ref info) ? null : new Win32Exception(Marshal.GetLastWin32Error());
    }
}
