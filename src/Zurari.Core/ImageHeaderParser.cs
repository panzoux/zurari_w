using System.Buffers.Binary;

namespace Zurari.Core;

/// <summary>
/// Pure, allocation-light parsing of pixel dimensions (and bit depth, where cheap to get) straight
/// out of an image file's leading bytes - no decoding, no external image library. Feeds
/// <see cref="PreviewMetadata.PixelWidth"/>/<see cref="PreviewMetadata.PixelHeight"/>/
/// <see cref="PreviewMetadata.BitsPerPixel"/>. Supports PNG, JPEG (baseline and progressive),
/// GIF, and BMP; anything else (or a truncated/garbage header) yields <c>null</c> rather than
/// throwing.
/// </summary>
public static class ImageHeaderParser
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>
    /// Parses <paramref name="head"/> (the file's leading bytes - a few KB is normally enough,
    /// though a progressive JPEG's SOF marker can in principle sit further in) and returns its
    /// pixel width/height plus bit depth when known. Returns <c>null</c> when the format is not
    /// recognized or the header is too short to read - never throws.
    /// </summary>
    public static (int Width, int Height, int? BitsPerPixel)? Parse(ReadOnlySpan<byte> head) =>
        TryParsePng(head) ?? TryParseGif(head) ?? TryParseBmp(head) ?? TryParseJpeg(head);

    private static (int, int, int?)? TryParsePng(ReadOnlySpan<byte> head)
    {
        if (head.Length < 26 || !head[..8].SequenceEqual(PngSignature))
        {
            return null;
        }

        // Bytes 8-11 are the IHDR chunk's 4-byte length; 12-15 must literally be "IHDR".
        if (head[12] != (byte)'I' || head[13] != (byte)'H' || head[14] != (byte)'D' || head[15] != (byte)'R')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(head.Slice(16, 4));
        var height = BinaryPrimitives.ReadInt32BigEndian(head.Slice(20, 4));
        var bitDepth = head[24];
        var colorType = head[25];

        var channels = colorType switch
        {
            0 => 1, // Grayscale
            2 => 3, // RGB
            3 => 1, // Palette (index)
            4 => 2, // Grayscale + alpha
            6 => 4, // RGBA
            _ => 0, // Unknown color type
        };

        if (width <= 0 || height <= 0 || channels == 0)
        {
            return null;
        }

        return (width, height, bitDepth * channels);
    }

    private static (int, int, int?)? TryParseGif(ReadOnlySpan<byte> head)
    {
        if (head.Length < 10)
        {
            return null;
        }

        if (head[0] != (byte)'G' || head[1] != (byte)'I' || head[2] != (byte)'F' || head[3] != (byte)'8'
            || (head[4] != (byte)'7' && head[4] != (byte)'9') || head[5] != (byte)'a')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(6, 2));
        var height = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(8, 2));
        if (width == 0 || height == 0)
        {
            return null;
        }

        return (width, height, null);
    }

    private static (int, int, int?)? TryParseBmp(ReadOnlySpan<byte> head)
    {
        if (head.Length < 30 || head[0] != (byte)'B' || head[1] != (byte)'M')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(18, 4));
        var heightRaw = BinaryPrimitives.ReadInt32LittleEndian(head.Slice(22, 4));
        var bpp = BinaryPrimitives.ReadUInt16LittleEndian(head.Slice(28, 2));
        if (width <= 0 || heightRaw == 0)
        {
            return null;
        }

        // A negative height means a top-down bitmap (rare) - the magnitude is still the pixel count.
        return (width, Math.Abs(heightRaw), bpp);
    }

    /// <summary>
    /// Walks JPEG marker segments from the SOI looking for a start-of-frame marker (baseline
    /// SOF0/extended SOF1 through progressive SOF2 etc, but not DHT/JPG/DAC which share the
    /// 0xC0-0xCF range) and reads width/height/precision from its payload.
    /// </summary>
    private static (int, int, int?)? TryParseJpeg(ReadOnlySpan<byte> head)
    {
        if (head.Length < 4 || head[0] != 0xFF || head[1] != 0xD8)
        {
            return null;
        }

        var pos = 2;
        while (pos + 1 < head.Length)
        {
            if (head[pos] != 0xFF)
            {
                pos++;
                continue;
            }

            var marker = head[pos + 1];

            // 0xFF fill bytes between markers, or a marker with no length-prefixed payload
            // (TEM=0x01, RST0-RST7=0xD0-0xD7, SOI=0xD8, EOI=0xD9).
            if (marker == 0xFF)
            {
                pos++;
                continue;
            }

            if (marker == 0x01 || marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7))
            {
                pos += 2;
                continue;
            }

            if (pos + 4 > head.Length)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(head.Slice(pos + 2, 2));
            var isStartOfFrame = marker is >= 0xC0 and <= 0xCF && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;

            if (isStartOfFrame)
            {
                var payloadStart = pos + 4;
                if (payloadStart + 6 > head.Length)
                {
                    return null;
                }

                var precision = head[payloadStart];
                var height = BinaryPrimitives.ReadUInt16BigEndian(head.Slice(payloadStart + 1, 2));
                var width = BinaryPrimitives.ReadUInt16BigEndian(head.Slice(payloadStart + 3, 2));
                var components = head[payloadStart + 5];
                if (width == 0 || height == 0 || components == 0)
                {
                    return null;
                }

                return (width, height, precision * components);
            }

            if (length < 2)
            {
                return null;
            }

            pos += 2 + length;
        }

        return null;
    }
}
