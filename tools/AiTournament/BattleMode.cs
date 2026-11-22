using System.Diagnostics;
using System.Globalization;
using System.Text;
using Gallean.Entities;
using Gallean.Entities.GameData;

namespace AiTournament;

// Gallean Upgrade (docs/battle-ai-designer.md phase 1): the battle bench from the command line.
//   --battle <army A> <army B> [--ai-a id] [--ai-b id] [--race-a race] [--race-b race] [--neutral-a] [--neutral-b]
//            [--defender-in open|city|capital] [--city-level n] [--garrison]
//            [--runs n] [--seed n] [--swap] [--parallel n] [--map id] [--difficulty d] [--csv file] [--verbose]
//   --export-armies <map id> <dir>
// An army is a preset file (BattleArmyStore JSON) or a preset name in --armies <dir>.
internal static class BattleMode
{

    public static int Run(string[] args)
    {
        List<string> armies = [];
        string aiA = AiBrainRegistry.ClassicId;
        string aiB = AiBrainRegistry.ClassicId;
        int runs = 20;
        int seed = 1;
        bool swap = false;
        int parallel = 1;
        string? hostMap = null;
        WorldDifficulty difficulty = WorldDifficulty.Average;
        string? csvPath = null;
        string? armiesDirectory = null;
        bool verbose = false;
        WorldRace? raceA = null;
        WorldRace? raceB = null;
        BattleBenchPlace place = BattleBenchPlace.Open;
        int cityLevel = 1;
        bool garrison = false;

        for (int i = 1; i < args.Length; i++)
        {
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"{args[i]} needs a value");

            switch (args[i])
            {
                case "--ai-a": aiA = Next(); break;
                case "--ai-b": aiB = Next(); break;
                case "--runs": runs = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--seed": seed = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--swap": swap = true; break;
                case "--parallel": parallel = Math.Max(1, int.Parse(Next(), CultureInfo.InvariantCulture)); break;
                case "--map": hostMap = Next(); break;
                case "--csv": csvPath = Next(); break;
                case "--armies": armiesDirectory = Next(); break;
                case "--battle-ais":
                {
                    IReadOnlyList<DesignedBattleAi> designs = new DesignedBattleAiStore(Next()).LoadAll();
                    AiBrainRegistry.SetDesignedBattleAis(designs);
                    Console.WriteLine($"Designed battle AIs: {(designs.Count == 0 ? "none" : string.Join(", ", designs.Select(design => design.Id)))}");
                    break;
                }
                case "--verbose": verbose = true; break;
                case "--race-a": raceA = ParseRace(Next()); break;
                case "--race-b": raceB = ParseRace(Next()); break;
                case "--neutral-a": raceA = WorldRace.Neutral; break;
                case "--neutral-b": raceB = WorldRace.Neutral; break;
                case "--defender-in": place = Enum.Parse<BattleBenchPlace>(Next(), ignoreCase: true); break;
                case "--city-level": cityLevel = int.Parse(Next(), CultureInfo.InvariantCulture); break;
                case "--garrison": garrison = true; break;
                case "--difficulty": difficulty = Enum.Parse<WorldDifficulty>(Next(), ignoreCase: true); break;
                default:
                    if (args[i].StartsWith("--", StringComparison.Ordinal))
                    {
                        Console.Error.WriteLine($"Unknown argument {args[i]}");
                        return 2;
                    }

                    armies.Add(args[i]);
                    break;
            }
        }

        if (armies.Count != 2)
        {
            Console.Error.WriteLine("Usage: AiTournament --battle <army A> <army B> [--ai-a id] [--ai-b id] [--race-a r] [--race-b r] [--neutral-a] [--neutral-b] [--defender-in open|city|capital] [--city-level n] [--garrison] [--runs n] [--seed n] [--swap] [--parallel n] [--map id] [--armies dir] [--csv file] [--verbose]");
            return 2;
        }

        foreach (string ai in new[] { aiA, aiB })
        {
            if (!AiBrainRegistry.BattleBrains.Any(brain => brain.Id == ai))
            {
                Console.Error.WriteLine($"Unknown battle AI {ai}; known: {string.Join(", ", AiBrainRegistry.BattleBrains.Select(brain => brain.Id))}");
                return 2;
            }
        }

        BattleArmy? armyA = LoadArmy(armies[0], armiesDirectory);
        BattleArmy? armyB = LoadArmy(armies[1], armiesDirectory);

        if (armyA is null || armyB is null)
        {
            return 2;
        }

        (AiBattleBench bench, GlobalDatabase globalDb) = CreateBench(hostMap, difficulty);

        BattleBenchSide sideA = new(armyA, aiA) { Race = raceA };
        BattleBenchSide sideB = new(armyB, aiB) { Race = raceB };
        BattleBenchSetup setup = new(place, cityLevel, garrison);
        string? problem = bench.Validate(sideA, sideB, setup, swap);

        if (problem is not null)
        {
            Console.Error.WriteLine(problem);
            return 2;
        }

        string where = place switch
        {
            BattleBenchPlace.City => $", defender in a level {cityLevel} city",
            BattleBenchPlace.Capital => ", defender in its capital",
            _ => ""
        } + (garrison && place is not BattleBenchPlace.Open ? " as the garrison" : "");

        Console.WriteLine($"A: {armyA.Name} ({aiA}{(raceA is null ? "" : ", " + raceA)})  vs  B: {armyB.Name} ({aiB}{(raceB is null ? "" : ", " + raceB)}); " +
            $"{runs} runs, seeds from {seed}, " + (swap ? "sides swapped every run" : "A attacks") + where + $", host map {hostMap ?? "generated (every race)"}, {difficulty}");

        Stopwatch time = Stopwatch.StartNew();
        BattleBenchResult result = bench.Run(sideA, sideB, setup, runs, seed, swap, parallel);

        if (verbose)
        {
            foreach (BattleBenchRun run in result.Runs)
            {
                Console.WriteLine(BattleSummary.FormatRun(run));
            }
        }

        Console.WriteLine();
        Console.WriteLine(BattleSummary.Format(result, UnitNames(globalDb)));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Total {time.Elapsed.TotalSeconds:F1} s"));

        if (csvPath is not null)
        {
            File.WriteAllText(csvPath, BattleSummary.ToCsv(result), Encoding.UTF8);
            Console.WriteLine($"CSV: {csvPath}");
        }

        return 0;
    }

    // --new-battle-ai <dir> <name> [source]: a designed battle AI copied from Classic (or a built-in), to edit by hand.
    public static int NewBattleAi(string[] args)
    {
        if (args.Length is < 3 or > 4)
        {
            Console.Error.WriteLine("Usage: AiTournament --new-battle-ai <dir> <name> [source AI]");
            return 2;
        }

        DesignedBattleAi design = DesignedBattleAi.FromBuiltIn(args.Length == 4 ? args[3] : AiBrainRegistry.ClassicId, args[2], args[2]);
        new DesignedBattleAiStore(args[1]).Save(design);
        Console.WriteLine($"Wrote {Path.Combine(args[1], args[2] + ".json")} (id {design.Id})");
        return 0;
    }

    public static int ExportArmies(string[] args)
    {
        if (args.Length != 3)
        {
            Console.Error.WriteLine("Usage: AiTournament --export-armies <map id> <dir>");
            return 2;
        }

        (AiBattleBench bench, _) = CreateBench(args[1]);
        BattleArmyStore store = new(args[2]);
        int written = 0;

        foreach (BattleArmy army in bench.ExportArmies(args[1]))
        {
            store.Save(army);
            written++;
        }

        Console.WriteLine($"Wrote {written} armies to {args[2]}");
        return 0;
    }

    // hostMap null = the generated host map with every race.
    private static (AiBattleBench, GlobalDatabase) CreateBench(string? hostMap, WorldDifficulty difficulty = WorldDifficulty.Average)
    {
        GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
        CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);

        if (hostMap is null)
        {
            return (AiBattleBench.CreateWithGeneratedHost(globalDb, campaignDb, difficulty), globalDb);
        }

        ScenarioDatabase scenarioDb = ScenarioDatabaseLoader.Load(RiseOfTheElves.ScenarioDatabasePath, hostMap);

        return (new AiBattleBench(globalDb, campaignDb, scenarioDb, hostMap, difficulty), globalDb);
    }

    // English unit names (Globals/Tglobal.json) for the summary; the unit ID when a name is missing.
    private static Func<UnitDefinitionId, string> UnitNames(GlobalDatabase globalDb)
    {
        Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(RiseOfTheElves.GlobalDatabasePath, "Tglobal.json");

        if (File.Exists(path))
        {
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(File.ReadAllBytes(path));

            foreach (System.Text.Json.JsonElement row in document.RootElement.GetProperty("rows").EnumerateArray())
            {
                texts[row.GetProperty("TXT_ID").GetString()!] = row.GetProperty("TEXT").GetString() ?? "";
            }
        }

        return id => globalDb.Units.TryGetValue(new Db.Id(id.Value), out Db.Unit? unit) && texts.TryGetValue(unit.NameText, out string? name) ? name : id.Value;
    }

    private static WorldRace ParseRace(string text)
    {
        return text.ToLowerInvariant() switch
        {
            "empire" or "human" => WorldRace.Human,
            "undead" => WorldRace.Undead,
            "legions" or "heretic" => WorldRace.Heretic,
            "clans" or "dwarf" => WorldRace.Dwarf,
            "elves" or "elf" => WorldRace.Elf,
            "neutral" => WorldRace.Neutral,
            _ => throw new ArgumentException($"Unknown race {text} (empire, undead, legions, clans, elves, neutral)")
        };
    }

    private static BattleArmy? LoadArmy(string argument, string? directory)
    {
        string path = File.Exists(argument) || directory is null
            ? argument
            : Path.Combine(directory, BattleArmyStore.GetFileName(argument) + ".json");
        BattleArmy? army = File.Exists(path) ? BattleArmyStore.TryLoadFile(path) : null;

        if (army is null)
        {
            Console.Error.WriteLine($"Can't read the army {argument} ({path})");
        }

        return army;
    }
}
