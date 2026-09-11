using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// Where Explorer's thumbnail sits in the video preview (6c.5): before ffmpeg, cached here, and never
/// asked for a file whose bytes live on a share.
/// </summary>
public class ShellThumbnailPreviewTests
{
    /// <summary>A genuine 1x1 PNG, so the metadata parser has a real header to read.</summary>
    private static readonly byte[] OnePixelPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

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

    /// <summary>A file the preview treats as video by its extension, whatever its bytes are.</summary>
    private static string VideoFile(string dir)
    {
        var path = Path.Combine(dir, "clip.mp4");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70, 0x6D, 0x70, 0x34, 0x32]);
        return path;
    }

    [Fact]
    public void Explorers_thumbnail_is_used_when_it_has_one()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var video = VideoFile(dir);
            var asked = 0;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    Interlocked.Increment(ref asked);
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, video));
            var loaded = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(PreviewKind.Image, loaded.Kind);
            Assert.Equal(OnePixelPng, loaded.ImageBytes);
            Assert.Equal(1, asked);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The shell is told not to keep what it extracts - that is what keeps Thumbs.db out of a share -
    /// so this app keeps it instead, and the second look costs nothing.
    /// </summary>
    [Fact]
    public void A_shell_thumbnail_is_kept_so_the_shell_is_not_asked_twice()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var video = VideoFile(dir);
            var asked = 0;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    Interlocked.Increment(ref asked);
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, video));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
            runtime.Submit(new Effect.LoadPreview(2, video));
            var second = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(OnePixelPng, second.ImageBytes);
            Assert.Equal(1, asked);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>No thumbnail from the shell is not the end of it - ffmpeg still gets its turn.</summary>
    [Fact]
    public void When_the_shell_has_nothing_the_preview_carries_on_without_it()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var video = VideoFile(dir);
            var asked = 0;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    Interlocked.Increment(ref asked);
                    return null;
                });

            runtime.Submit(new Effect.LoadPreview(1, video));
            var loaded = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(60));

            Assert.Equal(1, asked);
            Assert.NotEqual(OnePixelPng, loaded.ImageBytes);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The third guard, end to end: a file whose handle resolves to a share is never offered to the
    /// shell at all, even though its path reached the Runtime unrefused. Uses the loopback share, a
    /// real trip through the network redirector; skipped where that share is not reachable.
    /// </summary>
    [Fact]
    public void A_video_on_a_share_is_never_offered_to_the_shell()
    {
        var dir = TempDirectory.Create("zurari-shellpreview-unc");
        try
        {
            var viaShare = @"\\localhost\" + dir[0] + "$" + dir[2..];
            if (!Directory.Exists(viaShare))
            {
                return; // no loopback share on this machine; nothing network-shaped to test against.
            }

            VideoFile(dir);
            var asked = 0;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) =>
                {
                    Interlocked.Increment(ref asked);
                    return OnePixelPng;
                });

            runtime.Submit(new Effect.LoadPreview(1, Path.Combine(viaShare, "clip.mp4")));
            var loaded = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(60));

            Assert.Equal(0, asked);
            Assert.NotEqual(OnePixelPng, loaded.ImageBytes);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Theory]
    [InlineData(@"\\?\C:\Users\someone\clip.mp4", false)]
    [InlineData(@"\\?\d:\clip.mp4", false)]
    [InlineData(@"\\?\UNC\server\share\clip.mp4", true)]
    [InlineData(@"\\server\share\clip.mp4", true)]
    [InlineData(@"C:\clip.mp4", true)]
    [InlineData("", true)]
    public void A_final_path_is_local_only_when_it_is_plainly_a_drive_letter(string finalPath, bool network)
    {
        // Anything not recognisably local counts as network. Wrong that way costs one thumbnail; wrong
        // the other way costs a hidden file in somebody's share.
        Assert.Equal(network, WorkerRuntime.IsNetworkFinalPath(finalPath));
    }

    [Fact]
    public void A_local_file_handle_is_not_on_the_network()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var file = Path.Combine(dir, "a.bin");
            File.WriteAllBytes(file, [1]);
            using var stream = new FileStream(file, FileMode.Open, FileAccess.Read);

            Assert.False(WorkerRuntime.IsOnNetwork(stream.SafeFileHandle));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// The guard looks at where the bytes are, not at the path it was given - which is the only way
    /// to see through a link or a mapped drive into a share.
    /// </summary>
    [Fact]
    public void A_handle_opened_through_a_share_is_on_the_network()
    {
        var dir = TempDirectory.Create("zurari-shellpreview-unc");
        try
        {
            var viaShare = @"\\localhost\" + dir[0] + "$" + dir[2..];
            if (!Directory.Exists(viaShare))
            {
                return; // no loopback share on this machine.
            }

            File.WriteAllBytes(Path.Combine(dir, "a.bin"), [1]);
            using var stream = new FileStream(Path.Combine(viaShare, "a.bin"), FileMode.Open, FileAccess.Read);

            Assert.True(WorkerRuntime.IsOnNetwork(stream.SafeFileHandle));
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }
}
