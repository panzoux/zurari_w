using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// How long ffmpeg is given for each attempt at a video preview, and that running out of time is
/// reported as such, so the preview can offer to try again with a longer wait.
/// </summary>
public class VideoTimeoutTests
{
    private static T WaitFor<T>(ConcurrentQueue<Msg> queue, TimeSpan timeout)
        where T : Msg
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg) && msg is T match)
            {
                return match;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException($"No {typeof(T).Name} was posted within the timeout.");
    }

    private static string VideoFile(string dir)
    {
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32]);
        return path;
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 20)]
    [InlineData(3, 30)]
    public void Each_attempt_waits_longer(int attempt, int seconds)
    {
        Assert.Equal(TimeSpan.FromSeconds(seconds), WorkerRuntime.ThumbnailTimeoutFor(attempt));
    }

    [Fact]
    public void The_attempt_decides_how_long_ffmpeg_is_given()
    {
        var dir = TempDirectory.Create("zurari-videotimeout");
        try
        {
            var video = VideoFile(dir);
            var given = new ConcurrentQueue<TimeSpan>();
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")))
            {
                CreateVideoThumbnail = (_, timeout, _) =>
                {
                    given.Enqueue(timeout);
                    return ThumbnailOutcome.TimedOut("ffmpeg: タイムアウト");
                },
            };

            runtime.Submit(new Effect.LoadPreview(1, video, PreviewTarget.File, Attempt: 2));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal([TimeSpan.FromSeconds(20)], given);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void Running_out_of_time_is_reported_so_it_can_be_retried()
    {
        var dir = TempDirectory.Create("zurari-videotimeout");
        try
        {
            var video = VideoFile(dir);
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")))
            {
                CreateVideoThumbnail = (_, _, _) => ThumbnailOutcome.TimedOut("ffmpeg: タイムアウト"),
            };

            runtime.Submit(new Effect.LoadPreview(1, video));
            var loaded = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(PreviewKind.Binary, loaded.Kind);
            Assert.True(loaded.TimedOut);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>A file ffmpeg cannot decode will not decode on a longer wait, so it is not offered a retry.</summary>
    [Fact]
    public void A_rejected_file_is_not_reported_as_a_timeout()
    {
        var dir = TempDirectory.Create("zurari-videotimeout");
        try
        {
            var video = VideoFile(dir);
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")))
            {
                CreateVideoThumbnail = (_, _, _) => ThumbnailOutcome.FileRejected("ffmpeg: exit 1"),
            };

            runtime.Submit(new Effect.LoadPreview(1, video));
            var loaded = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.False(loaded.TimedOut);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
