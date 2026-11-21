using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Gallean.Common;
using Gallean.Entities;

namespace Gallean.Engine;

// Gallean Upgrade (docs/battle-ai-designer.md): fights the same battle many times, headless.
// The host map is a built-in scenario or, by default in the sandbox and the tool, a generated map with every
// race (CreateWithGeneratedHost).
// Army A (with its battle AI) against army B (with its own). Each run builds the battle exactly like the
// game does (WorldCommandHelpers.CreateStackBattleState, or CreateGarrisonBattleState when the defender is a
// city's garrison) from a host map: the armies are owned by players of that map (by race; Neutral = the
// neutral player), all computer players so world battle modifiers such as the difficulty's accuracy bonus
// apply to both alike. The defender stands in the open, in a city of a chosen level (with the city's
// protection, like in the game) or in its race's capital, as an army or as the garrison. Then the battle is
// resolved like an AI-vs-AI battle in the game (BattleAutoResolveCommand with both AI IDs: the attacker
// retreats after round 15, hard stop at round 45). Every run has its own seed: same seed = same battle. With
// swapSides, army A attacks in even runs and defends in odd runs.
public sealed class AiBattleBench
{
    private readonly IGameDefinitionProvider definitions;
    private readonly GlobalVariables globals;
    private readonly Scenario scenario;
    private readonly WorldStack template;
    private readonly WorldCity? cityTemplate;
    private readonly WorldPlayerId firstOwner;
    private readonly WorldPlayerId secondOwner;
    private readonly WorldPosition openTile;
    private readonly Dictionary<UnitModifierDefinitionId, int> leaderUpgrades = [];

    // difficulty: the game difficulty both sides fight at. It sets the computer players' accuracy and experience
    // bonuses, so it is fixed here instead of taken from the host map (S099 is Very hard, generated maps Average).
    public AiBattleBench(GlobalDatabase globalDb, CampaignDatabase campaignDb, ScenarioDatabase scenarioDb, string hostMapId, WorldDifficulty difficulty = WorldDifficulty.Average)
    {
        definitions = new GameDefinitionProvider(globalDb);
        globals = new GlobalVariables();
        scenario = ScenarioLoader.Load(globalDb, scenarioDb, campaignDb, new ScenarioId(hostMapId), definitions, globals);

        // Both sides are computer players, so the difficulty bonuses (accuracy, experience) are the same for both.
        scenario = scenario with
        {
            State = scenario.State with
            {
                Difficulty = difficulty,
                Players = scenario.State.Players.SetItems(scenario.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
            }
        };

        WorldState state = scenario.State;
        template = state.Stacks.Values.FirstOrDefault() ?? throw new InvalidOperationException($"{hostMapId} has no army to copy.");
        cityTemplate = state.Fortifications.Values.OfType<WorldCity>().FirstOrDefault();

        List<WorldPlayerId> players = [.. state.Players.Values.Where(player => player.Race is not WorldRace.Neutral).Select(player => player.Id)];
        if (players.Count < 2)
        {
            players = [.. state.Players.Keys];
        }

        firstOwner = players[0];
        secondOwner = players.Count > 1 ? players[1] : players[0];
        openTile = FindOpenTile(state);

        // Every leader upgrade of every lord, with the lowest level any lord can take it at.
        foreach (IGrouping<Db.Id, Db.LeaderUpgrade> lord in globalDb.LeaderUpgradesByBelongsToId)
        {
            foreach (LeaderUpgradeDefinition upgrade in definitions.GetLeaderUpgrades(new LordDefinitionId(lord.Key)))
            {
                leaderUpgrades[upgrade.ModifierId] = Math.Min(upgrade.MinLevel, leaderUpgrades.GetValueOrDefault(upgrade.ModifierId, int.MaxValue));
            }
        }
    }

    // The ID and name of the generated host map (it only lives in a temporary folder while the bench loads it).
    public const string GeneratedHostId = "S899SC0000";
    public const string GeneratedHostName = "Battle sandbox";

    // A bench on a small generated map with every playable race (each with its capital), the neutrals, a city and
    // an army to copy, so any race or the neutrals can own an army and every capital works. No built-in map has
    // all five races. Made with the map editor's code (CustomMap + CustomMapStore), like a player's own map.
    public static AiBattleBench CreateWithGeneratedHost(GlobalDatabase globalDb, CampaignDatabase campaignDb, WorldDifficulty difficulty = WorldDifficulty.Average)
    {
        IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDb);
        WorldRace[] races = [WorldRace.Human, WorldRace.Undead, WorldRace.Heretic, WorldRace.Dwarf, WorldRace.Elf];
        CustomMap map = CustomMap.Create(GeneratedHostId, GeneratedHostName, 48, WorldMapTerrain.Neutral, races);

        for (int index = 0; index < races.Length; index++)
        {
            Require(map.PlaceCapital(races[index], new WorldPosition(2 + (index * 9), 2)), $"{races[index]} capital");
        }

        Require(map.PlaceVillage(new WorldPosition(20, 20), 1, WorldRace.Neutral, "Sandbox city").Error, "city");

        UnitDefinition leader = definitions.GetUnit(new UnitDefinitionId("g000uu5201"));
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];
        slots[1] = CustomMapUnit.From(leader);

        if (map.PlaceStack(new WorldPosition(30, 30), WorldRace.Neutral, slots).Error is { } stackError)
        {
            throw new InvalidOperationException($"The generated host map refused its army: {stackError}.");
        }

        string directory = Path.Combine(Path.GetTempPath(), "gallean-battle-host-" + Guid.NewGuid().ToString("N"));

        try
        {
            CustomMapStore store = new(directory, globalDb, campaignDb);
            store.Save(map);

            return new AiBattleBench(globalDb, campaignDb, store.LoadDatabase(GeneratedHostId), GeneratedHostId, difficulty);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        static void Require(CustomMapPlacementError? error, string what)
        {
            if (error is not null)
            {
                throw new InvalidOperationException($"The generated host map refused its {what}: {error}.");
            }
        }
    }

    public IGameDefinitionProvider Definitions => definitions;

    public WorldDifficulty Difficulty => scenario.State.Difficulty;

    // The races that own a player on the host map (Neutral included when the map has neutrals).
    public IReadOnlyList<WorldRace> Races => [.. scenario.State.Players.Values.Select(player => player.Race).Distinct().Order()];

    // The races that have a capital on the host map.
    public IReadOnlyList<WorldRace> CapitalRaces => [.. scenario.State.Fortifications.Values.OfType<WorldCapital>()
        .Select(capital => scenario.State.Players[capital.OwnerId].Race).Distinct().Order()];

    public IReadOnlyList<UnitModifierDefinitionId> LeaderUpgradeIds => [.. leaderUpgrades.Keys.Order()];

    // Every army that starts on the host map, as presets (name = map ID + army ID + leader type), with its
    // leader's items and upgrades.
    public IReadOnlyList<BattleArmy> ExportArmies(string mapId)
    {
        List<BattleArmy> result = [];

        foreach (WorldStack stack in scenario.State.Stacks.Values)
        {
            ImmutableArray<BattleArmyUnit> units = [.. stack.Units.GetUnits().Select(pair => new BattleArmyUnit(pair.Item1.DefinitionId, pair.Item1.Level, pair.Item2))];
            WorldUnit? leader = stack.Units.GetUnits().Select(pair => pair.Item1).FirstOrDefault(unit => definitions.GetUnit(unit.DefinitionId).Leadership is > 0);

            if (units.IsEmpty)
            {
                continue;
            }

            result.Add(new BattleArmy($"{mapId}-{stack.Id.Value}-{leader?.DefinitionId.Value ?? "no-leader"}", units)
            {
                Equipment = [.. stack.Equipment.Items.Select(pair => new BattleArmyItem(pair.Key, pair.Value.DefinitionId))],
                LeaderUpgrades = leader is null
                    ? []
                    : [.. leader.Modifiers.Where(modifier => modifier.Source is UnitModifierSource.Unit && leaderUpgrades.ContainsKey(modifier.DefinitionId)).Select(modifier => modifier.DefinitionId)]
            });
        }

        return result;
    }

    // Null when the army is fine, else what is wrong with it (unknown unit or item, bad slot, game rule broken).
    // asGarrison: the army defends as a city's garrison (no leader, no items, guardians allowed).
    public string? Validate(BattleArmy army, bool asGarrison = false)
    {
        return TryBuildArmy(army, "V", asGarrison, out _, out string? error) ? null : error;
    }

    // Null when both armies can fight this way on the host map (every way round the runs use), else why not.
    public string? Validate(BattleBenchSide armyA, BattleBenchSide armyB, BattleBenchSetup setup, bool swapSides)
    {
        string? problem = ValidateBattle(armyA, armyB, setup);

        if (problem is null && swapSides)
        {
            problem = ValidateBattle(armyB, armyA, setup);
        }

        return problem;
    }

    private string? ValidateBattle(BattleBenchSide attacker, BattleBenchSide defender, BattleBenchSetup setup)
    {
        bool asGarrison = setup.DefenderIsGarrison && setup.Place is not BattleBenchPlace.Open;

        if ((Validate(attacker.Army) ?? ValidateOwner(attacker, BattleBenchSetup.Open)) is { } attackerProblem)
        {
            return $"army \"{attacker.Army.Name}\" (attacking): {attackerProblem}";
        }

        if ((Validate(defender.Army, asGarrison) ?? ValidateOwner(defender, setup)) is { } defenderProblem)
        {
            return $"army \"{defender.Army.Name}\" (defending): {defenderProblem}";
        }

        return null;
    }

    public BattleBenchResult Run(BattleBenchSide armyA, BattleBenchSide armyB, int runs, int firstSeed, bool swapSides, int parallel = 1, CancellationToken cancellationToken = default)
    {
        return Run(armyA, armyB, BattleBenchSetup.Open, runs, firstSeed, swapSides, parallel, cancellationToken);
    }

    public BattleBenchResult Run(BattleBenchSide armyA, BattleBenchSide armyB, BattleBenchSetup setup, int runs, int firstSeed, bool swapSides, int parallel = 1, CancellationToken cancellationToken = default)
    {
        // The battle doesn't depend on the seed: build it once per way round.
        BattleState aAttacks = CreateBattle(armyA, armyB, setup);
        BattleState? bAttacks = swapSides ? CreateBattle(armyB, armyA, setup) : null;
        BattleBenchRun[] results = new BattleBenchRun[runs];

        Parallel.For(0, runs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel), CancellationToken = cancellationToken }, index =>
        {
            bool armyAAttacks = !swapSides || index % 2 == 0;
            results[index] = Fight(armyA, armyB, armyAAttacks ? aAttacks : bAttacks!, firstSeed + index, armyAAttacks);
        });

        return new BattleBenchResult(armyA, armyB, [.. results]) { Setup = setup };
    }

    // One battle; also used by the tests to compare a run with a hand-played one.
    public BattleBenchRun RunOne(BattleBenchSide armyA, BattleBenchSide armyB, int seed, bool aAttacks, BattleBenchSetup? setup = null)
    {
        BattleBenchSetup actual = setup ?? BattleBenchSetup.Open;
        BattleState initial = aAttacks ? CreateBattle(armyA, armyB, actual) : CreateBattle(armyB, armyA, actual);

        return Fight(armyA, armyB, initial, seed, aAttacks);
    }

    // The battle as the game would start it (before BattleStartCommand), for tests and for the sandbox.
    internal BattleState CreateBattle(BattleArmy attacker, BattleArmy defender, BattleBenchSetup? setup = null)
    {
        return CreateBattle(new BattleBenchSide(attacker, AiBrainRegistry.ClassicId), new BattleBenchSide(defender, AiBrainRegistry.ClassicId), setup ?? BattleBenchSetup.Open);
    }

    internal BattleState CreateBattle(BattleBenchSide attacker, BattleBenchSide defender, BattleBenchSetup setup)
    {
        if ((ValidateOwner(attacker, BattleBenchSetup.Open) ?? ValidateOwner(defender, setup)) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        bool asGarrison = setup.DefenderIsGarrison && setup.Place is not BattleBenchPlace.Open;
        BuiltArmy attackerArmy = BuildArmy(attacker.Army, "A", asGarrison: false);
        BuiltArmy defenderArmy = BuildArmy(defender.Army, "D", asGarrison);
        WorldPlayerId attackerOwner = GetOwner(attacker, firstOwner);
        WorldPlayerId defenderOwner = GetOwner(defender, secondOwner);
        IEngineContext context = CreateContext(0);
        WorldStack attackerStack = CreateStack("BenchAttacker", attackerOwner, attackerArmy, openTile);
        WorldState state = scenario.State;

        if (setup.Place is BattleBenchPlace.Open)
        {
            return WorldCommandHelpers.CreateStackBattleState(state, attackerStack, CreateStack("BenchDefender", defenderOwner, defenderArmy, openTile), context);
        }

        WorldFortification place = CreatePlace(setup, defenderOwner, asGarrison ? defenderArmy.Units : UnitGrid<WorldUnit>.Empty);
        state = state with { Fortifications = state.Fortifications.SetItem(place.Id, place) };

        // The place's protection reaches its army or garrison the way it does in the game (unit modifiers).
        if (asGarrison)
        {
            state = FortificationCalculator.UpdateUnitModifiers(state, definitions);

            return WorldCommandHelpers.CreateGarrisonBattleState(state, attackerStack, state.Fortifications[place.Id], context);
        }

        WorldStack defenderStack = CreateStack("BenchDefender", defenderOwner, defenderArmy, FortificationCalculator.GetEntrance(place.Footprint));
        state = state with { Stacks = state.Stacks.SetItem(defenderStack.Id, defenderStack) };
        state = FortificationCalculator.UpdateUnitModifiers(state, definitions);

        return WorldCommandHelpers.CreateStackBattleState(state, attackerStack, state.Stacks[defenderStack.Id], context);
    }

    internal EngineContext CreateContext(int seed)
    {
        return new EngineContext(new RandomProvider(seed), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
    }

    private BattleBenchRun Fight(BattleBenchSide armyA, BattleBenchSide armyB, BattleState initial, int seed, bool aAttacks)
    {
        EngineContext context = CreateContext(seed);
        BattleEngine engine = new(context, initial, NullBattleUnitUpgradeResolver.Instance);

        engine.DispatchCommand(new BattleStartCommand());
        engine.DispatchCommand(new BattleAutoResolveCommand
        {
            AttackerAiId = aAttacks ? armyA.AiId : armyB.AiId,
            DefenderAiId = aAttacks ? armyB.AiId : armyA.AiId
        });

        BattleState final = engine.State;
        BattleSide sideA = aAttacks ? BattleSide.Attacker : BattleSide.Defender;
        BattleSide sideB = aAttacks ? BattleSide.Defender : BattleSide.Attacker;
        BattleBenchWinner winner = final.Outcome is null
            ? BattleBenchWinner.None
            : final.Outcome.WinningSide == sideA ? BattleBenchWinner.A : BattleBenchWinner.B;

        return new BattleBenchRun(
            seed,
            aAttacks,
            winner,
            final.RoundNumber,
            GetSideResult(initial, final, sideA, context),
            GetSideResult(initial, final, sideB, context));
    }

    private static BattleBenchSideResult GetSideResult(BattleState initial, BattleState final, BattleSide side, IEngineContext context)
    {
        UnitGrid<BattleUnit> start = side == BattleSide.Attacker ? initial.AttackerGrid : initial.DefenderGrid;
        UnitGrid<BattleUnit> end = side == BattleSide.Attacker ? final.AttackerGrid : final.DefenderGrid;
        ImmutableArray<BattleBenchUnitResult>.Builder units = ImmutableArray.CreateBuilder<BattleBenchUnitResult>();

        // Only the army's own units (summoned ones have no world unit); a transformed unit keeps its battle ID.
        foreach ((BattleUnit unit, GridPosition gridPosition) in start.GetUnits())
        {
            if (unit.WorldUnitId is null)
            {
                continue;
            }

            BattleUnit? after = end.GetUnits().Select(pair => pair.Item1).FirstOrDefault(other => other.Id == unit.Id);
            int maxHitpoints = context.UnitStats.GetBattleUnitStats(initial, unit.Id).MaxHitpoints;
            bool retreated = after is not null && after.IsAlive && after.StatusEffects.Any(effect => effect is BattleRetreatedStatusEffect or BattleRetreatStatusEffect);

            units.Add(new BattleBenchUnitResult(
                unit.DefinitionId,
                gridPosition,
                after is { IsAlive: true },
                retreated,
                after is { IsAlive: true } ? after.Hitpoints : 0,
                maxHitpoints));
        }

        return new BattleBenchSideResult(units.ToImmutable());
    }

    private WorldPlayerId GetOwner(BattleBenchSide side, WorldPlayerId fallback)
    {
        if (side.Race is null)
        {
            return fallback;
        }

        return scenario.State.Players.Values.FirstOrDefault(player => player.Race == side.Race)?.Id
            ?? throw new InvalidOperationException($"The host map has no {side.Race} player.");
    }

    private string? ValidateOwner(BattleBenchSide side, BattleBenchSetup setup)
    {
        if (side.Race is { } race && !scenario.State.Players.Values.Any(player => player.Race == race))
        {
            return $"the host map has no {race} player (races: {string.Join(", ", Races)})";
        }

        if (setup.Place is BattleBenchPlace.Capital)
        {
            WorldRace ownerRace = scenario.State.Players[GetOwner(side, secondOwner)].Race;

            if (!CapitalRaces.Contains(ownerRace))
            {
                return $"a {ownerRace} army has no capital on the host map (capitals: {string.Join(", ", CapitalRaces)})";
            }
        }

        if (setup.Place is BattleBenchPlace.City)
        {
            if (cityTemplate is null)
            {
                return "the host map has no city to copy";
            }

            if (setup.CityLevel is < 1 or > 5)
            {
                return $"city level {setup.CityLevel} (1-5)";
            }
        }

        return null;
    }

    // A city of the chosen level owned by the defender (a copy of the host map's first city, which keeps its
    // place and look), or the capital of the defender's race.
    private WorldFortification CreatePlace(BattleBenchSetup setup, WorldPlayerId owner, UnitGrid<WorldUnit> garrison)
    {
        if (setup.Place is BattleBenchPlace.Capital)
        {
            WorldRace race = scenario.State.Players[owner].Race;
            WorldCapital capital = scenario.State.Fortifications.Values.OfType<WorldCapital>().First(item => scenario.State.Players[item.OwnerId].Race == race);

            return capital with { OwnerId = owner, Garrison = garrison, CapturedById = null };
        }

        return cityTemplate! with
        {
            DefinitionId = new WorldCityDefinitionId(cityTemplate.DefinitionId.Race, setup.CityLevel),
            OwnerId = owner,
            Garrison = garrison,
            RiotTurns = 0
        };
    }

    private WorldStack CreateStack(string id, WorldPlayerId owner, BuiltArmy army, WorldPosition position)
    {
        return template with
        {
            Id = new WorldStackId(id),
            OwnerId = owner,
            Position = position,
            Units = army.Units,
            Inventory = WorldInventory.Empty,
            Equipment = army.Equipment,
            ExpirationTurn = null,
            SourceTemplateId = null
        };
    }

    private BuiltArmy BuildArmy(BattleArmy army, string prefix, bool asGarrison)
    {
        if (!TryBuildArmy(army, prefix, asGarrison, out BuiltArmy? built, out string? error))
        {
            throw new InvalidOperationException($"Army \"{army.Name}\": {error}");
        }

        return built;
    }

    private bool TryBuildArmy(BattleArmy army, string prefix, bool asGarrison, [NotNullWhen(true)] out BuiltArmy? built, out string? error)
    {
        built = null;

        if (army.Units.IsEmpty)
        {
            error = "the army has no units";
            return false;
        }

        WorldStackBuilder builder = new(definitions);
        UnitGrid<WorldUnit>.Builder raw = new();
        WorldUnit? leader = null;
        int index = 0;

        foreach (BattleArmyUnit unit in army.Units)
        {
            UnitDefinition? definition = definitions.TryGetUnit(unit.TypeId);

            if (definition is null)
            {
                error = $"unknown unit {unit.TypeId.Value}";
                return false;
            }

            if (unit.Position.Line is < 0 or >= GridPosition.LineCount || unit.Position.Flank is < 0 or >= GridPosition.FlankCount)
            {
                error = $"{unit.TypeId.Value}: no slot at line {unit.Position.Line}, flank {unit.Position.Flank}";
                return false;
            }

            if (unit.Level < definition.Level)
            {
                error = $"{unit.TypeId.Value}: level {unit.Level} is below the unit's own level {definition.Level}";
                return false;
            }

            WorldUnit worldUnit = new(new WorldUnitId($"{prefix}{index}"), unit.TypeId, 1, 0, unit.Level, [], null, IsTypeLocked: false);

            // Capital guardians have leadership too, but they are no leaders (they stand in garrisons).
            if (definition.Leadership is > 0 && definition.Category != UnitCategory.Guardian && leader is null)
            {
                // Leader upgrades are unit modifiers on the leader, like the ones the game grants on level-up.
                worldUnit = worldUnit with { Modifiers = [.. army.LeaderUpgrades.Select(id => new UnitModifier(id, null))] };
                leader = worldUnit;
            }

            if (!builder.TryPlaceUnit(worldUnit, unit.Position) || !raw.TryPlace(worldUnit, unit.Position, definition.IsLarge))
            {
                error = $"{unit.TypeId.Value}: the slot at line {unit.Position.Line}, flank {unit.Position.Flank} is taken";
                return false;
            }

            index++;
        }

        string? itemProblem = CheckItemsAndUpgrades(army, leader, asGarrison, out LeaderEquipment equipment);

        if (itemProblem is not null && (!army.AllowAnyArmy || itemProblem.StartsWith("unknown", StringComparison.Ordinal)))
        {
            error = itemProblem;
            return false;
        }

        UnitGrid<WorldUnit> grid;

        if (army.AllowAnyArmy)
        {
            grid = raw.Build();
        }
        else if (asGarrison)
        {
            // A garrison has no leader (and so no leadership limit); guardians may stand in it.
            if (leader is not null)
            {
                error = "a garrison has no leader";
                return false;
            }

            grid = raw.Build();
        }
        else
        {
            foreach ((WorldUnit unit, _) in raw.Build().GetUnits())
            {
                if (definitions.GetUnit(unit.DefinitionId).Category == UnitCategory.Guardian)
                {
                    error = "guardians can only be in a garrison";
                    return false;
                }
            }

            if (!builder.TryBuild(out UnitGrid<WorldUnit>? checkedGrid, out WorldCommandError? ruleError))
            {
                error = ruleError switch
                {
                    WorldCommandError.MultipleLeadersForbidden => "more than one leader",
                    WorldCommandError.LeadershipExceeded => "the units need more leadership than the leader has",
                    _ => ruleError.ToString()
                };
                return false;
            }

            grid = checkedGrid;
        }

        // Items (stat modifiers), then full hit points with every modifier, like a fresh army in the game.
        grid = LeaderEquipmentCalculator.UpdateUnitModifiers(grid, equipment, definitions);

        foreach ((WorldUnit unit, _) in grid.GetUnits())
        {
            UnitDefinition definition = definitions.GetUnit(unit.DefinitionId);
            grid = grid.Replace(unit, unit with { Hitpoints = UnitStatsCalculator.GetStats(definition, unit.Level, unit.Modifiers, definitions, globals).MaxHitpoints });
        }

        built = new BuiltArmy(grid, equipment);
        error = null;
        return true;
    }

    // The game's equip rules (slot fits the item, the leader has the ability the item needs, one item per
    // slot) and level-up rules (an upgrade of some lord, its minimum level reached, no repeats, at most
    // min(level, max abilities) - 1 upgrades). Returns the equipment even when a rule is broken, so
    // "anything goes" armies still carry their items.
    private string? CheckItemsAndUpgrades(BattleArmy army, WorldUnit? leader, bool asGarrison, out LeaderEquipment equipment)
    {
        ImmutableSortedDictionary<LeaderEquipmentSlot, WorldItem>.Builder items = ImmutableSortedDictionary.CreateBuilder<LeaderEquipmentSlot, WorldItem>();
        string? problem = null;
        int index = 0;

        foreach (BattleArmyItem item in army.Equipment)
        {
            ItemDefinition? definition = TryGetItem(item.ItemId);

            if (definition is null)
            {
                equipment = LeaderEquipment.Empty;
                return $"unknown item {item.ItemId.Value}";
            }

            if (items.ContainsKey(item.Slot))
            {
                problem ??= $"two items in the {item.Slot} slot";
                continue;
            }

            if (!LeaderEquipmentCalculator.CanPlace(definition.Category, item.Slot))
            {
                problem ??= $"{item.ItemId.Value} ({definition.Category}) doesn't fit the {item.Slot} slot";
            }

            items[item.Slot] = WorldItemFactory.Create(new WorldItemId($"BenchItem{index}"), definition, globals);
            index++;
        }

        equipment = new LeaderEquipment(items.ToImmutable());

        foreach (UnitModifierDefinitionId upgrade in army.LeaderUpgrades)
        {
            if (!leaderUpgrades.ContainsKey(upgrade))
            {
                return $"unknown leader upgrade {upgrade.Value}";
            }
        }

        if ((!army.Equipment.IsEmpty || !army.LeaderUpgrades.IsEmpty) && (leader is null || asGarrison))
        {
            return problem ?? (asGarrison ? "a garrison has no leader for items or upgrades" : "items and upgrades need a leader");
        }

        if (leader is null)
        {
            return problem;
        }

        int maxUpgrades = Math.Max(0, Math.Min(leader.Level, globals.MaxLeaderAbilities) - 1);

        if (army.LeaderUpgrades.Length > maxUpgrades)
        {
            problem ??= $"a level {leader.Level} leader has at most {maxUpgrades} upgrades";
        }

        if (army.LeaderUpgrades.Distinct().Count() != army.LeaderUpgrades.Length)
        {
            problem ??= "an upgrade is taken twice";
        }

        foreach (UnitModifierDefinitionId upgrade in army.LeaderUpgrades)
        {
            if (leaderUpgrades[upgrade] > leader.Level)
            {
                problem ??= $"upgrade {upgrade.Value} needs leader level {leaderUpgrades[upgrade]}";
            }
        }

        LeaderAbility abilities = CreateContext(0).UnitStats.GetWorldUnitAbilities(leader);

        foreach ((LeaderEquipmentSlot slot, WorldItem item) in equipment.Items)
        {
            LeaderAbility? required = LeaderEquipmentCalculator.GetRequiredAbility(definitions.GetItem(item.DefinitionId).Category);

            if (required is not null && !abilities.HasFlag(required.Value))
            {
                problem ??= $"{item.DefinitionId.Value} in the {slot} slot needs the leader ability {required}";
            }
        }

        return problem;
    }

    private ItemDefinition? TryGetItem(ItemDefinitionId id)
    {
        try
        {
            return definitions.GetItem(id);
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    private static WorldPosition FindOpenTile(WorldState state)
    {
        WorldPlan plan = WorldPlan.For(state);

        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                WorldPosition candidate = new(x, y);

                if (plan.GetFortificationAt(candidate) is null)
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException("The host map has no tile outside a city.");
    }

    private sealed record BuiltArmy(UnitGrid<WorldUnit> Units, LeaderEquipment Equipment);
}

// Race: which player of the host map owns the army (null = a default playable race; Neutral = the neutral
// player, whose leaders never flee).
public sealed record BattleBenchSide(BattleArmy Army, string AiId)
{
    public WorldRace? Race { get; init; }
}

public enum BattleBenchPlace
{
    Open,
    City,
    Capital
}

// Where the defending army stands. City/Capital: the defender gets the place's protection (armor) and can't
// flee, like in the game; DefenderIsGarrison = it fights as the place's garrison instead of an army in it.
public sealed record BattleBenchSetup(BattleBenchPlace Place = BattleBenchPlace.Open, int CityLevel = 1, bool DefenderIsGarrison = false)
{
    public static BattleBenchSetup Open { get; } = new();
}

public enum BattleBenchWinner
{
    None,
    A,
    B
}

public sealed record BattleBenchUnitResult(UnitDefinitionId TypeId, GridPosition Position, bool Survived, bool Retreated, int Hitpoints, int MaxHitpoints);

public sealed record BattleBenchSideResult(ImmutableArray<BattleBenchUnitResult> Units)
{
    public int Survivors => Units.Count(unit => unit.Survived);
    public bool Fled => Units.Any(unit => unit.Retreated);
    public int Hitpoints => Units.Sum(unit => unit.Hitpoints);
    public int MaxHitpoints => Units.Sum(unit => unit.MaxHitpoints);
}

public sealed record BattleBenchRun(int Seed, bool ArmyAAttacks, BattleBenchWinner Winner, int Rounds, BattleBenchSideResult A, BattleBenchSideResult B);

public sealed record BattleBenchResult(BattleBenchSide ArmyA, BattleBenchSide ArmyB, ImmutableArray<BattleBenchRun> Runs)
{
    public BattleBenchSetup Setup { get; init; } = BattleBenchSetup.Open;

    public int Wins(BattleBenchWinner side) => Runs.Count(run => run.Winner == side);

    // The side's share of wins with its margin of error (95 %), e.g. 0.62 ± 0.06.
    public BattleBenchShare WinShare(BattleBenchWinner side) => BattleBenchShare.From(Wins(side), Runs.Length);
}

// A share out of n runs with its 95 % margin of error. The margin is the half-width of the Wilson score interval,
// which stays honest near 0 % and 100 % (unlike p ± 1.96·sqrt(p(1-p)/n), which says ± 0 at 0 of 1000).
public readonly record struct BattleBenchShare(int Count, int Total, double Share, double Margin)
{
    private const double Z = 1.959963984540054;

    public static BattleBenchShare From(int count, int total)
    {
        if (total <= 0)
        {
            return new BattleBenchShare(count, total, 0, 0);
        }

        double p = (double)count / total;
        double z2 = Z * Z;
        double margin = Z * Math.Sqrt((p * (1 - p) / total) + (z2 / (4.0 * total * total))) / (1 + (z2 / total));

        return new BattleBenchShare(count, total, p, margin);
    }

    // "62 % ± 6" (whole percents; the margin rounded up so it is never shown as 0 when it isn't).
    public override string ToString()
    {
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"{Share * 100:F0} % ± {Math.Ceiling(Margin * 100):F0}");
    }
}
