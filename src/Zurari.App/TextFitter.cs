using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Zurari.Core;

namespace Zurari.App;

/// <summary>
/// Puts as much of a path or file name into a <see cref="TextBlock"/> as fits, shortening from the
/// middle rather than the end.
/// </summary>
/// <remarks>
/// <para>
/// WPF's <c>TextTrimming</c> can only cut from the end, which is the wrong end for these: a pane of
/// deleted files all read <c>C:\Users\user\AppData\Local…</c> and nothing distinguishes them.
/// <see cref="TextTrim"/> holds the rule (it is shared with the rest of the app and belongs in
/// Core); this type is only the measuring, which needs the realized control's own typeface and size.
/// </para>
/// <para>
/// The budget is found by bisection on the character count rather than computed, because character
/// width is not uniform - a path of Japanese folder names is roughly twice as wide per character as
/// an ASCII one. Eight or nine measurements settle a 260-character path.
/// </para>
/// </remarks>
internal static class TextFitter
{
    /// <summary>
    /// Sets <paramref name="target"/>'s text to as much of <paramref name="text"/> as fits in
    /// <paramref name="availableWidth"/>, shortened by <paramref name="shorten"/> when it does not.
    /// </summary>
    /// <param name="target">The block to fill. Its typeface and font size decide what fits.</param>
    /// <param name="text">The full text. Empty or <c>null</c> clears the block.</param>
    /// <param name="availableWidth">
    /// Width in device-independent pixels. Zero or negative (an unmeasured pane, a collapsed
    /// column) means "no budget known", so the full text is shown untouched rather than reduced to
    /// an ellipsis.
    /// </param>
    /// <param name="shorten">
    /// How to produce a shorter form - <see cref="TextTrim.Path"/> for a path,
    /// <see cref="TextTrim.Middle"/> for a bare name.
    /// </param>
    public static void Fit(TextBlock target, string? text, double availableWidth, Func<string, int, string> shorten)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(shorten);

        if (string.IsNullOrEmpty(text))
        {
            target.Text = string.Empty;
            return;
        }

        if (availableWidth <= 0 || Measure(target, text) <= availableWidth)
        {
            target.Text = text;
            return;
        }

        // Invariant across the search: `low` fits (0 characters trivially does) and `high` does not
        // (the full text, just measured). Bisect until they meet; `best` is then the longest form
        // that fits, or empty if not even one character does.
        var low = 0;
        var high = text.Length;
        var best = string.Empty;
        while (low + 1 < high)
        {
            var middle = low + ((high - low) / 2);
            var candidate = shorten(text, middle);
            if (Measure(target, candidate) <= availableWidth)
            {
                best = candidate;
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        target.Text = best;
    }

    private static double Measure(TextBlock target, string text) =>
        new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            target.FlowDirection,
            new Typeface(target.FontFamily, target.FontStyle, target.FontWeight, target.FontStretch),
            target.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(target).PixelsPerDip).WidthIncludingTrailingWhitespace;
}
