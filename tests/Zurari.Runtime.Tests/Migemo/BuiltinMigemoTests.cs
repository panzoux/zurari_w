using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Zurari.Runtime.Migemo;

namespace Zurari.Runtime.Tests.Migemo;

public class BuiltinMigemoTests
{
    private static readonly Lazy<BuiltinMigemo> Shared = new(BuiltinMigemo.LoadEmbedded);

    private static List<(string Word, string Pattern)> ReadGolden()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Migemo", "golden.tsv");
        string text = File.ReadAllText(path, new UTF8Encoding(false));
        var result = new List<(string, string)>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            int tab = line.IndexOf('\t', StringComparison.Ordinal);
            Assert.True(tab > 0, "golden line has no tab: " + line);
            result.Add((line.Substring(0, tab), line.Substring(tab + 1)));
        }

        return result;
    }

    [Fact]
    public void Golden_file_has_every_required_word()
    {
        string[] required =
        [
            "a", "i", "ka", "kai", "kaisha", "nihon", "nihongo", "tt", "tta", "n", "nn", "kyou", "toukyou", "shi",
            "chi", "tsu", "fu", "ja", "xtu", "ltu", "wo", "vu", "abc", "ABC", "Tower", "toukyouOosaka", "東京", "ﾃｽﾄ",
            "テスト", "123", "a b", "-", ".", "(", "[", "\\", "*",
        ];
        var words = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string word, string _) in ReadGolden())
        {
            words.Add(word);
        }

        foreach (string word in required)
        {
            Assert.Contains(word, words);
        }
    }

    [Fact]
    public void Query_matches_rustmigemo_byte_for_byte()
    {
        BuiltinMigemo migemo = Shared.Value;
        var mismatches = new List<string>();
        int count = 0;
        foreach ((string word, string expected) in ReadGolden())
        {
            count++;
            string actual = migemo.Query(word);
            if (!string.Equals(actual, expected, StringComparison.Ordinal))
            {
                mismatches.Add($"word: {word}{Environment.NewLine}  expected: {expected}{Environment.NewLine}  actual:   {actual}");
            }
        }

        Assert.True(count >= 37, "golden file is too short: " + count.ToString(CultureInfo.InvariantCulture));
        Assert.True(mismatches.Count == 0, string.Join(Environment.NewLine, mismatches));
    }

    [Fact]
    public void Every_golden_pattern_compiles_as_a_nonbacktracking_regex()
    {
        const RegexOptions options = RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
        foreach ((string word, string pattern) in ReadGolden())
        {
            Regex? regex = null;
            Exception? failure = null;
            try
            {
                regex = new Regex(pattern, options);
            }
            catch (ArgumentException ex)
            {
                failure = ex;
            }

            Assert.True(regex is not null, $"pattern for '{word}' does not compile: {failure?.Message}");
        }
    }

    [Fact]
    public void Nihon_matches_the_kanji()
    {
        var regex = new Regex(Shared.Value.Query("nihon"), RegexOptions.NonBacktracking | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        Assert.Matches(regex, "日本");
        Assert.Matches(regex, "にほん");
        Assert.Matches(regex, "ニホン");
        Assert.Matches(regex, "nihon");
    }

    [Fact]
    public void Empty_query_gives_an_empty_pattern()
    {
        Assert.Equal(string.Empty, Shared.Value.Query(string.Empty));
    }

    [Fact]
    public void Query_is_safe_to_call_concurrently()
    {
        BuiltinMigemo migemo = Shared.Value;
        string[] words = ["nihon", "toukyou", "kaisha", "n", "tt", "Tower"];
        string[] expected = Array.ConvertAll(words, migemo.Query);
        var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
        Parallel.For(0, 64, i =>
        {
            int w = i % words.Length;
            string actual = migemo.Query(words[w]);
            if (!string.Equals(actual, expected[w], StringComparison.Ordinal))
            {
                failures.Add(words[w]);
            }
        });
        Assert.Empty(failures);
    }

    [Fact]
    public void Load_rejects_null()
    {
        Assert.Throws<ArgumentNullException>(() => BuiltinMigemo.Load(null!));
    }
}
