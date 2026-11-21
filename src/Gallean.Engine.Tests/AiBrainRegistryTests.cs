namespace Gallean.Engine.Tests;

public sealed class AiBrainRegistryTests
{
    [Test]
    public void ClassicIsListedFirstTest()
    {
        Assert.That(AiBrainRegistry.WorldBrains[0].Id, Is.EqualTo(AiBrainRegistry.ClassicId));
        Assert.That(AiBrainRegistry.BattleBrains[0].Id, Is.EqualTo(AiBrainRegistry.ClassicId));
    }

    [Test]
    public void IdsAreUniqueTest()
    {
        Assert.That(AiBrainRegistry.WorldBrains.Select(b => b.Id), Is.Unique);
        Assert.That(AiBrainRegistry.BattleBrains.Select(b => b.Id), Is.Unique);
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("removed-ai")]
    public void UnknownIdFallsBackToClassicTest(string? id)
    {
        Assert.That(AiBrainRegistry.ResolveWorld(id), Is.SameAs(ClassicWorldAiBrain.Instance));
        Assert.That(AiBrainRegistry.ResolveBattle(id), Is.SameAs(ClassicBattleAiBrain.Instance));
    }
}
