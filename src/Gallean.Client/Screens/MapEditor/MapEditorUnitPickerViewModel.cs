using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed record MapEditorRaceFilter(WorldRace? Race, string Label);

internal sealed class MapEditorUnitPickerItemViewModel : IDetailsSource
{
    public MapEditorUnitPickerItemViewModel(
        MapEditorUnitPickerViewModel owner,
        UnitDefinition definition,
        string name,
        string description,
        string info,
        string? unavailableReason,
        UnitDetailsViewModel details)
    {
        Definition = definition;
        PortraitKey = new AssetSpriteKey.UnitPortrait(definition.Id);
        Name = name;
        Description = description;
        Info = info;
        UnavailableReason = unavailableReason;
        Details = details;
        PickCommand = new RelayCommand(() => owner.Pick(this), () => UnavailableReason is null);
    }

    public UnitDefinition Definition { get; }
    public AssetSpriteKey.UnitPortrait PortraitKey { get; }
    public string Name { get; }
    public string Description { get; }
    public string Info { get; }
    public string? UnavailableReason { get; }
    public bool IsUnavailable => UnavailableReason is not null;
    public UnitDetailsViewModel Details { get; }
    public IRelayCommand PickCommand { get; }

    object? IDetailsSource.DetailsContent => Details;
}

// Chooses a leader or a unit for an army, like the game's Hire leader / Hire unit lists but with every unit of
// every race and no prices. Right-click a row for the unit's full details.
internal sealed partial class MapEditorUnitPickerViewModel : AbstractScreenViewModel
{
    private readonly GameModel editorGame;
    private readonly IReadOnlyList<UnitDefinition> units;
    private readonly Func<UnitDefinition, string?> getUnavailableReason;
    private readonly Action<UnitDefinition> onPicked;

    public MapEditorUnitPickerViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        bool pickLeader,
        WorldRace initialRace,
        Func<UnitDefinition, string?> getUnavailableReason,
        Action<UnitDefinition> onPicked)
    {
        Game = game;
        this.editorGame = editorGame;
        this.getUnavailableReason = getUnavailableReason;
        this.onPicked = onPicked;
        this.units = [.. units.Where(unit => (unit.Leadership is > 0) == pickLeader)];
        Title = pickLeader ? Tr("Choose leader") : Tr("Choose unit");
        Filters =
        [
            new MapEditorRaceFilter(null, Tr("All")),
            .. new[] { WorldRace.Human, WorldRace.Undead, WorldRace.Heretic, WorldRace.Dwarf, WorldRace.Elf, WorldRace.Neutral }
                .Where(race => this.units.Any(unit => unit.Race == race))
                .Select(race => new MapEditorRaceFilter(race, MapEditorLabels.RaceShort(race)))
        ];
        SelectedFilter = Filters.FirstOrDefault(filter => filter.Race == initialRace) ?? Filters[0];
    }

    public GameViewModel Game { get; }
    public string Title { get; }
    public IReadOnlyList<MapEditorRaceFilter> Filters { get; }

    [ObservableProperty]
    public partial MapEditorRaceFilter SelectedFilter { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorUnitPickerItemViewModel> Options { get; private set; } = [];

    public void Pick(MapEditorUnitPickerItemViewModel item)
    {
        if (item.UnavailableReason is not null)
        {
            return;
        }

        CloseSelf();
        onPicked(item.Definition);
    }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    partial void OnSelectedFilterChanged(MapEditorRaceFilter value)
    {
        Options = BuildOptions(value.Race);
    }

    private IReadOnlyList<MapEditorUnitPickerItemViewModel> BuildOptions(WorldRace? race)
    {
        List<MapEditorUnitPickerItemViewModel> result = [];

        foreach (UnitDefinition definition in units.Where(unit => race is null || unit.Race == race))
        {
            string name = GetText(definition.NameTextId) ?? definition.Id.Value;
            string description = GetText(definition.DescriptionTextId) ?? string.Empty;
            List<string> info = [Tr("Level {0}", definition.Level)];

            if (definition.Leadership is > 0)
            {
                info.Add(Tr("leadership {0}", definition.Leadership));
            }

            if (definition.IsLarge)
            {
                info.Add(Tr("large"));
            }

            if (race is null)
            {
                info.Add(MapEditorLabels.RaceShort(definition.Race));
            }

            WorldUnit unit = new(UnitDetailsViewModel.PreviewUnitId, definition.Id, definition.MaxHitpoints, Experience: 0, definition.Level, [], null, false);
            UnitDetailsViewModel details = UnitDetailsViewModel.CreateFromWorld(unit, null, editorGame.Definitions, editorGame.Texts, editorGame.Stats);

            result.Add(new MapEditorUnitPickerItemViewModel(this, definition, name, description, string.Join(" · ", info), getUnavailableReason(definition), details));
        }

        return
        [
            .. result
                .OrderBy(item => item.Definition.Level)
                .ThenBy(item => item.Definition.Race)
                .ThenBy(item => item.Name, StringComparer.CurrentCulture)
        ];
    }

    private string? GetText(GlobalTextId id)
    {
        try
        {
            return editorGame.Texts.GetGlobalText(id).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return null;
        }
    }
}
