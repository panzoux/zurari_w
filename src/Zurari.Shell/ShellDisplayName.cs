using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>
/// The name Explorer shows for a path.
/// </summary>
/// <remarks>
/// Drives are why this exists. A volume with no label has no name of its own, and showing the bare
/// letter tells the user nothing - Explorer substitutes a type name, so <c>C:\</c> reads as
/// "ローカル ディスク (C:)" and a USB stick as "USB ドライブ (D:)". That substitution is the shell's,
/// including its localization, and reproducing it by hand would mean maintaining a table of names in
/// one language that would still not agree with the machine beside it.
/// </remarks>
public static class ShellDisplayName
{
    /// <summary>SIGDN_NORMALDISPLAY - the name as Explorer shows it.</summary>
    private const uint SigdnNormalDisplay = 0;

    /// <summary>
    /// Explorer's name for <paramref name="path"/>, or <c>null</c> when the shell cannot name it -
    /// which for a drive means falling back to whatever the caller would have shown anyway.
    /// </summary>
    public static string? For(string path)
    {
        FileOperationInterop.IShellItem? item = null;
        try
        {
            if (FileOperationInterop.SHCreateItemFromParsingName(
                    path, IntPtr.Zero, FileOperationInterop.IidIShellItem, out item) != 0
                || item is null)
            {
                return null;
            }

            if (item.GetDisplayName(SigdnNormalDisplay, out var buffer) != 0 || buffer == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                return Marshal.PtrToStringUni(buffer);
            }
            finally
            {
                ShellContextMenuInterop.CoTaskMemFree(buffer);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        {
            return null;
        }
        finally
        {
            if (item is not null)
            {
                Marshal.ReleaseComObject(item);
            }
        }
    }
}
