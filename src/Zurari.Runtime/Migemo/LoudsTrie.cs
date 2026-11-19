using System.Text;

namespace Zurari.Runtime.Migemo;

/// <summary>Immutable LOUDS trie over UTF-16 code units, ported from rustmigemo's <c>louds_trie.rs</c>.</summary>
internal sealed class LoudsTrie
{
    private readonly BitVector _bitVector;
    private readonly char[] _edges;

    public LoudsTrie(BitVector bitVector, char[] edges)
    {
        _bitVector = bitVector;
        _edges = edges;
    }

    /// <summary>The key (code units, root to node) of the 1-indexed node <paramref name="index"/>.</summary>
    public string GetKey(int index)
    {
        if (index <= 0 || index >= _edges.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        var sb = new StringBuilder();
        while (index > 1)
        {
            sb.Append(_edges[index]);
            index = Parent(index);
        }

        char[] chars = new char[sb.Length];
        for (int i = 0; i < chars.Length; i++)
        {
            chars[i] = sb[chars.Length - 1 - i];
        }

        return new string(chars);
    }

    public int Parent(int x) => _bitVector.Rank(_bitVector.Select(x, true), false);

    /// <summary>First child of node <paramref name="x"/>, or -1 when it has none.</summary>
    public int FirstChild(int x)
    {
        int y = _bitVector.Select(x, false) + 1;
        return _bitVector.Get(y) ? _bitVector.Rank(y, true) + 1 : -1;
    }

    /// <summary>The child of <paramref name="index"/> reached by edge <paramref name="c"/>, or -1.</summary>
    public int Traverse(int index, char c)
    {
        int firstChild = FirstChild(index);
        if (firstChild < 0)
        {
            return -1;
        }

        int childStartBit = _bitVector.Select(firstChild, true);
        int childEndBit = _bitVector.NextClearBit(childStartBit);
        int childSize = childEndBit - childStartBit;
        int found = Array.BinarySearch(_edges, firstChild, childSize, c);
        return found >= 0 ? found : -1;
    }

    /// <summary>The node for <paramref name="key"/>, or -1 when absent.</summary>
    public int Get(string key)
    {
        int nodeIndex = 1;
        foreach (char c in key)
        {
            nodeIndex = Traverse(nodeIndex, c);
            if (nodeIndex < 0)
            {
                return -1;
            }
        }

        return nodeIndex;
    }

    /// <summary>
    /// Nodes found by a predictive search from <paramref name="node"/>, in rustmigemo's iteration order
    /// (including its repeat of the first node of each deeper level, which is harmless downstream).
    /// </summary>
    public List<int> PredictiveSearch(int node)
    {
        var result = new List<int>();
        int lower = node;
        int upper = node + 1;
        int cursor = lower;
        int size = _bitVector.Size;
        while (true)
        {
            if (cursor < upper)
            {
                result.Add(cursor);
                cursor++;
                continue;
            }

            if (upper == lower)
            {
                break;
            }

            int lowerSelect = _bitVector.Select(lower, false);
            int upperSelect = _bitVector.Select(upper, false);
            lower = lowerSelect + 1 <= size
                ? _bitVector.Rank(lowerSelect + 1, true) + 1
                : _bitVector.Rank(size, true) + 1;
            upper = upperSelect + 1 <= size
                ? _bitVector.Rank(upperSelect + 1, true) + 1
                : _bitVector.Rank(size, true) + 1;
            cursor = lower;
            if (lower == upper)
            {
                break;
            }

            result.Add(lower);
        }

        return result;
    }

    /// <summary>Nodes along <paramref name="key"/> starting at <paramref name="start"/>, one per consumed unit, stopping at the first miss.</summary>
    public List<int> CommonPrefixSearch(string key, int start)
    {
        var result = new List<int>();
        int node = 1;
        for (int i = start; i < key.Length; i++)
        {
            node = Traverse(node, key[i]);
            if (node < 0)
            {
                break;
            }

            result.Add(node);
        }

        return result;
    }
}
