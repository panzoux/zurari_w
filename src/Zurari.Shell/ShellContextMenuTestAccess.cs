namespace Zurari.Shell;

/// <summary>
/// Exposes the one shell call the context menu depends on, so a test can check that a row's name is
/// resolvable without needing a window to hang a menu from.
/// </summary>
/// <remarks>
/// <c>ShellContextMenu.Show</c> parses each path first and returns silently if any fails - which is
/// how right-clicking a recycle-bin row came to do nothing at all. Checking the parse alone covers
/// that failure without any UI.
/// </remarks>
public static class ShellContextMenuTestAccess
{
    /// <summary>Whether the shell can turn <paramref name="name"/> into an item.</summary>
    public static bool CanResolve(string name)
    {
        var hr = ShellContextMenuInterop.SHParseDisplayName(name, IntPtr.Zero, out var pidl, 0, out _);
        var resolved = hr >= 0 && pidl != IntPtr.Zero;
        if (pidl != IntPtr.Zero)
        {
            ShellContextMenuInterop.CoTaskMemFree(pidl);
        }

        return resolved;
    }
}
