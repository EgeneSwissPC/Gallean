using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (docs/battle-ai-designer.md phase 1): an army preset for the battle bench and the battle
// sandbox: units of any type at any level on the 2x3 grid (a large unit sits on its front-line slot and also
// takes the back-line slot behind it, like in the game). Leader items are not part of it yet.
public sealed record BattleArmyUnit(UnitDefinitionId TypeId, int Level, GridPosition Position);

public sealed record BattleArmy(string Name, ImmutableArray<BattleArmyUnit> Units)
{
    // Off: the army must follow the game's rules (one leader at most, leadership not exceeded, no guardians).
    // On: anything that fits on the grid (stress tests).
    public bool AllowAnyArmy { get; init; }
}
