namespace Zurari.Shell;

/// <summary>
/// Tells you when the set of drives changes - one plugged in or pulled out, a disc inserted or
/// ejected, a network share appearing or going away.
/// </summary>
/// <remarks>
/// <para>
/// <c>SHChangeNotifyRegister</c> rather than <c>WM_DEVICECHANGE</c>: it reports a superset of the
/// same events (media and network shares as well as volumes), it needs no device-interface
/// registration, and it is the shell's own view - so what it says has appeared is what the drive
/// pane is about to enumerate.
/// </para>
/// <para>
/// This watches the shell namespace for structural events; the contents of a directory are still
/// <c>Zurari.Runtime</c>'s <c>DirectoryWatcher</c>. The two answer different questions.
/// </para>
/// <para>
/// Registration is tied to a window handle and the notifications arrive as window messages, which is
/// why the App owns the hook and hands the message here rather than this type owning a window.
/// </para>
/// </remarks>
public sealed class ShellChangeWatcher : IDisposable
{
    /// <summary>The message the shell posts to us. In the WM_APP range, so it collides with nothing.</summary>
    public const int NotifyMessage = 0x0400 + 0x21;

    private readonly Action onDriveChanged;
    private uint registrationId;
    private IntPtr desktopPidl;

    /// <summary>
    /// Registers for shell change notifications delivered to <paramref name="hwnd"/>. Silently does
    /// nothing if the shell declines - live refresh is a convenience, and F5 still works.
    /// </summary>
    public ShellChangeWatcher(IntPtr hwnd, Action onDriveChanged)
    {
        ArgumentNullException.ThrowIfNull(onDriveChanged);
        this.onDriveChanged = onDriveChanged;

        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        try
        {
            // Rooted at the desktop and recursive: a drive event is reported against the drive
            // itself, which is not underneath any one folder we could name instead.
            if (NativeMethods.SHGetSpecialFolderLocation(IntPtr.Zero, NativeMethods.CSIDL_DESKTOP, out desktopPidl) != 0)
            {
                return;
            }

            var entry = new NativeMethods.SHChangeNotifyEntry { pidl = desktopPidl, fRecursive = true };
            registrationId = NativeMethods.SHChangeNotifyRegister(
                hwnd,
                NativeMethods.SHCNRF_ShellLevel | NativeMethods.SHCNRF_InterruptLevel
                    | NativeMethods.SHCNRF_RecursiveInterrupt | NativeMethods.SHCNRF_NewDelivery,
                NativeMethods.DriveEvents,
                NotifyMessage,
                1,
                ref entry);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            registrationId = 0;
        }
    }

    /// <summary>Whether the shell accepted the registration.</summary>
    public bool IsRegistered => registrationId != 0;

    /// <summary>
    /// Handles one window message. Returns <c>true</c> when it was a shell notification this watcher
    /// asked for, having already reported it.
    /// </summary>
    /// <remarks>
    /// <c>SHCNRF_NewDelivery</c> means a notification arrives as a shared-memory handle that has to
    /// be locked to be read and unlocked afterwards. The older form passed pointers valid only for
    /// the duration of the message handler, which cannot survive being marshalled onto a dispatcher.
    /// </remarks>
    public bool HandleMessage(int message, IntPtr wParam, IntPtr lParam)
    {
        if (message != NotifyMessage || registrationId == 0)
        {
            return false;
        }

        // lParam is the sending process id, which fits an int; the cast is narrowing only in theory.
        var locked = NativeMethods.SHChangeNotification_Lock(
            wParam, unchecked((int)lParam.ToInt64()), out _, out var eventId);
        if (locked == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            if ((eventId & NativeMethods.DriveEvents) != 0)
            {
                onDriveChanged();
            }
        }
        finally
        {
            NativeMethods.SHChangeNotification_Unlock(locked);
        }

        return true;
    }

    /// <summary>Deregisters and releases the desktop PIDL. Safe to call twice.</summary>
    public void Dispose()
    {
        if (registrationId != 0)
        {
            NativeMethods.SHChangeNotifyDeregister(registrationId);
            registrationId = 0;
        }

        if (desktopPidl != IntPtr.Zero)
        {
            ShellContextMenuInterop.CoTaskMemFree(desktopPidl);
            desktopPidl = IntPtr.Zero;
        }
    }
}
