using System.Text.Json.Nodes;
using Gallean.Engine;
using Gallean.Entities;
using Gallean.Entities.GameData;
using MapFromPlan;

// MapFromPlan <plan.json> <maps directory> [overview.png] [-id S6xxSC0000] [-zoom westE,northN,eastE,southN close-up.png]
// Builds the map described by the plan and saves it like the map editor does (CustomMapStore). With -id an existing
// custom map is replaced, otherwise the next free ID is used.
List<string> positional = [];
string? mapId = null;
List<(string Path, double[] Window)> closeUps = [];
bool checks = true;

for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "-nochecks")
    {
        // Skip the travel distances and gate checks (slow on big maps).
        checks = false;
    }
    else if (args[i] == "-id" && i + 1 < args.Length)
    {
        mapId = args[++i];
    }
    else if (args[i] == "-zoom" && i + 2 < args.Length)
    {
        // -zoom westE,northN,eastE,southN close-up.png: a close-up of a plan window (miles) at 12 pixels per unit.
        closeUps.Add((args[i + 2], [.. args[i + 1].Split(',').Select(double.Parse)]));
        i += 2;
    }
    else
    {
        positional.Add(args[i]);
    }
}

if (positional.Count < 2)
{
    Console.Error.WriteLine("Usage: MapFromPlan <plan.json> <maps directory> [overview.png] [-id S6xxSC0000]");
    return 2;
}

JsonObject plan = JsonNode.Parse(File.ReadAllText(positional[0]), documentOptions: new System.Text.Json.JsonDocumentOptions { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
IGameDefinitionProvider definitions = new GameDefinitionProvider(globalDb);
CustomMapStore store = new(positional[1], globalDb, campaignDb);
GameNames names = new(RiseOfTheElves.GlobalDatabasePath, store, definitions);
Generator generator = new(plan, definitions, names);
System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
CustomMap map = generator.Generate(mapId ?? store.NextId());
store.Save(map);

int roads = map.Cells.Count(cell => cell.Feature == WorldMapFeature.Road);
int forests = map.Cells.Count(cell => cell.Feature == WorldMapFeature.Forest);
int water = map.Cells.Count(cell => cell.Terrain == WorldMapTerrain.Water);
Console.WriteLine($"Saved {map.Id} '{map.Name}' ({map.Size}x{map.Size}) to {positional[1]} in {watch.Elapsed.TotalSeconds:F1} s");
Console.WriteLine($"  capitals {map.Players.Count(player => player.Capital is not null)}/{map.Players.Count}, villages {map.Villages.Count}, ruins {map.Ruins.Count}, sites {map.Sites.Count}, " +
                  $"landmarks {map.Landmarks.Count}, mountains {map.Mountains.Count}, resources {map.Resources.Count}, armies {map.Stacks.Count}, bags {map.Bags.Count}");
Console.WriteLine($"  tiles: water {water}, forest {forests}, road {roads}, mountain {map.Cells.Count(cell => cell.Feature == WorldMapFeature.Mountain)}");

foreach (string problem in map.Validate())
{
    Console.WriteLine($"  validate: {problem}");
}

foreach (string warning in generator.Warnings)
{
    Console.WriteLine($"  warning: {warning}");
}

// Travel between named places: movement points along the cheapest path (plain 2, forest 4, road 1; water and
// mountains block), like WorldMovementCalculator for an ordinary leader.
foreach (JsonNode pair in checks ? Plan.List(plan["distances"]) : [])
{
    string from = Plan.Text(pair[0]);
    string to = Plan.Text(pair[1]);

    if (!generator.Approaches.TryGetValue(from, out WorldPosition start) || !generator.Approaches.TryGetValue(to, out WorldPosition goal))
    {
        Console.WriteLine($"  distance {from} -> {to}: unknown place");
        continue;
    }

    int? cost = Travel.Cost(map, start, goal);
    int movement = Plan.Integer(plan["leaderMovement"], 20);
    Console.WriteLine(cost is { } points
        ? $"  distance {from} -> {to}: {points} movement points, ~{Math.Ceiling(points / (double)movement)} turns at {movement}/turn"
        : $"  distance {from} -> {to}: no path");
}

// Gate checks: with every gate closed there must be no way from one place to the other, and each gate alone must
// let armies through (e.g. Mordor's three ways in).
foreach (JsonNode check in checks ? Plan.List(plan["gateChecks"]) : [])
{
    string name = Plan.Text(check["name"]);

    if (!generator.Approaches.TryGetValue(Plan.Text(check["from"]), out WorldPosition start) || !generator.Approaches.TryGetValue(Plan.Text(check["to"]), out WorldPosition goal))
    {
        Console.WriteLine($"  gate check '{name}': unknown place");
        continue;
    }

    double radius = generator.Geo.ToGridUnits(Plan.Number(check["radiusMiles"], 25));
    (string Name, double X, double Y, double Radius)[] gates =
    [
        .. Plan.List(check["gates"]).Select(gate =>
        {
            (double x, double y) = generator.Geo.ToTile(Plan.Point(gate["at"]));

            return (Plan.Text(gate["name"]), x, y, gate["radiusMiles"] is null ? radius : generator.Geo.ToGridUnits(Plan.Number(gate["radiusMiles"], 25)));
        })
    ];
    bool Closed(WorldPosition position, string? open)
    {
        return gates.Any(gate => gate.Name != open && Math.Sqrt(((gate.X - position.X) * (gate.X - position.X)) + ((gate.Y - position.Y) * (gate.Y - position.Y))) <= gate.Radius);
    }

    List<WorldPosition> leak = [];
    List<string> results = [Travel.Cost(map, start, goal, position => Closed(position, null), leak) is null ? "sealed when all closed" : "LEAKS when all gates are closed"];

    if (leak.Count > 0)
    {
        // Where the leaking route goes: every tenth tile, in plan miles.
        results.Add("leak route " + string.Join(" ", leak.Where((_, i) => i % 10 == 0).Select(tile =>
        {
            Miles at = generator.Geo.ToMiles(tile.X, tile.Y);

            return $"({at.E:F0},{at.N:F0})";
        })));
    }

    foreach ((string gateName, _, _, _) in gates)
    {
        int? cost = Travel.Cost(map, start, goal, position => Closed(position, gateName));
        results.Add(cost is null ? $"{gateName} BLOCKED" : $"{gateName} open ({cost} points)");
    }

    Console.WriteLine($"  gate check '{name}': {string.Join(", ", results)}");
}

if (positional.Count > 2)
{
    Overview.Write(map, generator.Labels, positional[2]);
    Console.WriteLine($"  overview: {positional[2]}");
}

foreach ((string path, double[] window) in closeUps)
{
    // Screen units of the four corners of the miles window (left = x - y, top = x + y).
    (double X, double Y)[] corners = [generator.Geo.ToTile(new Miles(window[0], window[1])), generator.Geo.ToTile(new Miles(window[2], window[1])),
                                      generator.Geo.ToTile(new Miles(window[0], window[3])), generator.Geo.ToTile(new Miles(window[2], window[3]))];
    Overview.Write(map, generator.Labels, path, 12, (corners.Min(c => c.X - c.Y), corners.Min(c => c.X + c.Y), corners.Max(c => c.X - c.Y), corners.Max(c => c.X + c.Y)));
    Console.WriteLine($"  close-up: {path}");
}

return 0;

internal static class Travel
{
    public static int? Cost(CustomMap map, WorldPosition start, WorldPosition goal, Func<WorldPosition, bool>? blocked = null, List<WorldPosition>? route = null)
    {
        int size = map.Size;
        int[] cost = new int[size * size];
        int[] previous = new int[size * size];
        Array.Fill(cost, int.MaxValue);
        PriorityQueue<int, int> queue = new();
        bool[] solid = Solid(map);

        int startIndex = (start.Y * size) + start.X;
        cost[startIndex] = 0;
        queue.Enqueue(startIndex, 0);

        while (queue.TryDequeue(out int current, out int currentCost))
        {
            if (currentCost > cost[current])
            {
                continue;
            }

            WorldPosition position = new(current % size, current / size);

            if (position == goal)
            {
                if (route is not null)
                {
                    for (int at = current; at != startIndex; at = previous[at])
                    {
                        route.Add(new WorldPosition(at % size, at / size));
                    }

                    route.Reverse();
                }

                return currentCost;
            }

            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    WorldPosition next = new(position.X + dx, position.Y + dy);

                    if ((dx == 0 && dy == 0) || !map.IsValidPosition(next) || solid[(next.Y * size) + next.X] || (blocked is not null && blocked(next)))
                    {
                        continue;
                    }

                    WorldMapCell cell = map.GetCell(next);

                    int step = cell.Feature switch
                    {
                        WorldMapFeature.Forest => 4,
                        WorldMapFeature.Road => 1,
                        _ => 2
                    };
                    int nextIndex = (next.Y * size) + next.X;

                    if (currentCost + step < cost[nextIndex])
                    {
                        cost[nextIndex] = currentCost + step;
                        previous[nextIndex] = current;
                        queue.Enqueue(nextIndex, currentCost + step);
                    }
                }
            }
        }

        return null;
    }

    private static CustomMap? solidMap;
    private static bool[] solidTiles = [];

    // Tiles no army walks on: water, mountains, structures and landmarks (computed once per map).
    private static bool[] Solid(CustomMap map)
    {
        if (ReferenceEquals(map, solidMap))
        {
            return solidTiles;
        }

        int size = map.Size;
        bool[] solid = new bool[size * size];
        IEnumerable<WorldFootprint> footprints = map.Villages.Select(village => village.Footprint)
            .Concat(map.Players.Where(player => player.Footprint is not null).Select(player => player.Footprint!.Value))
            .Concat(map.Ruins.Select(ruin => ruin.Footprint))
            .Concat(map.Sites.Select(site => site.Footprint))
            .Concat(map.Landmarks.Select(landmark => new WorldFootprint(landmark.TopLeft, landmark.Width, landmark.Height)));

        foreach (WorldFootprint footprint in footprints)
        {
            foreach (WorldPosition tile in footprint.EnumerateTiles())
            {
                if (map.IsValidPosition(tile))
                {
                    solid[(tile.Y * size) + tile.X] = true;
                }
            }
        }

        for (int index = 0; index < solid.Length; index++)
        {
            WorldMapCell cell = map.Cells[index];
            solid[index] |= cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain;
        }

        solidMap = map;
        solidTiles = solid;

        return solid;
    }
}
