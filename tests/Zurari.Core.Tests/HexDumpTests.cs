using Zurari.Core;

namespace Zurari.Core.Tests;

public class HexDumpTests
{
    private const string DefaultHeader =
        "address   +0 +1 +2 +3 +4 +5 +6 +7  +8 +9 +A +B +C +D +E +F  ASCII";

    private const string DefaultSeparator =
        "--------  -- -- -- -- -- -- -- --  -- -- -- -- -- -- -- --  ----------------";

    [Fact]
    public void Header_and_separator_precede_the_data_lines()
    {
        byte[] bytes = [0x41];

        var result = HexDump.Format(bytes);
        var lines = result.Split('\n');

        Assert.Equal(DefaultHeader, lines[0]);
        Assert.Equal(DefaultSeparator, lines[1]);
        Assert.Equal("00000000  41                                                A", lines[2]);
        Assert.Equal(3, lines.Length);
    }

    [Fact]
    public void Header_adapts_to_a_custom_bytesPerLine()
    {
        var result = HexDump.Format([0x01, 0x02], bytesPerLine: 4);
        var lines = result.Split('\n');

        Assert.Equal("address   +0 +1 +2 +3  ASCII", lines[0]);
        Assert.Equal("--------  -- -- -- --  ----", lines[1]);
    }

    [Fact]
    public void Empty_input_formats_as_just_the_header_and_separator()
    {
        Assert.Equal(DefaultHeader + "\n" + DefaultSeparator, HexDump.Format([]));
    }

    [Fact]
    public void An_exact_line_formats_as_a_single_line_after_the_header()
    {
        byte[] bytes = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, 0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00];

        var result = HexDump.Format(bytes);
        var lines = result.Split('\n');

        Assert.Equal(3, lines.Length);
        Assert.Equal(
            "00000000  4D 5A 90 00 03 00 00 00  04 00 00 00 FF FF 00 00  MZ..............",
            lines[2]);
    }

    [Fact]
    public void A_partial_last_line_pads_missing_hex_bytes_and_shows_only_the_actual_ASCII()
    {
        byte[] bytes = [0x41, 0x42, 0x43];

        var result = HexDump.Format(bytes);
        var lines = result.Split('\n');

        Assert.Equal(
            "00000000  41 42 43                                          ABC",
            lines[2]);
    }

    [Fact]
    public void Non_printable_bytes_render_as_a_dot_in_the_ASCII_column()
    {
        byte[] bytes = [0x00, 0x1F, 0x20, 0x7E, 0x7F, 0xFF];

        var result = HexDump.Format(bytes);

        Assert.EndsWith(".. ~..", result);
    }

    [Fact]
    public void Offset_advances_by_bytesPerLine_on_each_line()
    {
        var bytes = new byte[32];

        var result = HexDump.Format(bytes);
        var lines = result.Split('\n');

        Assert.Equal(4, lines.Length); // header + separator + 2 data lines
        Assert.StartsWith("00000000  ", lines[2]);
        Assert.StartsWith("00000010  ", lines[3]);
    }

    [Fact]
    public void A_custom_bytesPerLine_is_honored()
    {
        byte[] bytes = [0x01, 0x02, 0x03, 0x04, 0x05];

        var result = HexDump.Format(bytes, bytesPerLine: 4);
        var lines = result.Split('\n');

        Assert.Equal(4, lines.Length); // header + separator + 2 data lines
        Assert.StartsWith("00000000  ", lines[2]);
        Assert.StartsWith("00000004  ", lines[3]);
    }

    [Fact]
    public void Zero_or_negative_bytesPerLine_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => HexDump.Format([1, 2, 3], bytesPerLine: 0));
    }
}
