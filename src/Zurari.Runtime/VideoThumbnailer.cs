using System.Diagnostics;

namespace Zurari.Runtime;

/// <summary>
/// Best-effort video thumbnail generation via an external tool (<c>ffmpegthumbnailer</c>,
/// preferred, or <c>ffmpeg</c>) found on <c>PATH</c>. Used by
/// <see cref="WorkerRuntime.ExecuteLoadPreview"/> to turn a video file into a
/// <see cref="Zurari.Core.PreviewKind.Image"/> preview; when neither tool is installed (or the
/// tool fails on this particular file), every method here returns <c>null</c> so the caller can
/// fall back to the plain Binary preview - never throws.
/// </summary>
internal static class VideoThumbnailer
{
    private static readonly Lazy<string?> ToolPath = new(ProbeForTool);

    /// <summary>
    /// Tries to render a PNG thumbnail of <paramref name="videoPath"/>, waiting up to
    /// <paramref name="timeout"/> for the external tool to finish (killed and treated as failure
    /// if it does not). Returns the PNG bytes on success, or <c>null</c> if no tool is installed,
    /// the tool fails, times out, or the file cannot be read - any exception is swallowed.
    /// </summary>
    public static byte[]? TryCreateThumbnail(string videoPath, TimeSpan timeout)
    {
        var tool = ToolPath.Value;
        if (tool is null)
        {
            return null;
        }

        var tmpPng = Path.Combine(Path.GetTempPath(), "zurari-thumb-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            var isFfmpegThumbnailer = IsFfmpegThumbnailer(tool);

            if (isFfmpegThumbnailer)
            {
                if (!RunProcess(tool, BuildFfmpegThumbnailerArgs(videoPath, tmpPng), timeout) || !HasContent(tmpPng))
                {
                    return null;
                }
            }
            else
            {
                if (!RunProcess(tool, BuildFfmpegArgs(videoPath, tmpPng, seekSeconds: 3), timeout) || !HasContent(tmpPng))
                {
                    // Very short clips can have nothing at 3s in - retry from the very first frame.
                    if (!RunProcess(tool, BuildFfmpegArgs(videoPath, tmpPng, seekSeconds: 0), timeout) || !HasContent(tmpPng))
                    {
                        return null;
                    }
                }
            }

            return File.ReadAllBytes(tmpPng);
        }
        catch
        {
            return null;
        }
        finally
        {
            TryDelete(tmpPng);
        }
    }

    private static bool IsFfmpegThumbnailer(string tool) =>
        Path.GetFileNameWithoutExtension(tool).Equals("ffmpegthumbnailer", StringComparison.OrdinalIgnoreCase);

    private static string BuildFfmpegThumbnailerArgs(string videoPath, string outPng) =>
        $"-i \"{videoPath}\" -o \"{outPng}\" -s 512 -q 8";

    private static string BuildFfmpegArgs(string videoPath, string outPng, int seekSeconds) =>
        $"-ss {seekSeconds} -i \"{videoPath}\" -frames:v 1 -vf scale=512:-1 -y \"{outPng}\"";

    private static bool HasContent(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Best effort - a leftover temp file is harmless.
        }
        catch (UnauthorizedAccessException)
        {
            // Same as above.
        }
    }

    private static bool RunProcess(string exe, string arguments, TimeSpan timeout)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                },
            };

            process.Start();
            // Drain output so the process cannot block on a full pipe buffer; content is unused.
            _ = process.StandardOutput.ReadToEndAsync();
            _ = process.StandardError.ReadToEndAsync();

            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // Already exited between the timeout check and here - fine.
        }
    }

    /// <summary>Looks for ffmpegthumbnailer first (purpose-built, faster), then ffmpeg, on PATH.</summary>
    private static string? ProbeForTool() => FindOnPath("ffmpegthumbnailer") ?? FindOnPath("ffmpeg");

    /// <summary>
    /// Resolves <paramref name="name"/> to a full path via <c>where.exe</c> (Windows-only, matching
    /// this app), taking the first match. Returns <c>null</c> if the tool is not found or
    /// <c>where.exe</c> itself is unavailable - never throws.
    /// </summary>
    private static string? FindOnPath(string name)
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
            var output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                TryKill(process);
                return null;
            }

            if (process.ExitCode != 0)
            {
                return null;
            }

            var firstLine = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();
            return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return null;
        }
    }
}
