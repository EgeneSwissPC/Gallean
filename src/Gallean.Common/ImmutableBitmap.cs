using System.Numerics;
using System.Runtime.InteropServices;

namespace Gallean.Common;

public sealed partial record ImmutableBitmap
{
    // Bits are stored 64 to a word, bit i of the bitmap in bit (i % 64) of word i / 64, which is the same order as the
    // packed bytes (bit i in bit i % 8 of byte i / 8) on little-endian machines. Bits past Width * Height stay zero, so
    // whole words can be compared, counted and diffed.
    private readonly ulong[] words;
    private int hashCode;
    private bool hashCodeComputed;

    private ImmutableBitmap(int width, int height, ulong[] words)
    {
        Width = width;
        Height = height;
        this.words = words;
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>The bits, 64 per word (bit i in bit i % 64 of word i / 64); bits past Width * Height are zero.</summary>
    public ReadOnlySpan<ulong> GetWords() => words;

    public bool Equals(ImmutableBitmap? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Width == other.Width && Height == other.Height && words.AsSpan().SequenceEqual(other.words);
    }

    public static ImmutableBitmap Create(int width, int height, bool initialValue = false)
    {
        int bitCount = GetBitCount(width, height);
        ulong[] data = new ulong[GetWordCount(bitCount)];

        if (initialValue)
        {
            Array.Fill(data, ulong.MaxValue);
            ClearPadding(data, bitCount);
        }

        return new ImmutableBitmap(width, height, data);
    }

    public bool Get(int x, int y)
    {
        int index = y * Width + x;

        if ((uint)index >= (uint)(Width * Height))
        {
            throw new ArgumentOutOfRangeException(nameof(x));
        }

        return (words[index >> 6] & (1UL << index)) != 0;
    }

    /// <summary>Number of set bits.</summary>
    public int CountSet()
    {
        int count = 0;

        foreach (ulong word in words)
        {
            count += BitOperations.PopCount(word);
        }

        return count;
    }

    public byte[] ToBits()
    {
        byte[] bits = new byte[GetByteCount(GetBitCount(Width, Height))];

        MemoryMarshal.AsBytes(words.AsSpan())[..bits.Length].CopyTo(bits);

        return bits;
    }

    public Builder ToBuilder()
    {
        return new Builder(this);
    }

    public override int GetHashCode()
    {
        if (!hashCodeComputed)
        {
            HashCode hash = new();

            hash.Add(Width);
            hash.Add(Height);
            hash.AddBytes(MemoryMarshal.AsBytes(words.AsSpan()));
            hashCode = hash.ToHashCode();
            hashCodeComputed = true;
        }

        return hashCode;
    }

    private static int GetBitCount(int width, int height)
    {
        return checked(width * height);
    }

    private static int GetByteCount(int bitCount)
    {
        return (bitCount + 7) / 8;
    }

    private static int GetWordCount(int bitCount)
    {
        return (bitCount + 63) / 64;
    }

    private static void ClearPadding(ulong[] data, int bitCount)
    {
        int used = bitCount & 63;

        if (used != 0)
        {
            data[^1] &= (1UL << used) - 1;
        }
    }
}
