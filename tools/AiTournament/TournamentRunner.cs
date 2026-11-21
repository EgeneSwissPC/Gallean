using System.Diagnostics;
using Gallean.Entities;

namespace AiTournament;

// Plays one AI-only game per call: every playable race gets the contender of its seat as its own AI
// (WorldAiOverride, like the AI players panel); neutrals keep Classic. The game ends when the
// scenario ends, one playable race is left, or the turn limit is reached (a draw).
internal sealed class TournamentRunner(GlobalDatabase globalDb, CampaignDatabase campaignDb, ScenarioDatabase scenarioDb, string mapId, int maxTurns, bool verbose = false)
{
    private const int MaxActionsPerGame = 500_000;

    private readonly IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDb);

    public int CountPlayableSeats()
    {
        return GetSeats(LoadScenario().State).Count;
    }

    public GameResult Play(IReadOnlyList<Contender> seating, int seed)
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
        Dictionary<WorldPlayerId, Contender> contenders = [];

        for (int i = 0; i < seats.Count; i++)
        {
            contenders[seats[i]] = seating[i];
            Check(session.World.DispatchCommand(new WorldSetAiOverrideCommand(seats[i], new WorldAiOverride(seating[i].WorldAi, seating[i].BattleAi))));
        }

        Check(session.World.DispatchCommand(new WorldStartGameCommand()));

        Dictionary<WorldPlayerId, int> citiesAtStart = seats.ToDictionary(id => id, id => CountCities(session.World.State, id));
        Dictionary<WorldPlayerId, int?> firstCapture = seats.ToDictionary(id => id, _ => (int?)null);
        Dictionary<WorldPlayerId, List<double>> turnTimes = seats.ToDictionary(id => id, _ => new List<double>());
        Dictionary<WorldFortificationId, WorldPlayerId> owners = GetOwners(session.World.State);
        int startTurn = session.World.State.TurnInfo.TurnNumber;
        string outcome = "draw";
        WorldPlayerId? winner = null;

        WorldPlayerId turnPlayer = session.World.State.TurnInfo.CurrentPlayerId;
        int turnNumber = session.World.State.TurnInfo.TurnNumber;
        Stopwatch turnTime = Stopwatch.StartNew();

        for (int action = 0; action < MaxActionsPerGame; action++)
        {
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

            if (verbose && after.TurnInfo.TurnNumber != turnNumber)
            {
                Console.WriteLine($"  turn {after.TurnInfo.TurnNumber}: " + string.Join("; ", after.Players.Values.Select(player =>
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
                turnTime.Restart();
            }
        }

        WorldState end = session.World.State;
        WorldPlan plan = WorldPlan.For(end);
        List<PlayerResult> players = [];

        foreach (WorldPlayerId id in seats)
        {
            List<double> times = turnTimes[id];
            players.Add(new PlayerResult(
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
                times.Count == 0 ? 0 : times.Max()));
        }

        return new GameResult(seed, end.TurnInfo.TurnNumber - startTurn, outcome, players, gameTime.Elapsed.TotalSeconds);
    }

    private Scenario LoadScenario(GlobalVariables? globals = null)
    {
        return ScenarioLoader.Load(globalDb, scenarioDb, campaignDb, new ScenarioId(mapId), definitions, globals ?? new GlobalVariables());
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

    private static void Check(ICommandResult<WorldCommandError, WorldEvent, Empty> result)
    {
        if (result.Failed)
        {
            throw new InvalidOperationException($"Command failed: {result.Error}");
        }
    }
}
