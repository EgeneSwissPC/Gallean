using System.Collections.Immutable;
using System.Diagnostics;
using Gallean.Common;
using Gallean.Entities;

namespace Gallean.Engine;

// Gallean Upgrade (docs/ai-switching.md phase 4, docs/ai-designer.md phase 4): plays AI-only games on one
// map. Every playable race gets the contender of its seat as its own AI (WorldAiOverride, like the AI
// players panel); neutrals keep their usual AI. A game ends when the scenario ends, one playable race is
// left, or the turn limit is reached (a draw). Used by tools/AiTournament and the AI designer's Test panel.
public sealed record AiContender(string WorldAi, string BattleAi)
{
    public override string ToString() => $"{WorldAi}/{BattleAi}";
}

public sealed record AiPlayerResult(
    WorldPlayerId PlayerId,
    WorldRace Race,
    AiContender Contender,
    bool IsWinner,
    bool IsDefeated,
    int CitiesAtStart,
    int CitiesAtEnd,
    int? FirstCaptureTurn,
    int Turns,
    double AverageTurnMs,
    double MaxTurnMs,
    ImmutableDictionary<string, int> StageTurns);

public sealed record AiGameResult(int Seed, int Turns, string Outcome, IReadOnlyList<AiPlayerResult> Players, double Seconds);

public sealed class AiTournamentRunner(GlobalDatabase globalDb, CampaignDatabase campaignDb, ScenarioDatabase scenarioDb, string mapId, int maxTurns, Action<string>? log = null)
{
    private const int MaxActionsPerGame = 500_000;

    private readonly IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDb);

    public int CountPlayableSeats()
    {
        return GetSeats(LoadScenario(new GlobalVariables()).State).Count;
    }

    // Every rotation seats the contenders one place further, so each plays every seat (race, start).
    public IReadOnlyList<(AiContender[] Seating, int Seed)> Plan(IReadOnlyList<AiContender> contenders, int gamesPerRotation, int firstSeed)
    {
        int seats = CountPlayableSeats();
        int rotations = contenders.Count == 1 ? 1 : contenders.Count;
        List<(AiContender[], int)> plan = [];

        for (int rotation = 0; rotation < rotations; rotation++)
        {
            for (int game = 0; game < gamesPerRotation; game++)
            {
                plan.Add(([.. Enumerable.Range(0, seats).Select(seat => contenders[(seat + rotation) % contenders.Count])], firstSeed + (rotation * gamesPerRotation) + game));
            }
        }

        return plan;
    }

    public AiGameResult Play(IReadOnlyList<AiContender> seating, int seed, CancellationToken cancellationToken = default)
    {
        Stopwatch gameTime = Stopwatch.StartNew();
        GlobalVariables globals = new();
        Scenario scenario = LoadScenario(globals);
        WorldState initial = scenario.State with
        {
            Players = scenario.State.Players.SetItems(scenario.State.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
        };

        EngineContext context = new(new RandomProvider(seed), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, initial) { AllowAiOnly = true };
        IReadOnlyList<WorldPlayerId> seats = GetSeats(initial);
        Dictionary<WorldPlayerId, AiContender> contenders = [];

        for (int i = 0; i < seats.Count; i++)
        {
            contenders[seats[i]] = seating[i];
            Check(session.World.DispatchCommand(new WorldSetAiOverrideCommand(seats[i], new WorldAiOverride(seating[i].WorldAi, seating[i].BattleAi))));
        }

        Check(session.World.DispatchCommand(new WorldStartGameCommand()));

        Dictionary<WorldPlayerId, int> citiesAtStart = seats.ToDictionary(id => id, id => CountCities(session.World.State, id));
        Dictionary<WorldPlayerId, int?> firstCapture = seats.ToDictionary(id => id, _ => (int?)null);
        Dictionary<WorldPlayerId, List<double>> turnTimes = seats.ToDictionary(id => id, _ => new List<double>());
        Dictionary<WorldPlayerId, Dictionary<string, int>> stageTurns = seats.ToDictionary(id => id, _ => new Dictionary<string, int>());
        Dictionary<WorldFortificationId, WorldPlayerId> owners = GetOwners(session.World.State);
        int startTurn = session.World.State.TurnInfo.TurnNumber;
        string outcome = "draw";
        WorldPlayerId? winner = null;

        WorldPlayerId turnPlayer = session.World.State.TurnInfo.CurrentPlayerId;
        int turnNumber = session.World.State.TurnInfo.TurnNumber;
        bool turnCounted = false;
        Stopwatch turnTime = Stopwatch.StartNew();

        for (int action = 0; action < MaxActionsPerGame; action++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WorldState state = session.World.State;

            if (state.ScenarioState.IsEnded)
            {
                winner = state.ScenarioState.Winner;
                outcome = winner is null ? "scenario ended" : "win";
                break;
            }

            List<WorldPlayerId> alive = [.. seats.Where(id => !WorldPlan.For(state).IsPlayerDefeated(id))];

            if (alive.Count <= 1)
            {
                winner = alive.Count == 1 ? alive[0] : null;
                outcome = winner is null ? "all out" : "win";
                break;
            }

            if (state.TurnInfo.TurnNumber - startTurn >= maxTurns)
            {
                break;
            }

            // Which stage a designed AI plays this turn (counted once per own turn, at its first action).
            if (!turnCounted && stageTurns.TryGetValue(state.TurnInfo.CurrentPlayerId, out Dictionary<string, int>? stages) &&
                AiBrainRegistry.ResolveWorld(session.GetWorldAiId(state.TurnInfo.CurrentPlayerId)) is DesignedWorldAiBrain designed)
            {
                WorldAiMemory memory = state.AiMemories.GetValueOrDefault(state.TurnInfo.CurrentPlayerId, WorldAiMemory.Empty);
                string stageName = designed.FindStage(state, memory).Stage?.Name ?? "";
                stages[stageName] = stages.GetValueOrDefault(stageName) + 1;
            }

            turnCounted = true;
            session.ExecuteAiAction();

            WorldState after = session.World.State;

            foreach ((WorldFortificationId id, WorldPlayerId owner) in GetOwners(after))
            {
                if (owners.GetValueOrDefault(id) != owner && firstCapture.TryGetValue(owner, out int? captured) && captured is null)
                {
                    firstCapture[owner] = after.TurnInfo.TurnNumber;
                }
            }

            owners = GetOwners(after);

            if (log is not null && after.TurnInfo.TurnNumber != turnNumber)
            {
                log($"  turn {after.TurnInfo.TurnNumber}: " + string.Join("; ", after.Players.Values.Select(player =>
                    $"P{player.Id.Value} {player.Race}{(WorldPlan.For(after).IsPlayerDefeated(player.Id) ? " out" : "")} cities {CountCities(after, player.Id)} " +
                    $"armies {after.Stacks.Values.Count(stack => stack.OwnerId == player.Id)} gold {player.Resources.Gold}")));
            }

            if (after.TurnInfo.CurrentPlayerId != turnPlayer || after.TurnInfo.TurnNumber != turnNumber)
            {
                if (turnTimes.TryGetValue(turnPlayer, out List<double>? times))
                {
                    times.Add(turnTime.Elapsed.TotalMilliseconds);
                }

                turnPlayer = after.TurnInfo.CurrentPlayerId;
                turnNumber = after.TurnInfo.TurnNumber;
                turnCounted = false;
                turnTime.Restart();
            }
        }

        WorldState end = session.World.State;
        WorldPlan plan = WorldPlan.For(end);
        List<AiPlayerResult> players = [];

        foreach (WorldPlayerId id in seats)
        {
            List<double> times = turnTimes[id];
            players.Add(new AiPlayerResult(
                id,
                end.Players[id].Race,
                contenders[id],
                winner == id,
                plan.IsPlayerDefeated(id),
                citiesAtStart[id],
                CountCities(end, id),
                firstCapture[id],
                times.Count,
                times.Count == 0 ? 0 : times.Average(),
                times.Count == 0 ? 0 : times.Max(),
                stageTurns[id].ToImmutableDictionary()));
        }

        return new AiGameResult(seed, end.TurnInfo.TurnNumber - startTurn, outcome, players, gameTime.Elapsed.TotalSeconds);
    }

    private Scenario LoadScenario(GlobalVariables globals)
    {
        return ScenarioLoader.Load(globalDb, scenarioDb, campaignDb, new ScenarioId(mapId), definitions, globals);
    }

    private static List<WorldPlayerId> GetSeats(WorldState state)
    {
        return [.. state.Players.Values.Where(player => player.Race is not WorldRace.Neutral).Select(player => player.Id)];
    }

    private static Dictionary<WorldFortificationId, WorldPlayerId> GetOwners(WorldState state)
    {
        Dictionary<WorldFortificationId, WorldPlayerId> owners = [];

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            WorldPlayerId? owner = fortification switch
            {
                WorldCity city => city.OwnerId,
                WorldCapital capital => capital.OwnerId,
                _ => null
            };

            if (owner is not null)
            {
                owners[fortification.Id] = owner.Value;
            }
        }

        return owners;
    }

    private static int CountCities(WorldState state, WorldPlayerId playerId)
    {
        return GetOwners(state).Values.Count(owner => owner == playerId);
    }

    private static void Check(WorldCommandResult result)
    {
        if (result.Failed)
        {
            throw new InvalidOperationException($"Command failed: {result.Error}");
        }
    }
}

// Per-contender totals over a set of games.
public sealed record AiContenderSummary(
    AiContender Contender,
    int Seats,
    int Wins,
    int Draws,
    int Leads,
    int Defeats,
    double AverageWinTurn,
    double AverageFirstCapture,
    double AverageCitiesAtEnd,
    double AverageTurnMs,
    double MaxTurnMs,
    ImmutableDictionary<string, int> StageTurns)
{
    public static IReadOnlyList<AiContenderSummary> Summarize(IReadOnlyList<AiContender> contenders, IReadOnlyList<AiGameResult> results)
    {
        List<AiContenderSummary> summaries = [];

        foreach (AiContender contender in contenders.Distinct())
        {
            List<(AiGameResult Game, AiPlayerResult Player)> seats = [.. results.SelectMany(game => game.Players.Where(p => p.Contender == contender).Select(p => (game, p)))];
            Dictionary<string, int> stages = [];

            foreach ((string stage, int turns) in seats.SelectMany(seat => seat.Player.StageTurns))
            {
                stages[stage] = stages.GetValueOrDefault(stage) + turns;
            }

            summaries.Add(new AiContenderSummary(
                contender,
                seats.Count,
                seats.Count(seat => seat.Player.IsWinner),
                seats.Count(seat => !seat.Player.IsWinner && !seat.Player.IsDefeated && seat.Game.Outcome == "draw"),
                seats.Count(seat => seat.Game.Outcome == "draw" && seat.Game.Players.All(other => other == seat.Player || other.CitiesAtEnd < seat.Player.CitiesAtEnd)),
                seats.Count(seat => seat.Player.IsDefeated),
                seats.Where(seat => seat.Player.IsWinner).Select(seat => (double)seat.Game.Turns).DefaultIfEmpty(double.NaN).Average(),
                seats.Where(seat => seat.Player.FirstCaptureTurn is not null).Select(seat => (double)seat.Player.FirstCaptureTurn!.Value).DefaultIfEmpty(double.NaN).Average(),
                seats.Select(seat => (double)seat.Player.CitiesAtEnd).DefaultIfEmpty(double.NaN).Average(),
                seats.Select(seat => seat.Player.AverageTurnMs).DefaultIfEmpty(double.NaN).Average(),
                seats.Select(seat => seat.Player.MaxTurnMs).DefaultIfEmpty(double.NaN).Max(),
                stages.ToImmutableDictionary()));
        }

        return summaries;
    }
}
