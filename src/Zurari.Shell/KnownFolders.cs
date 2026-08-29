using System.Runtime.InteropServices;

namespace Zurari.Shell;

/// <summary>A place worth putting in the drive pane's favorites, and what to call it.</summary>
/// <param name="Path">Full path of the folder.</param>
/// <param name="Label">What the user reads.</param>
public sealed record KnownFolder(string Path, string Label);

/// <summary>
/// Resolves the handful of per-user folders the drive pane offers as favorites.
/// </summary>
/// <remarks>
/// <para>
/// All four go through <c>SHGetKnownFolderPath</c> rather than a mix of routes, because one of them
/// has no alternative: <b>there is no <c>Environment.SpecialFolder.Downloads</c></b>. Desktop,
/// Documents and the profile root all have members; Downloads does not, and never has. Resolving the
/// whole set the same way avoids a lookup that silently works for three folders and not the fourth.
/// </para>
/// <para>
/// Paths only. Whether a folder actually exists is a filesystem question, and this layer is barred
/// from touching the filesystem - Runtime filters the ones that are not there.
/// </para>
/// </remarks>
public static class KnownFolders
{
    // FOLDERID values from KnownFolders.h.
    private static readonly Guid Profile = new("5E6C858F-0E22-4760-9AFE-EA3317B67173");
    private static readonly Guid Desktop = new("B4BFCC3A-DB2C-424C-B029-7FE99A87C641");
    private static readonly Guid Documents = new("FDD39AD0-238F-46AF-ADB4-6C85480369C7");
    private static readonly Guid Downloads = new("374DE290-123F-4565-9164-39C4925E467B");

    /// <summary>
    /// The favorites, in the order they should appear. Folders that cannot be resolved are simply
    /// absent - a machine without one is not an error worth surfacing.
    /// </summary>
    public static IReadOnlyList<KnownFolder> UserPlaces()
    {
        var places = new List<KnownFolder>(4);
        Add(places, Profile, "ホーム");
        Add(places, Desktop, "デスクトップ");
        Add(places, Documents, "ドキュメント");
        Add(places, Downloads, "ダウンロード");
        return places;
    }

    private static void Add(List<KnownFolder> places, Guid folderId, string label)
    {
        if (TryResolve(folderId) is { } path)
        {
            places.Add(new KnownFolder(path, label));
        }
    }

    private static string? TryResolve(Guid folderId)
    {
        var buffer = IntPtr.Zero;
        try
        {
            // dwFlags 0: the folder's current path, without creating it.
            var hr = NativeMethods.SHGetKnownFolderPath(in folderId, 0, IntPtr.Zero, out buffer);
            if (hr != 0 || buffer == IntPtr.Zero)
            {
                return null;
            }

            var path = Marshal.PtrToStringUni(buffer);
            return string.IsNullOrEmpty(path) ? null : path;
        }
        catch (DllNotFoundException)
        {
            // Only reachable somewhere shell32 is absent; a missing favorite is not worth throwing for.
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                ShellContextMenuInterop.CoTaskMemFree(buffer);
            }
        }
    }
}
