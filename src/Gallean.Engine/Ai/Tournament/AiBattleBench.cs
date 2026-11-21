using System.Collections.Immutable;
using Gallean.Common;
using Gallean.Entities;

namespace Gallean.Engine;

// Gallean Upgrade (docs/battle-ai-designer.md phase 1): fights the same battle many times, headless.
// Army A (with its battle AI) against army B (with its own). Each run builds the battle exactly like the
// game does when one army attacks another in the open (WorldCommandHelpers.CreateStackBattleState, on a
// tile without a city, owners = two players of the host map, both computer players so world battle modifiers
// such as the difficulty's accuracy bonus apply to both alike), then
// resolves it like an AI-vs-AI battle in the game (BattleAutoResolveCommand with both AI IDs: the attacker
// retreats after round 15, hard stop at round 45). Every run has its own seed: same seed = same battle.
// With swapSides, army A attacks in even runs and defends in odd runs.
public sealed class AiBattleBench
{
    private readonly IGameDefinitionProvider definitions;
    private readonly GlobalVariables globals;
    private readonly Scenario scenario;
    private readonly WorldStack template;
    private readonly WorldPlayerId firstOwner;
    private readonly WorldPlayerId secondOwner;
    private readonly WorldPosition position;

    public AiBattleBench(GlobalDatabase globalDb, CampaignDatabase campaignDb, ScenarioDatabase scenarioDb, string hostMapId)
    {
        definitions = new GameDefinitionProvider(globalDb);
        globals = new GlobalVariables();
        scenario = ScenarioLoader.Load(globalDb, scenarioDb, campaignDb, new ScenarioId(hostMapId), definitions, globals);

        // Both sides are computer players, so the difficulty bonuses (accuracy, experience) are the same for both.
        scenario = scenario with
        {
            State = scenario.State with
            {
                Players = scenario.State.Players.SetItems(scenario.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
            }
        };

        WorldState state = scenario.State;
        template = state.Stacks.Values.FirstOrDefault() ?? throw new InvalidOperationException($"{hostMapId} has no army to copy.");

        List<WorldPlayerId> players = [.. state.Players.Values.Where(player => player.Race is not WorldRace.Neutral).Select(player => player.Id)];
        if (players.Count < 2)
        {
            players = [.. state.Players.Keys];
        }

        firstOwner = players[0];
        secondOwner = players.Count > 1 ? players[1] : players[0];
        position = FindOpenTile(state);
    }

    public IGameDefinitionProvider Definitions => definitions;

    // Every army that starts on the host map, as presets (name = map ID + army ID + leader type).
    public IReadOnlyList<BattleArmy> ExportArmies(string mapId)
    {
        List<BattleArmy> result = [];

        foreach (WorldStack stack in scenario.State.Stacks.Values)
        {
            ImmutableArray<BattleArmyUnit> units = [.. stack.Units.GetUnits().Select(pair => new BattleArmyUnit(pair.Item1.DefinitionId, pair.Item1.Level, pair.Item2))];
            string leader = stack.Units.GetUnits().Select(pair => definitions.GetUnit(pair.Item1.DefinitionId)).FirstOrDefault(definition => definition.Leadership is > 0)?.Id.Value ?? "no-leader";

            if (!units.IsEmpty)
            {
                result.Add(new BattleArmy($"{mapId}-{stack.Id.Value}-{leader}", units));
            }
        }

        return result;
    }

    // Null when the army is fine, else what is wrong with it (unknown unit, bad slot, game rule broken).
    public string? Validate(BattleArmy army)
    {
        return TryBuildUnits(army, "V", out _, out string? error) ? null : error;
    }

    public BattleBenchResult Run(BattleBenchSide armyA, BattleBenchSide armyB, int runs, int firstSeed, bool swapSides, int parallel = 1, CancellationToken cancellationToken = default)
    {
        UnitGrid<WorldUnit> unitsA = BuildUnits(armyA.Army, "A");
        UnitGrid<WorldUnit> unitsB = BuildUnits(armyB.Army, "B");
        BattleBenchRun[] results = new BattleBenchRun[runs];

        Parallel.For(0, runs, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallel), CancellationToken = cancellationToken }, index =>
        {
            bool aAttacks = !swapSides || index % 2 == 0;
            results[index] = Fight(armyA, unitsA, armyB, unitsB, firstSeed + index, aAttacks);
        });

        return new BattleBenchResult(armyA, armyB, [.. results]);
    }

    // One battle; also used by the tests to compare a run with a hand-played one.
    public BattleBenchRun RunOne(BattleBenchSide armyA, BattleBenchSide armyB, int seed, bool aAttacks)
    {
        return Fight(armyA, BuildUnits(armyA.Army, "A"), armyB, BuildUnits(armyB.Army, "B"), seed, aAttacks);
    }

    // The battle as the game would start it (before BattleStartCommand), for tests and for the sandbox.
    internal BattleState CreateBattle(BattleArmy attacker, BattleArmy defender)
    {
        return CreateBattleState(BuildUnits(attacker, "A"), BuildUnits(defender, "B"), CreateContext(0));
    }

    internal EngineContext CreateContext(int seed)
    {
        return new EngineContext(new RandomProvider(seed), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
    }

    private BattleBenchRun Fight(BattleBenchSide armyA, UnitGrid<WorldUnit> unitsA, BattleBenchSide armyB, UnitGrid<WorldUnit> unitsB, int seed, bool aAttacks)
    {
        EngineContext context = CreateContext(seed);
        BattleState initial = aAttacks ? CreateBattleState(unitsA, unitsB, context) : CreateBattleState(unitsB, unitsA, context);
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

    private BattleBenchSideResult GetSideResult(BattleState initial, BattleState final, BattleSide side, IEngineContext context)
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

    private BattleState CreateBattleState(UnitGrid<WorldUnit> attackerUnits, UnitGrid<WorldUnit> defenderUnits, IEngineContext context)
    {
        WorldStack attacker = CreateStack("BenchAttacker", firstOwner, attackerUnits);
        WorldStack defender = CreateStack("BenchDefender", secondOwner, defenderUnits);

        return WorldCommandHelpers.CreateStackBattleState(scenario.State, attacker, defender, context);
    }

    private WorldStack CreateStack(string id, WorldPlayerId owner, UnitGrid<WorldUnit> units)
    {
        return template with
        {
            Id = new WorldStackId(id),
            OwnerId = owner,
            Position = position,
            Units = units,
            Inventory = WorldInventory.Empty,
            Equipment = LeaderEquipment.Empty,
            ExpirationTurn = null,
            SourceTemplateId = null
        };
    }

    private UnitGrid<WorldUnit> BuildUnits(BattleArmy army, string prefix)
    {
        if (!TryBuildUnits(army, prefix, out UnitGrid<WorldUnit>? units, out string? error))
        {
            throw new InvalidOperationException($"Army \"{army.Name}\": {error}");
        }

        return units!;
    }

    private bool TryBuildUnits(BattleArmy army, string prefix, out UnitGrid<WorldUnit>? units, out string? error)
    {
        units = null;

        if (army.Units.IsEmpty)
        {
            error = "the army has no units";
            return false;
        }

        WorldStackBuilder builder = new(definitions);
        UnitGrid<WorldUnit>.Builder raw = new();
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

            int hitpoints = UnitStatsCalculator.GetStats(definition, unit.Level, [], definitions, globals).MaxHitpoints;
            WorldUnit worldUnit = new(new WorldUnitId($"{prefix}{index}"), unit.TypeId, hitpoints, 0, unit.Level, [], null, IsTypeLocked: false);

            if (!builder.TryPlaceUnit(worldUnit, unit.Position) || !raw.TryPlace(worldUnit, unit.Position, definition.IsLarge))
            {
                error = $"{unit.TypeId.Value}: the slot at line {unit.Position.Line}, flank {unit.Position.Flank} is taken";
                return false;
            }

            index++;
        }

        if (army.AllowAnyArmy)
        {
            units = raw.Build();
            error = null;
            return true;
        }

        if (!builder.TryBuild(out units, out WorldCommandError? ruleError))
        {
            error = ruleError switch
            {
                WorldCommandError.MultipleLeadersForbidden => "more than one leader",
                WorldCommandError.LeadershipExceeded => "the units need more leadership than the leader has",
                WorldCommandError.GuardianTransferForbidden => "guardians can't be in an army",
                _ => ruleError.ToString()
            };
            return false;
        }

        error = null;
        return true;
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
}

public sealed record BattleBenchSide(BattleArmy Army, string AiId);

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

public sealed record BattleBenchResult(BattleBenchSide ArmyA, BattleBenchSide ArmyB, ImmutableArray<BattleBenchRun> Runs);
