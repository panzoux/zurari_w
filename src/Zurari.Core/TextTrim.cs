namespace Zurari.Core;

/// <summary>
/// Shortening text to fit a budget while keeping the parts that identify it.
/// </summary>
/// <remarks>
/// Ported from rwf's <c>smart_truncate</c> / <c>shorten_path</c> (rwf-bin/src/ui/unicode_utils.rs).
/// Trimming from the end - which is all WPF's <c>TextTrimming</c> can do - is exactly wrong for
/// paths and filenames: a column of deleted files all showed as
/// <c>C:\Users\user\AppData\Local…</c>, identical to each other and useless.
/// </remarks>
public static class TextTrim
{
    private const char Ellipsis = '…';

    /// <summary>
    /// Shortens <paramref name="text"/> to <paramref name="maxChars"/> by removing from the middle,
    /// keeping twice as much of the start as of the end.
    /// </summary>
    /// <remarks>
    /// The end is worth keeping because it carries the extension and the most specific part of a
    /// name; the start, because it says where the thing lives. Only the middle is predictable
    /// enough to lose.
    /// </remarks>
    public static string Middle(string text, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        if (text.Length <= maxChars)
        {
            return text;
        }

        if (maxChars == 1)
        {
            return Ellipsis.ToString();
        }

        var available = maxChars - 1;
        var forEnd = available / 3;
        var forStart = available - forEnd;
        return string.Concat(text.AsSpan(0, forStart), stackalloc[] { Ellipsis }, text.AsSpan(text.Length - forEnd));
    }

    /// <summary>
    /// Shortens a path to <paramref name="maxChars"/>, sacrificing directories before the file name.
    /// </summary>
    /// <remarks>
    /// The file name is what a person reads, so it is kept whole for as long as it fits and only
    /// middle-trimmed once it alone is too long. Everything before it collapses to an ellipsis
    /// first, which is how a path stays recognisable at a glance.
    /// </remarks>
    public static string Path(string path, int maxChars)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (maxChars <= 0)
        {
            return string.Empty;
        }

        if (path.Length <= maxChars)
        {
            return path;
        }

        var separator = path.TrimEnd('\\', '/').LastIndexOfAny(['\\', '/']);
        if (separator < 0)
        {
            return Middle(path, maxChars);
        }

        var fileName = path[(separator + 1)..];

        // Not even the name fits on its own: trim that, and say nothing about the folders.
        if (fileName.Length + 2 > maxChars)
        {
            return Middle(fileName, maxChars);
        }

        var head = path[..(separator + 1)];
        var forHead = maxChars - fileName.Length - 1;
        return string.Concat(Middle(head, forHead), fileName);
    }
}
