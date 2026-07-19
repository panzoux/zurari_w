namespace Zurari.Core;

/// <summary>Broad category a file's content falls into, as guessed from its leading bytes.</summary>
public enum FileCategory
{
    /// <summary>No recognizable magic number and no NUL byte in the sampled head - probably text.</summary>
    Text,

    /// <summary>PNG/JPEG/GIF/BMP/WebP - the kinds the preview pane can render directly.</summary>
    Image,

    Video,

    Audio,

    Archive,

    Executable,

    Pdf,

    /// <summary>Nothing above matched, or the sample contained a NUL byte.</summary>
    Binary,
}

/// <summary>Result of <see cref="FileTypeDetector.Detect"/>: a category plus a short display label.</summary>
/// <param name="Category">Broad kind, used by the preview pane to pick a rendering strategy.</param>
/// <param name="Label">Human-readable description shown in the preview metadata (e.g. "PNG Image").</param>
public sealed record DetectedType(FileCategory Category, string Label);

/// <summary>
/// Pure magic-number file type sniffing, ported from
/// <c>C:\Users\user\source\repos\panzoux\zurari\Program.cs</c>'s <c>FileTypeDetector</c>. Unlike
/// the original, this has no extension-based fallback (Core never sees a path here - only the
/// bytes) - callers that also know the file name can layer that on separately if they want to.
/// Contains no I/O types (BannedSymbols enforces this): the caller (Runtime) supplies the leading
/// bytes it already read.
/// </summary>
public static class FileTypeDetector
{
    private static readonly (int Offset, byte[] Sig, DetectedType Type)[] Signatures =
    [
        (0, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], new DetectedType(FileCategory.Image, "PNG Image")),
        (0, [0x66, 0x4C, 0x61, 0x43], new DetectedType(FileCategory.Audio, "FLAC Audio")),
        (0, [0x4F, 0x67, 0x67, 0x53], new DetectedType(FileCategory.Audio, "OGG Audio")),
        (0, [0x1A, 0x45, 0xDF, 0xA3], new DetectedType(FileCategory.Video, "Matroska Video")),
        (0, [0x47, 0x49, 0x46, 0x38], new DetectedType(FileCategory.Image, "GIF Image")),
        (0, [0x25, 0x50, 0x44, 0x46], new DetectedType(FileCategory.Pdf, "PDF Document")),
        (0, [0x50, 0x4B, 0x03, 0x04], new DetectedType(FileCategory.Archive, "ZIP Archive")),
        (0, [0x50, 0x4B, 0x05, 0x06], new DetectedType(FileCategory.Archive, "ZIP Archive")),
        (0, [0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C], new DetectedType(FileCategory.Archive, "7-Zip Archive")),
        (0, [0x52, 0x61, 0x72, 0x21, 0x1A, 0x07], new DetectedType(FileCategory.Archive, "RAR Archive")),
        (0, [0x7F, 0x45, 0x4C, 0x46], new DetectedType(FileCategory.Executable, "ELF Executable")),
        (4, [0x66, 0x74, 0x79, 0x70], new DetectedType(FileCategory.Video, "MP4 Video")), // ftyp box
        (0, [0x4D, 0x5A], new DetectedType(FileCategory.Executable, "PE Executable")),
        (0, [0x49, 0x44, 0x33], new DetectedType(FileCategory.Audio, "MP3 Audio")),
        (0, [0x42, 0x5A, 0x68], new DetectedType(FileCategory.Archive, "BZip2 Archive")),
        (0, [0x1F, 0x8B], new DetectedType(FileCategory.Archive, "GZip Archive")),
        (0, [0xFF, 0xD8], new DetectedType(FileCategory.Image, "JPEG Image")),
        (0, [0x42, 0x4D], new DetectedType(FileCategory.Image, "BMP Image")),
        (0, [0xFF, 0xFB], new DetectedType(FileCategory.Audio, "MP3 Audio")),
        (0, [0xFF, 0xF3], new DetectedType(FileCategory.Audio, "MP3 Audio")),
        (0, [0xFF, 0xF2], new DetectedType(FileCategory.Audio, "MP3 Audio")),
    ];

    /// <summary>
    /// Guesses the file type from <paramref name="head"/> (typically the file's first ~64KB).
    /// Checks RIFF containers (AVI/WAV/WebP) first, then a table of fixed-offset magic numbers,
    /// then falls back to a NUL-byte heuristic (no NUL in the sample -&gt; probably text) and
    /// finally <see cref="FileCategory.Binary"/>.
    /// </summary>
    public static DetectedType Detect(ReadOnlySpan<byte> head)
    {
        var riff = DetectRiff(head);
        if (riff is not null)
        {
            return riff;
        }

        foreach (var (offset, sig, type) in Signatures)
        {
            if (head.Length < offset + sig.Length)
            {
                continue;
            }

            if (Matches(head, offset, sig))
            {
                return RefineMp4(head, offset, sig) ?? type;
            }
        }

        if (head.Length > 0)
        {
            var checkLength = Math.Min(head.Length, 512);
            var hasNull = false;
            for (var i = 0; i < checkLength; i++)
            {
                if (head[i] == 0)
                {
                    hasNull = true;
                    break;
                }
            }

            if (!hasNull)
            {
                return new DetectedType(FileCategory.Text, "Text File");
            }
        }

        return new DetectedType(FileCategory.Binary, "Binary Data");
    }

    private static bool Matches(ReadOnlySpan<byte> head, int offset, byte[] sig)
    {
        for (var i = 0; i < sig.Length; i++)
        {
            if (head[offset + i] != sig[i])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Disambiguates an <c>ftyp</c> box match into QuickTime/M4A when the brand says so.</summary>
    private static DetectedType? RefineMp4(ReadOnlySpan<byte> head, int offset, byte[] sig)
    {
        if (offset != 4 || sig[0] != 0x66 || head.Length < 12)
        {
            return null;
        }

        if (head[8] == 0x71 && head[9] == 0x74 && head[10] == 0x20 && head[11] == 0x20)
        {
            return new DetectedType(FileCategory.Video, "QuickTime Video");
        }

        if (head[8] == 0x4D && head[9] == 0x34 && head[10] == 0x41 && head[11] == 0x20)
        {
            return new DetectedType(FileCategory.Audio, "M4A Audio");
        }

        return null;
    }

    private static DetectedType? DetectRiff(ReadOnlySpan<byte> head)
    {
        if (head.Length < 4 || head[0] != 0x52 || head[1] != 0x49 || head[2] != 0x46 || head[3] != 0x46)
        {
            return null;
        }

        if (head.Length < 12)
        {
            return new DetectedType(FileCategory.Binary, "RIFF Data");
        }

        if (head[8] == 0x41 && head[9] == 0x56 && head[10] == 0x49 && head[11] == 0x20)
        {
            return new DetectedType(FileCategory.Video, "AVI Video");
        }

        if (head[8] == 0x57 && head[9] == 0x41 && head[10] == 0x56 && head[11] == 0x45)
        {
            return new DetectedType(FileCategory.Audio, "WAV Audio");
        }

        if (head[8] == 0x57 && head[9] == 0x45 && head[10] == 0x42 && head[11] == 0x50)
        {
            return new DetectedType(FileCategory.Image, "WebP Image");
        }

        return new DetectedType(FileCategory.Binary, "RIFF Data");
    }
}
