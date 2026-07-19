using System.Globalization;
using System.Text;

namespace Zurari.Core;

/// <summary>
/// Pure hex-dump formatter for the preview pane's Binary view: offset + hex bytes (grouped in
/// eights) + ASCII column, in the classic layout also used by
/// <c>C:\Users\user\source\repos\panzoux\zurari</c> (TUI版)'s hex view.
/// </summary>
public static class HexDump
{
    /// <summary>
    /// Formats <paramref name="bytes"/> as hex-dump text: one line per <paramref name="bytesPerLine"/>
    /// bytes (default 16), each shaped "OFFSET  HEX  HEX  ASCII" - an 8-digit hex offset, the line's
    /// bytes as two-digit uppercase hex with a single space between bytes and an extra space every 8
    /// bytes (so a 16-byte line reads as two 8-byte groups), then an ASCII column where printable
    /// bytes (0x20-0x7E) render as themselves and everything else as <c>.</c>. On a partial last
    /// line, missing hex bytes are rendered as blanks so the ASCII column of every line starts at the
    /// same column, but the ASCII column itself only shows the bytes actually present. Lines are
    /// joined with <c>\n</c> (no trailing newline). Empty input formats as an empty string.
    /// </summary>
    public static string Format(ReadOnlySpan<byte> bytes, int bytesPerLine = 16)
    {
        if (bytesPerLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerLine), bytesPerLine, "Must be at least 1.");
        }

        if (bytes.Length == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        for (var offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            if (offset > 0)
            {
                sb.Append('\n');
            }

            var lineLength = Math.Min(bytesPerLine, bytes.Length - offset);
            AppendLine(sb, bytes.Slice(offset, lineLength), offset, bytesPerLine);
        }

        return sb.ToString();
    }

    private static void AppendLine(StringBuilder sb, ReadOnlySpan<byte> line, int offset, int bytesPerLine)
    {
        sb.Append(offset.ToString("X8", CultureInfo.InvariantCulture));
        sb.Append("  ");

        for (var i = 0; i < bytesPerLine; i++)
        {
            if (i > 0 && i % 8 == 0)
            {
                sb.Append(' ');
            }

            if (i < line.Length)
            {
                sb.Append(line[i].ToString("X2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append("  ");
            }

            sb.Append(' ');
        }

        sb.Append(' ');
        foreach (var b in line)
        {
            sb.Append(b is >= 0x20 and <= 0x7E ? (char)b : '.');
        }
    }
}
