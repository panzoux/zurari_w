using System.Windows.Controls;
using System.Windows.Media;
using Zurari.App;
using Zurari.Core;

namespace Zurari.App.Tests;

/// <summary>
/// <see cref="TextFitter"/> is the measuring half of the preview header's middle-trimming; the rule
/// itself is <see cref="TextTrim"/>'s and is tested in Core. What matters here is that the budget
/// search lands on something that really fits.
/// </summary>
public class TextFitterTests
{
    private static TextBlock NewBlock() =>
        new() { FontFamily = new FontFamily("Segoe UI"), FontSize = 12 };

    [StaFact]
    public void Text_that_fits_is_shown_whole()
    {
        var block = NewBlock();

        TextFitter.Fit(block, "notes.txt", 400, TextTrim.Middle);

        Assert.Equal("notes.txt", block.Text);
    }

    [StaFact]
    public void An_unmeasured_pane_shows_the_text_rather_than_an_ellipsis()
    {
        var block = NewBlock();

        TextFitter.Fit(block, @"C:\Users\someone\Documents\notes.txt", 0, TextTrim.Path);

        Assert.Equal(@"C:\Users\someone\Documents\notes.txt", block.Text);
    }

    [StaFact]
    public void Null_or_empty_clears_the_block()
    {
        var block = NewBlock();
        block.Text = "leftover";

        TextFitter.Fit(block, null, 400, TextTrim.Middle);

        Assert.Equal(string.Empty, block.Text);
    }

    [StaFact]
    public void A_long_path_is_shortened_to_something_that_actually_fits()
    {
        const string path = @"C:\Users\someone\AppData\Local\Packages\SomeVendor.SomeApp\LocalState\notes.txt";
        var block = NewBlock();
        const double width = 160;

        TextFitter.Fit(block, path, width, TextTrim.Path);

        Assert.NotEqual(path, block.Text);
        Assert.Contains('…', block.Text);
        Assert.EndsWith(@"\notes.txt", block.Text, StringComparison.Ordinal);
        Assert.True(Width(block) <= width, $"'{block.Text}' measures wider than the pane");
    }

    /// <summary>
    /// The search must find the *longest* form that fits, not merely a short one - otherwise the
    /// header would throw away room it has.
    /// </summary>
    [StaFact]
    public void The_result_is_the_longest_form_that_fits()
    {
        const string path = @"C:\Users\someone\AppData\Local\Packages\SomeVendor.SomeApp\LocalState\notes.txt";
        var block = NewBlock();
        const double width = 160;

        TextFitter.Fit(block, path, width, TextTrim.Path);
        var chosen = block.Text;

        var oneMore = TextTrim.Path(path, chosen.Length + 1);
        block.Text = oneMore;
        Assert.True(
            oneMore == chosen || Width(block) > width,
            $"'{oneMore}' also fits, so '{chosen}' was not the longest form");
    }

    private static double Width(TextBlock block)
    {
        block.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        return block.DesiredSize.Width;
    }
}
