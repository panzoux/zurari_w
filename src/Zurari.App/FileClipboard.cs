using System.Collections.Immutable;
using System.IO;
using System.Windows;

namespace Zurari.App;

/// <summary>
/// The OS clipboard, as Explorer uses it for files: a <see cref="DataFormats.FileDrop"/> payload plus
/// the "Preferred DropEffect" marker saying whether the paste is a copy or a move.
/// </summary>
/// <remarks>
/// Clipboard access can transiently fail with CLIPBRD_E_CANT_OPEN when another process holds the
/// clipboard open; both methods swallow that and report failure, so the user simply sees nothing
/// happen and can retry.
/// </remarks>
internal static class FileClipboard
{
    /// <summary>
    /// Places <paramref name="paths"/> on the clipboard, readable by Explorer as a copy or a cut.
    /// Returns whether it reached the clipboard.
    /// </summary>
    public static bool TrySet(ImmutableArray<string> paths, bool isMove)
    {
        try
        {
            var data = new DataObject();
            data.SetData(DataFormats.FileDrop, (string[])[.. paths]);
            var effect = isMove ? DragDropEffects.Move : DragDropEffects.Copy;
            data.SetData("Preferred DropEffect", new MemoryStream(BitConverter.GetBytes((int)effect)));
            Clipboard.SetDataObject(data, copy: true);
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }

    /// <summary>
    /// Reads a file list off the clipboard (via <see cref="ClipboardFileDropParser.TryParse"/>, shared
    /// with tests). Returns <c>false</c> when it holds nothing usable or cannot be opened.
    /// </summary>
    public static bool TryGet(out ImmutableArray<string> paths, out bool isMove)
    {
        paths = [];
        isMove = false;
        try
        {
            var data = Clipboard.GetDataObject();
            if (data is null || !ClipboardFileDropParser.TryParse(data, out var parsed, out isMove))
            {
                return false;
            }

            paths = parsed;
            return true;
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            return false;
        }
    }
}
