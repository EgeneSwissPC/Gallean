using System.Runtime.InteropServices;

namespace Gallean.Common;

public sealed partial record ImmutableBitmap
{
    public sealed class Builder
    {
        private ulong[] data;
        private bool shared;
        private ImmutableBitmap? unchanged;

        public Builder(ImmutableBitmap bitmap)
        {
            Width = bitmap.Width;
            Height = bitmap.Height;
            // Copy on first write: a builder that only reads or sets bits that are already set costs nothing.
            data = bitmap.words;
            shared = true;
            unchanged = bitmap;
        }

        public Builder(int width, int height, ReadOnlySpan<byte> bits)
        {
            int bitCount = GetBitCount(width, height);

            if (bits.Length != GetByteCount(bitCount))
            {
                throw new ArgumentException("Packed bits do not match the bitmap dimensions.", nameof(bits));
            }

            Width = width;
            Height = height;
            data = new ulong[GetWordCount(bitCount)];
            bits.CopyTo(MemoryMarshal.AsBytes(data.AsSpan()));
            ClearPadding(data, bitCount);
        }

        public int Width { get; }
        public int Height { get; }

        public bool Get(int x, int y)
        {
            int index = GetIndex(x, y);

            return (data[index >> 6] & (1UL << index)) != 0;
        }

        public void Set(int x, int y)
        {
            int index = GetIndex(x, y);
            ulong bit = 1UL << index;

            if ((data[index >> 6] & bit) == 0)
            {
                Unshare();
                data[index >> 6] |= bit;
            }
        }

        public void Clear(int x, int y)
        {
            int index = GetIndex(x, y);
            ulong bit = 1UL << index;

            if ((data[index >> 6] & bit) != 0)
            {
                Unshare();
                data[index >> 6] &= ~bit;
            }
        }

        public ImmutableBitmap ToImmutable()
        {
            // Nothing changed since the source bitmap (or the last ToImmutable): hand back that same instance, so
            // callers that compare by reference see no change.
            if (unchanged is not null)
            {
                return unchanged;
            }

            // The builder copies the array before its next write, so the bitmap can share it.
            shared = true;
            unchanged = new ImmutableBitmap(Width, Height, data);

            return unchanged;
        }

        public static long GetBitsLength(int width, int height)
        {
            return ((long)width * height + 7) / 8;
        }

        private int GetIndex(int x, int y)
        {
            int index = y * Width + x;

            if ((uint)index >= (uint)(Width * Height))
            {
                throw new ArgumentOutOfRangeException(nameof(x));
            }

            return index;
        }

        private void Unshare()
        {
            if (shared)
            {
                data = (ulong[])data.Clone();
                shared = false;
            }

            unchanged = null;
        }
    }
}
