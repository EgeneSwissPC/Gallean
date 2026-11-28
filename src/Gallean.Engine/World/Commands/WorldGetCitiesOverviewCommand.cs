using System.Collections.Immutable;
using Gallean.Common;
using CommandResult = Gallean.Engine.ICommandResult<Gallean.Engine.WorldCommandError, Gallean.Engine.WorldEvent, Gallean.Engine.WorldGetCitiesOverviewCommand.Result>;

namespace Gallean.Engine;

// Gallean Upgrade (docs/cities-overview.md): one row per city and capital the current player owns, with the numbers the
// cities list shows that the client can't compute itself (engine-internal rules and unit stats).
public sealed record WorldGetCitiesOverviewCommand : IWorldCommand<WorldGetCitiesOverviewCommand.Result>
{
    public sealed record City(
        WorldFortificationId FortificationId,
        int TerrainGrowth,
        int OccupiedCapacity,
        int TotalCapacity,
        int WoundedUnits,
        int DeadUnits,
        WorldGetCityInfoCommand.Growth? Growth)
    {
        // Gold per turn the town pays (map rule "town income", before difficulty and AI scaling); null when the rule is off.
        public int? TownIncome { get; init; }

        // Distance in tiles (king moves, to the nearest tile of the town) to the nearest army of a hostile playable
        // race that stands on a tile the player has explored; null when there is none.
        public int? NearestEnemyDistance { get; init; }
    }

    public sealed record Result(ImmutableArray<City> Cities)
    {
        public bool Equals(Result? other)
        {
            return other is not null && Cities.SequenceEqual(other.Cities);
        }

        public override int GetHashCode()
        {
            HashCode hash = new();

            foreach (City city in Cities)
            {
                hash.Add(city);
            }

            return hash.ToHashCode();
        }
    }

    public CommandResult Execute(WorldState state, IEngineContext context)
    {
        WorldPlayer player = state.CurrentPlayer;
        ImmutableArray<int> townTable = context.Scenario.Definition.TownIncome;
        List<WorldStack> enemies = FindVisibleEnemies(state, player);
        ImmutableArray<City>.Builder cities = ImmutableArray.CreateBuilder<City>();

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            if (fortification is not (WorldCity or WorldCapital) || FortificationCalculator.GetOwner(fortification) != player.Id)
            {
                continue;
            }

            (int wounded, int dead) = CountWounded(fortification, context);
            WorldGetCityInfoCommand.Growth? growth = null;

            if (fortification is WorldCity city)
            {
                WorldCityDefinition definition = context.Definitions.GetCity(city.DefinitionId);
                WorldResourceSet cost = CityCalculator.GetGrowthCost(definition, player, context);
                WorldCommandResult upgrade = new WorldUpgradeCityCommand(city.Id).Execute(state, context);

                growth = new WorldGetCityInfoCommand.Growth(cost, upgrade.Error);
            }

            cities.Add(new City(
                fortification.Id,
                FortificationCalculator.GetTerrainGrowth(fortification, context.Globals),
                GarrisonCalculator.GetOccupiedCapacity(fortification.Garrison, context.Definitions),
                GarrisonCalculator.GetTotalCapacity(fortification, context.Globals),
                wounded,
                dead,
                growth)
            {
                TownIncome = townTable.IsDefaultOrEmpty ? null : TownIncomeCalculator.GetFortificationGold(fortification, townTable, context.Definitions),
                NearestEnemyDistance = GetNearestDistance(fortification.Footprint, enemies)
            });
        }

        return CommandResult.Success(new Result(cities.ToImmutable()), []);
    }

    private static List<WorldStack> FindVisibleEnemies(WorldState state, WorldPlayer player)
    {
        List<WorldStack> result = [];

        foreach (WorldStack stack in state.Stacks.Values)
        {
            if (stack.OwnerId == player.Id || stack.Concealed ||
                !state.Players.TryGetValue(stack.OwnerId, out WorldPlayer? owner) ||
                !WorldHelpers.IsRacePlayable(owner.Race) ||
                !DiplomacyCalculator.IsHostile(state, player.Id, stack.OwnerId) ||
                !IsExplored(player.FogOfWar, stack.Position))
            {
                continue;
            }

            result.Add(stack);
        }

        return result;
    }

    private static bool IsExplored(ImmutableBitmap fog, WorldPosition position)
    {
        return position.X >= 0 && position.Y >= 0 && position.X < fog.Width && position.Y < fog.Height && fog.Get(position.X, position.Y);
    }

    private static int? GetNearestDistance(WorldFootprint footprint, List<WorldStack> enemies)
    {
        int? nearest = null;

        foreach (WorldStack enemy in enemies)
        {
            int distance = GetDistance(footprint, enemy.Position);

            if (nearest is null || distance < nearest)
            {
                nearest = distance;
            }
        }

        return nearest;
    }

    // Tiles between a position and the nearest tile of a footprint, counting diagonal steps as one (armies move in 8 directions).
    public static int GetDistance(WorldFootprint footprint, WorldPosition position)
    {
        int right = footprint.TopLeft.X + footprint.Width - 1;
        int bottom = footprint.TopLeft.Y + footprint.Height - 1;
        int dx = Math.Max(Math.Max(footprint.TopLeft.X - position.X, position.X - right), 0);
        int dy = Math.Max(Math.Max(footprint.TopLeft.Y - position.Y, position.Y - bottom), 0);

        return Math.Max(dx, dy);
    }

    private static (int Wounded, int Dead) CountWounded(WorldFortification fortification, IEngineContext context)
    {
        int wounded = 0;
        int dead = 0;

        foreach ((WorldUnit unit, _) in fortification.Garrison.GetUnits())
        {
            if (!unit.IsAlive)
            {
                dead++;
            }
            else if (unit.Hitpoints < context.UnitStats.GetWorldUnitStats(unit).MaxHitpoints)
            {
                wounded++;
            }
        }

        return (wounded, dead);
    }
}
