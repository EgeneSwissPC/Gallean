using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Recipes (docs/map-editor-events.md, phase 4): ready-made events built from ordinary blocks, so they open in the
// block editor and can be changed like any other. A recipe can make several events (one win per attacking race), so
// recipes live in the events list. Targets start at the map's first village, zone or ruin and the first race; the
// user then points them at the right place ("On map"). Every recipe that ends the game for a race also gets that race
// a conquest event, because in the game a race with a win event no longer wins by being the last one standing.
internal enum MapEditorRecipe
{
    CaptureCity,
    ReachZone,
    SurviveTurns,
    HoldOut,
    LoseCity,
    ZoneWarning,
    RuinTreasure
}

internal sealed partial class MapEditorScreenViewModel
{
    private const int DefaultRecipeTurns = 30;

    public IReadOnlyList<MapEditorOption<MapEditorRecipe>> Recipes { get; } =
    [
        new(MapEditorRecipe.CaptureCity, Tr("Capture a city to win")),
        new(MapEditorRecipe.ReachZone, Tr("Reach a zone to win")),
        new(MapEditorRecipe.SurviveTurns, Tr("Survive N turns")),
        new(MapEditorRecipe.HoldOut, Tr("Hold out against attackers")),
        new(MapEditorRecipe.LoseCity, Tr("Lose if a city falls")),
        new(MapEditorRecipe.ZoneWarning, Tr("Warning when entering a zone")),
        new(MapEditorRecipe.RuinTreasure, Tr("Message when looting a ruin"))
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RecipeDescription))]
    public partial MapEditorOption<MapEditorRecipe>? SelectedRecipe { get; set; }

    // The box starts on the first recipe: an empty drop-down looked like nothing was there.
    private void InitRecipes() => SelectedRecipe = Recipes[0];

    public string RecipeDescription => SelectedRecipe?.Value switch
    {
        MapEditorRecipe.CaptureCity => Tr("The first race wins when it owns the chosen city."),
        MapEditorRecipe.ReachZone => Tr("The first race wins when one of its armies enters the zone. The computer gets no goal from zones."),
        MapEditorRecipe.SurviveTurns => Tr("The first race wins on turn {0}. The computer gets no goal from it.", DefaultRecipeTurns),
        MapEditorRecipe.HoldOut => Tr("The first race wins on turn {0}; every other race wins if it takes the first race's capital before that. The computer races march on that capital.", DefaultRecipeTurns),
        MapEditorRecipe.LoseCity => Tr("The players lose when another race takes the city (for maps where the player defends with the first race)."),
        MapEditorRecipe.ZoneWarning => Tr("A message and the zone revealed when an army of the first race enters it."),
        MapEditorRecipe.RuinTreasure => Tr("A message for whoever loots the ruin."),
        _ => Tr("Pick a recipe: it adds ready-made events you can then change.")
    };

    [RelayCommand]
    private void AddRecipe()
    {
        if (SelectedRecipe is not { } recipe)
        {
            return;
        }

        int? first = null;
        CommitEvents(current =>
        {
            first = AddRecipeEvents(current, recipe.Value);
            AddConquestWins(current);
            return true;
        });

        if (first is { } id)
        {
            OpenEvent(id);
        }
    }

    // Adds the recipe's events to the map; returns the main one's ID.
    private static int AddRecipeEvents(CustomMap current, MapEditorRecipe recipe)
    {
        WorldRace race = current.Players[0].Race;
        string raceName = MapEditorLabels.RaceShort(race);
        WorldRace[] others = [.. current.Players.Select(player => player.Race).Where(other => other != race)];
        CustomMapVillage? village = current.Villages.FirstOrDefault();
        CustomMapCity city = village is not null
            ? new CustomMapCity.Village(village.Id)
            : new CustomMapCity.Capital(others.FirstOrDefault(race));
        string cityName = village?.Name ?? Tr("{0} capital", MapEditorLabels.RaceShort(others.FirstOrDefault(race)));
        int zone = current.Locations.FirstOrDefault()?.Id ?? 0;
        string zoneName = current.Locations.FirstOrDefault()?.Name ?? Tr("the zone");
        int ruin = current.Ruins.FirstOrDefault()?.Id ?? 0;

        CustomMapMessageEffect Message(string text, CustomMapMessageAudience audience = CustomMapMessageAudience.AllPlayers)
        {
            return new CustomMapMessageEffect(CustomMapText.Of(text), audience, null, true);
        }

        CustomMapEvent Event(string name, WorldRace triggeredBy, ImmutableArray<CustomMapCondition> conditions, params CustomMapEffect[] effects)
        {
            return CustomMapEvent.Create(name, triggeredBy) with { Groups = [conditions], Effects = [.. effects] };
        }

        switch (recipe)
        {
            case MapEditorRecipe.CaptureCity:
                return current.AddEvent(Event(
                    Tr("{0} captures {1}", raceName, cityName),
                    race,
                    [new CustomMapOwnCityCondition(city)],
                    Message(Tr("{0} has captured {1}!", raceName, cityName)),
                    new CustomMapWinEffect(race))).Id;
            case MapEditorRecipe.ReachZone:
                return current.AddEvent(Event(
                    Tr("{0} reaches {1}", raceName, zoneName),
                    race,
                    [new CustomMapEnterZoneCondition(zone)],
                    Message(Tr("An army of {0} has reached {1}!", raceName, zoneName)),
                    new CustomMapWinEffect(race))).Id;
            case MapEditorRecipe.SurviveTurns:
                return current.AddEvent(Event(
                    Tr("{0} survives", raceName),
                    race,
                    [new CustomMapTurnCondition(DefaultRecipeTurns, Repeat: false)],
                    Message(Tr("{0} has held out to the end!", raceName)),
                    new CustomMapWinEffect(race))).Id;
            case MapEditorRecipe.HoldOut:
            {
                int main = current.AddEvent(Event(
                    Tr("{0} holds out", raceName),
                    race,
                    [new CustomMapTurnCondition(DefaultRecipeTurns, Repeat: false)],
                    Message(Tr("{0} has held out to the end!", raceName)),
                    new CustomMapWinEffect(race))).Id;

                foreach (WorldRace attacker in others)
                {
                    string attackerName = MapEditorLabels.RaceShort(attacker);
                    current.AddEvent(Event(
                        Tr("{0} takes the {1} capital", attackerName, raceName),
                        attacker,
                        [new CustomMapOwnCityCondition(new CustomMapCity.Capital(race))],
                        Message(Tr("{0} has taken the {1} capital!", attackerName, raceName)),
                        new CustomMapWinEffect(attacker)));
                }

                return main;
            }

            case MapEditorRecipe.LoseCity:
                return current.AddEvent(CustomMapEvent.Create(Tr("{0} falls", cityName), others.FirstOrDefault(race)) with
                {
                    Races = [.. others],
                    Groups = [[new CustomMapOwnCityCondition(village is not null ? city : new CustomMapCity.Capital(race))]],
                    Effects = [Message(Tr("{0} has fallen.", village?.Name ?? Tr("{0} capital", raceName))), new CustomMapLoseEffect()]
                }).Id;
            case MapEditorRecipe.ZoneWarning:
                return current.AddEvent(Event(
                    Tr("Warning at {0}", zoneName),
                    race,
                    [new CustomMapEnterZoneCondition(zone)],
                    Message(Tr("Scout\n\"Beware, my lord: danger lies ahead in {0}.\"", zoneName), CustomMapMessageAudience.Triggerer),
                    new CustomMapRevealZoneEffect(zone, []))).Id;
            case MapEditorRecipe.RuinTreasure:
            {
                string ruinName = current.Ruins.FirstOrDefault()?.Name ?? Tr("the ruin");

                return current.AddEvent(CustomMapEvent.Create(Tr("Looting {0}", ruinName), race) with
                {
                    Races = [.. current.Players.Select(player => player.Race)],
                    Groups = [[new CustomMapLootRuinCondition(ruin)]],
                    Effects = [Message(Tr("Among the stones of {0} lie forgotten riches.", ruinName), CustomMapMessageAudience.Triggerer)]
                }).Id;
            }

            default:
                throw new ArgumentOutOfRangeException(nameof(recipe), recipe, null);
        }
    }

    private static void AddConquestWins(CustomMap current)
    {
        foreach (WorldRace race in current.GetRacesNeedingConquestWin())
        {
            current.AddConquestWin(race, Tr("{0} conquers all capitals", MapEditorLabels.RaceShort(race)));
        }
    }
}
