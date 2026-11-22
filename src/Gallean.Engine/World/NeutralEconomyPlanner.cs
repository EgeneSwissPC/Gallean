using System.Collections.Immutable;
using Gallean.Common;

namespace Gallean.Engine;

// Gallean Upgrade map rule "neutrals buy units" (docs/town-income.md phase 4): at a neutral side's turn start, right
// after its income, it spends its gold village by village (random order). Each village makes one purchase per turn,
// any number while the side's gold is above the spending line:
//   1. its garrison, while it has room: one of the kind's soldiers;
//   2. while the side owns fewer units than factor x (sum of its villages' levels): a unit for its army waiting at the
//      village entrance, or, with the entrance free, a new leader there (the army waits with the Stand order);
//   3. otherwise, with the garrison full: a village upgrade.
// A level-N village sells the cheapest N/5 (rounded up, at least one) of its kind's soldiers and leaders, and the pick
// among those that are affordable and fit is random, weighted by price ^ t with t = clamp(gold / 2000 - 1, -1, 1):
// cheap units are likelier while the side is poor, dear ones while it is rich.
internal static class NeutralEconomyPlanner
{
    public const int GoldPivot = 2000;
    private const int MaxRounds = 50;

    public sealed record Pools(ImmutableArray<UnitDefinition> Soldiers, ImmutableArray<UnitDefinition> Leaders);

    public static Pools GetPools(WorldSubraceType kind, IGameDefinitionProvider definitions)
    {
        UnitDefinition[] units =
        [
            .. definitions.GetNeutralUnits().Where(unit =>
                unit.Subrace == kind &&
                !unit.IsWaterOnly &&
                unit.PurchaseCost.Gold > 0 &&
                unit.Category is UnitCategory.Soldier or UnitCategory.NeutralSoldier or UnitCategory.Leader or UnitCategory.NeutralLeader)
        ];

        // OrderBy is stable: equal prices keep the data order.
        return new Pools(
            [.. units.Where(unit => (unit.Leadership ?? 0) == 0).OrderBy(unit => unit.PurchaseCost.Gold)],
            [.. units.Where(unit => (unit.Leadership ?? 0) > 0).OrderBy(unit => unit.PurchaseCost.Gold)]);
    }

    // The cheapest level/5 of the pool (rounded up, at least one).
    public static IEnumerable<UnitDefinition> GetAllowed(ImmutableArray<UnitDefinition> pool, int level)
    {
        int count = Math.Max(1, ((pool.Length * Math.Clamp(level, 1, TownIncomeCalculator.LevelCount)) + TownIncomeCalculator.LevelCount - 1) / TownIncomeCalculator.LevelCount);

        return pool.Take(count);
    }

    // Weight of a candidate for a side with this much gold.
    public static double GetWeight(int price, int gold)
    {
        double t = Math.Clamp(((double)gold / GoldPivot) - 1, -1, 1);

        return Math.Pow(Math.Max(1, price), t);
    }

    public static TItem? Pick<TItem>(IReadOnlyList<(TItem Item, int Price)> candidates, int gold, IRandomProvider random)
        where TItem : class
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        double[] weights = [.. candidates.Select(candidate => GetWeight(candidate.Price, gold))];
        double roll = random.NextDouble() * weights.Sum();

        for (int i = 0; i < candidates.Count; i++)
        {
            roll -= weights[i];

            if (roll < 0)
            {
                return candidates[i].Item;
            }
        }

        return candidates[^1].Item;
    }

    public static ImmutableArray<WorldEvent> Plan(WorldState state, WorldPlayer player, IEngineContext context)
    {
        WorldSubrace? subrace = state.Subraces.Values.FirstOrDefault(candidate => candidate.PlayerId == player.Id);

        if (subrace is null)
        {
            return [];
        }

        Pools pools = GetPools(subrace.Subrace, context.Definitions);

        if (pools.Soldiers.IsEmpty && pools.Leaders.IsEmpty)
        {
            return [];
        }

        return new Session(state, player, pools, context).Run();
    }

    private sealed class Session(WorldState state, WorldPlayer player, Pools pools, IEngineContext context)
    {
        private readonly List<WorldEvent> events = [];
        private readonly Dictionary<WorldFortificationId, UnitGrid<WorldUnit>> garrisons = [];
        private readonly Dictionary<WorldFortificationId, int> levels = [];
        private readonly Dictionary<WorldPosition, WorldStack> entranceStacks = [];
        private readonly HashSet<WorldFortificationId> grown = [];
        private readonly HashSet<WorldStackId> usedStackIds = [.. state.Stacks.Keys, .. state.ScenarioState.DestroyedStacks.Select(stack => stack.StackId)];
        private IEnumerator<WorldUnitId>? unitIds;
        private int gold = player.Resources.Gold;
        private int unitCount;

        private int SpendLine => context.Globals.MaxResourceAmount * WorldAiSpending.DefaultSpendAbovePercent / 100;

        private int Target => context.Scenario.Definition.NeutralUnitFactor * levels.Values.Sum();

        public ImmutableArray<WorldEvent> Run()
        {
            WorldPlan plan = WorldPlan.For(state);
            List<WorldCity> cities = [];

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                if (fortification is WorldCity city && city.OwnerId == player.Id)
                {
                    levels[city.Id] = context.Definitions.GetCity(city.DefinitionId).Level;
                    garrisons[city.Id] = city.Garrison;
                    unitCount += city.Garrison.GetUnits().Count();

                    if (!FortificationCalculator.IsRioting(city))
                    {
                        cities.Add(city);
                    }

                    if (plan.GetStackAt(city.Entrance) is { } stationed)
                    {
                        entranceStacks[city.Entrance] = stationed;
                    }
                }
            }

            unitCount += state.Stacks.Values.Where(stack => stack.OwnerId == player.Id).Sum(stack => stack.Units.GetUnits().Count());

            if (cities.Count == 0)
            {
                return [];
            }

            WorldCity[] order = [.. cities.OrderBy(city => city.Id.Value, StringComparer.Ordinal)];
            context.Random.Shuffle(order);

            for (int round = 0; round < MaxRounds; round++)
            {
                bool bought = false;

                foreach (WorldCity city in order)
                {
                    if (round > 0 && gold <= SpendLine)
                    {
                        break;
                    }

                    bought |= TryPurchase(city);
                }

                if (!bought || gold <= SpendLine)
                {
                    break;
                }
            }

            return [.. events];
        }

        private bool TryPurchase(WorldCity city)
        {
            int level = levels[city.Id];
            UnitGrid<WorldUnit> garrison = garrisons[city.Id];
            ImmutableArray<UnitDefinition> soldiers = [.. GetAllowed(pools.Soldiers, level)];

            // 1. The garrison while one of the allowed soldiers could still fit (if none is affordable, save the gold).
            if (soldiers.Any(soldier => FindGarrisonPosition(garrison, level, MakeUnit(soldier, new WorldUnitId("probe"))) is not null))
            {
                return BuyForGarrison(city, level, garrison, soldiers);
            }

            // 2. Below the unit target: the army at the entrance, or a new leader there.
            if (unitCount < Target)
            {
                if (entranceStacks.TryGetValue(city.Entrance, out WorldStack? waiting))
                {
                    return waiting.OwnerId == player.Id && BuyForArmy(waiting, soldiers);
                }

                return HireLeader(city, level);
            }

            // 3. A full garrison and enough units: grow the village.
            return Upgrade(city, level);
        }

        private bool BuyForGarrison(WorldCity city, int level, UnitGrid<WorldUnit> garrison, ImmutableArray<UnitDefinition> soldiers)
        {
            List<((UnitDefinition Definition, GridPosition Position) Item, int Price)> candidates = [];

            foreach (UnitDefinition soldier in soldiers.Where(soldier => soldier.PurchaseCost.Gold <= gold))
            {
                if (FindGarrisonPosition(garrison, level, MakeUnit(soldier, new WorldUnitId("probe"))) is { } position)
                {
                    candidates.Add(((soldier, position), soldier.PurchaseCost.Gold));
                }
            }

            if (PickTuple(candidates) is not { } picked)
            {
                return false;
            }

            WorldUnit unit = MakeUnit(picked.Definition, NextUnitId());
            WorldGarrisonBuilder builder = NewGarrisonBuilder(garrison, level);
            builder.PlaceUnit(unit, picked.Position);
            garrisons[city.Id] = builder.Build();
            Pay(picked.Definition.PurchaseCost.Gold);
            events.Add(new WorldUnitPurchasedEvent(city.Id, unit, picked.Position, picked.Definition.PurchaseCost));

            return true;
        }

        private bool BuyForArmy(WorldStack stack, ImmutableArray<UnitDefinition> soldiers)
        {
            List<((UnitDefinition Definition, GridPosition Position) Item, int Price)> candidates = [];

            foreach (UnitDefinition soldier in soldiers.Where(soldier => soldier.PurchaseCost.Gold <= gold))
            {
                if (FindStackPosition(stack.Units, MakeUnit(soldier, new WorldUnitId("probe"))) is { } position)
                {
                    candidates.Add(((soldier, position), soldier.PurchaseCost.Gold));
                }
            }

            if (PickTuple(candidates) is not { } picked)
            {
                return false;
            }

            WorldUnit unit = MakeUnit(picked.Definition, NextUnitId());
            WorldStackBuilder builder = NewStackBuilder(stack.Units);
            builder.PlaceUnit(unit, picked.Position);
            entranceStacks[stack.Position] = stack with { Units = builder.Build() };
            Pay(picked.Definition.PurchaseCost.Gold);
            unitCount++;
            events.Add(new WorldNeutralUnitHiredEvent(stack.Id, unit, picked.Position, picked.Definition.PurchaseCost));

            return true;
        }

        private bool HireLeader(WorldCity city, int level)
        {
            List<(UnitDefinition Item, int Price)> candidates =
            [
                .. GetAllowed(pools.Leaders, level)
                    .Where(leader => leader.PurchaseCost.Gold <= gold)
                    .Select(leader => (leader, leader.PurchaseCost.Gold))
            ];

            if (Pick(candidates, gold, context.Random) is not { } definition)
            {
                return false;
            }

            WorldUnit leader = MakeUnit(definition, NextUnitId()) with
            {
                Name = LeaderNameCalculator.TryGetName(definition, context.Definitions, context.Random)
            };

            WorldStackBuilder builder = new WorldStackBuilder(context.Definitions).PlaceUnit(leader, GridPosition.FrontCenter);

            if (!builder.TryBuild(out UnitGrid<WorldUnit>? units, out _))
            {
                return false;
            }

            WorldStack stack = new(
                NextStackId(),
                player.Id,
                city.Entrance,
                WorldStackDirection.South,
                units,
                definition.MovementPoints ?? 0,
                Inventory: WorldInventory.Empty,
                LeaderEquipment.Empty,
                WorldCommandHelpers.GetPlayerSubraceId(state, player.Id),
                context.Globals.AiDefaultPriority,
                AiIgnore: false,
                Concealed: false,
                WorldStackOrder.Stand.Instance,
                ExpirationTurn: null,
                PendingLeaderUpgrades: 0,
                BattlesWon: 0,
                SourceTemplateId: null)
            {
                NeutralHomeId = city.Id
            };

            entranceStacks[city.Entrance] = stack;
            Pay(definition.PurchaseCost.Gold);
            unitCount++;
            events.Add(new WorldStackCreatedEvent(stack, definition.PurchaseCost));

            return true;
        }

        private bool Upgrade(WorldCity city, int level)
        {
            if (grown.Contains(city.Id) || city.GrowthTurn == state.TurnInfo.TurnNumber || level >= context.Scenario.Definition.MaxCityLevel)
            {
                return false;
            }

            WorldCityDefinitionId next = city.DefinitionId with { Level = level + 1 };

            if (!context.Definitions.GetRace(city.DefinitionId.Race).Cities.Contains(next))
            {
                return false;
            }

            WorldCityDefinition current = context.Definitions.GetCity(city.DefinitionId with { Level = level });
            WorldResourceSet cost = CityCalculator.GetGrowthCost(current, player, context);

            if (cost.Gold > gold || !ResourceSetCalculator.CanAfford(WorldResourceSet.Empty with { Gold = gold }, cost))
            {
                return false;
            }

            grown.Add(city.Id);
            levels[city.Id] = level + 1;
            Pay(cost.Gold);
            events.Add(new WorldCityUpgradedEvent(city.Id, next, cost));

            return true;
        }

        private (UnitDefinition Definition, GridPosition Position)? PickTuple(List<((UnitDefinition Definition, GridPosition Position) Item, int Price)> candidates)
        {
            List<(Box Item, int Price)> boxed = [.. candidates.Select(candidate => (new Box(candidate.Item.Definition, candidate.Item.Position), candidate.Price))];

            return Pick(boxed, gold, context.Random) is { } box ? (box.Definition, box.Position) : null;
        }

        private sealed record Box(UnitDefinition Definition, GridPosition Position);

        private void Pay(int amount)
        {
            gold -= amount;
        }

        private WorldUnit MakeUnit(UnitDefinition definition, WorldUnitId id)
        {
            UnitStats stats = UnitStatsCalculator.GetStats(definition, definition.Level, [], context.Definitions, context.Globals);

            return new WorldUnit(id, definition.Id, stats.MaxHitpoints, 0, definition.Level, [], null, IsTypeLocked: false);
        }

        private WorldUnitId NextUnitId()
        {
            unitIds ??= WorldCommandHelpers.GenerateUnitIds(state).GetEnumerator();
            unitIds.MoveNext();

            return unitIds.Current;
        }

        private WorldStackId NextStackId()
        {
            for (int index = 1;; index++)
            {
                WorldStackId id = new($"Stack{index:D4}");

                if (usedStackIds.Add(id))
                {
                    return id;
                }
            }
        }

        private WorldGarrisonBuilder NewGarrisonBuilder(UnitGrid<WorldUnit> garrison, int level)
        {
            WorldGarrisonBuilder builder = new(context.Definitions, garrison, level);

            foreach ((WorldUnit unit, GridPosition position) in garrison.GetUnits())
            {
                builder.PlaceUnit(unit, position);
            }

            return builder;
        }

        private WorldStackBuilder NewStackBuilder(UnitGrid<WorldUnit> units)
        {
            WorldStackBuilder builder = new(context.Definitions);

            foreach ((WorldUnit unit, GridPosition position) in units.GetUnits())
            {
                builder.PlaceUnit(unit, position);
            }

            return builder;
        }

        private GridPosition? FindGarrisonPosition(UnitGrid<WorldUnit> garrison, int level, WorldUnit unit)
        {
            foreach (GridPosition position in GridPosition.All)
            {
                WorldGarrisonBuilder builder = NewGarrisonBuilder(garrison, level);

                if (builder.TryPlaceUnit(unit, position) && builder.TryBuild(out _, out _))
                {
                    return position;
                }
            }

            return null;
        }

        private GridPosition? FindStackPosition(UnitGrid<WorldUnit> units, WorldUnit unit)
        {
            foreach (GridPosition position in GridPosition.All)
            {
                WorldStackBuilder builder = NewStackBuilder(units);

                if (builder.TryPlaceUnit(unit, position) && builder.TryBuild(out _, out _))
                {
                    return position;
                }
            }

            return null;
        }
    }
}
