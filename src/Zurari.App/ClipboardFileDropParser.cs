using System.Collections.Immutable;
using System.IO;
using System.Windows;

namespace Zurari.App;

/// <summary>
/// Parses a WPF <see cref="IDataObject"/> holding a <see cref="DataFormats.FileDrop"/> payload
/// (optionally accompanied by the "Preferred DropEffect" marker Explorer and zurari both read/
/// write) into the plain paths + move-flag shape <see cref="Core.Msg.PasteRequested"/> needs.
/// Extracted out of <see cref="MainWindow"/>'s Ctrl+V handler so it is testable without any real
/// key events or a live clipboard (see <c>Zurari.App.Tests</c>).
/// </summary>
internal static class ClipboardFileDropParser
{
    /// <summary>
    /// Attempts to read <see cref="DataFormats.FileDrop"/> paths and the move/copy intent off
    /// <paramref name="data"/>. Returns <c>false</c> (with empty <paramref name="paths"/> and
    /// <paramref name="isMove"/> <c>false</c>) when no <see cref="DataFormats.FileDrop"/> payload
    /// is present or it does not decode to a non-empty string array. <paramref name="isMove"/> is
    /// <c>true</c> only when a "Preferred DropEffect" stream is present and decodes to a
    /// <see cref="DragDropEffects"/> value that includes <see cref="DragDropEffects.Move"/>;
    /// absent that marker (e.g. a plain Explorer copy), the paste defaults to a copy.
    /// </summary>
    public static bool TryParse(IDataObject data, out ImmutableArray<string> paths, out bool isMove)
    {
        paths = [];
        isMove = false;

        if (!data.GetDataPresent(DataFormats.FileDrop))
        {
            return false;
        }

        if (data.GetData(DataFormats.FileDrop) is not string[] { Length: > 0 } fileDrop)
        {
            return false;
        }

        paths = [.. fileDrop];
        isMove = TryReadIsMove(data);
        return true;
    }

    private static bool TryReadIsMove(IDataObject data)
    {
        if (!data.GetDataPresent("Preferred DropEffect"))
        {
            return false;
        }

        if (data.GetData("Preferred DropEffect") is not MemoryStream stream)
        {
            return false;
        }

        var buffer = stream.ToArray();
        if (buffer.Length < sizeof(int))
        {
            return false;
        }

        var effect = (DragDropEffects)BitConverter.ToInt32(buffer, 0);
        return (effect & DragDropEffects.Move) != 0;
    }
}
