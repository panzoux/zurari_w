using Zurari.Runtime.Migemo;

namespace Zurari.Runtime.Tests.Migemo;

/// <summary>Ported from the tokenize tests in rustmigemo's query.rs.</summary>
public class MigemoTokenizeTests
{
    private static void AssertTokens(string input, params string[] expected)
    {
        Assert.Equal(new List<string>(expected), MigemoQuery.Tokenize(input));
    }

    [Fact]
    public void Splits_at_case_changes_and_whitespace()
    {
        AssertTokens("toukyouOosaka nagoyaFUKUOKAhokkaido ", "toukyou", "Oosaka", "nagoya", "FUKUOKA", "hokkaido");
    }

    [Theory]
    [InlineData("a")]
    [InlineData("A")]
    [InlineData("あ")]
    [InlineData("/")]
    public void Single_character_is_one_token(string input)
    {
        AssertTokens(input, input);
    }

    [Fact]
    public void Lowercase_then_uppercase_splits()
    {
        AssertTokens("aaA", "aa", "A");
    }

    [Fact]
    public void Empty_input_gives_no_tokens()
    {
        Assert.Empty(MigemoQuery.Tokenize(string.Empty));
    }

    [Fact]
    public void Surrounding_and_repeated_whitespace_is_dropped()
    {
        AssertTokens("  word1   word2 ", "word1", "word2");
    }

    [Fact]
    public void Splits_between_japanese_and_ascii()
    {
        AssertTokens("東京Tower", "東京", "Tower");
    }
}
