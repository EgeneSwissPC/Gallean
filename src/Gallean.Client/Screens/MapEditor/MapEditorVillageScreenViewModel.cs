using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Sets up one village: name, owner, level and garrison. Laid out like the army screen, with the village picture in
// place of the leader. A village of level n holds n units; a large unit counts twice.
internal sealed partial class MapEditorVillageScreenViewModel : MapEditorGarrisonScreenViewModel
{
    public const int MaxNameLength = 40;

    private readonly Func<int, WorldRace, string, CustomMapUnit?[], string?> onConfirm;
    private readonly Action onDelete;
    private readonly bool initialized;

    public MapEditorVillageScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        IReadOnlyList<MapEditorOption<WorldRace>> owners,
        CustomMapVillage village,
        Func<int, WorldRace, string, CustomMapUnit?[], string?> onConfirm,
        Action onDelete)
        : base(game, editorGame, units, village.Garrison)
    {
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Title = Tr("Village at {0}, {1}", village.TopLeft.X, village.TopLeft.Y);
        Owners = owners;
        Levels = MapEditorLabels.VillageLevels();
        Name = village.Name;
        SelectedOwner = owners.FirstOrDefault(option => option.Value == village.Owner) ?? owners[0];
        SelectedLevel = Levels.FirstOrDefault(option => option.Value == village.Level) ?? Levels[0];
        initialized = true;

        Refresh();
    }

    public string Title { get; }
    public IReadOnlyList<MapEditorOption<WorldRace>> Owners { get; }
    public IReadOnlyList<MapEditorOption<int>> Levels { get; }
    public int NameLength => MaxNameLength;
    public AssetSpriteKey ImageKey => new AssetSpriteKey.CityImage(SelectedLevel.Value);

    protected override int GarrisonCapacity => SelectedLevel.Value;
    protected override WorldRace PickerRace => SelectedOwner.Value;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedOwner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageKey))]
    public partial MapEditorOption<int> SelectedLevel { get; set; }

    partial void OnSelectedLevelChanged(MapEditorOption<int> value)
    {
        if (initialized)
        {
            Refresh();
        }
    }

    protected override void OnValidityChanged()
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        if (onConfirm(SelectedLevel.Value, SelectedOwner.Value, Name.Trim(), [.. Slots]) is { } error)
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
