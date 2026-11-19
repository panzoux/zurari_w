using System.Runtime.InteropServices;
using System.Text;

namespace Zurari.Runtime.Migemo;

/// <summary>
/// The user's own cmigemo (<c>migemo.dll</c> plus a <c>migemo-dict</c>), when one is installed. Preferred
/// over <see cref="BuiltinMigemo"/> so a user who keeps a larger or personal SKK dictionary goes on
/// using it - and so we never have to ship a GPL dictionary ourselves.
/// </summary>
/// <remarks>
/// Bound through <see cref="NativeLibrary"/> and function pointers rather than <c>DllImport</c>: the
/// DLL is found by searching folders, and must be loaded from the folder its dictionary was found in,
/// not wherever the loader's search order would pick some other <c>migemo.dll</c> up. Every call is
/// serialized; cmigemo's handle is not documented as thread-safe.
/// </remarks>
internal sealed class CMigemo : IDisposable
{
    private readonly object gate = new();
    private readonly IntPtr library;
    private readonly QueryFn query;
    private readonly ReleaseFn release;
    private readonly CloseFn close;
    private IntPtr handle;

    private CMigemo(IntPtr library, IntPtr handle, QueryFn query, ReleaseFn release, CloseFn close)
    {
        this.library = library;
        this.handle = handle;
        this.query = query;
        this.release = release;
        this.close = close;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr OpenFn(IntPtr dictPath);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr QueryFn(IntPtr migemo, IntPtr word);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ReleaseFn(IntPtr migemo, IntPtr result);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseFn(IntPtr migemo);

    /// <summary>
    /// The folders searched, in order, for <c>migemo.dll</c> with a UTF-8 dictionary beside it: next to the
    /// exe, the app's settings folder, then zrr's publish folder (where this user's cmigemo already lives).
    /// </summary>
    internal static IReadOnlyList<string> CandidateFolders()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return
        [
            AppContext.BaseDirectory,
            Path.Combine(appData, "zurari_w"),
            Path.Combine(profile, "source", "repos", "panzoux", "zurari", "publish"),
        ];
    }

    /// <summary>
    /// Loads the first cmigemo found in <paramref name="folders"/>, or <c>null</c> when there is none or
    /// it will not load (a 32-bit DLL, a dictionary it rejects).
    /// </summary>
    public static CMigemo? TryLoad(IEnumerable<string> folders)
    {
        ArgumentNullException.ThrowIfNull(folders);
        foreach (var folder in folders)
        {
            var dll = Path.Combine(folder, "migemo.dll");
            var dict = FindDictionary(folder);
            if (dict is null || !File.Exists(dll) || !NativeLibrary.TryLoad(dll, out var library))
            {
                continue;
            }

            var loaded = TryOpen(library, dict);
            if (loaded is not null)
            {
                return loaded;
            }

            NativeLibrary.Free(library);
        }

        return null;
    }

    private static string? FindDictionary(string folder)
    {
        foreach (var candidate in new[] { Path.Combine(folder, "dict", "utf-8", "migemo-dict"), Path.Combine(folder, "dict", "migemo-dict") })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static CMigemo? TryOpen(IntPtr library, string dict)
    {
        if (!NativeLibrary.TryGetExport(library, "migemo_open", out var openPtr)
            || !NativeLibrary.TryGetExport(library, "migemo_query", out var queryPtr)
            || !NativeLibrary.TryGetExport(library, "migemo_release", out var releasePtr)
            || !NativeLibrary.TryGetExport(library, "migemo_close", out var closePtr))
        {
            return null;
        }

        var open = Marshal.GetDelegateForFunctionPointer<OpenFn>(openPtr);
        var close = Marshal.GetDelegateForFunctionPointer<CloseFn>(closePtr);

        // cmigemo opens the dictionary with fopen, which takes a path in the ANSI code page.
        var path = Marshal.StringToHGlobalAnsi(dict);
        IntPtr handle;
        try
        {
            handle = open(path);
        }
        finally
        {
            Marshal.FreeHGlobal(path);
        }

        if (handle == IntPtr.Zero)
        {
            return null;
        }

        return new CMigemo(
            library,
            handle,
            Marshal.GetDelegateForFunctionPointer<QueryFn>(queryPtr),
            Marshal.GetDelegateForFunctionPointer<ReleaseFn>(releasePtr),
            close);
    }

    /// <summary>The pattern for <paramref name="word"/>, in cmigemo's own syntax, or <c>null</c>.</summary>
    public string? Query(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var bytes = Encoding.UTF8.GetBytes(word + '\0');
        var native = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, native, bytes.Length);
            lock (gate)
            {
                if (handle == IntPtr.Zero)
                {
                    return null;
                }

                var result = query(handle, native);
                if (result == IntPtr.Zero)
                {
                    return null;
                }

                try
                {
                    return Marshal.PtrToStringUTF8(result);
                }
                finally
                {
                    release(handle, result);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(native);
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (handle == IntPtr.Zero)
            {
                return;
            }

            close(handle);
            handle = IntPtr.Zero;
        }

        NativeLibrary.Free(library);
    }
}
