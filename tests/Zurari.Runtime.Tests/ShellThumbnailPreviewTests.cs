using System.Collections.Concurrent;
using Zurari.Core;
using Zurari.Runtime;

namespace Zurari.Runtime.Tests;

/// <summary>
/// Where Explorer's thumbnail sits in the preview: before ffmpeg for video (6c.5), before the text
/// sniff and hex dump for PDF and Office documents, cached here, and never asked for a file whose
/// bytes live on a share.
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

            // The picture is the video's, so it says so - and its own 1x1 size is not the video's
            // resolution.
            Assert.Equal("MP4 Video", loaded.Text);
            Assert.Null(loaded.Metadata!.PixelWidth);
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

    /// <summary>A PDF whose bytes are all ASCII - legal, and common for small generated ones.</summary>
    private const string AsciiPdf =
        "%PDF-1.4\n1 0 obj << /Type /Catalog /Pages 2 0 R >> endobj\ntrailer << /Root 1 0 R >>\n%%EOF\n";

    /// <summary>A ZIP local-file header: what a .docx, .xlsx or .pptx starts with.</summary>
    private static readonly byte[] ZipHead = [0x50, 0x4B, 0x03, 0x04, 0x14, 0x00, 0x06, 0x00, 0x08, 0x00];

    /// <summary>An OLE compound-file header: what a .doc, .xls or .ppt starts with.</summary>
    private static readonly byte[] OleHead = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00, 0x00];

    private static Msg.PreviewLoaded LoadOnce(string dir, string file, Func<string, int, byte[]?> shellThumbnail)
    {
        var queue = new ConcurrentQueue<Msg>();
        using var runtime = new WorkerRuntime(
            queue.Enqueue,
            thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
            shellThumbnail: shellThumbnail);

        runtime.Submit(new Effect.LoadPreview(1, file));
        return WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// The ASCII PDF matters: it decodes as text, so if the document branch sat after the text sniff
    /// the preview would be the PDF's own source.
    /// </summary>
    [Fact]
    public void A_pdf_previews_as_explorers_thumbnail_labelled_as_a_pdf()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var pdf = Path.Combine(dir, "paper.pdf");
            File.WriteAllText(pdf, AsciiPdf);
            var askedFor = new ConcurrentQueue<(string Path, int Size)>();

            var loaded = LoadOnce(dir, pdf, (path, size) =>
            {
                askedFor.Enqueue((path, size));
                return OnePixelPng;
            });

            Assert.Equal(PreviewKind.Image, loaded.Kind);
            Assert.Equal(OnePixelPng, loaded.ImageBytes);
            Assert.Equal("PDF Document", loaded.Text);
            Assert.Null(loaded.Metadata!.PixelWidth);
            Assert.Equal([(pdf, 512)], askedFor);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Theory]
    [InlineData("letter.docx", "zip", "Word Document")]
    [InlineData("old-letter.doc", "ole", "Word Document")]
    [InlineData("budget.xlsx", "zip", "Excel Workbook")]
    [InlineData("old-budget.xls", "ole", "Excel Workbook")]
    [InlineData("deck.pptx", "zip", "PowerPoint Presentation")]
    [InlineData("SHOUTING.PDF", "zip", "PDF Document")]
    public void Office_documents_are_offered_by_their_extension(string name, string head, string label)
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var file = Path.Combine(dir, name);
            File.WriteAllBytes(file, head == "zip" ? ZipHead : OleHead);

            var loaded = LoadOnce(dir, file, (_, _) => OnePixelPng);

            Assert.Equal(PreviewKind.Image, loaded.Kind);
            Assert.Equal(label, loaded.Text);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// With no thumbnail handler installed - this machine, as it happens - a document previews
    /// exactly as it did before documents were offered to the shell at all.
    /// </summary>
    [Fact]
    public void Without_a_handler_a_document_keeps_the_preview_it_had()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var pdf = Path.Combine(dir, "paper.pdf");
            File.WriteAllText(pdf, AsciiPdf);
            var docx = Path.Combine(dir, "letter.docx");
            File.WriteAllBytes(docx, ZipHead);
            var asked = 0;
            byte[]? NoHandler(string path, int size)
            {
                Interlocked.Increment(ref asked);
                return null;
            }

            var pdfPreview = LoadOnce(dir, pdf, NoHandler);
            var docxPreview = LoadOnce(dir, docx, NoHandler);

            Assert.Equal(2, asked);
            Assert.Equal(PreviewKind.Text, pdfPreview.Kind);
            Assert.Equal(AsciiPdf, pdfPreview.Text);
            Assert.Equal(PreviewKind.Binary, docxPreview.Kind);
            Assert.Equal("ZIP Archive", docxPreview.BinaryLabel);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// No handler is a fact about the machine, not the file: install Office and the next look should
    /// get a thumbnail, not a remembered failure.
    /// </summary>
    [Fact]
    public void A_missing_handler_is_not_remembered_against_the_file()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var docx = Path.Combine(dir, "letter.docx");
            File.WriteAllBytes(docx, ZipHead);
            var handlerInstalled = false;
            var queue = new ConcurrentQueue<Msg>();
            using var runtime = new WorkerRuntime(
                queue.Enqueue,
                thumbnailCache: new ThumbnailCache(Path.Combine(dir, "cache")),
                shellThumbnail: (_, _) => Volatile.Read(ref handlerInstalled) ? OnePixelPng : null);

            runtime.Submit(new Effect.LoadPreview(1, docx));
            var before = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
            Volatile.Write(ref handlerInstalled, true);
            runtime.Submit(new Effect.LoadPreview(2, docx));
            var after = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(PreviewKind.Binary, before.Kind);
            Assert.Equal(PreviewKind.Image, after.Kind);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Fact]
    public void A_document_thumbnail_is_kept_so_the_shell_is_not_asked_twice()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            var pdf = Path.Combine(dir, "paper.pdf");
            File.WriteAllText(pdf, AsciiPdf);
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

            runtime.Submit(new Effect.LoadPreview(1, pdf));
            WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));
            runtime.Submit(new Effect.LoadPreview(2, pdf));
            var second = WaitFor<Msg.PreviewLoaded>(queue, TimeSpan.FromSeconds(10));

            Assert.Equal(PreviewKind.Image, second.Kind);
            Assert.Equal("PDF Document", second.Text);
            Assert.Equal(1, asked);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>
    /// A list, not "anything that would otherwise be a hex dump": archives, executables, text and
    /// real pictures keep the previews they had, and the shell is not asked about them.
    /// </summary>
    [Fact]
    public void Files_that_are_not_documents_are_never_offered()
    {
        var dir = TempDirectory.Create("zurari-shellpreview");
        try
        {
            File.WriteAllBytes(Path.Combine(dir, "archive.zip"), ZipHead);
            File.WriteAllBytes(Path.Combine(dir, "app.exe"), [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00]);
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "hello");
            File.WriteAllText(Path.Combine(dir, "pdf"), AsciiPdf);
            File.WriteAllBytes(Path.Combine(dir, "picture.png"), OnePixelPng);
            var asked = 0;
            byte[]? Shell(string path, int size)
            {
                Interlocked.Increment(ref asked);
                return OnePixelPng;
            }

            var picture = LoadOnce(dir, Path.Combine(dir, "picture.png"), Shell);
            foreach (var name in new[] { "archive.zip", "app.exe", "notes.txt", "pdf" })
            {
                Assert.NotEqual(PreviewKind.Image, LoadOnce(dir, Path.Combine(dir, name), Shell).Kind);
            }

            Assert.Equal(0, asked);
            Assert.Equal(PreviewKind.Image, picture.Kind);
            Assert.Null(picture.Text);
            Assert.Equal(1, picture.Metadata!.PixelWidth);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    /// <summary>The same third guard as for video, for a document reached through a share.</summary>
    [Fact]
    public void A_document_on_a_share_is_never_offered_to_the_shell()
    {
        var dir = TempDirectory.Create("zurari-shellpreview-unc");
        try
        {
            var viaShare = @"\\localhost\" + dir[0] + "$" + dir[2..];
            if (!Directory.Exists(viaShare))
            {
                return; // no loopback share on this machine.
            }

            File.WriteAllText(Path.Combine(dir, "paper.pdf"), AsciiPdf);
            var asked = 0;

            var loaded = LoadOnce(dir, Path.Combine(viaShare, "paper.pdf"), (_, _) =>
            {
                Interlocked.Increment(ref asked);
                return OnePixelPng;
            });

            Assert.Equal(0, asked);
            Assert.Equal(PreviewKind.Text, loaded.Kind);
        }
        finally
        {
            TempDirectory.Delete(dir);
        }
    }

    [Theory]
    [InlineData("paper.pdf", "PDF Document")]
    [InlineData(@"C:\Docs\PAPER.PDF", "PDF Document")]
    [InlineData("letter.docm", "Word Document")]
    [InlineData("template.dotx", "Word Document")]
    [InlineData("binary.xlsb", "Excel Workbook")]
    [InlineData("template.xltm", "Excel Workbook")]
    [InlineData("show.ppsx", "PowerPoint Presentation")]
    [InlineData("template.potx", "PowerPoint Presentation")]
    [InlineData("archive.zip", null)]
    [InlineData("paper.pdf.txt", null)]
    [InlineData("pdf", null)]
    [InlineData(@"C:\folder.pdf\notes", null)]
    [InlineData("", null)]
    public void Documents_are_recognised_by_extension_alone(string path, string? label)
    {
        Assert.Equal(label, WorkerRuntime.DocumentLabel(path));
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
