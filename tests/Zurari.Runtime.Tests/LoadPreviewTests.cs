using System.Collections.Concurrent;
using System.Text;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

public class LoadPreviewTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string CreateTempDir()
    {
        var path = Path.Combine(Path.GetTempPath(), "zurari-preview-tests-" + Guid.NewGuid());
        Directory.CreateDirectory(path);
        return path;
    }

    private static Msg WaitForMsg(ConcurrentQueue<Msg> queue, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (queue.TryDequeue(out var msg))
            {
                return msg;
            }

            Thread.Sleep(10);
        }

        throw new TimeoutException("No message was posted within the timeout.");
    }

    [Fact]
    public void LoadPreview_of_a_PNG_file_reports_Image_with_the_whole_file_bytes()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "pic.png");
            byte[] content = [.. PngSignature, 0x01, 0x02, 0x03, 0x04];
            File.WriteAllBytes(path, content);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(7, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(7, loaded.Generation);
            Assert.Equal(PreviewKind.Image, loaded.Kind);
            Assert.Equal(content, loaded.ImageBytes);
            Assert.Null(loaded.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_an_image_over_the_size_limit_reports_Binary_with_a_size_suffix()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "big.png");
            byte[] content = [.. PngSignature, .. new byte[100]];
            File.WriteAllBytes(path, content);

            var queue = new ConcurrentQueue<Msg>();
            // A tiny override limit lets this test exercise the "too big" branch without writing
            // an actual 50MB fixture.
            using var runtime = new WorkerRuntime(queue.Enqueue, previewImageSizeLimitBytes: 4);
            runtime.Submit(new Effect.LoadPreview(1, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Binary, loaded.Kind);
            Assert.NotEmpty(loaded.ImageBytes);
            Assert.True(loaded.ImageBytes.Length <= 4096);
            Assert.Equal(content, loaded.ImageBytes);
            Assert.Contains("PNG Image", loaded.BinaryLabel);
            Assert.Contains("サイズ超過", loaded.BinaryLabel);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_UTF8_text_file_reports_Text_with_decoded_content()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "note.txt");
            File.WriteAllText(path, "hello, world - こんにちは", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(2, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Text, loaded.Kind);
            Assert.Equal("hello, world - こんにちは", loaded.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_ShiftJIS_text_file_reports_Text_with_decoded_content()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "sjis.txt");
            var sjis = Encoding.GetEncoding(932);
            File.WriteAllBytes(path, sjis.GetBytes("こんにちは、世界"));

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(3, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Text, loaded.Kind);
            Assert.Equal("こんにちは、世界", loaded.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_binary_file_with_NUL_bytes_reports_Binary_with_a_label()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "data.bin");
            byte[] content = [0x10, 0x20, 0x00, 0x30, 0x40, 0x00, 0x50];
            File.WriteAllBytes(path, content);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(4, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Binary, loaded.Kind);
            Assert.Equal("Binary Data", loaded.BinaryLabel);
            Assert.Equal(content, loaded.ImageBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_ZIP_file_reports_Binary_with_the_detected_label()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "archive.zip");
            byte[] content = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00, 0x00, 0x00];
            File.WriteAllBytes(path, content);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(5, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Binary, loaded.Kind);
            Assert.Equal("ZIP Archive", loaded.BinaryLabel);
            Assert.Equal(content, loaded.ImageBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_binary_file_larger_than_4KB_caps_the_carried_head_at_4096_bytes()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "large.bin");
            var content = new byte[10_000];
            content[0] = 0x10;
            content[1] = 0x00; // Forces the NUL-byte binary heuristic rather than any text decode.
            File.WriteAllBytes(path, content);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(9, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Binary, loaded.Kind);
            Assert.Equal(4096, loaded.ImageBytes.Length);
            Assert.Equal(content[..4096], loaded.ImageBytes);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_missing_file_reports_PreviewFailed()
    {
        var path = Path.Combine(Path.GetTempPath(), "zurari-does-not-exist-" + Guid.NewGuid() + ".txt");

        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);
        runtime.Submit(new Effect.LoadPreview(6, path));

        var failed = Assert.IsType<Msg.PreviewFailed>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
        Assert.Equal(6, failed.Generation);
        Assert.False(string.IsNullOrEmpty(failed.Error));
    }

    [Fact]
    public void LoadPreview_of_an_empty_file_reports_Text_with_empty_content()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "empty.txt");
            File.WriteAllBytes(path, []);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(8, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Text, loaded.Kind);
            Assert.Equal(string.Empty, loaded.Text);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_attaches_file_metadata_to_a_text_result()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "note.txt");
            File.WriteAllText(path, "hello");

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(10, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.NotNull(loaded.Metadata);
            Assert.Equal("note.txt", loaded.Metadata!.FileName);
            Assert.Equal(5, loaded.Metadata.SizeBytes);
            Assert.Null(loaded.Metadata.PixelWidth);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_attaches_file_metadata_to_a_binary_result()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "data.bin");
            File.WriteAllBytes(path, [0x10, 0x00, 0x20]);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(11, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.NotNull(loaded.Metadata);
            Assert.Equal("data.bin", loaded.Metadata!.FileName);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void LoadPreview_of_a_PNG_reports_metadata_with_parsed_pixel_dimensions()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "pic.png");
            byte[] content =
            [
                .. PngSignature,
                0x00, 0x00, 0x00, 0x0D,
                (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                0x00, 0x00, 0x00, 0x64, // width = 100
                0x00, 0x00, 0x00, 0x32, // height = 50
                0x08, // bit depth
                0x02, // color type = RGB
                0x00, 0x00, 0x00,
            ];
            File.WriteAllBytes(path, content);

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(12, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(PreviewKind.Image, loaded.Kind);
            Assert.NotNull(loaded.Metadata);
            Assert.Equal(100, loaded.Metadata!.PixelWidth);
            Assert.Equal(50, loaded.Metadata.PixelHeight);
            Assert.Equal(24, loaded.Metadata.BitsPerPixel);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A file with a video extension but no recognizable magic number (e.g. WMV's ASF container is
    /// not in <see cref="FileTypeDetector"/>'s signature table) must still be routed through the
    /// video preview path rather than falling into the generic Binary/Text branches. Since this
    /// test machine's ffmpeg availability is unknown, only the shape that must hold either way is
    /// asserted: either an Image (thumbnail succeeded) or a Binary result whose label says why it
    /// did not.
    /// </summary>
    [Fact]
    public void LoadPreview_of_a_video_extension_with_no_magic_match_is_routed_as_video()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "clip.wmv");
            File.WriteAllBytes(path, [0x00, 0x01, 0x02, 0x00, 0x03]); // not a real WMV - no tool can decode it

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);
            runtime.Submit(new Effect.LoadPreview(13, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(20)));
            Assert.NotNull(loaded.Metadata);
            if (loaded.Kind == PreviewKind.Binary)
            {
                Assert.Contains("Video", loaded.BinaryLabel);
            }
            else
            {
                Assert.Equal(PreviewKind.Image, loaded.Kind);
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void CancelPreview_with_nothing_in_flight_is_a_no_op()
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(queue.Enqueue);

        runtime.Submit(new Effect.CancelPreview(42));

        // Cancelling produces no Msg of its own (see Effect.CancelPreview), and cancelling when
        // nothing is running must not throw or wedge the slot for later previews.
        Thread.Sleep(200);
        Assert.Empty(queue);
    }

    [Fact]
    public void A_preview_still_loads_after_a_cancel()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "note.txt");
            File.WriteAllText(path, "hello");

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);

            // The cancel slot must be reusable: a cancelled preview leaves no state behind that
            // would stop the next one from running.
            runtime.Submit(new Effect.CancelPreview(1));
            runtime.Submit(new Effect.LoadPreview(2, path));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(2, loaded.Generation);
            Assert.Equal(PreviewKind.Text, loaded.Kind);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// A cancel that arrives after the load it was meant to precede must not kill it.
    /// </summary>
    /// <remarks>
    /// Transition emits CancelPreview(old) and LoadPreview(new) together, in that order, but they
    /// are drained from one channel by several workers, so the cancel can execute second. Before
    /// the generation check in CancelInFlightPreview this cancelled the *new* preview and the pane
    /// sat on "loading" forever - found as an intermittent timeout in the test above, which submits
    /// the same pair and lost the race roughly one run in four.
    /// </remarks>
    [Fact]
    public void A_late_cancel_for_a_superseded_generation_does_not_kill_the_current_preview()
    {
        var dir = CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "note.txt");
            File.WriteAllText(path, "hello");

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue);

            // Deliberately inverted relative to the order Transition emits them.
            runtime.Submit(new Effect.LoadPreview(2, path));
            runtime.Submit(new Effect.CancelPreview(1));

            var loaded = Assert.IsType<Msg.PreviewLoaded>(WaitForMsg(queue, TimeSpan.FromSeconds(10)));
            Assert.Equal(2, loaded.Generation);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Guards the wiring, not the stall. Preview used to execute on the worker pool, so a slow one
    /// (a cloud placeholder hydrating, or ffmpeg taking its full 20s) could occupy every worker and
    /// leave navigation queued behind it; it now runs on its own slot.
    /// </summary>
    /// <remarks>
    /// This does not reproduce that stall and would have passed before the fix: a text preview
    /// completes in microseconds, so the read was never really waiting. Reproducing it needs a
    /// preview that genuinely blocks, which needs either ffmpeg present (not guaranteed) or a
    /// seam for injecting a slow preview. What this does catch is preview being routed back onto
    /// the pool *and* blocking - the combination that caused the bug. The stall itself was
    /// verified by hand.
    /// </remarks>
    [Fact]
    public void ReadDirectory_completes_while_a_preview_is_outstanding()
    {
        var dir = CreateTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "a.txt"), "a");

            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(queue.Enqueue, workerCount: 1);
            runtime.Submit(new Effect.LoadPreview(1, Path.Combine(dir, "a.txt")));
            runtime.Submit(new Effect.ReadDirectory(0, new Location.RealDirectory(dir)));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                if (queue.Any(m => m is Msg.DirectoryLoaded))
                {
                    return;
                }

                Thread.Sleep(20);
            }

            Assert.Fail("ReadDirectory never completed while a preview was outstanding.");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
