using System.Collections.Immutable;

namespace Gallean.Engine;

// One resource type of a race in the economy overview: how many resources of that type stand on its land, and its
// income per turn of that type from the capital (the race's base income), from those resources and from its towns
// (map rule "town income", gold only).
public sealed record CustomMapEconomyRow(WorldResourceType Type, int Resources, int CapitalIncome, int ResourcesIncome)
{
    public int TownsIncome { get; init; }

    public int Total => CapitalIncome + ResourcesIncome + TownsIncome;
}

// A race in the economy overview: its land (tiles and the share of the land the game counts for "transform land"),
// its rods, and one row per resource type it has or earns.
public sealed record CustomMapEconomyRace(WorldRace Race, int LandTiles, int LandPercent, int Rods, ImmutableArray<CustomMapEconomyRow> Rows)
{
    // Where the resources on its land stand.
    public ImmutableArray<WorldPosition> ResourcePositions { get; init; } = [];

    public CustomMapEconomyRow? GetRow(WorldResourceType type)
    {
        return Rows.FirstOrDefault(row => row.Type == type);
    }
}

// The map editor's economy overview: who owns which resources and what each race earns per turn, computed with the
// game's own income code (PlayerIncomeCalculator, the code behind the world screen's "Income per turn" table) on
// the editor's preview state, so the numbers match the game. Always at Average difficulty, as a human player: no
// difficulty bonus and no AI attitude scaling (both are 100 % at Average anyway).
public sealed record CustomMapEconomy(ImmutableArray<CustomMapEconomyRace> Races, ImmutableArray<(WorldResourceType Type, int Count)> Unclaimed)
{
    // Gold per turn all neutral towns bring their neutral sides together (town income phase 3).
    public int NeutralTownsIncome { get; init; }

    private static readonly ImmutableArray<WorldResourceType> ResourceTypes =
    [
        WorldResourceType.Gold,
        WorldResourceType.LifeMana,
        WorldResourceType.InfernalMana,
        WorldResourceType.DeathMana,
        WorldResourceType.RuneMana,
        WorldResourceType.GroveMana
    ];

    public CustomMapEconomyRace? GetRace(WorldRace race)
    {
        return Races.FirstOrDefault(entry => entry.Race == race);
    }

    // townIncome: the table being edited (the loaded scenario's when null).
    public static CustomMapEconomy Calculate(WorldState state, IEngineContext context, ImmutableArray<int>? townIncome = null)
    {
        WorldState average = state with { Difficulty = WorldDifficulty.Average };
        WorldPlan plan = WorldPlan.For(average);
        Dictionary<WorldPlayerId, Dictionary<WorldResourceType, int>> counts = [];
        Dictionary<WorldResourceType, int> unclaimed = [];
        Dictionary<WorldPlayerId, List<WorldPosition>> positions = [];

        foreach (WorldResourceNode node in average.ResourceNodes.Values)
        {
            Dictionary<WorldResourceType, int> bucket = plan.GetLandOwner(node.Position) is { } owner
                ? counts.TryGetValue(owner, out Dictionary<WorldResourceType, int>? existing) ? existing : counts[owner] = []
                : unclaimed;

            bucket[node.ResourceType] = bucket.GetValueOrDefault(node.ResourceType) + 1;

            if (plan.GetLandOwner(node.Position) is { } landOwner)
            {
                (positions.TryGetValue(landOwner, out List<WorldPosition>? list) ? list : positions[landOwner] = []).Add(node.Position);
            }
        }

        List<CustomMapEconomyRace> races = [];

        foreach (WorldPlayer player in average.Players.Values)
        {
            if (!WorldHelpers.IsRacePlayable(player.Race))
            {
                continue;
            }

            PlayerIncomeCalculator.IncomeInfo income = PlayerIncomeCalculator.GetIncomeInfo(player with { IsAi = false }, average, context, townIncome ?? context.Scenario.Definition.TownIncome);
            Dictionary<WorldResourceType, int> owned = counts.GetValueOrDefault(player.Id) ?? [];
            List<CustomMapEconomyRow> rows = [];

            foreach (WorldResourceType type in ResourceTypes)
            {
                CustomMapEconomyRow row = new(type, owned.GetValueOrDefault(type), Get(income.Capital, type), Get(income.ResourceNodes, type))
                {
                    TownsIncome = Get(income.Towns, type)
                };

                if (row.Resources > 0 || row.Total > 0)
                {
                    rows.Add(row);
                }
            }

            WorldMapTerrain terrain = WorldHelpers.GetNativeTerrain(player.Race);
            int tiles = average.Map.Cells.Count(cell => cell.Terrain == terrain);

            races.Add(new CustomMapEconomyRace(
                player.Race,
                tiles,
                TerrainTransformCalculator.GetTransformedPercentage(average.Map, terrain),
                average.Rods.Values.Count(rod => rod.OwnerId == player.Id),
                [.. rows])
            {
                ResourcePositions = [.. positions.GetValueOrDefault(player.Id) ?? []]
            });
        }

        int neutralTowns = average.Players.Values
            .Where(player => player.Race == WorldRace.Neutral)
            .Sum(player => PlayerIncomeCalculator.GetIncomeInfo(player with { IsAi = false }, average, context, townIncome ?? context.Scenario.Definition.TownIncome).Towns.Gold);

        return new CustomMapEconomy(
            [.. races],
            [.. ResourceTypes.Where(unclaimed.ContainsKey).Select(type => (type, unclaimed[type]))])
        {
            NeutralTownsIncome = neutralTowns
        };
    }

    public static int Get(WorldResourceSet resources, WorldResourceType type)
    {
        return type switch
        {
            WorldResourceType.Gold => resources.Gold,
            WorldResourceType.LifeMana => resources.LifeMana,
            WorldResourceType.DeathMana => resources.DeathMana,
            WorldResourceType.RuneMana => resources.RuneMana,
            WorldResourceType.InfernalMana => resources.InfernalMana,
            WorldResourceType.GroveMana => resources.GroveMana,
            _ => 0
        };
    }
}
