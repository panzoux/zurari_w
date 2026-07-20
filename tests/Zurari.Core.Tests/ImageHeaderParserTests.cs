using Zurari.Core;

namespace Zurari.Core.Tests;

public class ImageHeaderParserTests
{
    [Fact]
    public void Parses_PNG_dimensions_and_bit_depth_from_the_IHDR_chunk()
    {
        byte[] head =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, // signature
            0x00, 0x00, 0x00, 0x0D, // IHDR chunk length (unused by the parser)
            (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0x00, 0x00, 0x06, 0xDA, // width = 1754 (BE)
            0x00, 0x00, 0x03, 0x24, // height = 804 (BE)
            0x08, // bit depth
            0x02, // color type = RGB (3 channels)
            0x00, 0x00, 0x00, // compression, filter, interlace
        ];

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((1754, 804, (int?)24), result);
    }

    [Fact]
    public void Parses_PNG_RGBA_bit_depth_as_bitDepth_times_four_channels()
    {
        byte[] head =
        [
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D,
            (byte)'I', (byte)'H', (byte)'D', (byte)'R',
            0x00, 0x00, 0x00, 0x64, // width = 100
            0x00, 0x00, 0x00, 0x32, // height = 50
            0x08, // bit depth
            0x06, // color type = RGBA (4 channels)
            0x00, 0x00, 0x00,
        ];

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((100, 50, (int?)32), result);
    }

    [Fact]
    public void Parses_baseline_JPEG_dimensions_from_the_SOF0_marker()
    {
        byte[] head =
        [
            0xFF, 0xD8, // SOI
            0xFF, 0xE0, 0x00, 0x10, // APP0, length 16 -> 14 content bytes follow
            (byte)'J', (byte)'F', (byte)'I', (byte)'F', 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00,
            0xFF, 0xC0, 0x00, 0x11, // SOF0, length 17
            0x08, // precision
            0x03, 0x20, // height = 800 (BE)
            0x04, 0xB0, // width = 1200 (BE)
            0x03, // components
            0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        ];

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((1200, 800, (int?)24), result);
    }

    [Fact]
    public void Parses_progressive_JPEG_dimensions_from_the_SOF2_marker()
    {
        byte[] head =
        [
            0xFF, 0xD8, // SOI
            0xFF, 0xC2, 0x00, 0x11, // SOF2 (progressive), length 17
            0x08, // precision
            0x00, 0x64, // height = 100 (BE)
            0x00, 0xC8, // width = 200 (BE)
            0x03, // components
            0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01,
        ];

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((200, 100, (int?)24), result);
    }

    [Fact]
    public void Parses_GIF_dimensions_little_endian()
    {
        byte[] head = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'9', (byte)'a', 0x64, 0x00, 0x32, 0x00];

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((100, 50, (int?)null), result);
    }

    [Fact]
    public void Parses_GIF87a_variant_too()
    {
        byte[] head = [(byte)'G', (byte)'I', (byte)'F', (byte)'8', (byte)'7', (byte)'a', 0x0A, 0x00, 0x05, 0x00];

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((10, 5, (int?)null), result);
    }

    [Fact]
    public void Parses_BMP_dimensions_and_bit_depth_from_BITMAPINFOHEADER()
    {
        var head = new byte[30];
        head[0] = (byte)'B';
        head[1] = (byte)'M';
        BitConverter.GetBytes(40).CopyTo(head, 14); // BITMAPINFOHEADER size
        BitConverter.GetBytes(200).CopyTo(head, 18); // width
        BitConverter.GetBytes(-100).CopyTo(head, 22); // height, negative = top-down
        BitConverter.GetBytes((short)1).CopyTo(head, 26); // planes
        BitConverter.GetBytes((short)32).CopyTo(head, 28); // bpp

        var result = ImageHeaderParser.Parse(head);

        Assert.Equal((200, 100, (int?)32), result);
    }

    [Fact]
    public void Unrecognized_or_garbage_bytes_return_null()
    {
        Assert.Null(ImageHeaderParser.Parse([]));
        Assert.Null(ImageHeaderParser.Parse([0x00]));
        Assert.Null(ImageHeaderParser.Parse([0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC, 0xDE, 0xF0]));
    }

    [Fact]
    public void Truncated_PNG_header_returns_null_instead_of_throwing()
    {
        byte[] head = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];

        Assert.Null(ImageHeaderParser.Parse(head));
    }

    [Fact]
    public void Truncated_JPEG_missing_the_SOF_marker_returns_null_instead_of_throwing()
    {
        byte[] head = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04];

        Assert.Null(ImageHeaderParser.Parse(head));
    }
}
