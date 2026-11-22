namespace Gallean.Engine.Tests;

// Gallean Upgrade (AI designer, phase 2): a designed AI started from a built-in one plays like it.
// Reads or changes the designed AIs in AiBrainRegistry (static): never alongside other such fixtures.
[NonParallelizable]
public sealed class DesignedWorldAiStartTests
{
    [TestCase(AiBrainRegistry.ClassicId)]
    [TestCase(AiPersonalities.ClassicPlusId)]
    [TestCase(AiPersonalities.AggressiveId)]
    [TestCase(AiPersonalities.BuilderId)]
    [TestCase(AiPersonalities.ExpansionistId)]
    public void StartFromBuiltInKeepsItsProfileTest(string sourceId)
    {
        WorldAiProfile source = AiBrainRegistry.ResolveWorld(sourceId) is ProfileWorldAiBrain personality ? personality.Profile : WorldAiProfile.Classic;

        DesignedWorldAi design = DesignedWorldAi.FromBuiltIn(sourceId, "copy", "Copy");
        WorldAiProfile copy = DesignedStageEvaluator.ToProfile(design.Stages.Single());

        Assert.That(design.Id, Is.EqualTo("custom:copy"));
        Assert.That(copy.Priorities, Is.EquivalentTo(DesignedStageEvaluator.GetEffectivePriorities(source)));
        // Every designed AI spends at the cap (docs/ai-overflow-spending.md), also a copy of Classic.
        Assert.That(copy with { Priorities = null, RodPriority = source.RodPriority, BuildingPriority = source.BuildingPriority, UnitPriority = source.UnitPriority },
            Is.EqualTo(source with { SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent, SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent, UseAllSpells = true }));
    }

    [Test]
    public void ClassicStartIsTheClassicStageTest()
    {
        Assert.That(DesignedWorldAi.FromBuiltIn(AiBrainRegistry.ClassicId, "c", "C").Stages.Single(), Is.EqualTo(DesignedWorldStage.Classic));
    }

    [Test]
    public void StartFromDesignedCopiesItsStagesTest()
    {
        DesignedWorldAi source = new("custom:two-stages", "Two stages", [
            DesignedWorldStage.Classic with { Name = "Late", When = [[new DesignedStageCondition.FromTurn(20)]] },
            DesignedWorldStage.Classic
        ]);

        try
        {
            AiBrainRegistry.SetDesignedWorldAis([source]);

            DesignedWorldAi copy = DesignedWorldAi.FromBuiltIn("custom:two-stages", "copy-2", "Copy");

            Assert.That(copy.Stages, Is.EqualTo(source.Stages));
            Assert.That(copy.Id, Is.EqualTo("custom:copy-2"));
        }
        finally
        {
            AiBrainRegistry.SetDesignedWorldAis([]);
        }
    }
}
