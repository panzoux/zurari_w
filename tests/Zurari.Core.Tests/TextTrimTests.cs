using Zurari.Core;

namespace Zurari.Core.Tests;

public class TextTrimTests
{
    private const char Ellipsis = '…';

    [Fact]
    public void Text_that_fits_is_returned_unchanged()
    {
        Assert.Equal("notes.txt", TextTrim.Middle("notes.txt", 9));
        Assert.Equal("notes.txt", TextTrim.Middle("notes.txt", 40));
    }

    [Fact]
    public void Middle_keeps_both_ends_and_never_exceeds_the_budget()
    {
        var trimmed = TextTrim.Middle("abcdefghijklmnopqrstuvwxyz", 11);

        Assert.Equal(11, trimmed.Length);
        Assert.StartsWith("abcdefg", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("xyz", trimmed, StringComparison.Ordinal);
        Assert.Contains(Ellipsis, trimmed);
    }

    /// <summary>
    /// The whole point of trimming from the middle: an extension survives, so two files still read
    /// as different things. Trimming from the end - all WPF can do on its own - loses it.
    /// </summary>
    [Fact]
    public void Middle_keeps_the_extension_of_a_long_name()
    {
        var trimmed = TextTrim.Middle("quarterly-report-2026-final-revised.xlsx", 20);

        Assert.EndsWith(".xlsx", trimmed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void A_budget_of_nothing_yields_nothing(int maxChars)
    {
        Assert.Equal(string.Empty, TextTrim.Middle("anything", maxChars));
        Assert.Equal(string.Empty, TextTrim.Path(@"C:\a\b.txt", maxChars));
    }

    [Fact]
    public void One_character_of_budget_is_the_ellipsis_alone()
    {
        Assert.Equal("…", TextTrim.Middle("anything", 1));
    }

    [Fact]
    public void Path_spends_its_budget_on_the_file_name_and_shortens_the_folders()
    {
        var trimmed = TextTrim.Path(@"C:\Users\someone\AppData\Local\Temp\notes.txt", 24);

        Assert.True(trimmed.Length <= 24, $"'{trimmed}' is longer than the budget");
        Assert.EndsWith(@"\notes.txt", trimmed, StringComparison.Ordinal);
        Assert.StartsWith("C:", trimmed, StringComparison.Ordinal);
        Assert.Contains(Ellipsis, trimmed);
    }

    [Fact]
    public void Path_falls_back_to_trimming_the_name_when_even_that_does_not_fit()
    {
        var trimmed = TextTrim.Path(@"C:\Users\someone\a-very-long-file-name-indeed.txt", 12);

        // A third of the budget goes to the tail, so at twelve characters that is "txt" without
        // its dot - the rwf rule, kept as-is rather than special-cased for extensions.
        Assert.Equal(12, trimmed.Length);
        Assert.DoesNotContain(@"\", trimmed, StringComparison.Ordinal);
        Assert.EndsWith("txt", trimmed, StringComparison.Ordinal);
    }

    [Fact]
    public void Path_without_a_separator_is_just_a_name()
    {
        var trimmed = TextTrim.Path("a-very-long-file-name-indeed.txt", 12);

        Assert.Equal(TextTrim.Middle("a-very-long-file-name-indeed.txt", 12), trimmed);
    }

    /// <summary>
    /// Two files deleted from different folders must stay distinguishable at the same budget - the
    /// failure that prompted this: every row read <c>C:\Users\user\AppData\Local…</c>.
    /// </summary>
    [Fact]
    public void Paths_sharing_a_long_prefix_still_differ_once_shortened()
    {
        var first = TextTrim.Path(@"C:\Users\user\AppData\Local\Packages\one\report.txt", 28);
        var second = TextTrim.Path(@"C:\Users\user\AppData\Local\Packages\two\summary.txt", 28);

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Null_input_is_rejected_rather_than_silently_treated_as_empty()
    {
        Assert.Throws<ArgumentNullException>(() => TextTrim.Middle(null!, 10));
        Assert.Throws<ArgumentNullException>(() => TextTrim.Path(null!, 10));
    }
}
