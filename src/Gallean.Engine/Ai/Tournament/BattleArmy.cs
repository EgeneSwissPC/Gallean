using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (docs/battle-ai-designer.md): an army preset for the battle bench and the battle sandbox:
// units of any type at any level on the 2x3 grid (a large unit sits on its front-line slot and also takes the
// back-line slot behind it, like in the game), plus the leader's items and level-up upgrades.
public sealed record BattleArmyUnit(UnitDefinitionId TypeId, int Level, GridPosition Position);

// An item in one of the leader's equipment slots (banner, tome, two battle items, two artifacts, boots).
public sealed record BattleArmyItem(LeaderEquipmentSlot Slot, ItemDefinitionId ItemId);

public sealed record BattleArmy(string Name, ImmutableArray<BattleArmyUnit> Units)
{
    // Off: the army must follow the game's rules (one leader at most, leadership not exceeded, no guardians,
    // items in fitting slots and allowed by the leader's abilities, upgrades the leader's level allows).
    // On: anything that fits on the grid (stress tests).
    public bool AllowAnyArmy { get; init; }

    public ImmutableArray<BattleArmyItem> Equipment { get; init; } = [];

    // Leader upgrades (unit modifier IDs from Gleaupg, e.g. "Leadership", "Banner bearer"), applied to the
    // leader like the ones the game grants on level-up.
    public ImmutableArray<UnitModifierDefinitionId> LeaderUpgrades { get; init; } = [];

    // Items in the army's bag (no effect in battle; a designed battle AI can react to their value, "we carry").
    public ImmutableArray<ItemDefinitionId> Cargo { get; init; } = [];
}
