using System.Diagnostics;
using System.Globalization;
using System.Text;
using AiTournament;
using Gallean.Entities;
using Gallean.Entities.GameData;

// Gallean Upgrade: AI tournament (docs/ai-switching.md, phase 4).
// dotnet run --project tools/AiTournament -- <map id> [--maps <dir>] [--ai world/battle]... [--games n] [--turns n] [--seed n] [--csv file]

string? mapId = null;
string? mapsDirectory = null;
List<AiContender> contenders = [];
int games = 2;
int maxTurns = 50;
int seed = 1;
string? csvPath = null;
bool verbose = false;
int parallel = 1;
string? aisDirectory = null;

// Gallean Upgrade (docs/battle-ai-designer.md phase 1): the battle bench.
if (args.Length > 0 && args[0] == "--battle")
{
    return BattleMode.Run(args);
}

if (args.Length > 0 && args[0] == "--new-battle-ai")
{
    return BattleMode.NewBattleAi(args);
}

if (args.Length > 0 && args[0] == "--export-armies")
{
    return BattleMode.ExportArmies(args);
}

for (int i = 0; i < args.Length; i++)
{
    string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

    switch (args[i])
    {
        case "--maps": mapsDirectory = Next(); break;
        case "--ai": contenders.Add(Contender.Parse(Next())); break;
        case "--games": games = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--turns": maxTurns = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--seed": seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
        case "--csv": csvPath = Next(); break;
        case "--verbose": verbose = true; break;
        case "--parallel": parallel = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
        case "--ais": aisDirectory = Next(); break;
        case "--new-ai":
        {
            // Writes a designed map AI that plays like Classic, to edit by hand until the AI editor exists.
            string directory = Next();
            string fileName = Next();
            new DesignedWorldAiStore(directory).Save(DesignedWorldAi.FromClassic(fileName, fileName));
            Console.WriteLine($"Wrote {Path.Combine(directory, fileName + ".json")} (id custom:{fileName})");
            return 0;
        }
        case "--list" when aisDirectory is not null:
            AiBrainRegistry.SetDesignedWorldAis(new DesignedWorldAiStore(aisDirectory).LoadAll());
            goto case "--list-all";
        case "--list":
        case "--list-all":
            Console.WriteLine("World AIs:  " + string.Join(", ", AiBrainRegistry.WorldBrains.Select(brain => brain.Id)));
            Console.WriteLine("Battle AIs: " + string.Join(", ", AiBrainRegistry.BattleBrains.Select(brain => brain.Id)));
            return 0;
        default:
            if (args[i].StartsWith("--", StringComparison.Ordinal) || mapId is not null)
            {
                Console.Error.WriteLine($"Unknown argument {args[i]}");
                return 2;
            }

            mapId = args[i];
            break;
    }
}

if (aisDirectory is not null)
{
    IReadOnlyList<DesignedWorldAi> designs = new DesignedWorldAiStore(aisDirectory).LoadAll();
    AiBrainRegistry.SetDesignedWorldAis(designs);
    Console.WriteLine($"Designed AIs from {aisDirectory}: {(designs.Count == 0 ? "none" : string.Join(", ", designs.Select(design => design.Id)))}");
}

if (mapId is null)
{
    Console.Error.WriteLine("Usage: AiTournament <map id> [--maps <custom maps dir>] [--ai world/battle]... [--games n] [--turns n] [--seed n] [--csv file] [--parallel n] [--ais <dir>] [--verbose] | --list | --new-ai <dir> <name>");
    return 2;
}

if (contenders.Count == 0)
{
    contenders.Add(Contender.Parse(AiBrainRegistry.ClassicId));
}

foreach (AiContender contender in contenders)
{
    if (!Contender.IsKnown(contender))
    {
        Console.Error.WriteLine($"Unknown AI in {contender}; known: --list");
        return 2;
    }
}

GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
ScenarioDatabase scenarioDb = mapsDirectory is null
    ? ScenarioDatabaseLoader.Load(RiseOfTheElves.ScenarioDatabasePath, mapId)
    : new CustomMapStore(mapsDirectory, globalDb, campaignDb).LoadDatabase(mapId);

AiTournamentRunner runner = new(globalDb, campaignDb, scenarioDb, mapId, maxTurns, verbose ? Console.WriteLine : null);
List<AiGameResult> results = [];
int seats = runner.CountPlayableSeats();
int rotations = contenders.Count == 1 ? 1 : contenders.Count;

Console.WriteLine($"{mapId}: {seats} playable seats, contenders {string.Join(" | ", contenders)}, " +
    $"{rotations} rotation(s) x {games} game(s), up to {maxTurns} turns, seeds from {seed}");

if (seats < 2)
{
    Console.Error.WriteLine("The map needs at least two playable races.");
    return 2;
}

Stopwatch total = Stopwatch.StartNew();

List<(AiContender[] Seating, int Seed)> plan = [.. runner.Plan(contenders, games, seed)];

if (parallel == 1 || verbose)
{
    foreach ((AiContender[] seating, int gameSeed) in plan)
    {
        AiGameResult result = runner.Play(seating, gameSeed);
        results.Add(result);
        Console.WriteLine(Summary.FormatGame(result));
    }
}
else
{
    // Games are independent (own session, own random seed), so the results don't depend on the order.
    AiGameResult[] parallelResults = new AiGameResult[plan.Count];
    object consoleLock = new();

    Parallel.For(0, plan.Count, new ParallelOptions { MaxDegreeOfParallelism = parallel }, index =>
    {
        parallelResults[index] = runner.Play(plan[index].Seating, plan[index].Seed);

        lock (consoleLock)
        {
            Console.WriteLine(Summary.FormatGame(parallelResults[index]));
        }
    });

    results.AddRange(parallelResults);
}

Console.WriteLine();
Console.WriteLine(Summary.Format(contenders, results));
Console.WriteLine($"Total {total.Elapsed.TotalSeconds:F0} s");

if (csvPath is not null)
{
    File.WriteAllText(csvPath, Summary.ToCsv(results), Encoding.UTF8);
    Console.WriteLine($"CSV: {csvPath}");
}

return 0;
