namespace Gallean.Engine.Contracts;

public sealed record WorldStack(
    WorldStackId Id,
    WorldPlayerId OwnerId,
    WorldPosition Position,
    WorldStackDirection Direction,
    UnitGrid<WorldUnit> Units,
    int MovementPoints,
    WorldInventory Inventory,
    LeaderEquipment Equipment,
    WorldSubraceId SubraceId,
    int AiPriority,
    bool AiIgnore,
    bool Concealed,
    WorldCanonicalStackOrder Order,
    int? ExpirationTurn,
    int PendingLeaderUpgrades,
    int BattlesWon,
    ScenarioStackTemplateId? SourceTemplateId)
{
    public bool LeaderUpgradePostponed { get; init; }

    /// <summary>
    /// Gallean Upgrade (large maps): mana a summoned or illusion army costs at the start of each of its
    /// owner's turns instead of expiring. Null for every other army.
    /// </summary>
    public WorldResourceSet? SummonUpkeep { get; init; }

    /// <summary>
    /// Gallean Upgrade (neutral economy, docs/town-income.md phase 5): the village an army bought by a neutral side
    /// calls home; such armies get their orders from <c>NeutralArmyPlanner</c>. Null for every other army.
    /// </summary>
    public WorldFortificationId? NeutralHomeId { get; init; }
}
