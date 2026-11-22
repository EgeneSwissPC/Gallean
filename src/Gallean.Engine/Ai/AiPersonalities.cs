namespace Gallean.Engine;

// Gallean Upgrade: the AI personalities (docs/ai-switching.md, phase 5). Each one is the Classic AI
// with other knobs; all world personalities include the custom-map idle fix.
internal static class AiPersonalities
{
    public const string ClassicPlusId = "classic-plus";
    public const string AggressiveId = "aggressive";
    public const string BuilderId = "builder";
    public const string ExpansionistId = "expansionist";
    public const string CautiousId = "cautious";
    public const string RuthlessId = "ruthless";

    public static IWorldAiBrain[] CreateWorldBrains()
    {
        return
        [
            new ProfileWorldAiBrain(
                new AiBrainInfo(ClassicPlusId, "Classic+", "The original AI, but it no longer waits in the capital to save up for a leader."),
                new WorldAiProfile { WaitForFreeCapital = true, SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent, SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent, UseAllSpells = true }),
            new ProfileWorldAiBrain(
                new AiBrainInfo(AggressiveId, "Aggressive", "Attacks with less advantage and sends armies out earlier, before they are full or healed."),
                new WorldAiProfile
                {
                    WaitForFreeCapital = true,
                    SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent,
                    SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent,
                    UseAllSpells = true,
                    AttackPowerPercent = 80,
                    HurtThreshold = 700,
                    HealedThreshold = 850,
                    ReinforceThreshold = 600
                }),
            new ProfileWorldAiBrain(
                new AiBrainInfo(BuilderId, "Builder", "Attacks only with a clear advantage and keeps its armies full and healed."),
                new WorldAiProfile
                {
                    WaitForFreeCapital = true,
                    SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent,
                    SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent,
                    UseAllSpells = true,
                    AttackPowerPercent = 130,
                    HurtThreshold = 900,
                    HealedThreshold = 950,
                    ReinforceThreshold = 900
                }),
            new ProfileWorldAiBrain(
                new AiBrainInfo(ExpansionistId, "Expansionist", "Hires leaders early, plants rods first and sends armies out with fewer units."),
                new WorldAiProfile
                {
                    WaitForFreeCapital = true,
                    SpendAbovePercent = WorldAiSpending.DefaultSpendAbovePercent,
                    SummonUpkeepPercent = WorldAiSpending.DefaultSummonUpkeepPercent,
                    UseAllSpells = true,
                    LeaderOvercommitGold = 800,
                    ReinforceThreshold = 500,
                    RodPriority = 3
                })
        ];
    }

    public static IBattleAiBrain[] CreateBattleBrains()
    {
        return
        [
            new ProfileBattleAiBrain(
                new AiBrainInfo(CautiousId, "Cautious", "Leaders flee as soon as the battle turns against them."),
                new BattleAiProfile { RetreatPowerShare = 400 }),
            new ProfileBattleAiBrain(
                new AiBrainInfo(RuthlessId, "Ruthless", "Never flees and never defends: every unit attacks."),
                new BattleAiProfile { CanRetreat = false, CanDefend = false })
        ];
    }
}
