namespace Gallean.Common.Tests;

public sealed class ImmutableBitmapTests
{
    private static IReadOnlyList<string> FormatBitmap(ImmutableBitmap bitmap)
    {
        List<string> rows = [];

        for (int y = 0; y < bitmap.Height; y++)
        {
            char[] row = new char[bitmap.Width];

            for (int x = 0; x < bitmap.Width; x++)
            {
                row[x] = bitmap.Get(x, y) ? '#' : '.';
            }

            rows.Add(new string(row));
        }

        return rows;
    }

    [Test]
    public void CreateTest()
    {
        ImmutableBitmap bitmap = ImmutableBitmap.Create(3, 3, false);

        Assert.That(FormatBitmap(bitmap), Is.EqualTo([
            "...",
            "...",
            "..."
        ]));
    }

    [Test]
    public void CreateFilledTest()
    {
        ImmutableBitmap bitmap = ImmutableBitmap.Create(3, 3, true);

        Assert.That(FormatBitmap(bitmap), Is.EqualTo([
            "###",
            "###",
            "###"
        ]));
    }

    [Test]
    public void BuilderSetTest()
    {
        ImmutableBitmap bitmap = ImmutableBitmap.Create(3, 3, false);
        ImmutableBitmap.Builder builder = bitmap.ToBuilder();

        builder.Set(1, 1);

        Assert.That(FormatBitmap(builder.ToImmutable()), Is.EqualTo([
            "...",
            ".#.",
            "..."
        ]));

        Assert.That(FormatBitmap(bitmap), Is.EqualTo([
            "...",
            "...",
            "..."
        ]));
    }

    [Test]
    public void BuilderClearTest()
    {
        ImmutableBitmap bitmap = ImmutableBitmap.Create(3, 3, true);
        ImmutableBitmap.Builder builder = bitmap.ToBuilder();

        builder.Clear(1, 1);

        Assert.That(FormatBitmap(builder.ToImmutable()), Is.EqualTo([
            "###",
            "#.#",
            "###"
        ]));

        Assert.That(FormatBitmap(bitmap), Is.EqualTo([
            "###",
            "###",
            "###"
        ]));
    }

    [Test]
    public void BuilderChangeAfterToImmutableTest()
    {
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(3, 3).ToBuilder();
        builder.Set(1, 1);
        ImmutableBitmap bitmap = builder.ToImmutable();

        builder.Clear(1, 1);

        Assert.That(FormatBitmap(bitmap), Is.EqualTo([
            "...",
            ".#.",
            "..."
        ]));
    }

    [Test]
    public void EqualityTest()
    {
        ImmutableBitmap bitmap1 = ImmutableBitmap.Create(3, 3, true);
        ImmutableBitmap bitmap2 = ImmutableBitmap.Create(3, 3, true);
        ImmutableBitmap bitmap3 = ImmutableBitmap.Create(3, 3, false);
        ImmutableBitmap bitmap4 = ImmutableBitmap.Create(4, 3, true);

        Assert.That(bitmap1, Is.Not.Null);
#pragma warning disable NUnit2009
        Assert.That(bitmap1, Is.EqualTo(bitmap1));
#pragma warning restore NUnit2009
        Assert.That(bitmap1, Is.EqualTo(bitmap2));
        Assert.That(bitmap1, Is.Not.EqualTo(bitmap3));
        Assert.That(bitmap1, Is.Not.EqualTo(bitmap4));
    }

    [Test]
    public void BitsRoundTripTest()
    {
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(5, 3).ToBuilder();
        builder.Set(0, 0);
        builder.Set(4, 0);
        builder.Set(2, 1);
        builder.Set(4, 2);
        ImmutableBitmap bitmap = builder.ToImmutable();

        byte[] bits = bitmap.ToBits();

        Assert.That(bits, Has.Length.EqualTo(2));
        Assert.That(new ImmutableBitmap.Builder(5, 3, bits).ToImmutable(), Is.EqualTo(bitmap));
    }

    [Test]
    public void BitsLengthMismatchTest()
    {
        Assert.Throws<ArgumentException>(() => new ImmutableBitmap.Builder(5, 3, [0, 0, 0]));
    }

    [Test]
    public void GetHashCodeTest()
    {
        ImmutableBitmap bitmap1 = ImmutableBitmap.Create(3, 3, true);
        ImmutableBitmap bitmap2 = ImmutableBitmap.Create(3, 3, true);

#pragma warning disable NUnit2009
        Assert.That(bitmap1.GetHashCode(), Is.EqualTo(bitmap1.GetHashCode()));
#pragma warning restore NUnit2009
        Assert.That(bitmap1.GetHashCode(), Is.EqualTo(bitmap2.GetHashCode()));
    }
    [Test]
    public void PaddingBitsRoundTripTest()
    {
        // 5x3 = 15 bits: two packed bytes, the last bit of the second byte is padding.
        ImmutableBitmap bitmap = new ImmutableBitmap.Builder(5, 3, [0b1010_0101, 0b1111_1110]).ToImmutable();

        Assert.That(FormatBitmap(bitmap), Is.EqualTo([
            "#.#..",
            "#.#.#",
            "#####"
        ]));
        Assert.That(bitmap.ToBits(), Is.EqualTo(new byte[] { 0b1010_0101, 0b0111_1110 }));
        Assert.That(bitmap.CountSet(), Is.EqualTo(10));
        Assert.That(bitmap.GetWords().ToArray(), Is.EqualTo(new ulong[] { 0b0111_1110_1010_0101 }));
    }

    [Test]
    public void FilledPaddingTest()
    {
        ImmutableBitmap filled = ImmutableBitmap.Create(10, 10, true);
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(10, 10, false).ToBuilder();

        for (int y = 0; y < 10; y++)
        {
            for (int x = 0; x < 10; x++)
            {
                builder.Set(x, y);
            }
        }

        // The bits past Width * Height stay zero, so a filled bitmap equals one filled bit by bit.
        Assert.That(filled.CountSet(), Is.EqualTo(100));
        Assert.That(builder.ToImmutable(), Is.EqualTo(filled));
        Assert.That(builder.ToImmutable().GetHashCode(), Is.EqualTo(filled.GetHashCode()));
    }

    [Test]
    public void BuilderCopyOnWriteTest()
    {
        ImmutableBitmap bitmap = ImmutableBitmap.Create(70, 2, false);
        ImmutableBitmap.Builder builder = bitmap.ToBuilder();

        Assert.That(builder.ToImmutable(), Is.SameAs(bitmap));

        builder.Set(69, 1);
        ImmutableBitmap first = builder.ToImmutable();

        Assert.That(builder.ToImmutable(), Is.SameAs(first));

        builder.Set(0, 0);
        ImmutableBitmap second = builder.ToImmutable();

        Assert.That(bitmap.CountSet(), Is.EqualTo(0));
        Assert.That(first.CountSet(), Is.EqualTo(1));
        Assert.That(first.Get(69, 1), Is.True);
        Assert.That(second.CountSet(), Is.EqualTo(2));
        Assert.That(second, Is.Not.EqualTo(first));
        Assert.That(builder.Get(0, 0), Is.True);
    }
    [Test]
    public void UnionWithTest()
    {
        ImmutableBitmap.Builder other = ImmutableBitmap.Create(3, 2).ToBuilder();
        other.Set(0, 0);
        other.Set(2, 1);
        ImmutableBitmap source = ImmutableBitmap.Create(3, 2).ToBuilder().ToImmutable();
        ImmutableBitmap.Builder builder = source.ToBuilder();

        builder.UnionWith(other.ToImmutable());

        Assert.That(FormatBitmap(builder.ToImmutable()), Is.EqualTo([
            "#..",
            "..#"
        ]));
        Assert.That(source.CountSet(), Is.EqualTo(0));

        // Nothing new: the builder still hands back the bitmap it started from.
        ImmutableBitmap.Builder same = other.ToImmutable().ToBuilder();
        same.UnionWith(source);
        Assert.That(same.ToImmutable(), Is.SameAs(other.ToImmutable()));
        Assert.Throws<ArgumentException>(() => same.UnionWith(ImmutableBitmap.Create(2, 3)));
    }
}
