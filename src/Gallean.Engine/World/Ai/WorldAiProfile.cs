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

    // New behaviours (AI designer, docs/ai-designer.md phase 5); 0/false = off, as in Classic.
    // The N fighting armies nearest the capital stay within HomeRadius tiles of its entrance.
    public int KeepArmiesHome { get; init; }

    // An army only attacks when at least N of the player's fighting armies (itself included) stand within
    // GatherRadius tiles of it; otherwise it walks towards the nearest other one.
    public int GatherArmies { get; init; }

    // Armies with nothing better to do march on the nearest enemy capital the player has seen.
    public bool RushEnemyCapital { get; init; }

    // Economy actions may not spend the treasury below this much gold.
    public int GoldReserve { get; init; }
    // Gallean Upgrade, overflow spending (docs/ai-overflow-spending.md): at or above this share of the
    // resource cap the AI lifts its spending limits. 0 = off, as in Classic.
    public int SpendAbovePercent { get; init; }
    // Gallean Upgrade, unused features phase 1 (docs/ai-unused-features.md): on large maps, the share of a mana's
    // income per turn that running summon upkeep may take before the AI stops summoning. 0 = no check (Classic).
    public int SummonUpkeepPercent { get; init; }
    // Gallean Upgrade, unused features phase 2: also research spells the game data marks "not for the AI"
    // (AI_CAT None) when their effect is one the AI knows how to cast. False = Classic.
    public bool UseAllSpells { get; init; }
}
