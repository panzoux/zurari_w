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
    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "zurari-thumb-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    /// <summary>
    /// A garbage ".mp4" (not a real video) must never throw, regardless of whether ffmpeg /
    /// ffmpegthumbnailer are installed: either the tool is absent (null immediately) or the tool
    /// runs and fails to decode the garbage (also null). Either way, only "no exception, and a
    /// null-or-bytes result" is guaranteed.
    /// </summary>
    [Fact]
    public void TryCreateThumbnail_on_a_garbage_file_never_throws()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "not-a-video.mp4");
            File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x03, 0x04]);

            var exception = Record.Exception(() => VideoThumbnailer.TryCreateThumbnail(path, TimeSpan.FromSeconds(4)));

            Assert.Null(exception);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// When neither ffmpegthumbnailer nor ffmpeg is on PATH (checked independently of
    /// <see cref="VideoThumbnailer"/>, which caches its own probe), the result for any input -
    /// including a nonexistent path - must be null. Skipped (passes trivially) when a tool is
    /// actually installed on this machine, since that is a different code path covered by the
    /// "garbage file never throws" test above.
    /// </summary>
    [Fact]
    public void TryCreateThumbnail_returns_null_fast_when_no_tool_is_installed()
    {
        if (IsToolOnPath("ffmpegthumbnailer") || IsToolOnPath("ffmpeg"))
        {
            // Tool is present on this machine - this test's premise does not hold here; the
            // "never throws" test above already covers the tool-present path.
            return;
        }

        var path = Path.Combine(Path.GetTempPath(), "zurari-does-not-exist-" + Guid.NewGuid() + ".mp4");

        var result = VideoThumbnailer.TryCreateThumbnail(path, TimeSpan.FromSeconds(4));

        Assert.Null(result);
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
