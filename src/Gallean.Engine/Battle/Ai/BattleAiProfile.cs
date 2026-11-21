namespace Gallean.Engine;

// Gallean Upgrade: the knobs a battle AI personality turns on the Classic battle AI
// (docs/ai-switching.md). Classic holds the original values.
internal sealed record BattleAiProfile
{
    public static BattleAiProfile Classic { get; } = new();

    // A leader thinks about fleeing when its side holds less than this share (per mille) of the
    // battle's power.
    public int RetreatPowerShare { get; init; } = 250;

    public bool CanRetreat { get; init; } = true;
    public bool CanDefend { get; init; } = true;
}
