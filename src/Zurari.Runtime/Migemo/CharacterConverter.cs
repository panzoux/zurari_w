using System.Text;

namespace Zurari.Runtime.Migemo;

/// <summary>Half-width / full-width and hiragana / katakana conversion, ported from rustmigemo's <c>character_converter.rs</c>.</summary>
internal static partial class CharacterConverter
{
    private static readonly Dictionary<int, int> Han2ZenMap = new();
    private static readonly Dictionary<int, string> Zen2HanMap = new();

    static CharacterConverter()
    {
        // Indexer assignment: a later duplicate wins, like Rust's HashMap collect.
        foreach ((int han, int zen) in Han2ZenPairs)
        {
            Han2ZenMap[han] = zen;
        }

        foreach ((int zen, string han) in Zen2HanPairs)
        {
            Zen2HanMap[zen] = han;
        }
    }

    /// <summary>Decodes UTF-16 into Unicode scalars; a lone surrogate becomes U+FFFD (Rust's lossy decode).</summary>
    public static int[] ToCodePoints(string s)
    {
        var result = new List<int>(s.Length);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
            {
                result.Add(char.ConvertToUtf32(c, s[i + 1]));
                i++;
            }
            else if (char.IsSurrogate(c))
            {
                result.Add(0xFFFD);
            }
            else
            {
                result.Add(c);
            }
        }

        return result.ToArray();
    }

    /// <summary>The string for <paramref name="source"/> with lone surrogates replaced by U+FFFD.</summary>
    public static string ToLossy(string source)
    {
        var sb = new StringBuilder(source.Length);
        foreach (int cp in ToCodePoints(source))
        {
            sb.Append(char.ConvertFromUtf32(cp));
        }

        return sb.ToString();
    }

    public static string Han2Zen(string source)
    {
        var sb = new StringBuilder(source.Length);
        foreach (int cp in ToCodePoints(source))
        {
            sb.Append(char.ConvertFromUtf32(Han2ZenMap.TryGetValue(cp, out int zen) ? zen : cp));
        }

        return sb.ToString();
    }

    public static string Zen2Han(string source)
    {
        var sb = new StringBuilder(source.Length);
        foreach (int cp in ToCodePoints(source))
        {
            if (Zen2HanMap.TryGetValue(cp, out string? han))
            {
                sb.Append(han);
            }
            else
            {
                sb.Append(char.ConvertFromUtf32(cp));
            }
        }

        return sb.ToString();
    }

    /// <summary>Hiragana U+3041..U+3093 to katakana; everything else unchanged.</summary>
    public static string Hira2Kata(string source)
    {
        var sb = new StringBuilder(source.Length);
        foreach (int cp in ToCodePoints(source))
        {
            sb.Append(char.ConvertFromUtf32(cp >= 0x3041 && cp <= 0x3093 ? cp - 0x3041 + 0x30A1 : cp));
        }

        return sb.ToString();
    }
}
