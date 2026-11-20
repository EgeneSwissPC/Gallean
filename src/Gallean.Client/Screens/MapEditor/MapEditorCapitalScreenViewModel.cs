using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Sets up a capital like a village: the garrison (the race's guardian, who never leaves, and up to 6 places in all)
// and the army stationed in it. Every capital starts with an army; for a human player its leader is swapped for the
// one chosen when the game starts, and the rest of the army stays.
internal sealed partial class MapEditorCapitalScreenViewModel : MapEditorUnitsScreenViewModel
{
    private readonly Func<CustomMapUnit?[], CustomMapUnit?[], string?> onConfirm;

    public MapEditorCapitalScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        WorldRace race,
        WorldPosition topLeft,
        IReadOnlyList<CustomMapUnit?> garrison,
        IReadOnlyList<CustomMapUnit?> army,
        Func<CustomMapUnit?[], CustomMapUnit?[], string?> onConfirm)
        : base(game, editorGame, units)
    {
        this.onConfirm = onConfirm;
        LeaderSiteProblem = MapEditorLabels.SeaCreatureInCity;
        Race = race;
        Title = Tr("Capital of {0} at {1}, {2}", MapEditorLabels.Owner(race), topLeft.X, topLeft.Y);
        Garrison = AddGroup(MapEditorUnitGroupKind.CapitalGarrison, garrison, () => CustomMapPlayer.GarrisonCapacity, () => race);
        Army = AddGroup(MapEditorUnitGroupKind.Army, army, () => 0, () => race);

        Refresh();
    }

    public WorldRace Race { get; }
    public string Title { get; }
    public AssetSpriteKey ImageKey => new AssetSpriteKey.CapitalImage(Race);
    public MapEditorUnitGroup Garrison { get; }
    public MapEditorUnitGroup Army { get; }

    protected override void OnRefreshed()
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ChooseArmyLeader()
    {
        ChooseLeader(Army);
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        if (onConfirm([.. Garrison.Slots], [.. Army.Slots]) is { } error)
        {
            Game.ShowError(error);
            return;
        }

        CloseSelf();
    }
}
