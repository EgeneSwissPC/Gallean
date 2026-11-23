using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;
using Gallean.Entities;

namespace Gallean.Client;

internal sealed record MapEditorMapItem(string Id, string Name, string Details);

internal abstract record MapEditorLabeled(string Label);

internal sealed record MapEditorOption<T>(T Value, string Label) : MapEditorLabeled(Label);

internal sealed partial class MapEditorRaceOption(WorldRace race, string label, bool isSelected) : ObservableObject
{
    public WorldRace Race { get; } = race;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = isSelected;
}

// Lists the custom maps and creates new ones.
internal sealed partial class MapEditorMenuScreenViewModel : AbstractScreenViewModel
{
    public const int MaxNameLength = 60;

    private readonly CustomMapStore store;

    public MapEditorMenuScreenViewModel(GameViewModel game, CustomMapStore store)
    {
        Game = game;
        this.store = store;

        Sizes = [.. new[] { 48, 72, 96, 144, 192, 256, 384, 512, 768, 1024 }.Select(size => new MapEditorOption<int>(size, $"{size} × {size}"))];
        SelectedSize = Sizes.Single(option => option.Value == 96);
        BaseTerrains =
        [
            new MapEditorOption<WorldMapTerrain>(WorldMapTerrain.Neutral, Tr("Land")),
            new MapEditorOption<WorldMapTerrain>(WorldMapTerrain.Water, Tr("Water"))
        ];
        SelectedBaseTerrain = BaseTerrains[0];
        Races =
        [
            new MapEditorRaceOption(WorldRace.Human, MapEditorLabels.Race(WorldRace.Human), true),
            new MapEditorRaceOption(WorldRace.Undead, MapEditorLabels.Race(WorldRace.Undead), true),
            new MapEditorRaceOption(WorldRace.Heretic, MapEditorLabels.Race(WorldRace.Heretic), false),
            new MapEditorRaceOption(WorldRace.Dwarf, MapEditorLabels.Race(WorldRace.Dwarf), false),
            new MapEditorRaceOption(WorldRace.Elf, MapEditorLabels.Race(WorldRace.Elf), false)
        ];

        foreach (MapEditorRaceOption race in Races)
        {
            race.PropertyChanged += (_, _) => CreateCommand.NotifyCanExecuteChanged();
        }

        Name = Tr("New map");
        Background = MainMenuBackgroundViewModel.Create();
        Refresh();
    }

    public GameViewModel Game { get; }
    public MainMenuBackgroundViewModel Background { get; }
    public ObservableCollection<MapEditorMapItem> Maps { get; } = [];
    public IReadOnlyList<MapEditorOption<int>> Sizes { get; }
    public IReadOnlyList<MapEditorOption<WorldMapTerrain>> BaseTerrains { get; }
    public IReadOnlyList<MapEditorRaceOption> Races { get; }
    public string MapsDirectory => store.MapsDirectory;
    public int NameLength => MaxNameLength;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCommand), nameof(DeleteCommand))]
    public partial MapEditorMapItem? SelectedMap { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<int> SelectedSize { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldMapTerrain> SelectedBaseTerrain { get; set; }

    private readonly MusicQueue musicQueue = MusicQueue.Create([new AssetSoundKey.MenuMusic()], isLooping: true);

    public override bool PaintsOwnBackground => true;
    public override ScreenMusic Music => ScreenMusic.Tracks(musicQueue);

    protected override bool TryClose()
    {
        Game.ShowMainMenuCommand.Execute(null);

        return true;
    }

    private void Refresh()
    {
        Maps.Clear();

        foreach (Db.ScenarioInfo info in store.List())
        {
            int players = new[] { info.Player1, info.Player2, info.Player3, info.Player4, info.Player5, info.Player6, info.Player7 }.Count(race => race is >= 0 and < 99) - 1;
            Maps.Add(new MapEditorMapItem(info.ScenarioId.Value, info.Name, Tr("{0} × {0}, players: {1}", info.MapSize, players)));
        }

        SelectedMap = Maps.FirstOrDefault();
    }

    private bool CanCreate()
    {
        return !string.IsNullOrWhiteSpace(Name) && Races.Any(race => race.IsSelected);
    }

    [RelayCommand(CanExecute = nameof(CanCreate))]
    private async Task CreateAsync()
    {
        string name = Name.Trim();
        int size = SelectedSize.Value;
        WorldMapTerrain terrain = SelectedBaseTerrain.Value;
        WorldRace[] races = [.. Races.Where(race => race.IsSelected).Select(race => race.Race)];
        string id = store.NextId();

        await Game.OpenMapEditorAsync(store, _ =>
        {
            CustomMap map = CustomMap.Create(id, name, size, terrain, races);

            // Gallean Upgrade: new maps start with town income on (docs/town-income.md, decision A).
            map.SetTownIncome(CustomMap.DefaultTownIncome);
            map.NeutralBuying = true;

            return map;
        });
    }

    private bool CanOpen()
    {
        return SelectedMap is not null;
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private async Task OpenAsync()
    {
        if (SelectedMap is { } selected)
        {
            await Game.OpenMapEditorAsync(store, definitions => store.Open(selected.Id, definitions));
        }
    }

    [RelayCommand(CanExecute = nameof(CanOpen))]
    private void Delete()
    {
        if (SelectedMap is not { } selected)
        {
            return;
        }

        Game.OpenScreen(new ConfirmPromptViewModel(
            TrSpans("Delete map"),
            TrSpans("Delete \"{0}\"? This cannot be undone.", selected.Name),
            Tr("Delete"),
            ConfirmPromptType.Danger,
            () =>
            {
                store.Delete(selected.Id);
                Refresh();

                return Task.CompletedTask;
            }));
    }

    [RelayCommand]
    private void Back()
    {
        TryClose();
    }
}
