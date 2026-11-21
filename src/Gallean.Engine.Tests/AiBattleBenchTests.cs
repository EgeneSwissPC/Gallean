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
    private static readonly UnitDefinitionId Archer = new("g000uu0006");
    private static readonly UnitDefinitionId Myzrael = new("g000uu3001");
    private static readonly ItemDefinitionId BannerOfStriking = new("G000IG1003");
    private static readonly ItemDefinitionId Runestone = new("G000IG2001");
    private static readonly ItemDefinitionId TomeOfAir = new("G000IG4001");
    private static readonly UnitModifierDefinitionId BannerBearer = new("g000um4003");
    private static readonly UnitModifierDefinitionId ArtifactLore = new("g000um4001");

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
    public void BothSidesGetTheSameWorldModifiersTest()
    {
        BattleState state = bench.CreateBattle(Empire(), Undead());

        Assert.That(state.Modifiers.Attacker with { CanRetreat = true }, Is.EqualTo(state.Modifiers.Defender with { CanRetreat = true }));
        Assert.That(state.Modifiers.Attacker.CanRetreat, Is.True);
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

    // A level 3 Pegasus Knight may take two upgrades: Banner Bearer and Artifact Lore allow a banner and an artifact.
    private static BattleArmy GearedEmpire(bool withUpgrades = true)
    {
        return Empire() with
        {
            Name = "Geared",
            Units = [new BattleArmyUnit(PegasusKnight, 3, GridPosition.BackCenter), .. Empire().Units.Skip(1)],
            Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Banner, BannerOfStriking), new BattleArmyItem(LeaderEquipmentSlot.Artifact1, Runestone)],
            LeaderUpgrades = withUpgrades ? [BannerBearer, ArtifactLore] : []
        };
    }

    private static BattleArmy Garrison(bool withGuardian = false)
    {
        return new BattleArmy("Garrison",
        [
            new BattleArmyUnit(withGuardian ? Myzrael : Squire, 1, GridPosition.FrontBottom),
            new BattleArmyUnit(Squire, 2, GridPosition.FrontCenter),
            new BattleArmyUnit(Squire, 2, GridPosition.FrontTop),
            new BattleArmyUnit(Archer, 2, GridPosition.BackBottom),
            new BattleArmyUnit(Archer, 2, GridPosition.BackCenter),
            new BattleArmyUnit(Acolyte, 2, GridPosition.BackTop)
        ]);
    }

    private static int FortificationArmor(BattleState state)
    {
        return state.DefenderGrid.GetUnits()
            .Select(pair => pair.Item1.StatusEffects.OfType<BattleFortificationStatusEffect>().Sum(effect => effect.Armor))
            .Min();
    }

    [Test]
    public void LeaderItemsReachTheBattleTest()
    {
        BattleState plain = bench.CreateBattle(GearedEmpire() with { Equipment = [] }, Undead());
        BattleState geared = bench.CreateBattle(GearedEmpire(), Undead());

        // The banner works on the whole army, the artifact on the leader only (like LeaderEquipmentCalculator).
        Assert.That(plain.AttackerGrid.GetUnits().SelectMany(pair => pair.Item1.Modifiers).Count(modifier => modifier.Source is UnitModifierSource.Equipment), Is.Zero);
        Assert.That(geared.AttackerGrid.GetUnits().Select(pair => pair.Item1.Modifiers.Count(modifier => modifier.Source is UnitModifierSource.Equipment)),
            Is.All.GreaterThanOrEqualTo(1));
        Assert.That(geared.AttackerGrid.GetUnits().Single(pair => pair.Item1.DefinitionId == PegasusKnight).Item1.Modifiers.Count(modifier => modifier.Source is UnitModifierSource.Equipment), Is.EqualTo(2));

        // And they change the outcome over many runs (control: the same army without items).
        BattleBenchSide undead = new(Undead(), AiBrainRegistry.ClassicId);
        int plainWins = bench.Run(new BattleBenchSide(GearedEmpire() with { Equipment = [] }, AiBrainRegistry.ClassicId), undead, 100, 1, false, 4).Runs.Count(run => run.Winner == BattleBenchWinner.A);
        int gearedWins = bench.Run(new BattleBenchSide(GearedEmpire(), AiBrainRegistry.ClassicId), undead, 100, 1, false, 4).Runs.Count(run => run.Winner == BattleBenchWinner.A);
        Assert.That(gearedWins, Is.GreaterThan(plainWins));
    }

    [Test]
    public void ItemAttacksAndBattleItemsReachTheBattleTest()
    {
        // An artifact with an attack gives the leader that attack; an orb in a battle slot is a battle item.
        BattleArmy army = GearedEmpire() with
        {
            Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Artifact1, new ItemDefinitionId("G000IG3018")), new BattleArmyItem(LeaderEquipmentSlot.Battle1, new ItemDefinitionId("G000IG9022"))],
            AllowAnyArmy = true
        };
        BattleState state = bench.CreateBattle(army, Undead());
        BattleUnit leader = state.AttackerGrid.GetUnits().Single(pair => pair.Item1.DefinitionId == PegasusKnight).Item1;

        Assert.That(leader.EquipmentAttackIds, Is.Not.Empty);
        Assert.That(state.Items.Where(item => item.Side == BattleSide.Attacker).Select(item => item.Slot), Is.EqualTo(new[] { LeaderEquipmentSlot.Battle1 }));
        Assert.That(bench.CreateBattle(GearedEmpire() with { Equipment = [] }, Undead()).Items, Is.Empty);
    }

    [Test]
    public void LeaderUpgradesAreLeaderModifiersTest()
    {
        BattleState state = bench.CreateBattle(GearedEmpire(), Undead());
        BattleUnit leader = state.AttackerGrid.GetUnits().Single(pair => pair.Item1.DefinitionId == PegasusKnight).Item1;

        Assert.That(leader.Modifiers.Where(modifier => modifier.Source is UnitModifierSource.Unit).Select(modifier => modifier.DefinitionId), Is.EquivalentTo(new[] { BannerBearer, ArtifactLore }));
    }

    [Test]
    public void ItemAndUpgradeRulesTest()
    {
        Assert.That(bench.Validate(GearedEmpire()), Is.Null);
        Assert.That(bench.Validate(GearedEmpire(withUpgrades: false)), Does.Contain("needs the leader ability"));
        Assert.That(bench.Validate(GearedEmpire(withUpgrades: false) with { AllowAnyArmy = true }), Is.Null);
        Assert.That(bench.Validate(GearedEmpire() with { Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Boots, BannerOfStriking)] }), Does.Contain("doesn't fit"));
        Assert.That(bench.Validate(GearedEmpire() with { Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Banner, BannerOfStriking), new BattleArmyItem(LeaderEquipmentSlot.Banner, BannerOfStriking)] }), Does.Contain("two items"));
        Assert.That(bench.Validate(GearedEmpire() with { Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Tome, new ItemDefinitionId("G000IG9999"))] }), Does.Contain("unknown item"));
        Assert.That(bench.Validate(GearedEmpire() with { Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Tome, new ItemDefinitionId("G000IG9999"))], AllowAnyArmy = true }), Does.Contain("unknown item"));
        Assert.That(bench.Validate(Empire() with { LeaderUpgrades = [BannerBearer] }), Does.Contain("at most 0 upgrades"));
        Assert.That(bench.Validate(GearedEmpire() with { LeaderUpgrades = [BannerBearer, BannerBearer] }), Does.Contain("twice"));
        Assert.That(bench.Validate(GearedEmpire() with { LeaderUpgrades = [new UnitModifierDefinitionId("g000um9999")] }), Does.Contain("unknown leader upgrade"));
        Assert.That(bench.Validate(Garrison() with { Equipment = [new BattleArmyItem(LeaderEquipmentSlot.Tome, TomeOfAir)] }, asGarrison: true), Does.Contain("garrison"));
    }

    [Test]
    public void CityGivesTheDefenderItsProtectionTest()
    {
        BattleState open = bench.CreateBattle(Undead(), Undead());
        BattleState city1 = bench.CreateBattle(Undead(), Undead(), new BattleBenchSetup(BattleBenchPlace.City, 1));
        BattleState city5 = bench.CreateBattle(Undead(), Undead(), new BattleBenchSetup(BattleBenchPlace.City, 5));
        BattleState capital = bench.CreateBattle(new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId), new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId) { Race = WorldRace.Undead }, new BattleBenchSetup(BattleBenchPlace.Capital));

        Assert.That(FortificationArmor(open), Is.Zero);
        Assert.That(FortificationArmor(city1), Is.GreaterThan(0));
        Assert.That(FortificationArmor(city5), Is.GreaterThan(FortificationArmor(city1)));
        Assert.That(FortificationArmor(capital), Is.GreaterThan(0));

        // The defender of a city can't flee; the attacker still can. In the open both can.
        Assert.That(open.Modifiers.Defender.CanRetreat, Is.True);
        Assert.That(city1.Modifiers.Defender.CanRetreat, Is.False);
        Assert.That(city1.Modifiers.Attacker.CanRetreat, Is.True);
    }

    [Test]
    public void CityChangesTheOutcomeTest()
    {
        // Mirror battle: about even in the open, the defender wins far more often in a level 5 city.
        BattleBenchSide undead = new(Undead(), AiBrainRegistry.ClassicId);
        int openDefenderWins = bench.Run(undead, undead, BattleBenchSetup.Open, 100, 1, false, 4).Runs.Count(run => run.Winner == BattleBenchWinner.B);
        int cityDefenderWins = bench.Run(undead, undead, new BattleBenchSetup(BattleBenchPlace.City, 5), 100, 1, false, 4).Runs.Count(run => run.Winner == BattleBenchWinner.B);

        Assert.That(openDefenderWins, Is.InRange(30, 70));
        Assert.That(cityDefenderWins, Is.GreaterThan(openDefenderWins + 20));
    }

    [Test]
    public void GarrisonDefendsWithoutLeaderTest()
    {
        BattleBenchSetup garrison = new(BattleBenchPlace.City, 3, DefenderIsGarrison: true);
        BattleState state = bench.CreateBattle(Undead(), Garrison(), garrison);

        Assert.That(state.DefenderGrid.GetUnits().Count(), Is.EqualTo(6));
        Assert.That(FortificationArmor(state), Is.GreaterThan(0));

        // A garrison has no leader and no leadership limit; guardians may stand in it, but not in an army.
        Assert.That(bench.Validate(Garrison()), Does.Contain("leadership"));
        Assert.That(bench.Validate(Garrison(), asGarrison: true), Is.Null);
        Assert.That(bench.Validate(Garrison(withGuardian: true), asGarrison: true), Is.Null);
        Assert.That(bench.Validate(Garrison(withGuardian: true) with { Units = Garrison(withGuardian: true).Units.SetItem(1, new BattleArmyUnit(PegasusKnight, 1, GridPosition.FrontCenter)) }), Does.Contain("guardians"));
        Assert.That(bench.Validate(Undead(), asGarrison: true), Does.Contain("no leader"));
        Assert.That(bench.Validate(new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId), new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId), garrison, swapSides: false), Does.Contain("defending"));
        Assert.That(bench.Validate(new BattleBenchSide(Undead(), AiBrainRegistry.ClassicId), new BattleBenchSide(Garrison(), AiBrainRegistry.ClassicId), garrison, swapSides: false), Is.Null);
    }

    [Test]
    public void NeutralSideNeverFleesTest()
    {
        // Against a far stronger army a playable race's leader flees (Classic); a neutral one never does.
        BattleBenchSide strong = new(Undead(leaderLevel: 10, unitLevel: 8), AiBrainRegistry.ClassicId);
        BattleBenchResult race = bench.Run(new BattleBenchSide(Empire(), AiBrainRegistry.ClassicId), strong, 6, 1, false);
        BattleBenchResult neutral = bench.Run(new BattleBenchSide(Empire(), AiBrainRegistry.ClassicId) { Race = WorldRace.Neutral }, strong, 6, 1, false);

        Assert.That(race.Runs.Count(run => run.A.Fled), Is.GreaterThan(0));
        Assert.That(neutral.Runs.Count(run => run.A.Fled), Is.Zero);
    }

    [Test]
    public void NeutralsHaveNoCapitalTest()
    {
        BattleBenchSide neutral = new(Undead(), AiBrainRegistry.ClassicId) { Race = WorldRace.Neutral };
        BattleBenchSide empire = new(Empire(), AiBrainRegistry.ClassicId);

        Assert.That(bench.Validate(empire, neutral, new BattleBenchSetup(BattleBenchPlace.Capital), swapSides: false), Does.Contain("no capital"));
        Assert.That(bench.Validate(empire, neutral, new BattleBenchSetup(BattleBenchPlace.City, 2), swapSides: false), Is.Null);
        Assert.That(bench.Validate(neutral, empire, new BattleBenchSetup(BattleBenchPlace.Capital), swapSides: false), Is.Null);
        Assert.That(bench.Validate(neutral, empire, new BattleBenchSetup(BattleBenchPlace.Capital), swapSides: true), Does.Contain("no capital"));
        Assert.That(bench.Races, Does.Contain(WorldRace.Neutral));
    }

    [Test]
    public void StoreRoundTripWithItemsTest()
    {
        BattleArmyStore store = new(directory);
        store.Save(GearedEmpire());

        BattleArmy back = store.LoadAll().Single();

        Assert.That(back.Equipment, Is.EqualTo(GearedEmpire().Equipment));
        Assert.That(back.LeaderUpgrades, Is.EqualTo(GearedEmpire().LeaderUpgrades));
        Assert.That(bench.Validate(back), Is.Null);
    }
}
