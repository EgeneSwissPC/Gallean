using System.Runtime.CompilerServices;
using Gallean.Common;

namespace Gallean.Client;

// For every tile, whether any tile of the window from Padding tiles up and left of it down to the tile itself
// (x - Padding .. x, y - Padding .. y) is explored. The composer draws an object when any tile of that window around its
// footprint is explored, and asking the fog bitmap 64 times per hidden object (124k forests on The War of the Ring)
// cost ~12 ms per step that revealed a tile. Built once per fog bitmap with word operations.
internal sealed class FogWindowMask
{
    public const int Padding = 7;

    private static readonly ConditionalWeakTable<ImmutableBitmap, FogWindowMask> Cache = new();

    private readonly int rowWords;
    private readonly ulong[] words;

    private FogWindowMask(ImmutableBitmap fog)
    {
        Width = fog.Width;
        Height = fog.Height;
        rowWords = (Width + 63) / 64;

        ulong[] rows = LoadRows(fog, rowWords);
        ulong[] spread = new ulong[rows.Length];

        // Along each row: tile x is set when any of x - Padding .. x is explored.
        for (int y = 0; y < Height; y++)
        {
            int start = y * rowWords;

            for (int k = 0; k < rowWords; k++)
            {
                ulong word = rows[start + k];
                ulong previous = k > 0 ? rows[start + k - 1] : 0;
                ulong value = word;

                for (int shift = 1; shift <= Padding; shift++)
                {
                    value |= (word << shift) | (previous >> (64 - shift));
                }

                spread[start + k] = value;
            }

            ClearRowPadding(spread, start);
        }

        // Down each column: tile y is set when any of y - Padding .. y is set in the row pass.
        words = new ulong[rows.Length];

        for (int y = 0; y < Height; y++)
        {
            int start = y * rowWords;

            for (int k = 0; k < rowWords; k++)
            {
                ulong value = 0;

                for (int up = 0; up <= Padding && up <= y; up++)
                {
                    value |= spread[start - (up * rowWords) + k];
                }

                words[start + k] = value;
            }
        }
    }

    public int Width { get; }
    public int Height { get; }

    public static FogWindowMask For(ImmutableBitmap fog)
    {
        return Cache.GetValue(fog, static bitmap => new FogWindowMask(bitmap));
    }

    public bool Get(int x, int y)
    {
        return (words[(y * rowWords) + (x >> 6)] & (1UL << x)) != 0;
    }

    /// <summary>Whether any tile from Padding tiles up and left of the footprint to its bottom-right corner is explored.</summary>
    public bool IsAnyExploredAround(WorldFootprint footprint)
    {
        WorldPosition topLeft = footprint.TopLeft;

        for (int y = topLeft.Y; y < topLeft.Y + footprint.Height; y++)
        {
            for (int x = topLeft.X; x < topLeft.X + footprint.Width; x++)
            {
                if (Get(x, y))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public bool Contains(WorldFootprint footprint)
    {
        WorldPosition topLeft = footprint.TopLeft;

        return topLeft.X >= 0 && topLeft.Y >= 0 && topLeft.X + footprint.Width <= Width && topLeft.Y + footprint.Height <= Height;
    }

    // The fog's bits with each row starting on a word boundary.
    private static ulong[] LoadRows(ImmutableBitmap fog, int rowWords)
    {
        ReadOnlySpan<ulong> source = fog.GetWords();
        ulong[] rows = new ulong[fog.Height * rowWords];

        for (int y = 0; y < fog.Height; y++)
        {
            for (int k = 0; k < rowWords; k++)
            {
                int bit = (y * fog.Width) + (k * 64);
                int index = bit >> 6;
                int shift = bit & 63;
                ulong value = source[index] >> shift;

                if (shift != 0 && index + 1 < source.Length)
                {
                    value |= source[index + 1] << (64 - shift);
                }

                rows[(y * rowWords) + k] = value;
            }
        }

        int used = fog.Width & 63;

        if (used != 0)
        {
            for (int y = 0; y < fog.Height; y++)
            {
                rows[(y * rowWords) + rowWords - 1] &= (1UL << used) - 1;
            }
        }

        return rows;
    }

    private void ClearRowPadding(ulong[] rows, int start)
    {
        int used = Width & 63;

        if (used != 0)
        {
            rows[start + rowWords - 1] &= (1UL << used) - 1;
        }
    }
}
