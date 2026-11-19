using System.Text.RegularExpressions;
using Zurari.Core;

namespace Zurari.Core.Tests;

/// <summary>The matcher behind search and Leap (7b.1).</summary>
public class NameQueryTests
{
    private static readonly Regex Nihon = new("日本", NameQuery.PatternOptions);

    [Theory]
    [InlineData("map tex", "map|tex")]
    [InlineData(@"a\ b c", "a b|c")]
    [InlineData("  x  ", "x")]
    [InlineData("", "")]
    [InlineData(@"tail\", @"tail\")]
    [InlineData(@"\ lead", " lead")]
    public void Segments_split_on_spaces_and_escaped_spaces_stay(string text, string expected)
    {
        Assert.Equal(expected, string.Join('|', NameQuery.SplitSegments(text)));
    }

    [Theory]
    [InlineData("tex2map.txt")]
    [InlineData("map2tex.txt")]
    public void Segments_match_in_any_order(string name)
    {
        Assert.NotEqual(MatchTier.None, NameQuery.Literal("map tex").Match(name));
    }

    [Fact]
    public void Every_segment_has_to_match()
    {
        Assert.Equal(MatchTier.None, NameQuery.Literal("map tex").Match("mapping.doc"));
    }

    [Fact]
    public void A_name_starting_with_the_first_segment_is_a_prefix_match()
    {
        var query = NameQuery.Literal("Map tex");

        Assert.Equal(MatchTier.Prefix, query.Match("mapTEX.txt"));
        Assert.Equal(MatchTier.Substring, query.Match("tex2map.txt"));
    }

    [Fact]
    public void An_escaped_space_matches_a_space()
    {
        var query = NameQuery.Literal(@"my\ doc");

        Assert.Equal(MatchTier.Prefix, query.Match("my doc.txt"));
        Assert.Equal(MatchTier.None, query.Match("mydoc.txt"));
    }

    [Fact]
    public void An_empty_query_matches_nothing()
    {
        Assert.True(NameQuery.Empty.IsEmpty);
        Assert.Equal(MatchTier.None, NameQuery.Empty.Match("anything"));
    }

    [Fact]
    public void A_migemo_pattern_matches_what_the_letters_do_not()
    {
        var query = NameQuery.Literal("nihon").WithPattern("nihon", Nihon);

        Assert.Equal(MatchTier.Migemo, query.Match("日本語.txt"));
        Assert.Equal(MatchTier.Prefix, query.Match("nihongo.txt"));
    }

    [Fact]
    public void Migemo_segments_are_ANDed_with_the_others()
    {
        var query = NameQuery.Literal("nihon txt").WithPattern("nihon", Nihon);

        Assert.Equal(MatchTier.Migemo, query.Match("日本.txt"));
        Assert.Equal(MatchTier.None, query.Match("日本.doc"));
    }

    [Fact]
    public void A_word_without_a_pattern_matches_literally_only()
    {
        var query = NameQuery.Literal("nihon").WithPattern("nihon", null);

        Assert.Equal(MatchTier.None, query.Match("日本.txt"));
        Assert.True(query.Pending.IsEmpty);
    }

    [Fact]
    public void Words_to_expand_are_the_segments_not_asked_for_yet()
    {
        var query = NameQuery.Literal("ka ka sa");
        Assert.Equal<string>(["ka", "sa"], query.WordsToExpand());

        var asked = query.WithPending(query.WordsToExpand());
        Assert.Empty(asked.WordsToExpand());

        var answered = asked.WithPattern("ka", null);
        Assert.Equal<string>(["sa"], answered.Pending);
        Assert.Empty(answered.WordsToExpand());
    }

    [Fact]
    public void Editing_keeps_the_patterns_of_the_words_still_there()
    {
        var query = NameQuery.Literal("nihon").WithPattern("nihon", Nihon);

        var longer = query.WithText("nihon txt");

        Assert.Same(Nihon, longer.Patterns["nihon"]);
        Assert.Equal<string>(["txt"], longer.WordsToExpand());
    }

    [Fact]
    public void Editing_drops_patterns_and_requests_for_words_that_are_gone()
    {
        var query = NameQuery.Literal("nihon ka").WithPattern("nihon", Nihon).WithPending(["ka"]);

        var edited = query.WithText("niho");

        Assert.Empty(edited.Patterns);
        Assert.Empty(edited.Pending);
    }

    [Fact]
    public void A_pattern_for_a_word_no_longer_typed_is_ignored()
    {
        var query = NameQuery.Literal("niho");

        Assert.Same(query, query.WithPattern("nihon", Nihon));
    }

    [Fact]
    public void A_regular_expression_is_one_pattern()
    {
        var query = NameQuery.RegularExpression(@"^\d+ - .*\.mp3$");

        Assert.False(query.IsInvalid);
        Assert.Empty(query.Segments);
        Assert.Equal(MatchTier.Substring, query.Match("01 - Intro.MP3"));
        Assert.Equal(MatchTier.None, query.Match("Intro.mp3"));
        Assert.Empty(query.WordsToExpand());
    }

    [Theory]
    [InlineData("(")]
    [InlineData("[a-")]
    [InlineData(@"(a)\1")]
    [InlineData("(?=a)b")]
    public void A_pattern_that_does_not_compile_matches_nothing(string text)
    {
        var query = NameQuery.RegularExpression(text);

        Assert.True(query.IsInvalid);
        Assert.Equal(MatchTier.None, query.Match(text));
    }

    [Fact]
    public void An_empty_pattern_is_empty_not_invalid()
    {
        var query = NameQuery.RegularExpression(string.Empty);

        Assert.True(query.IsEmpty);
        Assert.False(query.IsInvalid);
    }

    [Fact]
    public void Switching_to_a_regular_expression_and_back_keeps_the_text()
    {
        var literal = NameQuery.Literal("a.b").WithPattern("a.b", null);

        var regex = literal.AsRegex(true);
        Assert.True(regex.IsRegex);
        Assert.Equal(MatchTier.Substring, regex.Match("axb"));

        var back = regex.AsRegex(false);
        Assert.False(back.IsRegex);
        Assert.Equal(MatchTier.None, back.Match("axb"));
        Assert.Same(literal, literal.AsRegex(false));
    }

    [Fact]
    public void Unchanged_text_is_the_same_query()
    {
        var query = NameQuery.Literal("abc");

        Assert.Same(query, query.WithText("abc"));
    }
}
