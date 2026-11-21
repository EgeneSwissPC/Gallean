using System.Collections.Immutable;
using System.Text;
using Gallean.Common;
using Gallean.Entities.GameData;

namespace Gallean.Engine.Tests;

// Gallean Upgrade (docs/battle-ai-designer.md phase 1): the headless battle bench and army presets.
public sealed class AiBattleBenchTests
{
    private const string HostMap = "S099SC0000";
    private static readonly UnitDefinitionId PegasusKnight = new("g000uu0019");
    private static readonly UnitDefinitionId DeathKnight = new("g000uu0096");
    private static readonly UnitDefinitionId Squire = new("g000uu0001");
    private static readonly UnitDefinitionId Acolyte = new("g000uu0011");
    private static readonly UnitDefinitionId Fighter = new("g000uu0086");
    private static readonly UnitDefinitionId Initiate = new("g000uu0080");
    private static readonly UnitDefinitionId Ogre = new("g000uu5015");

    private AiBattleBench bench = null!;
    private string directory = null!;

    [OneTimeSetUp]
    public void OneTimeSetUp()
    {
        GlobalDatabase globalDb = GlobalDatabaseLoader.Load(RiseOfTheElves.GlobalDatabasePath);
        CampaignDatabase campaignDb = CampaignDatabaseLoader.Load(RiseOfTheElves.CampaignDatabasePath);
        ScenarioDatabase scenarioDb = ScenarioDatabaseLoader.Load(RiseOfTheElves.ScenarioDatabasePath, HostMap);
        bench = new AiBattleBench(globalDb, campaignDb, scenarioDb, HostMap);
    }

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), "gallean-battle-armies-" + Guid.NewGuid().ToString("N"));
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static BattleArmy Empire(int level = 1)
    {
        return new BattleArmy("Empire",
        [
            new BattleArmyUnit(PegasusKnight, level, GridPosition.BackCenter),
            new BattleArmyUnit(Squire, level, GridPosition.FrontBottom),
            new BattleArmyUnit(Squire, level, GridPosition.FrontCenter),
            new BattleArmyUnit(Acolyte, level, GridPosition.BackTop)
        ]);
    }

    private static BattleArmy Undead(int leaderLevel = 1, int unitLevel = 1)
    {
        return new BattleArmy("Undead",
        [
            new BattleArmyUnit(DeathKnight, leaderLevel, GridPosition.BackCenter),
            new BattleArmyUnit(Fighter, unitLevel, GridPosition.FrontBottom),
            new BattleArmyUnit(Fighter, unitLevel, GridPosition.FrontCenter),
            new BattleArmyUnit(Initiate, unitLevel, GridPosition.BackTop)
        ]);
    }

    // Records holding ImmutableArrays compare arrays by reference, so compare a text form of each run.
    private static string Describe(BattleBenchResult result)
    {
        StringBuilder text = new();

        foreach (BattleBenchRun run in result.Runs)
        {
            text.Append($"{run.Seed} {run.ArmyAAttacks} {run.Winner} {run.Rounds} |");

            foreach (BattleBenchUnitResult unit in run.A.Units.Concat(run.B.Units))
            {
                text.Append($" {unit.TypeId.Value}:{unit.Survived}:{unit.Retreated}:{unit.Hitpoints}/{unit.MaxHitpoints}");
            }

            text.AppendLine();
        }

        return text.ToString();
    }

    [Test]
    public void SameSeedSameResultTest()
    {
        BattleBenchSide a = new(Empire(), AiBrainRegistry.ClassicId);
        BattleBenchSide b = new(Undead(), AiBrainRegistry.ClassicId);

        string first = Describe(bench.Run(a, b, runs: 6, firstSeed: 10, swapSides: true));
        string again = Describe(bench.Run(a, b, runs: 6, firstSeed: 10, swapSides: true, parallel: 4));
        string otherSeeds = Describe(bench.Run(a, b, runs: 6, firstSeed: 500, swapSides: true));

        Assert.That(again, Is.EqualTo(first));
        Assert.That(otherSeeds, Is.Not.EqualTo(first));
    }

    [Test]
    public void SwapSidesAlternatesAttackerTest()
    {
        BattleBenchSide a = new(Empire(), AiBrainRegistry.ClassicId);
        BattleBenchSide b = new(Undead(), AiBrainRegistry.ClassicId);

        BattleBenchResult swapped = bench.Run(a, b, runs: 4, firstSeed: 1, swapSides: true);
        BattleBenchResult fixedSides = bench.Run(a, b, runs: 4, firstSeed: 1, swapSides: false);

        Assert.That(swapped.Runs.Select(run => run.ArmyAAttacks), Is.EqualTo(new[] { true, false, true, false }));
        Assert.That(fixedSides.Runs.Select(run => run.ArmyAAttacks), Is.All.True);
        Assert.That(swapped.Runs.Select(run => run.Seed), Is.EqualTo(new[] { 1, 2, 3, 4 }));
    }

    [Test]
    public void RunMatchesTheGamesAutoResolveTest()
    {
        // The bench must fight exactly the battle the game fights: the battle state the game builds for two
        // armies in the open, resolved by BattleAutoResolveCommand with both AI IDs (like GameSession does).
        BattleState initial = bench.CreateBattle(Empire(), Undead());
        EngineContext context = bench.CreateContext(seed: 7);
        BattleEngine engine = new(context, initial, NullBattleUnitUpgradeResolver.Instance);
        engine.DispatchCommand(new BattleStartCommand());
        engine.DispatchCommand(new BattleAutoResolveCommand { AttackerAiId = AiBrainRegistry.ClassicId, DefenderAiId = AiBrainRegistry.ClassicId });

        BattleBenchRun run = bench.RunOne(new BattleBenchSide(Empire(), AiBrainRegistry.ClassicId), new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId), seed: 7, aAttacks: true);

        Assert.That(engine.State.Outcome, Is.Not.Null);
        Assert.That(run.Winner, Is.EqualTo(engine.State.Outcome!.WinningSide == BattleSide.Attacker ? BattleBenchWinner.A : BattleBenchWinner.B));
        Assert.That(run.Rounds, Is.EqualTo(engine.State.RoundNumber));
        Assert.That(run.A.Hitpoints, Is.EqualTo(engine.State.AttackerGrid.GetUnits().Sum(pair => Math.Max(0, pair.Item1.Hitpoints))));
        Assert.That(run.B.Hitpoints, Is.EqualTo(engine.State.DefenderGrid.GetUnits().Sum(pair => Math.Max(0, pair.Item1.Hitpoints))));
    }

    [Test]
    public void BattleAiReachesTheBattleTest()
    {
        // Against a far stronger army the Classic leader flees; Ruthless never does (control + change).
        BattleBenchSide strong = new(Undead(leaderLevel: 10, unitLevel: 8), AiBrainRegistry.ClassicId);

        BattleBenchResult classic = bench.Run(new BattleBenchSide(Empire(), AiBrainRegistry.ClassicId), strong, runs: 6, firstSeed: 1, swapSides: false);
        BattleBenchResult ruthless = bench.Run(new BattleBenchSide(Empire(), "ruthless"), strong, runs: 6, firstSeed: 1, swapSides: false);

        Assert.That(classic.Runs.Count(run => run.A.Fled), Is.GreaterThan(0));
        Assert.That(ruthless.Runs.Count(run => run.A.Fled), Is.Zero);
        Assert.That(ruthless.Runs.Select(run => run.Winner), Is.All.EqualTo(BattleBenchWinner.B));
    }

    [Test]
    public void LevelRaisesHitpointsTest()
    {
        BattleState low = bench.CreateBattle(Empire(), Undead(leaderLevel: 1, unitLevel: 1));
        BattleState high = bench.CreateBattle(Empire(), Undead(leaderLevel: 5, unitLevel: 5));

        int lowHitpoints = low.DefenderGrid.GetUnits().Sum(pair => pair.Item1.Hitpoints);
        int highHitpoints = high.DefenderGrid.GetUnits().Sum(pair => pair.Item1.Hitpoints);

        Assert.That(highHitpoints, Is.GreaterThan(lowHitpoints));
        Assert.That(high.DefenderGrid.GetUnits().Select(pair => pair.Item1.Level), Is.EqualTo(new[] { 5, 5, 5, 5 }));
    }

    [Test]
    public void ValidationFollowsTheGameRulesTest()
    {
        Assert.That(bench.Validate(Empire()), Is.Null);
        Assert.That(bench.Validate(new BattleArmy("Empty", [])), Does.Contain("no units"));
        Assert.That(bench.Validate(new BattleArmy("Unknown", [new BattleArmyUnit(new UnitDefinitionId("g000uu9999"), 1, GridPosition.FrontCenter)])), Does.Contain("unknown unit"));
        Assert.That(bench.Validate(new BattleArmy("Two leaders", [new BattleArmyUnit(PegasusKnight, 1, GridPosition.FrontCenter), new BattleArmyUnit(DeathKnight, 1, GridPosition.BackCenter)])), Does.Contain("more than one leader"));
        Assert.That(bench.Validate(new BattleArmy("Same slot", [new BattleArmyUnit(PegasusKnight, 1, GridPosition.FrontCenter), new BattleArmyUnit(Squire, 1, GridPosition.FrontCenter)])), Does.Contain("taken"));
        Assert.That(bench.Validate(new BattleArmy("Large", [new BattleArmyUnit(PegasusKnight, 1, GridPosition.FrontCenter), new BattleArmyUnit(Ogre, 1, GridPosition.FrontBottom), new BattleArmyUnit(Squire, 1, GridPosition.BackBottom)])), Does.Contain("taken"));
        Assert.That(bench.Validate(new BattleArmy("Low level", [new BattleArmyUnit(new UnitDefinitionId("g000uu0002"), 1, GridPosition.FrontCenter)])), Does.Contain("below"));

        // Pegasus Knight leads 4: a fifth unit breaks the rule unless "anything goes" is on.
        BattleArmy crowded = Empire() with { Units = Empire().Units.Add(new BattleArmyUnit(Squire, 1, GridPosition.FrontTop)) };
        Assert.That(bench.Validate(crowded), Does.Contain("leadership"));
        Assert.That(bench.Validate(crowded with { AllowAnyArmy = true }), Is.Null);
        Assert.That(bench.Run(new BattleBenchSide(crowded with { AllowAnyArmy = true }, AiBrainRegistry.ClassicId), new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId), 1, 1, false).Runs[0].A.Units, Has.Length.EqualTo(5));
    }

    [Test]
    public void StoreRoundTripTest()
    {
        BattleArmyStore store = new(directory);
        BattleArmy army = Undead(leaderLevel: 7, unitLevel: 3) with { Name = "My undead", AllowAnyArmy = true };

        store.Save(army);
        store.Save(Empire());
        IReadOnlyList<BattleArmy> loaded = store.LoadAll();

        Assert.That(loaded.Select(item => item.Name), Is.EqualTo(new[] { "Empire", "My undead" }));
        BattleArmy back = loaded[1];
        Assert.That(back.AllowAnyArmy, Is.True);
        Assert.That(back.Units, Is.EqualTo(army.Units));

        store.Delete("Empire");
        Assert.That(store.LoadAll().Select(item => item.Name), Is.EqualTo(new[] { "My undead" }));
    }

    [Test]
    public void StoreSkipsBrokenFilesAndIgnoresUnknownPropertiesTest()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "broken.json"), "{ not json");
        File.WriteAllText(Path.Combine(directory, "newer.json"),
            """{ "format": 9, "name": "Newer", "future": [1, 2], "units": [ { "unit": "g000uu0001", "level": 2, "line": 0, "flank": 1, "items": [] } ] }""");

        IReadOnlyList<BattleArmy> loaded = new BattleArmyStore(directory).LoadAll();

        Assert.That(loaded.Select(item => item.Name), Is.EqualTo(new[] { "Newer" }));
        Assert.That(loaded[0].Units, Is.EqualTo(ImmutableArray.Create(new BattleArmyUnit(Squire, 2, GridPosition.FrontCenter))));
    }

    [Test]
    public void ExportedArmiesAreValidTest()
    {
        IReadOnlyList<BattleArmy> armies = bench.ExportArmies(HostMap);

        Assert.That(armies, Is.Not.Empty);
        Assert.That(armies.Select(army => army.Name), Is.Unique);
        Assert.That(armies.Select(army => bench.Validate(army)).Where(problem => problem is not null), Is.Empty);
    }
}
