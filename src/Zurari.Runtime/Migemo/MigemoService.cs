using System.Text.RegularExpressions;
using Zurari.Core;

namespace Zurari.Runtime.Migemo;

/// <summary>
/// Turns a romaji word into a compiled pattern for <see cref="Msg.MigemoExpanded"/>: cmigemo when the
/// user has one, otherwise the built-in port. Remembers the last <see cref="Capacity"/> words.
/// </summary>
/// <remarks>
/// <para>
/// The cache is per word, as wappsw measured worthwhile: <c>afx 2</c> and <c>afx 3</c> share
/// <c>afx</c>, and backspacing over a word and typing it again costs nothing. What is cached is the
/// compiled <see cref="Regex"/>, so a repeat costs neither the expansion nor the compilation.
/// </para>
/// <para>
/// The pattern is compiled here, off the UI thread, with <see cref="NameQuery.PatternOptions"/>. One
/// that does not compile - cmigemo can emit syntax .NET rejects - is reported as no pattern, and the
/// word then matches by its letters only.
/// </para>
/// </remarks>
public sealed class MigemoService : IDisposable
{
    /// <summary>How many words are remembered.</summary>
    public const int Capacity = 100;

    private readonly Func<string, string?> query;
    private readonly IDisposable? owned;
    private readonly object gate = new();
    private readonly Dictionary<string, LinkedListNode<(string Word, Regex? Pattern)>> entries = new(StringComparer.Ordinal);
    private readonly LinkedList<(string Word, Regex? Pattern)> recency = new();

    /// <summary>Wraps a raw expansion (word to pattern text, or <c>null</c>).</summary>
    /// <param name="query">The expansion.</param>
    /// <param name="source">What does the expanding, for diagnostics: <c>cmigemo</c> or <c>built-in</c>.</param>
    public MigemoService(Func<string, string?> query, string source)
        : this(query, source, owned: null)
    {
    }

    private MigemoService(Func<string, string?> query, string source, IDisposable? owned)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(source);
        this.query = query;
        this.owned = owned;
        Source = source;
    }

    /// <summary>What does the expanding: <c>cmigemo</c> or <c>built-in</c>.</summary>
    public string Source { get; }

    /// <summary>
    /// cmigemo from <see cref="CMigemo.CandidateFolders"/> when one loads, else the built-in port. The
    /// built-in dictionary is read on the first expansion, not here, so starting the app does not pay
    /// for it.
    /// </summary>
    public static MigemoService CreateDefault() => Create(CMigemo.CandidateFolders());

    /// <summary><see cref="CreateDefault"/>, searching <paramref name="cmigemoFolders"/> instead.</summary>
    internal static MigemoService Create(IEnumerable<string> cmigemoFolders)
    {
        if (CMigemo.TryLoad(cmigemoFolders) is { } cmigemo)
        {
            return new MigemoService(cmigemo.Query, "cmigemo", cmigemo);
        }

        var builtin = new Lazy<BuiltinMigemo>(BuiltinMigemo.LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);
        return new MigemoService(word => builtin.Value.Query(word), "built-in");
    }

    /// <summary>The compiled pattern for <paramref name="word"/>, or <c>null</c> when there is none.</summary>
    public Regex? Expand(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        lock (gate)
        {
            if (entries.TryGetValue(word, out var hit))
            {
                recency.Remove(hit);
                recency.AddFirst(hit);
                return hit.Value.Pattern;
            }
        }

        var pattern = Compile(query(word));

        lock (gate)
        {
            // Two threads may have expanded the same word; the first one stored wins, so every caller
            // gets the same instance.
            if (entries.TryGetValue(word, out var raced))
            {
                return raced.Value.Pattern;
            }

            entries[word] = recency.AddFirst((word, pattern));
            if (entries.Count > Capacity)
            {
                var oldest = recency.Last!;
                recency.RemoveLast();
                entries.Remove(oldest.Value.Word);
            }

            return pattern;
        }
    }

    private static Regex? Compile(string? pattern)
    {
        if (string.IsNullOrEmpty(pattern))
        {
            return null;
        }

        try
        {
            return new Regex(pattern, NameQuery.PatternOptions);
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

    public void Dispose() => owned?.Dispose();
}
