using System.Buffers.Binary;

namespace Zurari.Runtime.Migemo;

/// <summary>Reader for rustmigemo's compact dictionary format (<c>compact_dictionary.rs</c>); immutable once loaded.</summary>
internal sealed class CompactDictionary
{
    private readonly LoudsTrie _keyTrie;
    private readonly LoudsTrie _valueTrie;
    private readonly BitVector _mappingBitVector;
    private readonly uint[] _mapping;
    private readonly bool[] _hasMapping;

    private CompactDictionary(LoudsTrie keyTrie, LoudsTrie valueTrie, BitVector mappingBitVector, uint[] mapping)
    {
        _keyTrie = keyTrie;
        _valueTrie = valueTrie;
        _mappingBitVector = mappingBitVector;
        _mapping = mapping;
        _hasMapping = CreateHasMapping(mappingBitVector);
    }

    public static CompactDictionary Parse(byte[] buffer)
    {
        var reader = new Reader(buffer);
        LoudsTrie keyTrie = ReadTrie(reader, compactHiragana: true);
        LoudsTrie valueTrie = ReadTrie(reader, compactHiragana: false);
        BitVector mappingBitVector = ReadBitVector(reader);
        int mappingSize = checked((int)reader.ReadUInt32());
        uint[] mapping = new uint[mappingSize];
        for (int i = 0; i < mapping.Length; i++)
        {
            mapping[i] = reader.ReadUInt32();
        }

        return new CompactDictionary(keyTrie, valueTrie, mappingBitVector, mapping);
    }

    /// <summary>All values of keys that start with <paramref name="key"/>, in rustmigemo's order.</summary>
    public List<string> PredictiveSearch(string key)
    {
        var result = new List<string>();
        int keyIndex = _keyTrie.Get(key);
        if (keyIndex <= 1)
        {
            return result;
        }

        foreach (int nodeIndex in _keyTrie.PredictiveSearch(keyIndex))
        {
            if (nodeIndex >= _hasMapping.Length || !_hasMapping[nodeIndex])
            {
                continue;
            }

            int valueStartPos = _mappingBitVector.Select(nodeIndex, false);
            int valueEndPos = _mappingBitVector.NextClearBit(valueStartPos + 1);
            int size = valueEndPos - valueStartPos - 1;
            if (size <= 0)
            {
                continue;
            }

            int offset = _mappingBitVector.Rank(valueStartPos, false);
            int start = valueStartPos - offset;
            for (int i = start; i < start + size; i++)
            {
                result.Add(_valueTrie.GetKey((int)_mapping[i]));
            }
        }

        return result;
    }

    private static LoudsTrie ReadTrie(Reader reader, bool compactHiragana)
    {
        int edgeCount = checked((int)reader.ReadUInt32());
        char[] edges = new char[edgeCount];
        for (int i = 0; i < edges.Length; i++)
        {
            edges[i] = compactHiragana ? Decode(reader.ReadByte()) : (char)reader.ReadUInt16();
        }

        return new LoudsTrie(ReadBitVector(reader), edges);
    }

    private static BitVector ReadBitVector(Reader reader)
    {
        int sizeInBits = checked((int)reader.ReadUInt32());
        ulong[] words = new ulong[(sizeInBits + 63) / 64];
        for (int i = 0; i < words.Length; i++)
        {
            words[i] = reader.ReadUInt64();
        }

        return new BitVector(words, sizeInBits);
    }

    private static bool[] CreateHasMapping(BitVector bitVector)
    {
        int numOfNodes = bitVector.Rank(bitVector.Size, false);
        bool[] result = new bool[numOfNodes + 1];
        int bitPosition = 0;
        for (int node = 1; node <= numOfNodes; node++)
        {
            result[node] = bitVector.Get(bitPosition + 1);
            bitPosition = bitVector.NextClearBit(bitPosition + 1);
        }

        return result;
    }

    /// <summary>The key trie stores hiragana and ASCII in one byte each.</summary>
    private static char Decode(byte b)
    {
        if (b >= 0x20 && b <= 0x7E)
        {
            return (char)b;
        }

        if (b >= 0xA1 && b <= 0xF6)
        {
            return (char)(b - 0xA0 + 0x3040);
        }

        return b == 0xF7 ? 'ー' : '\0';
    }

    private sealed class Reader
    {
        private readonly byte[] _buffer;
        private int _position;

        public Reader(byte[] buffer)
        {
            _buffer = buffer;
        }

        public byte ReadByte() => _buffer[_position++];

        public ushort ReadUInt16()
        {
            ushort v = BinaryPrimitives.ReadUInt16BigEndian(_buffer.AsSpan(_position));
            _position += 2;
            return v;
        }

        public uint ReadUInt32()
        {
            uint v = BinaryPrimitives.ReadUInt32BigEndian(_buffer.AsSpan(_position));
            _position += 4;
            return v;
        }

        public ulong ReadUInt64()
        {
            ulong v = BinaryPrimitives.ReadUInt64BigEndian(_buffer.AsSpan(_position));
            _position += 8;
            return v;
        }
    }
}
