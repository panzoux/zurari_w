using System.IO;
using System.Windows;

namespace Zurari.App.Tests;

/// <summary>
/// Unit coverage of <see cref="ClipboardFileDropParser"/> (the internal helper the Ctrl+V handler
/// uses) via plain in-memory <see cref="DataObject"/> instances - no OS clipboard involved, so
/// this runs reliably regardless of STA/session clipboard availability.
/// </summary>
public class ClipboardFileDropParserTests
{
    private static readonly string[] TwoPaths = [@"C:\temp\a.txt", @"C:\temp\b.txt"];
    private static readonly string[] OnePath = [@"C:\temp\a.txt"];

    [Fact]
    public void TryParse_returns_false_when_no_file_drop_present()
    {
        var data = new DataObject();
        data.SetData(DataFormats.Text, "not a file drop");

        var result = ClipboardFileDropParser.TryParse(data, out var paths, out var isMove);

        Assert.False(result);
        Assert.Empty(paths);
        Assert.False(isMove);
    }

    [Fact]
    public void TryParse_defaults_to_copy_when_no_drop_effect_marker_present()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, TwoPaths);

        var result = ClipboardFileDropParser.TryParse(data, out var paths, out var isMove);

        Assert.True(result);
        Assert.Equal(TwoPaths, paths);
        Assert.False(isMove);
    }

    [Fact]
    public void TryParse_reads_move_from_preferred_drop_effect_marker()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, OnePath);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Move)));

        var result = ClipboardFileDropParser.TryParse(data, out var paths, out var isMove);

        Assert.True(result);
        Assert.Single(paths);
        Assert.True(isMove);
    }

    [Fact]
    public void TryParse_reads_copy_from_preferred_drop_effect_marker()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, OnePath);
        data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)DragDropEffects.Copy)));

        var result = ClipboardFileDropParser.TryParse(data, out var paths, out var isMove);

        Assert.True(result);
        Assert.False(isMove);
    }

    [Fact]
    public void TryParse_returns_false_for_empty_file_drop_array()
    {
        var data = new DataObject();
        data.SetData(DataFormats.FileDrop, Array.Empty<string>());

        var result = ClipboardFileDropParser.TryParse(data, out var paths, out var isMove);

        Assert.False(result);
        Assert.Empty(paths);
        Assert.False(isMove);
    }
}
