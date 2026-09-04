using System.Diagnostics;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// <see cref="VideoThumbnailer"/> shells out to an external tool that may or may not be installed
/// on the machine running the tests - these assertions are deliberately environment-tolerant (see
/// each test's remarks) rather than asserting a specific tool is present.
/// </summary>
public class VideoThumbnailerTests
{
    private static string CreateTempDir() => TempDirectory.Create("zurari-thumb-tests");

    /// <summary>
    /// A garbage ".mp4" (not a real video) must never throw, regardless of whether ffmpeg is
    /// installed: either the tool is absent (failure immediately) or the tool runs and fails to
    /// decode the garbage (also failure, now with a reason attached). Either way, only "no
    /// exception, and a well-formed outcome" is guaranteed.
    /// </summary>
    [Fact]
    public void TryCreateThumbnail_on_a_garbage_file_never_throws()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "not-a-video.mp4");
            File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);

            ThumbnailOutcome outcome = default;
            var exception = Record.Exception(() => outcome = VideoThumbnailer.TryCreateThumbnail(path, TimeSpan.FromSeconds(4)));

            Assert.Null(exception);
            if (outcome.Bytes is null)
            {
                Assert.False(string.IsNullOrWhiteSpace(outcome.FailureDetail));
            }
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// When ffmpeg is not on PATH (checked independently of <see cref="VideoThumbnailer"/>, which
    /// caches its own probe), the result for any input - including a nonexistent path - must be a
    /// failure explaining that no tool was found. Skipped (passes trivially) when ffmpeg is
    /// actually installed on this machine, since that is a different code path covered by the
    /// "garbage file never throws" test above.
    /// </summary>
    [Fact]
    public void TryCreateThumbnail_fails_fast_with_a_reason_when_no_tool_is_installed()
    {
        if (IsToolOnPath("ffmpeg"))
        {
            // Tool is present on this machine - this test's premise does not hold here; the
            // "never throws" test above already covers the tool-present path.
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "zurari-does-not-exist-" + Guid.NewGuid() + ".mp4");

        var outcome = VideoThumbnailer.TryCreateThumbnail(path, TimeSpan.FromSeconds(4));

        Assert.Null(outcome.Bytes);
        Assert.False(string.IsNullOrWhiteSpace(outcome.FailureDetail));
    }

    private static bool IsToolOnPath(string name)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "where.exe",
                    Arguments = name,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };
            process.Start();
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit(3000);
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
