using Zurari.Core;

namespace Zurari.Core.Tests;

public class FileTypeDetectorTests
{
    [Fact]
    public void Detects_PNG_by_signature()
    {
        byte[] head = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Image, result.Category);
        Assert.Equal("PNG Image", result.Label);
    }

    [Fact]
    public void Detects_JPEG_by_signature()
    {
        byte[] head = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Image, result.Category);
        Assert.Equal("JPEG Image", result.Label);
    }

    [Fact]
    public void Detects_GIF_by_signature()
    {
        byte[] head = "GIF89a"u8.ToArray();

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Image, result.Category);
        Assert.Equal("GIF Image", result.Label);
    }

    [Fact]
    public void Detects_BMP_by_signature()
    {
        byte[] head = [0x42, 0x4D, 0x00, 0x00, 0x00, 0x00];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Image, result.Category);
        Assert.Equal("BMP Image", result.Label);
    }

    [Fact]
    public void Detects_WebP_via_RIFF_container()
    {
        byte[] head = [.. "RIFF"u8.ToArray(), 0x00, 0x00, 0x00, 0x00, .. "WEBP"u8.ToArray()];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Image, result.Category);
        Assert.Equal("WebP Image", result.Label);
    }

    [Fact]
    public void Detects_WAV_via_RIFF_container()
    {
        byte[] head = [.. "RIFF"u8.ToArray(), 0x00, 0x00, 0x00, 0x00, .. "WAVE"u8.ToArray()];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Audio, result.Category);
        Assert.Equal("WAV Audio", result.Label);
    }

    [Fact]
    public void Detects_ZIP_by_signature()
    {
        byte[] head = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Archive, result.Category);
        Assert.Equal("ZIP Archive", result.Label);
    }

    [Fact]
    public void Detects_PE_executable_by_MZ_signature()
    {
        byte[] head = [0x4D, 0x5A, 0x90, 0x00];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Executable, result.Category);
        Assert.Equal("PE Executable", result.Label);
    }

    [Fact]
    public void Detects_PDF_by_signature()
    {
        byte[] head = "%PDF-1.7"u8.ToArray();

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Pdf, result.Category);
    }

    [Fact]
    public void Empty_head_is_binary()
    {
        var result = FileTypeDetector.Detect([]);

        Assert.Equal(FileCategory.Binary, result.Category);
    }

    [Fact]
    public void Head_without_a_known_signature_or_NUL_byte_falls_back_to_text()
    {
        byte[] head = "hello, world! this is plain text content."u8.ToArray();

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Text, result.Category);
    }

    [Fact]
    public void Head_with_a_NUL_byte_and_no_known_signature_falls_back_to_binary()
    {
        byte[] head = [0x10, 0x20, 0x00, 0x30, 0x40];

        var result = FileTypeDetector.Detect(head);

        Assert.Equal(FileCategory.Binary, result.Category);
        Assert.Equal("Binary Data", result.Label);
    }

    [Fact]
    public void Detect_is_deterministic()
    {
        byte[] head = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

        var first = FileTypeDetector.Detect(head);
        var second = FileTypeDetector.Detect(head);

        Assert.Equal(first, second);
    }
}
