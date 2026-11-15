using System.Diagnostics;
using Gallean.Entities.GameData;

namespace Gallean.Engine.Tests;

// Measures how scenario size affects load time, memory and AI turn time. Explicit: run on demand, e.g. with a
// generated large map installed in the test output's RiseOfTheElves folder (GALLEAN_BENCH_SCENARIOS=S216SC0000,S900SC0000).
[Explicit]
[Category("Benchmark")]
public sealed class LargeMapBenchmarkTests
{
    private static IEnumerable<TestCaseData> GetScenarioCases()
    {
        string scenarios = Environment.GetEnvironmentVariable("GALLEAN_BENCH_SCENARIOS") ?? "S216SC0000";

        foreach (string scenarioId in scenarios.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            yield return new TestCaseData(scenarioId);
        }
    }

    [TestCaseSource(nameof(GetScenarioCases))]
    public void AiTurnTest(string scenarioId)
    {
        int turnCount = int.TryParse(Environment.GetEnvironmentVariable("GALLEAN_BENCH_TURNS"), out int turns) ? turns : 5;

        GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
        CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
        IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDb);
        GlobalVariables globals = new();

        long memoryBefore = GetManagedMemory();
        Stopwatch load = Stopwatch.StartNew();
        ScenarioDatabase scenarioDb = ScenarioDatabaseLoader.Load(RiseOfTheElves.ScenarioDatabasePath, new Db.ScenarioId(scenarioId));
        Scenario scenario = ScenarioLoader.Load(globalDb, scenarioDb, campaignDb, new ScenarioId(scenarioId), definitions, globals);
        load.Stop();

        EngineContext context = new(new FixedRandomProvider(), definitions, new UnitStatsProvider(definitions, globals, scenario.Provider.Definition), scenario.Provider, globals);
        GameSession session = new(context, scenario.State with { Difficulty = WorldDifficulty.Hard });
        Stopwatch start = Stopwatch.StartNew();
        WorldCommandResult startResult = session.World.DispatchCommand(new WorldStartGameCommand());
        start.Stop();
        Assert.That(startResult.Failed, Is.False, $"Start failed: {startResult.Error}");

        long memoryLoaded = GetManagedMemory();
        WorldMap map = session.World.State.Map;
        TestContext.Out.WriteLine($"{scenarioId}: map {map.Width}x{map.Height}, {session.World.State.Players.Count} players, " +
            $"{session.World.State.Stacks.Count} stacks, {session.World.State.ResourceNodes.Count} resource nodes, " +
            $"{session.World.State.Landmarks.Count} landmarks, {session.World.State.Mountains.Count} mountains");
        TestContext.Out.WriteLine($"  load {load.ElapsedMilliseconds} ms, start {start.ElapsedMilliseconds} ms, managed heap +{(memoryLoaded - memoryBefore) / 1024 / 1024} MB");

        TestWorldAiRunner runner = new(session, context);
        Dictionary<WorldPlayerId, List<long>> turnTimes = [];
        int startTurn = session.World.State.TurnInfo.TurnNumber;
        SortedDictionary<string, int> battles = [];

        while (session.World.State.TurnInfo.TurnNumber < startTurn + turnCount)
        {
            WorldPlayerId playerId = session.World.State.TurnInfo.CurrentPlayerId;
            Dictionary<WorldPosition, WorldPlayerId> ownersBefore = session.World.State.Stacks.Values
                .GroupBy(stack => stack.Position)
                .ToDictionary(group => group.Key, group => group.First().OwnerId);
            Stopwatch turn = Stopwatch.StartNew();
            IReadOnlyList<TestWorldAiRunner.Trace> traces = runner.PlayTurn();
            turn.Stop();

            foreach (TestWorldAiRunner.Trace trace in traces)
            {
                if (trace is { Battle: not null, Plan.Command: WorldAiMoveStackCommand move } && ownersBefore.TryGetValue(move.TargetPosition, out WorldPlayerId defenderId))
                {
                    string key = $"{FormatPlayer(session.World.State, playerId)} -> {FormatPlayer(session.World.State, defenderId)}";
                    battles[key] = battles.GetValueOrDefault(key) + 1;
                }
            }

            if (!turnTimes.TryGetValue(playerId, out List<long>? times))
            {
                turnTimes[playerId] = times = [];
            }

            times.Add(turn.ElapsedMilliseconds);
            TestContext.Out.WriteLine($"  turn {session.World.State.TurnInfo.TurnNumber,3} {FormatPlayer(session.World.State, playerId)}: {turn.ElapsedMilliseconds,6} ms, {traces.Count} actions");
        }

        foreach ((WorldPlayerId playerId, List<long> times) in turnTimes)
        {
            TestContext.Out.WriteLine($"  player {playerId.Value}: avg {times.Average():F0} ms, max {times.Max()} ms over {times.Count} turns");
        }

        foreach ((string pair, int count) in battles)
        {
            TestContext.Out.WriteLine($"  battles {pair}: {count}");
        }

        TestContext.Out.WriteLine($"  managed heap after turns +{(GetManagedMemory() - memoryBefore) / 1024 / 1024} MB, process RSS {Environment.WorkingSet / 1024 / 1024} MB");
    }

    private static string FormatPlayer(WorldState state, WorldPlayerId playerId)
    {
        return $"P{playerId.Value} {state.Players[playerId].Race}";
    }

    private static long GetManagedMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        return GC.GetTotalMemory(forceFullCollection: true);
    }
}
