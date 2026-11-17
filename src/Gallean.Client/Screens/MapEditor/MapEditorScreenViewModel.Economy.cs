using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The economy panel (E): per race the resources on its land and its income per turn, at Average difficulty, from the
// game's own income code (CustomMapEconomy). It follows every change of the map; selecting a race outlines its
// resources and rods on the map.
internal sealed partial class MapEditorScreenViewModel
{
    private WorldState? economyState;
    private bool updatingEconomy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLandmarkPalette))]
    public partial bool IsEconomyOpen { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorEconomyRaceViewModel> EconomyRaces { get; private set; } = [];

    // Resources on land no race owns.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnclaimed))]
    public partial IReadOnlyList<MapEditorEconomyRowViewModel> UnclaimedResources { get; private set; } = [];

    [ObservableProperty]
    public partial MapEditorEconomyRaceViewModel? SelectedEconomyRace { get; set; }

    public bool HasUnclaimed => UnclaimedResources.Count > 0;

    // The palette shares the left of the map with the economy panel.
    public bool ShowLandmarkPalette => IsLandmarkTool && !IsEconomyOpen;

    public CustomMapEconomy? Economy { get; private set; }

    [RelayCommand]
    private void ToggleEconomy()
    {
        IsEconomyOpen = !IsEconomyOpen;
    }

    partial void OnIsEconomyOpenChanged(bool value)
    {
        if (!value)
        {
            SelectedEconomyRace = null;
        }

        RefreshPreview();
    }

    partial void OnSelectedEconomyRaceChanged(MapEditorEconomyRaceViewModel? value)
    {
        if (!updatingEconomy)
        {
            RefreshPreview();
        }
    }

    // Recomputes the numbers when the shown state changed (terrain strokes, objects, rods, undo, preview rebuilds).
    private void UpdateEconomy()
    {
        if (!IsEconomyOpen || ReferenceEquals(state, economyState))
        {
            return;
        }

        economyState = state;
        Economy = CustomMapEconomy.Calculate(state, editorGame.Session.EngineContext);
        WorldRace? selected = SelectedEconomyRace?.Race;
        updatingEconomy = true;

        try
        {
            EconomyRaces = [.. Economy.Races.Select(race => new MapEditorEconomyRaceViewModel(race))];
            UnclaimedResources = [.. Economy.Unclaimed.Select(entry => new MapEditorEconomyRowViewModel(new CustomMapEconomyRow(entry.Type, entry.Count, 0, 0)))];
            SelectedEconomyRace = EconomyRaces.FirstOrDefault(race => race.Race == selected);
        }
        finally
        {
            updatingEconomy = false;
        }
    }

    // The tiles outlined for the selected race: resources on its land and its rods.
    private IReadOnlyList<WorldPosition> GetEconomyHighlight()
    {
        if (!IsEconomyOpen || SelectedEconomyRace is not { } selected)
        {
            return [];
        }

        return
        [
            .. map.Resources.Where(resource => map.GetLandOwner(resource.Position) == selected.Race).Select(resource => resource.Position),
            .. map.Rods.Where(rod => rod.Owner == selected.Race).Select(rod => rod.Position)
        ];
    }
}

// One race's block in the economy panel, laid out like the game's "Income per turn" table.
internal sealed class MapEditorEconomyRaceViewModel(CustomMapEconomyRace race)
{
    public WorldRace Race => race.Race;
    public string Title => MapEditorLabels.Race(race.Race);
    public string Summary => race.Rods == 1
        ? Tr("Land: {0} tiles ({1}%) · 1 rod", race.LandTiles, race.LandPercent)
        : Tr("Land: {0} tiles ({1}%) · {2} rods", race.LandTiles, race.LandPercent, race.Rods);
    public IReadOnlyList<MapEditorEconomyRowViewModel> Rows { get; } = [.. race.Rows.Select(row => new MapEditorEconomyRowViewModel(row))];
}

// A resource type: the game's income row (icon, name, capital, sources, total) plus how many resources of it.
internal sealed class MapEditorEconomyRowViewModel(CustomMapEconomyRow row)
{
    public WorldIncomeRowViewModel Income { get; } = new(row.Type, row.CapitalIncome, row.ResourcesIncome, row.Total);
    public int Count => row.Resources;
    public string CountText => row.Resources.ToString();
}
