using System.Diagnostics;

namespace Zurari.Runtime;

/// <summary>
/// Best-effort video thumbnail generation via <c>ffmpeg</c> found on <c>PATH</c>. <c>ffmpeg</c> is
/// used exclusively (not <c>ffmpegthumbnailer</c>) because its Windows CLI already handles Unicode
/// paths correctly (<c>GetCommandLineW</c>/<c>CommandLineToArgvW</c>), whereas ffmpegthumbnailer
/// has no well-maintained fork with the same Windows Unicode-path handling - keeping a second tool
/// around would mean carrying that gap ourselves for no real benefit. Used by
/// <see cref="WorkerRuntime.ExecuteLoadPreview"/> to turn a video file into a
/// <see cref="Zurari.Core.PreviewKind.Image"/> preview; when ffmpeg can't produce a frame, methods
/// here return a <see cref="ThumbnailOutcome"/> carrying a human-readable reason (rather than
/// silently returning <c>null</c>) so the caller can show *why* it fell back to the plain Binary
/// preview - never throws.
/// </summary>
internal static class VideoThumbnailer
{
    /// <summary>
    /// How often <see cref="RunProcess"/> re-checks whether ffmpeg has exited, instead of blocking
    /// for the whole per-attempt timeout in one <c>WaitForExit</c> call. A slow decode (large file,
    /// slow disk) keeps running across polls instead of being killed the instant one wait expires;
    /// the process is only killed once the *overall* timeout elapses.
    /// </summary>
    /// <remarks>
    /// This is granularity, not budget - the overall timeout is measured independently - so it is
    /// short. It bounds how long a cancelled thumbnail keeps ffmpeg alive after the cursor has
    /// moved on, and at four seconds that wait was longer than the interaction it was blocking.
    /// </remarks>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private static readonly Lazy<string?> FfmpegPath = new(() => FindOnPath("ffmpeg"));

    /// <summary>
    /// Tries to render a PNG thumbnail of <paramref name="videoPath"/> via ffmpeg, if it is on
    /// PATH. The whole call - both seek positions it may try - shares one <paramref name="timeout"/>,
    /// polled in <see cref="PollInterval"/> slices (see <see cref="RunProcess"/>) rather than a
    /// single hard wait. It used to be per seek position, so the real wait was twice the number.
    /// </summary>
    public static ThumbnailOutcome TryCreateThumbnail(string videoPath, TimeSpan timeout, CancellationToken token = default)
    {
        var ffmpeg = FfmpegPath.Value;
        if (ffmpeg is null)
        {
            return ThumbnailOutcome.Unavailable("ffmpeg が見つかりません(PATH未登録)");
        }

        var tmpPng = Path.Combine(Path.GetTempPath(), "zurari-thumb-" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            return TryFfmpeg(ffmpeg, videoPath, tmpPng, timeout, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Cancellation is not a failure to report - the caller no longer wants this thumbnail.
            // It propagates so ExecuteLoadPreview can stay silent rather than posting a
            // PreviewFailed that would race the result which superseded it.
            return ThumbnailOutcome.Unavailable($"予期しないエラー: {ex.GetType().Name}");
        }
        finally
        {
            TryDelete(tmpPng);
        }
    }

    private static ThumbnailOutcome TryFfmpeg(
        string tool, string videoPath, string tmpPng, TimeSpan timeout, CancellationToken token)
    {
        var clock = Stopwatch.StartNew();
        var atThreeSeconds = RunProcess(tool, BuildFfmpegArgs(videoPath, tmpPng, seekSeconds: 3), timeout, token);
        if (atThreeSeconds.Success && HasContent(tmpPng))
        {
            return ThumbnailOutcome.Success(File.ReadAllBytes(tmpPng));
        }

        // Out of time already: the budget is for the whole call, not per seek position.
        var remaining = timeout - clock.Elapsed;
        if (atThreeSeconds.TimedOut || remaining <= TimeSpan.Zero)
        {
            return ThumbnailOutcome.TimedOut(DescribeFailure("ffmpeg", atThreeSeconds with { TimedOut = true }));
        }

        // Very short clips can have nothing at 3s in - retry from the very first frame.
        var atFirstFrame = RunProcess(tool, BuildFfmpegArgs(videoPath, tmpPng, seekSeconds: 0), remaining, token);
        if (atFirstFrame.Success && HasContent(tmpPng))
        {
            return ThumbnailOutcome.Success(File.ReadAllBytes(tmpPng));
        }

        var detail = DescribeFailure("ffmpeg", atFirstFrame);

        // Only a verdict ffmpeg actually reached is about the file. A timeout may just mean a busy
        // machine, and a start failure means ffmpeg itself is the problem - neither should be
        // remembered against this file (see ThumbnailFailure). A timeout is also the one worth
        // offering to retry with longer.
        if (atFirstFrame.TimedOut)
        {
            return ThumbnailOutcome.TimedOut(detail);
        }

        return atFirstFrame.ExitCode is null
            ? ThumbnailOutcome.Unavailable(detail)
            : ThumbnailOutcome.FileRejected(detail);
    }

    private static string DescribeFailure(string tool, ProcessOutcome result)
    {
        if (result.TimedOut)
        {
            return $"{tool}: タイムアウト";
        }

        if (result.ExitCode is null)
        {
            return result.StartError is null
                ? $"{tool}: 起動できませんでした"
                : $"{tool}: 起動できませんでした ({Truncate(result.StartError)})";
        }

        if (result.ExitCode != 0)
        {
            return string.IsNullOrWhiteSpace(result.StdErr)
                ? $"{tool}: exit {result.ExitCode}"
                : $"{tool}: exit {result.ExitCode} - {Truncate(result.StdErr)}";
        }

        return $"{tool}: 出力ファイルが空でした";
    }

    private static string Truncate(string text)
    {
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return oneLine.Length > 120 ? oneLine[..120] + "…" : oneLine;
    }

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

    /// <summary>Result of running one external tool invocation to completion (or not).</summary>
    private readonly record struct ProcessOutcome(bool TimedOut, int? ExitCode, string? StdErr, string? StartError)
    {
        public bool Success => !TimedOut && ExitCode == 0;
    }

    /// <summary>
    /// Runs <paramref name="exe"/> and waits up to <paramref name="timeout"/> total, re-checking
    /// every <see cref="PollInterval"/> instead of a single blocking wait - so a process that is
    /// still alive and working keeps getting more time, in <see cref="PollInterval"/>-sized slices,
    /// until the overall budget is exhausted (then it is killed and reported as timed out).
    /// </summary>
    private static ProcessOutcome RunProcess(string exe, string arguments, TimeSpan timeout, CancellationToken token)
    {
        Process? process = null;
        try
        {
            process = new Process
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
            // CancellationToken.None on purpose: stderr is what DescribeFailure reports, and
            // cancelling these reads would discard the diagnostics for a process we are about to
            // kill anyway. The reads end on their own when the process exits or is killed.
            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            var started = Stopwatch.StartNew();
            while (!process.WaitForExit((int)PollInterval.TotalMilliseconds))
            {
                // Checked between slices rather than by waiting out the whole budget: the cursor
                // has moved off this file, so the frame is not wanted any more. Without this, a
                // superseded thumbnail kept ffmpeg running for the full timeout.
                if (token.IsCancellationRequested)
                {
                    TryKill(process);
                    throw new OperationCanceledException(token);
                }

                if (started.Elapsed >= timeout)
                {
                    TryKill(process);
                    return new ProcessOutcome(TimedOut: true, ExitCode: null, StdErr: null, StartError: null);
                }
            }

            var stdErr = stderrTask.IsCompletedSuccessfully ? stderrTask.Result : null;
            _ = stdoutTask;
            return new ProcessOutcome(TimedOut: false, ExitCode: process.ExitCode, StdErr: stdErr, StartError: null);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return new ProcessOutcome(TimedOut: false, ExitCode: null, StdErr: null, StartError: ex.Message);
        }
        finally
        {
            process?.Dispose();
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

/// <summary>
/// Result of <see cref="VideoThumbnailer.TryCreateThumbnail"/>: either the PNG bytes on success, or
/// a human-readable <see cref="FailureDetail"/> (which tool(s) were tried and why each failed) on
/// failure - shown directly in the Binary preview's label so the cause is visible without needing
/// to dig through logs.
/// </summary>
internal readonly record struct ThumbnailOutcome(
    byte[]? Bytes, string? FailureDetail, ThumbnailFailure Failure = ThumbnailFailure.None)
{
    public static ThumbnailOutcome Success(byte[] bytes) => new(bytes, null);

    /// <summary>ffmpeg ran and could not get a frame out of this particular file.</summary>
    public static ThumbnailOutcome FileRejected(string detail) => new(null, detail, ThumbnailFailure.FileRejected);

    /// <summary>Something about the environment stopped us, not this file.</summary>
    public static ThumbnailOutcome Unavailable(string detail) => new(null, detail, ThumbnailFailure.Unavailable);

    /// <summary>ffmpeg was still working when its time ran out - worth trying again with longer.</summary>
    public static ThumbnailOutcome TimedOut(string detail) => new(null, detail, ThumbnailFailure.TimedOut);
}

/// <summary>Why a thumbnail could not be produced - see <see cref="ThumbnailOutcome"/>.</summary>
/// <remarks>
/// The distinction exists for caching. A file ffmpeg rejects will be rejected again as long as the
/// file does not change, so that verdict is worth remembering; the alternative is paying the full
/// timeout on every cursor landing. Everything else - ffmpeg missing from PATH, failing to start,
/// or running out of time on a loaded machine - says nothing about the file and must not be
/// remembered, or installing ffmpeg (or simply trying again on an idle machine) would not help.
/// </remarks>
internal enum ThumbnailFailure
{
    /// <summary>No failure: a thumbnail was produced.</summary>
    None,

    /// <summary>ffmpeg ran to completion and could not decode a frame from this file.</summary>
    FileRejected,

    /// <summary>ffmpeg was missing or failed to start - not the file's fault.</summary>
    Unavailable,

    /// <summary>
    /// ffmpeg was still working when its time ran out. Not the file's fault either, and never
    /// cached - but unlike <see cref="Unavailable"/> a longer wait might succeed, so the preview
    /// offers to try again.
    /// </summary>
    TimedOut,
}
