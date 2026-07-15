using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Zurari.Shell;

/// <summary>
/// Shows the real Explorer context menu (including shell extensions) for a set of filesystem
/// paths and, if the user picks a command, invokes it. Ported from the working C++ example at
/// <c>win_ctxmenu/ctxmenu.cpp</c>: <c>IShellFolder.GetUIObjectOf</c> -&gt; <c>QueryContextMenu</c>
/// -&gt; <c>TrackPopupMenuEx</c> -&gt; <c>InvokeCommand</c>, with <c>IContextMenu2</c>/<c>3</c>
/// message forwarding while the popup is open so owner-drawn submenus (e.g. "Send to") render.
/// </summary>
/// <remarks>
/// <para>
/// Must be called on the UI (STA) thread that owns <paramref name="ownerHwnd"/> - see
/// <see cref="Show"/>. <c>TrackPopupMenuEx</c> pumps its own nested message loop while the popup
/// is open, which blocks the calling thread until the user picks a command or dismisses the menu;
/// that is normal Explorer/Finder behaviour, not a hang.
/// </para>
/// <para>
/// This type is deliberately not routed through <see cref="ShellEffectExecutor"/>: that executor
/// runs work on a background worker thread, but a context menu must be shown from the thread that
/// owns the window (so <c>TrackPopupMenuEx</c>'s nested pump can dispatch to WPF) and must be
/// synchronous from the caller's point of view (the caller needs to know whether to refresh
/// immediately after <see cref="Show"/> returns).
/// </para>
/// </remarks>
public static class ShellContextMenu
{
    private const uint IdCmdFirst = 1;
    private const uint IdCmdLast = 0x7FFF;

    /// <summary>
    /// Shows the shell context menu for <paramref name="paths"/> (all assumed to share the same
    /// parent folder - the caller guarantees this) at the given screen position, and invokes
    /// whichever command the user picks. Returns <c>false</c> - never throws - on any failure
    /// (invalid path, COM error, user dismissed the menu without picking anything).
    /// </summary>
    /// <param name="ownerHwnd">
    /// The window that owns the popup and receives forwarded menu messages. Must belong to the
    /// calling (UI/STA) thread.
    /// </param>
    /// <param name="paths">Absolute paths to show the menu for. Must contain at least one path.</param>
    /// <param name="screenX">Screen X coordinate (device pixels) to show the menu at.</param>
    /// <param name="screenY">Screen Y coordinate (device pixels) to show the menu at.</param>
    public static bool Show(IntPtr ownerHwnd, IReadOnlyList<string> paths, int screenX, int screenY)
    {
        ArgumentNullException.ThrowIfNull(paths);
        if (ownerHwnd == IntPtr.Zero || paths.Count == 0)
        {
            return false;
        }

        var fullPidls = new IntPtr[paths.Count];
        var childPidls = new IntPtr[paths.Count];
        ShellContextMenuInterop.IShellFolder? parentFolder = null;
        ShellContextMenuInterop.IContextMenu? contextMenu = null;
        ShellContextMenuInterop.IContextMenu2? contextMenu2 = null;
        ShellContextMenuInterop.IContextMenu3? contextMenu3 = null;
        var menu = IntPtr.Zero;
        var apidlBuffer = IntPtr.Zero;
        HwndSource? hookSource = null;
        HwndSourceHook? hook = null;

        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                var hr = ShellContextMenuInterop.SHParseDisplayName(paths[i], IntPtr.Zero, out fullPidls[i], 0, out _);
                if (hr < 0 || fullPidls[i] == IntPtr.Zero)
                {
                    return false;
                }
            }

            for (var i = 0; i < paths.Count; i++)
            {
                var hr = ShellContextMenuInterop.SHBindToParent(
                    fullPidls[i], ShellContextMenuInterop.IidIShellFolder, out var folder, out childPidls[i]);
                if (hr < 0 || folder is null || childPidls[i] == IntPtr.Zero)
                {
                    return false;
                }

                if (i == 0)
                {
                    parentFolder = folder;
                }
                else
                {
                    // All items are required (by contract) to share one parent; only the first
                    // folder RCW is kept for GetUIObjectOf, the rest are redundant handles to the
                    // same folder and are released immediately.
                    Marshal.ReleaseComObject(folder);
                }
            }

            if (parentFolder is null)
            {
                return false;
            }

            apidlBuffer = Marshal.AllocCoTaskMem(IntPtr.Size * childPidls.Length);
            for (var i = 0; i < childPidls.Length; i++)
            {
                Marshal.WriteIntPtr(apidlBuffer, i * IntPtr.Size, childPidls[i]);
            }

            var hrUi = parentFolder.GetUIObjectOf(
                ownerHwnd,
                (uint)childPidls.Length,
                apidlBuffer,
                ShellContextMenuInterop.IidIContextMenu,
                IntPtr.Zero,
                out contextMenu);
            if (hrUi < 0 || contextMenu is null)
            {
                return false;
            }

            contextMenu3 = contextMenu as ShellContextMenuInterop.IContextMenu3;
            if (contextMenu3 is null)
            {
                contextMenu2 = contextMenu as ShellContextMenuInterop.IContextMenu2;
            }

            menu = ShellContextMenuInterop.CreatePopupMenu();
            if (menu == IntPtr.Zero)
            {
                return false;
            }

            var hrQuery = contextMenu.QueryContextMenu(
                menu, indexMenu: 0, IdCmdFirst, IdCmdLast, ShellContextMenuInterop.CMF_NORMAL);
            if (hrQuery < 0)
            {
                return false;
            }

            hook = (IntPtr _, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
                ForwardMenuMessage(contextMenu3, contextMenu2, msg, wParam, lParam, ref handled);
            hookSource = HwndSource.FromHwnd(ownerHwnd);
            hookSource?.AddHook(hook);

            var cmd = ShellContextMenuInterop.TrackPopupMenuEx(
                menu,
                ShellContextMenuInterop.TPM_RETURNCMD | ShellContextMenuInterop.TPM_RIGHTBUTTON,
                screenX,
                screenY,
                ownerHwnd,
                IntPtr.Zero);

            if (cmd < (int)IdCmdFirst || cmd > (int)IdCmdLast)
            {
                // 0 (dismissed without a choice) or an out-of-range value: nothing to invoke.
                return false;
            }

            var info = new ShellContextMenuInterop.CMINVOKECOMMANDINFOEX
            {
                cbSize = Marshal.SizeOf<ShellContextMenuInterop.CMINVOKECOMMANDINFOEX>(),
                fMask = ShellContextMenuInterop.CMIC_MASK_UNICODE,
                hwnd = ownerHwnd,
                lpVerb = new IntPtr(cmd - (int)IdCmdFirst),
                lpVerbW = new IntPtr(cmd - (int)IdCmdFirst),
                nShow = ShellContextMenuInterop.SW_SHOWNORMAL,
                ptInvoke = new ShellContextMenuInterop.POINT { X = screenX, Y = screenY },
            };

            var hrInvoke = contextMenu.InvokeCommand(ref info);
            return hrInvoke >= 0;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (hookSource is not null && hook is not null)
            {
                hookSource.RemoveHook(hook);
            }

            if (menu != IntPtr.Zero)
            {
                ShellContextMenuInterop.DestroyMenu(menu);
            }

            if (apidlBuffer != IntPtr.Zero)
            {
                Marshal.FreeCoTaskMem(apidlBuffer);
            }

            // contextMenu2/contextMenu3 are QueryInterface views of the same COM identity as
            // contextMenu (the .NET RCW is keyed on that identity), so releasing contextMenu
            // once is sufficient - releasing the same RCW multiple times would throw.
            if (contextMenu is not null)
            {
                Marshal.ReleaseComObject(contextMenu);
            }

            if (parentFolder is not null)
            {
                Marshal.ReleaseComObject(parentFolder);
            }

            foreach (var pidl in fullPidls)
            {
                if (pidl != IntPtr.Zero)
                {
                    ShellContextMenuInterop.CoTaskMemFree(pidl);
                }
            }
        }
    }

    /// <summary>
    /// Forwards the small set of window messages an owner-drawn shell submenu needs
    /// (<c>WM_INITMENUPOPUP</c>/<c>WM_DRAWITEM</c>/<c>WM_MEASUREITEM</c>/<c>WM_MENUCHAR</c>) to
    /// <c>IContextMenu3.HandleMenuMsg2</c> (preferred, returns a result) or
    /// <c>IContextMenu2.HandleMenuMsg</c>. Every other message passes through untouched.
    /// </summary>
    private static IntPtr ForwardMenuMessage(
        ShellContextMenuInterop.IContextMenu3? contextMenu3,
        ShellContextMenuInterop.IContextMenu2? contextMenu2,
        int msg,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (msg != ShellContextMenuInterop.WM_INITMENUPOPUP
            && msg != ShellContextMenuInterop.WM_DRAWITEM
            && msg != ShellContextMenuInterop.WM_MEASUREITEM
            && msg != ShellContextMenuInterop.WM_MENUCHAR)
        {
            return IntPtr.Zero;
        }

        if (contextMenu3 is not null)
        {
            var hr = contextMenu3.HandleMenuMsg2((uint)msg, wParam, lParam, out var result);
            if (hr >= 0)
            {
                handled = true;
                return result;
            }

            return IntPtr.Zero;
        }

        if (contextMenu2 is not null)
        {
            var hr = contextMenu2.HandleMenuMsg((uint)msg, wParam, lParam);
            if (hr >= 0)
            {
                handled = true;
            }
        }

        return IntPtr.Zero;
    }
}
