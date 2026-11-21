using Gallean.Common;

namespace Gallean.Client.Tests;

public sealed class HiddenTileListTests
{
    [Test]
    public void EnumerateTest()
    {
        // 70 columns: rows cross word boundaries.
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(70, 3, true).ToBuilder();
        List<WorldPosition> expected = [];

        foreach (WorldPosition position in new WorldPosition[] { new(0, 0), new(1, 0), new(63, 0), new(64, 0), new(69, 0), new(5, 1), new(68, 2), new(69, 2) })
        {
            builder.Clear(position.X, position.Y);
            expected.Add(position);
        }

        HiddenPositionList hidden = new(builder.ToImmutable());

        Assert.That(hidden, Is.EqualTo(expected));
        Assert.That(hidden, Has.Count.EqualTo(8));
        Assert.That(hidden[4], Is.EqualTo(new WorldPosition(69, 0)));
        Assert.That(new FogTileList(builder.ToImmutable()).Select(tile => tile.Position), Is.EqualTo(expected));
        Assert.That(hidden.EnumerateRuns(), Is.EqualTo(new[] { (0, 0, 1), (0, 63, 64), (0, 69, 69), (1, 5, 5), (2, 68, 69) }));
    }

    [Test]
    public void AllHiddenTest()
    {
        HiddenPositionList hidden = new(ImmutableBitmap.Create(130, 2));

        Assert.That(hidden, Has.Count.EqualTo(260));
        Assert.That(hidden.EnumerateRuns(), Is.EqualTo(new[] { (0, 0, 129), (1, 0, 129) }));
        Assert.That(new HiddenPositionList(ImmutableBitmap.Create(130, 2, true)), Is.Empty);
    }
}
