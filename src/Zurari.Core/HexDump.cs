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
    /// Formats <paramref name="bytes"/> as hex-dump text, preceded by a two-line header (a column
    /// legend and a dashed separator, see <see cref="AppendHeader"/>/<see cref="AppendSeparator"/>)
    /// so the pane reads like a disassembler's hex view. After the header, one line per
    /// <paramref name="bytesPerLine"/> bytes (default 16), each shaped "OFFSET  HEX  HEX  ASCII" -
    /// an 8-digit hex offset, the line's bytes as two-digit uppercase hex with a single space
    /// between bytes and an extra space every 8 bytes (so a 16-byte line reads as two 8-byte
    /// groups), then an ASCII column where printable bytes (0x20-0x7E) render as themselves and
    /// everything else as <c>.</c>. On a partial last line, missing hex bytes are rendered as
    /// blanks so the ASCII column of every line starts at the same column, but the ASCII column
    /// itself only shows the bytes actually present. All lines (including the header) are joined
    /// with <c>\n</c> (no trailing newline). Empty input still formats the header (there is simply
    /// nothing below it).
    /// </summary>
    public static string Format(ReadOnlySpan<byte> bytes, int bytesPerLine = 16)
    {
        if (bytesPerLine < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerLine), bytesPerLine, "Must be at least 1.");
        }

        var sb = new StringBuilder();
        AppendHeader(sb, bytesPerLine);
        sb.Append('\n');
        AppendSeparator(sb, bytesPerLine);

        for (var offset = 0; offset < bytes.Length; offset += bytesPerLine)
        {
            sb.Append('\n');
            var lineLength = Math.Min(bytesPerLine, bytes.Length - offset);
            AppendLine(sb, bytes.Slice(offset, lineLength), offset, bytesPerLine);
        }

        return sb.ToString();
    }

    /// <summary>
    /// "address   +0 +1 ... +F  ASCII" - a column legend lined up with <see cref="AppendLine"/>'s
    /// layout: an 8-wide label in place of the offset, then one <c>+N</c> (hex digit) per byte
    /// column with the same group-of-8 extra space, then the "ASCII" label over the ASCII column.
    /// </summary>
    private static void AppendHeader(StringBuilder sb, int bytesPerLine)
    {
        sb.Append("address ");
        sb.Append("  ");

        for (var i = 0; i < bytesPerLine; i++)
        {
            if (i > 0 && i % 8 == 0)
            {
                sb.Append(' ');
            }

            sb.Append('+');
            sb.Append(i.ToString("X", CultureInfo.InvariantCulture));
            sb.Append(' ');
        }

        sb.Append(' ');
        sb.Append("ASCII");
    }

    /// <summary>Dashed rule under <see cref="AppendHeader"/>, same column widths.</summary>
    private static void AppendSeparator(StringBuilder sb, int bytesPerLine)
    {
        sb.Append('-', 8);
        sb.Append("  ");

        for (var i = 0; i < bytesPerLine; i++)
        {
            if (i > 0 && i % 8 == 0)
            {
                sb.Append(' ');
            }

            sb.Append("--");
            sb.Append(' ');
        }

        sb.Append(' ');
        sb.Append('-', bytesPerLine);
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
