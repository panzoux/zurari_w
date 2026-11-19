using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace Zurari.Core;

/// <summary>How well a name matched a <see cref="NameQuery"/>. Later members rank lower.</summary>
public enum MatchTier
{
    /// <summary>Not a match.</summary>
    None,

    /// <summary>The name starts with the first segment, and contains every other one.</summary>
    Prefix,

    /// <summary>The name contains every segment, or matches the regular expression.</summary>
    Substring,

    /// <summary>At least one segment matched only through its migemo pattern.</summary>
    Migemo,
}

/// <summary>
/// What the user typed into search or Leap, and how a name is tested against it. Pure data: the
/// migemo patterns are expanded elsewhere and handed in through <see cref="WithPattern"/>.
/// </summary>
/// <remarks>
/// <para>
/// A literal query is a set of AND segments separated by spaces, matched in any order: <c>map tex</c>
/// finds both <c>tex2map</c> and <c>map2tex</c>. <c>\ </c> is a literal space. This is zrr's Leap rule,
/// used for <c>/</c> search too.
/// </para>
/// <para>
/// A segment matches when it is a case-insensitive substring of the name or, failing that, when its
/// migemo pattern matches - so <c>nihon</c> finds <c>日本語.txt</c>. Every segment must match. zrr's Leap
/// ORed the segments in its migemo fallback; that contradicted its own AND rule and is not copied.
/// </para>
/// <para>
/// A regular-expression query is one pattern, compiled with <see cref="PatternOptions"/>. The
/// non-backtracking engine is the point: a pattern typed by the user runs on the UI thread, and must
/// not be able to stop it. A pattern that does not compile matches nothing (<see cref="IsInvalid"/>).
/// </para>
/// </remarks>
public sealed record NameQuery
{
    /// <summary>How every pattern is compiled, here and in the Runtime's migemo expansion.</summary>
    public const RegexOptions PatternOptions =
        RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    private readonly Regex? compiled;

    private NameQuery(string text, bool isRegex)
    {
        Text = text;
        IsRegex = isRegex;
        if (isRegex)
        {
            Segments = [];
            compiled = text.Length == 0 ? null : TryCompile(text);
            IsInvalid = text.Length > 0 && compiled is null;
        }
        else
        {
            Segments = SplitSegments(text);
        }
    }

    /// <summary>An empty literal query: matches nothing and filters nothing.</summary>
    public static NameQuery Empty { get; } = new(string.Empty, isRegex: false);

    /// <summary>A literal query of AND segments.</summary>
    public static NameQuery Literal(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new NameQuery(text, isRegex: false);
    }

    /// <summary>A regular-expression query. Check <see cref="IsInvalid"/>.</summary>
    public static NameQuery RegularExpression(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return new NameQuery(text, isRegex: true);
    }

    /// <summary>What was typed.</summary>
    public string Text { get; }

    /// <summary>Whether <see cref="Text"/> is a regular expression rather than literal segments.</summary>
    public bool IsRegex { get; }

    /// <summary>A regular expression that did not compile. Matches nothing.</summary>
    public bool IsInvalid { get; }

    /// <summary>Nothing to match: no segments, or an empty pattern.</summary>
    public bool IsEmpty => IsRegex ? Text.Length == 0 : Segments.IsEmpty;

    /// <summary>The literal segments, unescaped. Empty for a regular expression.</summary>
    public ImmutableArray<string> Segments { get; }

    /// <summary>
    /// The migemo patterns that have arrived, by segment. A <c>null</c> value means the word has no
    /// pattern (no migemo, or it did not compile) and matches literally only.
    /// </summary>
    public ImmutableDictionary<string, Regex?> Patterns { get; init; } =
        ImmutableDictionary.Create<string, Regex?>(StringComparer.Ordinal);

    /// <summary>Words whose expansion has been asked for and has not arrived.</summary>
    public ImmutableHashSet<string> Pending { get; init; } = ImmutableHashSet.Create<string>(StringComparer.Ordinal);

    /// <summary>
    /// Segments with no pattern and no request outstanding - what to ask the Runtime to expand.
    /// Always empty for a regular expression, which has no migemo.
    /// </summary>
    public ImmutableArray<string> WordsToExpand()
    {
        if (IsRegex)
        {
            return [];
        }

        var words = ImmutableArray.CreateBuilder<string>();
        foreach (var segment in Segments)
        {
            if (!Patterns.ContainsKey(segment) && !Pending.Contains(segment) && !words.Contains(segment))
            {
                words.Add(segment);
            }
        }

        return words.ToImmutable();
    }

    /// <summary>Records that <paramref name="words"/> have been asked for.</summary>
    public NameQuery WithPending(ImmutableArray<string> words) =>
        words.IsEmpty ? this : this with { Pending = Pending.Union(words) };

    /// <summary>
    /// The same kind of query with new text. Patterns and outstanding requests for words still in it
    /// are kept, so typing one more segment does not ask again for the ones before it.
    /// </summary>
    public NameQuery WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (string.Equals(text, Text, StringComparison.Ordinal))
        {
            return this;
        }

        return Carry(IsRegex ? RegularExpression(text) : Literal(text));
    }

    /// <summary>The same text as a regular expression, or back as literal segments.</summary>
    public NameQuery AsRegex(bool isRegex) =>
        isRegex == IsRegex ? this : Carry(isRegex ? RegularExpression(Text) : Literal(Text));

    /// <summary>
    /// Takes the expansion of <paramref name="word"/>. A word that is no longer one of the segments
    /// is a reply to text that has since been edited, and is ignored (the same instance comes back).
    /// </summary>
    public NameQuery WithPattern(string word, Regex? pattern)
    {
        ArgumentNullException.ThrowIfNull(word);
        if (IsRegex || !Segments.Contains(word))
        {
            return this;
        }

        return this with { Patterns = Patterns.SetItem(word, pattern), Pending = Pending.Remove(word) };
    }

    /// <summary>How <paramref name="name"/> matches, or <see cref="MatchTier.None"/>.</summary>
    public MatchTier Match(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (IsRegex)
        {
            return compiled is not null && compiled.IsMatch(name) ? MatchTier.Substring : MatchTier.None;
        }

        if (Segments.IsEmpty)
        {
            return MatchTier.None;
        }

        var neededMigemo = false;
        foreach (var segment in Segments)
        {
            if (name.Contains(segment, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (Patterns.TryGetValue(segment, out var pattern) && pattern is not null && pattern.IsMatch(name))
            {
                neededMigemo = true;
                continue;
            }

            return MatchTier.None;
        }

        if (neededMigemo)
        {
            return MatchTier.Migemo;
        }

        return name.StartsWith(Segments[0], StringComparison.OrdinalIgnoreCase) ? MatchTier.Prefix : MatchTier.Substring;
    }

    /// <summary>
    /// Splits literal text into segments: a space separates, <c>\ </c> is a space inside a segment,
    /// and empty segments (leading, trailing or doubled spaces) are dropped.
    /// </summary>
    public static ImmutableArray<string> SplitSegments(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var segments = ImmutableArray.CreateBuilder<string>();
        var current = new System.Text.StringBuilder();
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] == '\\' && i + 1 < text.Length && text[i + 1] == ' ')
            {
                current.Append(' ');
                i++;
            }
            else if (text[i] == ' ')
            {
                if (current.Length > 0)
                {
                    segments.Add(current.ToString());
                    current.Clear();
                }
            }
            else
            {
                current.Append(text[i]);
            }
        }

        if (current.Length > 0)
        {
            segments.Add(current.ToString());
        }

        return segments.ToImmutable();
    }

    private NameQuery Carry(NameQuery next)
    {
        if (next.IsRegex)
        {
            return next;
        }

        var patterns = next.Patterns;
        foreach (var (word, pattern) in Patterns)
        {
            if (next.Segments.Contains(word))
            {
                patterns = patterns.SetItem(word, pattern);
            }
        }

        var pending = next.Pending;
        foreach (var word in Pending)
        {
            if (next.Segments.Contains(word))
            {
                pending = pending.Add(word);
            }
        }

        return next with { Patterns = patterns, Pending = pending };
    }

    private static Regex? TryCompile(string text)
    {
        try
        {
            return new Regex(text, PatternOptions);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }
}
