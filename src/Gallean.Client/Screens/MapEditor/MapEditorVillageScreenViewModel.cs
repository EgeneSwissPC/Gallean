using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Sets up one village like the game's city screen: name, owner and level, the garrison (as many units as the level,
// a large unit counting twice) and the army stationed in the village, which serves the village's owner. Units can be
// dragged between the garrison and the army.
internal sealed partial class MapEditorVillageScreenViewModel : MapEditorGarrisonScreenViewModel
{
    public const int MaxNameLength = 40;

    private readonly Func<int, WorldRace, string, CustomMapUnit?[], CustomMapUnit?[]?, string?> onConfirm;
    private readonly Action onDelete;
    private readonly bool initialized;

    public MapEditorVillageScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        IReadOnlyList<MapEditorOption<WorldRace>> owners,
        CustomMapVillage village,
        IReadOnlyList<CustomMapUnit?>? stationed,
        Func<int, WorldRace, string, CustomMapUnit?[], CustomMapUnit?[]?, string?> onConfirm,
        Action onDelete)
        : base(game, editorGame, units, village.Garrison)
    {
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        LeaderSiteProblem = MapEditorLabels.SeaCreatureInCity;
        Title = Tr("Village at {0}, {1}", village.TopLeft.X, village.TopLeft.Y);
        Owners = owners;
        Levels = MapEditorLabels.VillageLevels();
        Name = village.Name;
        SelectedOwner = owners.FirstOrDefault(option => option.Value == village.Owner) ?? owners[0];
        SelectedLevel = Levels.FirstOrDefault(option => option.Value == village.Level) ?? Levels[0];
        Army = AddGroup(MapEditorUnitGroupKind.Army, stationed, () => 0, () => SelectedOwner.Value, isOptional: true);
        initialized = true;

        Refresh();
    }

    public string Title { get; }
    public IReadOnlyList<MapEditorOption<WorldRace>> Owners { get; }
    public IReadOnlyList<MapEditorOption<int>> Levels { get; }
    public int NameLength => MaxNameLength;
    public AssetSpriteKey ImageKey => new AssetSpriteKey.CityImage(SelectedLevel.Value);
    public MapEditorUnitGroup Army { get; }

    protected override int GarrisonCapacity => SelectedLevel.Value;
    protected override WorldRace PickerRace => SelectedOwner.Value;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedOwner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageKey))]
    public partial MapEditorOption<int> SelectedLevel { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveArmyCommand))]
    public partial bool HasArmy { get; private set; }

    partial void OnSelectedLevelChanged(MapEditorOption<int> value)
    {
        if (initialized)
        {
            Refresh();
        }
    }

    partial void OnSelectedOwnerChanged(MapEditorOption<WorldRace> value)
    {
        if (initialized)
        {
            Refresh();
        }
    }

    protected override void OnValidityChanged()
    {
        HasArmy = !Army.IsEmpty;
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ChooseArmyLeader()
    {
        ChooseLeader(Army);
    }

    [RelayCommand(CanExecute = nameof(HasArmy))]
    private void RemoveArmy()
    {
        Army.Slots = new CustomMapUnit?[GridPosition.Count];
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        CustomMapUnit?[]? army = Army.IsEmpty ? null : [.. Army.Slots];

        if (onConfirm(SelectedLevel.Value, SelectedOwner.Value, Name.Trim(), [.. Garrison.Slots], army) is { } error)
        {
            Game.ShowError(error);
            return;
        }

        CloseSelf();
    }

    [RelayCommand]
    private void Delete()
    {
        CloseSelf();
        onDelete();
    }
}
