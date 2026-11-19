using System.Text;

namespace Zurari.Runtime.Migemo;

/// <summary>The migemo query path, ported from rustmigemo's <c>query.rs</c> (<c>RegexOperator::Default</c>).</summary>
internal static class MigemoQuery
{
    public static string Query(string word, CompactDictionary dict, RomajiProcessor romaji)
    {
        if (word.Length == 0)
        {
            return string.Empty;
        }

        var result = new StringBuilder();
        foreach (string token in Tokenize(word))
        {
            result.Append(QueryAWord(token, dict, romaji));
        }

        return result.ToString();
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Globalization",
        "CA1308:Normalize strings to uppercase",
        Justification = "Not normalization: the dictionary keys and romaji tables are lowercase, as in rustmigemo.")]
    public static string QueryAWord(string word, CompactDictionary dict, RomajiProcessor romaji)
    {
        var generator = new TernaryRegexGenerator();
        generator.Add(CharacterConverter.ToCodePoints(word));

        // Rust's str::to_lowercase; ToLowerInvariant differs only for context-dependent special cases.
        string lower = word.ToLowerInvariant();
        foreach (string elem in dict.PredictiveSearch(lower))
        {
            generator.Add(CharacterConverter.ToCodePoints(elem));
        }

        generator.Add(CharacterConverter.ToCodePoints(CharacterConverter.Han2Zen(word)));
        generator.Add(CharacterConverter.ToCodePoints(CharacterConverter.Zen2Han(word)));

        RomajiPredictiveResult hiragana = romaji.RomajiToHiraganaPredictively(lower);
        foreach (string suffix in hiragana.Suffixes)
        {
            string hira = hiragana.Prefix + suffix;
            generator.Add(CharacterConverter.ToCodePoints(hira));
            foreach (string elem in dict.PredictiveSearch(hira))
            {
                generator.Add(CharacterConverter.ToCodePoints(elem));
            }

            string kata = CharacterConverter.Hira2Kata(CharacterConverter.ToLossy(hira));
            generator.Add(CharacterConverter.ToCodePoints(kata));
            generator.Add(CharacterConverter.ToCodePoints(CharacterConverter.Zen2Han(kata)));
        }

        return generator.Generate();
    }

    /// <summary>
    /// Splits a query into words at whitespace and at case / script boundaries
    /// (<c>toukyouOosaka</c> becomes <c>toukyou</c>, <c>Oosaka</c>; <c>東京Tower</c> becomes <c>東京</c>, <c>Tower</c>).
    /// Rust's Unicode lower/upper/whitespace properties are approximated with .NET's rune categories.
    /// </summary>
    public static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        var runes = new List<(int Index, Rune Value)>();
        int index = 0;
        foreach (Rune r in input.EnumerateRunes())
        {
            runes.Add((index, r));
            index += r.Utf16SequenceLength;
        }

        int pos = 0;
        while (true)
        {
            while (pos < runes.Count && Rune.IsWhiteSpace(runes[pos].Value))
            {
                pos++;
            }

            if (pos >= runes.Count)
            {
                break;
            }

            int startPos = pos;
            pos++;
            while (pos < runes.Count)
            {
                Rune current = runes[pos].Value;
                if (Rune.IsWhiteSpace(current))
                {
                    break;
                }

                Rune prev = runes[pos - 1].Value;
                if (IsBoundary(runes, startPos, pos, prev, current))
                {
                    break;
                }

                pos++;
            }

            int endIndex = pos < runes.Count ? runes[pos].Index : input.Length;
            tokens.Add(input.Substring(runes[startPos].Index, endIndex - runes[startPos].Index));
        }

        return tokens;
    }

    private static bool IsBoundary(List<(int Index, Rune Value)> runes, int startPos, int pos, Rune prev, Rune current)
    {
        // Rule 1: lowercase followed by uppercase.
        if (Rune.IsLower(prev) && Rune.IsUpper(current))
        {
            return true;
        }

        // Rule 2: two or more uppercase letters followed by lowercase ("FUKUOKAh").
        if (Rune.IsUpper(prev) && Rune.IsLower(current) && pos - startPos > 1)
        {
            bool allUpper = true;
            for (int i = startPos; i < pos; i++)
            {
                if (!Rune.IsUpper(runes[i].Value))
                {
                    allUpper = false;
                    break;
                }
            }

            if (allUpper)
            {
                return true;
            }
        }

        // Rules 3 and 4: ASCII alphanumeric next to non-ASCII-alphanumeric.
        return IsAsciiAlphanumeric(prev) != IsAsciiAlphanumeric(current);
    }

    private static bool IsAsciiAlphanumeric(Rune r) =>
        r.Value < 0x80 && (char.IsAsciiLetterOrDigit((char)r.Value));
}
