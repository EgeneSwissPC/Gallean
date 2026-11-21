namespace Gallean.Engine;

// Gallean Upgrade: the knobs an AI personality turns on the Classic world AI (docs/ai-switching.md).
// Classic holds the original values, so the Classic brain plays exactly as before.
internal sealed record WorldAiProfile
{
    public static WorldAiProfile Classic { get; } = new();

    // Scales the power advantage the AI wants before it attacks (100 = the difficulty's own value).
    public int AttackPowerPercent { get; init; } = 100;

    // Army hit points (per mille) under which it goes healing, and when it counts as healed again.
    public int HurtThreshold { get; init; } = 800;
    public int HealedThreshold { get; init; } = 900;

    // Leadership filled (per mille) under which an army goes back to a city for more units.
    public int ReinforceThreshold { get; init; } = 800;

    // Gold it may be short by while saving up for a new leader.
    public int LeaderOvercommitGold { get; init; } = 400;

    // Doesn't save up for a leader while an army stands on the capital entrance (the hire would
    // fail anyway): fixes the custom-map idle loop (references: notes-custom-map-ai.md).
    public bool WaitForFreeCapital { get; init; }

    // Action priorities (higher wins first; equal priorities compare scores).
    public int BuildingPriority { get; init; } = 1;
    public int UnitPriority { get; init; } = 0;
    public int RodPriority { get; init; } = 2;

    // AI designer: when set, every action's priority comes from here and actions missing from it are never
    // done (change order always runs, at 0). Null = the priorities above and Classic's constants.
    public System.Collections.Immutable.ImmutableDictionary<WorldAiActionKind, int>? Priorities { get; init; }
}
