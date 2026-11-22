namespace Gallean.Engine;

internal static class WorldBattleSideInfoCalculator
{
    public static BattleSideInfo GetInfo(WorldState state, WorldPlayerId? ownerId)
    {
        bool isNeutral = ownerId is null || state.Players[ownerId.Value].Race == WorldRace.Neutral;

        return new BattleSideInfo(isNeutral);
    }

    // Gallean Upgrade (battle AI designer): with what the army carries (bag and leader equipment).
    public static BattleSideInfo GetInfo(WorldState state, WorldPlayerId? ownerId, WorldStack stack, IGameDefinitionProvider definitions)
    {
        int Value(WorldItem item) => definitions.GetItem(item.DefinitionId).Value.Gold;
        bool IsQuest(WorldItem item) => definitions.GetItem(item.DefinitionId).Category == ItemCategory.Special;

        return GetInfo(state, ownerId) with
        {
            CargoValue = stack.Inventory.Items.Sum(Value),
            EquippedValue = stack.Equipment.Items.Values.Sum(Value),
            CarriesQuestItem = stack.Inventory.Items.Any(IsQuest) || stack.Equipment.Items.Values.Any(IsQuest)
        };
    }
}
