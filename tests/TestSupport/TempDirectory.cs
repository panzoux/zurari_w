using System.IO;

namespace Zurari.TestSupport;

/// <summary>
/// Removing a test's temporary directory without failing the test when Windows is not ready to let
/// go of it.
/// </summary>
/// <remarks>
/// <para>
/// A test that has finished asserting has already passed or failed on its own merits. Cleanup
/// throwing afterwards turns a green test red for a reason that has nothing to do with what it was
/// checking - and on Windows that happens for reasons outside the test: an antivirus scanner reading
/// a file it just saw created, a watcher thread unwinding, a child process not yet reaped, an
/// indexer. The failure lands on whichever test happened to be running, which is the least useful
/// place for it to land.
/// </para>
/// <para>
/// So: retry briefly, then give up quietly. What is left behind is a uniquely named directory under
/// the system temp folder, which is exactly what that folder is for.
/// </para>
/// </remarks>
public static class TempDirectory
{
    /// <summary>How many times to try before leaving the directory to the temp folder.</summary>
    private const int Attempts = 5;

    /// <summary>Creates a uniquely named directory under the system temp folder.</summary>
    /// <param name="prefix">A short name so a leftover directory can be traced back to its test.</param>
    public static string Create(string prefix) =>
        Directory.CreateDirectory(
            Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"))).FullName;

    /// <summary>
    /// Deletes <paramref name="path"/> and everything under it, best effort. Never throws.
    /// </summary>
    public static void Delete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    return;
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A read-only file refuses to be deleted no matter how long one waits, so clear that
                // once before spending the remaining attempts on waiting.
                if (attempt == 0)
                {
                    ClearReadOnly(path);
                }

                Thread.Sleep(20 * (attempt + 1));
            }
        }
    }

    private static void ClearReadOnly(string path)
    {
        try
        {
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                var attributes = File.GetAttributes(file);
                if ((attributes & FileAttributes.ReadOnly) != 0)
                {
                    File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort throughout - the retry loop is what actually decides the outcome.
        }
    }
}
