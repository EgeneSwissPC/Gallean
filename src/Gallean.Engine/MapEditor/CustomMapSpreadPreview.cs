namespace Gallean.Engine;

// The map editor's land spread preview: the land after N turns in which nobody moves, fights, captures, plants or
// removes rods; only the game's turn-start spread happens (ExpansionCalculator.ExpandPlayerTerrain, the call in
// WorldEngine.Events.TurnStarting): every capital, village and rod grows its race's land, and mines change owner
// with it. One turn = every race once, in the game's order for computer players (Empire, Legions, Clans, Undead,
// Elves; in a game the human player moves first). Turns are computed on demand and kept, so going from turn 10 to
// 20 only computes 10 more.
public sealed class CustomMapSpreadPreview
{
    // Enough to see a map fill up; each turn costs one spread per race.
    public const int MaxTurns = 200;

    private readonly WorldState start;
    private readonly IEngineContext context;
    private readonly List<WorldMap> maps;
    private readonly List<WorldPlayerId> turnOrder;
    private readonly Lock gate = new();
    private TimeSpan computeTime;

    public CustomMapSpreadPreview(WorldState start, IEngineContext context)
    {
        this.start = start;
        this.context = context;
        maps = [start.Map];

        // The engine's turn order with every player a computer player: by race, as the game sorts them.
        WorldState allAi = start with
        {
            Players = start.Players.SetItems(start.Players.Select(pair => KeyValuePair.Create(pair.Key, pair.Value with { IsAi = true })))
        };
        turnOrder = [];
        WorldPlayerId first = allAi.GetFirstPlayerId();
        WorldPlayerId current = first;

        do
        {
            if (WorldHelpers.IsRacePlayable(allAi.Players[current].Race))
            {
                turnOrder.Add(current);
            }

            current = allAi.GetNextPlayerId(current);
        }
        while (current != first);
    }

    public IReadOnlyList<WorldRace> TurnOrder => [.. turnOrder.Select(id => start.Players[id].Race)];

    public WorldState Start => start;

    // Turns computed so far (the editor computes far steps on a background thread; GetState is thread-safe).
    public int ComputedTurns
    {
        get
        {
            lock (gate)
            {
                return maps.Count - 1;
            }
        }
    }

    // How long one turn took on average so far (zero before the first).
    public TimeSpan AverageTurnTime
    {
        get
        {
            lock (gate)
            {
                return maps.Count > 1 ? computeTime / (maps.Count - 1) : TimeSpan.Zero;
            }
        }
    }

    // The state after turns turns of spread (0 = the map as it is).
    public WorldState GetState(int turns)
    {
        turns = Math.Clamp(turns, 0, MaxTurns);

        lock (gate)
        {
            long started = System.Diagnostics.Stopwatch.GetTimestamp();
            int computed = 0;

            while (maps.Count <= turns)
            {
                WorldState state = start with { Map = maps[^1] };

                foreach (WorldPlayerId player in turnOrder)
                {
                    state = state with { Map = ExpansionCalculator.ExpandPlayerTerrain(state, player, context) };
                }

                maps.Add(state.Map);
                computed++;
            }

            if (computed > 0)
            {
                computeTime += System.Diagnostics.Stopwatch.GetElapsedTime(started);
            }

            return turns == 0 ? start : start with { Map = maps[turns] };
        }
    }
}
