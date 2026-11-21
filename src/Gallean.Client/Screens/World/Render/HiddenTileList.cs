using System.Collections;
using System.Numerics;
using Gallean.Common;

namespace Gallean.Client;

// The tiles a fog bitmap leaves hidden (bit clear), row by row and left to right, read straight from the bitmap. A
// 1024x1024 map has a million hidden tiles at the start, and a list of them was rebuilt on every step that revealed
// one; consumers that know this type (fog layer, minimap) work on the bitmap's words instead of enumerating.
internal abstract class HiddenTileList<T> : IReadOnlyList<T>
{
    private int count = -1;

    protected HiddenTileList(ImmutableBitmap explored)
    {
        Explored = explored;
    }

    public ImmutableBitmap Explored { get; }

    public int Count
    {
        get
        {
            if (count < 0)
            {
                count = (Explored.Width * Explored.Height) - Explored.CountSet();
            }

            return count;
        }
    }

    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            int current = 0;

            foreach (T item in this)
            {
                if (current++ == index)
                {
                    return item;
                }
            }

            throw new InvalidOperationException();
        }
    }

    public IEnumerator<T> GetEnumerator()
    {
        foreach ((int y, int firstX, int lastX) in EnumerateRuns())
        {
            for (int x = firstX; x <= lastX; x++)
            {
                yield return Create(new WorldPosition(x, y));
            }
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    /// <summary>Runs of hidden tiles along each row: (row, first column, last column), row by row, left to right.</summary>
    public IEnumerable<(int Y, int FirstX, int LastX)> EnumerateRuns()
    {
        int width = Explored.Width;

        for (int y = 0; y < Explored.Height; y++)
        {
            int rowStart = y * width;
            int rowEnd = rowStart + width;
            int index = rowStart;

            while (index < rowEnd)
            {
                int first = FindNext(Explored, index, rowEnd, set: false);

                if (first == rowEnd)
                {
                    break;
                }

                int end = FindNext(Explored, first, rowEnd, set: true);

                yield return (y, first - rowStart, end - 1 - rowStart);
                index = end;
            }
        }
    }

    protected abstract T Create(WorldPosition position);

    // The first index in [from, end) whose bit equals set, or end.
    private static int FindNext(ImmutableBitmap bitmap, int from, int end, bool set)
    {
        ReadOnlySpan<ulong> words = bitmap.GetWords();
        int index = from;

        while (index < end)
        {
            ulong word = words[index >> 6];

            if (!set)
            {
                word = ~word;
            }

            word &= ulong.MaxValue << (index & 63);

            if (word != 0)
            {
                return Math.Min(end, (index & ~63) + BitOperations.TrailingZeroCount(word));
            }

            index = (index & ~63) + 64;
        }

        return end;
    }
}

internal sealed class FogTileList(ImmutableBitmap explored) : HiddenTileList<WorldRenderState.FogTile>(explored)
{
    protected override WorldRenderState.FogTile Create(WorldPosition position)
    {
        return new WorldRenderState.FogTile(position);
    }
}

internal sealed class HiddenPositionList(ImmutableBitmap explored) : HiddenTileList<WorldPosition>(explored)
{
    protected override WorldPosition Create(WorldPosition position)
    {
        return position;
    }
}
