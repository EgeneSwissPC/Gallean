using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed partial class WorldStackItemViewModel(WorldStackId stackId) : ObservableObject
{
    public WorldStackId StackId => stackId;

    [ObservableProperty]
    public partial WorldPosition Position { get; private set; }

    [ObservableProperty]
    public partial AssetSpriteKey.UnitEventPortrait? PortraitKey { get; private set; }

    [ObservableProperty]
    public partial string LeaderName { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial int Level { get; private set; }

    [ObservableProperty]
    public partial int MovementPoints { get; private set; }

    [ObservableProperty]
    public partial int MaxMovementPoints { get; private set; }

    [ObservableProperty]
    public partial bool IsSelected { get; private set; }

    [ObservableProperty]
    public partial bool IsFinished { get; private set; }

    // Gallean Upgrade: move orders (docs/move-orders.md).
    [ObservableProperty]
    public partial bool HasMoveOrder { get; private set; }

    [ObservableProperty]
    public partial int? OrderTurns { get; private set; }

    public void Update(
        WorldStack stack,
        bool isSelected,
        bool isFinished,
        bool hasMoveOrder,
        int? orderTurns,
        IGameDefinitionProvider definitions,
        IGameTextProvider texts,
        IUnitStatsProvider stats)
    {
        (WorldUnit leader, GridPosition _, UnitDefinition _) = stack.GetLeader(definitions);
        StackStats stackStats = stats.GetWorldStackStats(stack);

        Position = stack.Position;
        PortraitKey = new AssetSpriteKey.UnitEventPortrait(ScenarioEventPortraitId.Unit(leader.DefinitionId));
        LeaderName = UnitNameResolver.Resolve(leader, definitions, texts);
        Level = leader.Level;
        MovementPoints = stack.MovementPoints;
        MaxMovementPoints = stackStats.MaxMovementPoints;
        IsSelected = isSelected;
        IsFinished = isFinished;
        HasMoveOrder = hasMoveOrder;
        OrderTurns = orderTurns;
    }
}
