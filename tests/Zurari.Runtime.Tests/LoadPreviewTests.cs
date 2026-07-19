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
            Assert.Empty(loaded.ImageBytes);
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
            Assert.Empty(loaded.ImageBytes);
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
}
