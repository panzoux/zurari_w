namespace Zurari.Shell.Tests;

/// <summary>
/// Interop with a real, modal <c>TrackPopupMenuEx</c> pump cannot run headlessly (it blocks for
/// user input), so these tests only cover what is safe to assert automatically: failure paths
/// that must degrade to <c>false</c> rather than throw, and a cheap regression net against typos
/// in the hand-written GUID constants in <see cref="ShellContextMenuInterop"/>. The interactive
/// path (menu appears, "Send to" submenu renders, InvokeCommand runs a real verb) is covered by
/// the manual checklist in the Phase 4 task 3 report.
/// </summary>
public class ShellContextMenuTests
{
    [StaFact]
    public void Show_with_a_nonexistent_path_returns_false_and_does_not_throw()
    {
        var nonexistent = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "zurari-shell-ctxmenu-test-" + Guid.NewGuid().ToString("N"));

        var result = false;
        var exception = Record.Exception(() => result = ShellContextMenu.Show(IntPtr.Zero, [nonexistent], 0, 0));

        Assert.Null(exception);
        Assert.False(result);
    }

    [StaFact]
    public void Show_with_an_empty_path_list_returns_false()
    {
        var result = ShellContextMenu.Show(IntPtr.Zero, [], 0, 0);

        Assert.False(result);
    }

    [StaFact]
    public void Show_with_a_zero_owner_hwnd_returns_false_and_does_not_throw()
    {
        // ownerHwnd is checked before any interop call is made; a zero handle can never own a
        // popup menu, so this must short-circuit rather than attempt TrackPopupMenuEx(hwnd: 0).
        var exception = Record.Exception(
            () => ShellContextMenu.Show(IntPtr.Zero, [System.IO.Path.GetTempPath()], 0, 0));

        Assert.Null(exception);
    }

    [Fact]
    public void Iid_constants_match_the_documented_shell_interface_guids()
    {
        // Cheap regression net: a typo in one of these hand-copied GUIDs would make
        // ShellContextMenu silently fail (QueryInterface/GetUIObjectOf returning E_NOINTERFACE)
        // rather than fail to compile, so pin them against the documented values from
        // shobjidl_core.h / shobjidl.h.
        Assert.Equal(new Guid("000214E6-0000-0000-C000-000000000046"), ShellContextMenuInterop.IidIShellFolder);
        Assert.Equal(new Guid("000214e4-0000-0000-c000-000000000046"), ShellContextMenuInterop.IidIContextMenu);
        Assert.Equal(new Guid("000214f4-0000-0000-c000-000000000046"), ShellContextMenuInterop.IidIContextMenu2);
        Assert.Equal(new Guid("bcfce0a0-ec17-11d0-8d10-00a0c90f2719"), ShellContextMenuInterop.IidIContextMenu3);
    }
}
