using System.Numerics;

namespace Zurari.Runtime.Migemo;

/// <summary>
/// Immutable bit vector with rank/select, ported from rustmigemo's <c>bit_vector.rs</c>.
/// <c>Rank</c> counts bits in <c>[0, pos)</c>; <c>Select</c> takes a 1-indexed count.
/// </summary>
internal sealed class BitVector
{
    private readonly ulong[] _words;
    private readonly int _sizeInBits;
    private readonly uint[] _lb;
    private readonly ushort[] _sb;

    public BitVector(ulong[] words, int sizeInBits)
    {
        if ((sizeInBits + 63) / 64 != words.Length)
        {
            throw new ArgumentException("Word array length does not match the size in bits.", nameof(words));
        }

        _words = words;
        _sizeInBits = sizeInBits;
        _lb = new uint[(sizeInBits + 511) / 512];
        _sb = new ushort[_lb.Length * 8];
        uint sum = 0;
        uint sumInLb = 0;
        for (int i = 0; i < _sb.Length; i++)
        {
            uint bitCount = i < words.Length ? (uint)BitOperations.PopCount(words[i]) : 0;
            _sb[i] = (ushort)sumInLb;
            sumInLb += bitCount;
            if ((i & 7) == 7)
            {
                _lb[i >> 3] = sum;
                sum += sumInLb;
                sumInLb = 0;
            }
        }
    }

    public int Size => _sizeInBits;

    public bool Get(int pos)
    {
        if ((uint)pos >= (uint)_sizeInBits)
        {
            throw new ArgumentOutOfRangeException(nameof(pos));
        }

        return ((_words[pos >> 6] >> (pos & 63)) & 1) == 1;
    }

    public int Rank(int pos, bool b)
    {
        if ((uint)pos > (uint)_sizeInBits)
        {
            throw new ArgumentOutOfRangeException(nameof(pos));
        }

        int count1 = _sb[pos / 64] + (int)_lb[pos / 512];
        int wordIndex = pos / 64;
        if (wordIndex < _words.Length)
        {
            int shift = 64 - (pos & 63);
            ulong mask = shift == 64 ? 0 : ulong.MaxValue >> shift;
            count1 += BitOperations.PopCount(_words[wordIndex] & mask);
        }

        return b ? count1 : pos - count1;
    }

    public int Select(int count, bool b)
    {
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), "select() requires a 1-indexed count.");
        }

        int lbIndex = LowerBoundLb((uint)count, b) - 1;
        int countInLb = b
            ? count - (int)_lb[lbIndex]
            : count - ((512 * lbIndex) - (int)_lb[lbIndex]);
        int sbIndex = LowerBoundSb(countInLb, lbIndex * 8, (lbIndex * 8) + 8, b) - 1;
        int countInSb = b
            ? countInLb - _sb[sbIndex]
            : countInLb - ((64 * (sbIndex % 8)) - _sb[sbIndex]);
        ulong word = _words[sbIndex];
        if (!b)
        {
            word = ~word;
        }

        return (sbIndex * 64) + SelectInWord(word, countInSb);
    }

    public int NextClearBit(int fromIndex)
    {
        int u = fromIndex >> 6;
        if (u >= _words.Length)
        {
            return fromIndex;
        }

        ulong word = ~_words[u] & (ulong.MaxValue << (fromIndex & 63));
        while (true)
        {
            if (word != 0)
            {
                return (u * 64) + BitOperations.TrailingZeroCount(word);
            }

            u++;
            if (u == _words.Length)
            {
                return 64 * _words.Length;
            }

            word = ~_words[u];
        }
    }

    /// <summary>Position of the <paramref name="count"/>-th (1-indexed) set bit of <paramref name="word"/>.</summary>
    private static int SelectInWord(ulong word, int count)
    {
        for (int i = 1; i < count; i++)
        {
            word &= word - 1;
        }

        return BitOperations.TrailingZeroCount(word);
    }

    private int LowerBoundLb(uint key, bool b)
    {
        long high = _lb.Length;
        long low = -1;
        while (high - low > 1)
        {
            long mid = (high + low) / 2;
            long value = b ? _lb[mid] : (mid << 9) - _lb[mid];
            if (value < key)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (int)high;
    }

    private int LowerBoundSb(int key, int fromIndex, int toIndex, bool b)
    {
        long high = toIndex;
        long low = (long)fromIndex - 1;
        while (high - low > 1)
        {
            long mid = (high + low) / 2;
            long value = b ? _sb[mid] : ((mid & 7) << 6) - _sb[mid];
            if (value < key)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return (int)high;
    }
}
