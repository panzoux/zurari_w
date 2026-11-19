using System.Text;

namespace Zurari.Runtime.Migemo;

/// <summary>Result of <see cref="RomajiProcessor.RomajiToHiraganaPredictively"/>: a fixed prefix plus the possible endings.</summary>
internal sealed record RomajiPredictiveResult(string Prefix, List<string> Suffixes);

/// <summary>
/// Romaji to hiragana conversion over built-in tries, ported from rustmigemo's <c>romaji_processor.rs</c>.
/// Immutable after construction, so it is safe to share between threads.
/// </summary>
internal sealed partial class RomajiProcessor
{
    private readonly LoudsTrie _keyTrie;
    private readonly BitVector _keyTerminals;
    private readonly LoudsTrie _valueTrie;
    private readonly MappingEntry?[] _nodeMappings;

    public RomajiProcessor()
    {
        _keyTrie = new LoudsTrie(new BitVector(RomajiKeyTrieBits, RomajiKeyTrieSize), RomajiKeyTrieEdges.ToCharArray());
        _keyTerminals = new BitVector(RomajiKeyTerminalBits, RomajiKeyTerminalSize);
        _valueTrie = new LoudsTrie(new BitVector(RomajiValueTrieBits, RomajiValueTrieSize), RomajiValueTrieEdges.ToCharArray());
        _nodeMappings = new MappingEntry?[_keyTerminals.Size];
        int mappingIdx = 0;
        for (int nodeIndex = 0; nodeIndex < _nodeMappings.Length; nodeIndex++)
        {
            if (!_keyTerminals.Get(nodeIndex))
            {
                continue;
            }

            if (mappingIdx >= RomajiMappingIndex.Length)
            {
                break;
            }

            _nodeMappings[nodeIndex] = new MappingEntry(RomajiMappingIndex[mappingIdx], RomajiMappingRemain[mappingIdx]);
            mappingIdx++;
        }
    }

    /// <summary>
    /// Converts as much of <paramref name="romaji"/> as is settled to hiragana (the prefix) and lists the
    /// hiragana an unfinished tail could still become. The suffix list is de-duplicated and sorted in
    /// descending ordinal order: rustmigemo iterates a Rust HashSet here (random order), and its generator
    /// drops a word that extends an earlier one, so the order must be fixed for stable output; descending
    /// inserts the longer endings first, which loses nothing.
    /// </summary>
    public RomajiPredictiveResult RomajiToHiraganaPredictively(string romaji)
    {
        if (romaji.Length == 0)
        {
            return new RomajiPredictiveResult(string.Empty, [string.Empty]);
        }

        var hiragana = new StringBuilder(romaji.Length);
        int start = 0;
        while (start < romaji.Length)
        {
            (int nodeIndex, int matchLen)? lastFound = null;
            List<int> path = _keyTrie.CommonPrefixSearch(romaji, start);
            for (int i = 0; i < path.Count; i++)
            {
                if (_keyTerminals.Get(path[i]))
                {
                    lastFound = (path[i], i + 1);
                }
            }

            List<int> terminalNodes = TerminalNodesForPrefix(romaji.Substring(start));
            if (terminalNodes.Count > 1)
            {
                return new RomajiPredictiveResult(hiragana.ToString(), ExpandSuffixes(romaji, terminalNodes));
            }

            if (lastFound is { } found && MappingEntryAt(found.nodeIndex) is { } entry)
            {
                hiragana.Append(_valueTrie.GetKey(entry.ValueIndex));
                start += found.matchLen - entry.Remain;
                continue;
            }

            hiragana.Append(romaji[start]);
            start++;
        }

        return new RomajiPredictiveResult(hiragana.ToString(), [string.Empty]);
    }

    private List<string> ExpandSuffixes(string romaji, List<int> terminalNodes)
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (int nodeIndex in terminalNodes)
        {
            if (MappingEntryAt(nodeIndex) is not { } entry)
            {
                continue;
            }

            if (entry.Remain > 0)
            {
                int remain = entry.Remain;
                if (romaji.Length < remain)
                {
                    continue;
                }

                int offset = romaji.Length - remain;
                foreach (int nextNode in TerminalNodesForPrefix(romaji.Substring(offset)))
                {
                    if (MappingEntryAt(nextNode) is { Remain: 0 } nextEntry)
                    {
                        set.Add(_valueTrie.GetKey(entry.ValueIndex) + _valueTrie.GetKey(nextEntry.ValueIndex));
                    }
                }
            }
            else
            {
                set.Add(_valueTrie.GetKey(entry.ValueIndex));
            }
        }

        var suffixes = new List<string>(set);
        suffixes.Sort(static (a, b) => string.CompareOrdinal(b, a));
        return suffixes;
    }

    private MappingEntry? MappingEntryAt(int nodeIndex) =>
        (uint)nodeIndex < (uint)_nodeMappings.Length ? _nodeMappings[nodeIndex] : null;

    private List<int> TerminalNodesForPrefix(string prefix)
    {
        var result = new List<int>();
        int nodeIndex = _keyTrie.Get(prefix);
        if (nodeIndex < 0)
        {
            return result;
        }

        foreach (int index in _keyTrie.PredictiveSearch(nodeIndex))
        {
            if (index < _keyTerminals.Size && _keyTerminals.Get(index))
            {
                result.Add(index);
            }
        }

        return result;
    }

    private readonly record struct MappingEntry(int ValueIndex, int Remain);
}
