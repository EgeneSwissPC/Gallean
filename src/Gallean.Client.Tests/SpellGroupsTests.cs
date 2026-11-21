using Gallean.Common;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: spell groups (Create spell group in the Spellbook, the Spell groups tab) and the spell cursor
// staying on after a cast so the next click casts again.
[NonParallelizable]
public sealed class SpellGroupsTests
{
    private static readonly WorldSpellDefinitionId RestoreId = new("restore");
    private static readonly WorldSpellDefinitionId HasteId = new("haste");
    private static readonly WorldStackId StackId = new("Stack1");

    private string previousPath = string.Empty;
    private string tempPath = string.Empty;

    [SetUp]
    public void SetUp()
    {
        previousPath = SpellGroupStore.FilePath;
        tempPath = Path.Combine(Path.GetTempPath(), $"spell-groups-{Guid.NewGuid():N}.json");
        SpellGroupStore.FilePath = tempPath;
    }

    [TearDown]
    public void TearDown()
    {
        SpellGroupStore.FilePath = previousPath;
        File.Delete(tempPath);
    }

    private sealed record Setup(GameSession Session, GameViewModel GameViewModel, WorldScreenViewModel World) : IDisposable
    {
        public int MovementPoints => Session.World.State.Stacks[StackId].MovementPoints;

        public void Dispose()
        {
            GameViewModel.Dispose();
        }
    }

    private static WorldSpellDefinition Spell(WorldSpellDefinitionId id, string name, int level, int percent)
    {
        return new WorldSpellDefinition(
            id,
            new GlobalTextId(name),
            new GlobalTextId(name + " description"),
            new GlobalTextId(name + " effect"),
            WorldRace.Undead,
            Level: level,
            WorldResourceSet.Empty,
            PurchaseCost: WorldResourceSet.Empty,
            AreaRadius: 0,
            new WorldRestoreMovementSpellEffect(percent),
            AiType: WorldAiSpellType.All);
    }

    // An army with 200 move points that has walked 30 tiles, so restoring move points shows.
    private static Setup Create(LordCategory lord)
    {
        WorldMap map = TestHelpers.SimpleMap(31, 1);
        UnitDefinition leaderDefinition = TestHelpers.SimpleLeaderDefinition(
            TestHelpers.SimpleUnitDefinition("leader", maxHitpoints: 100, initiative: 50, damage: 10),
            movementPoints: 200, leadership: 3);

        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Undead, map) with
        {
            Buildings = new WorldPlayerBuildings([], []),
            Spellbook = new WorldPlayerSpellbook([], [RestoreId, HasteId])
        };

        WorldStack stack = TestHelpers.SimpleStack(
            StackId,
            player.Id,
            new WorldPosition(0, 0),
            new UnitGrid<WorldUnit>.Builder()
                .Place(TestHelpers.SimpleUnit(new WorldUnitId("leader"), leaderDefinition), GridPosition.FrontBottom, false)
                .Build(),
            movementPoints: 2);

        (GameSession session, _) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, lord))
            .WithPlayer(player)
            .WithDefinition(leaderDefinition)
            .WithDefinition(Spell(RestoreId, "Restoration", 1, 5))
            .WithDefinition(Spell(HasteId, "Haste", 2, 10))
            .WithStack(stack)
            .WithSubrace(TestHelpers.NeutralSubrace)
            .BuildSession();

        session.World.DispatchCommand(new WorldStartGameCommand());
        session.World.DispatchCommand(new WorldMoveStackCommand(StackId, new WorldPosition(30, 0)));
        Assert.That(session.World.State.Stacks[StackId].MovementPoints, Is.LessThan(170));

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);
        GameViewModel gameViewModel = new TestGameViewModelBuilder().Build();
        WorldScreenViewModel world = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(world);
        world.CompleteGameStart();

        return new Setup(session, gameViewModel, world);
    }

    private static (ManagementScreenViewModel Screen, SpellbookTabViewModel Spellbook, SpellGroupsTabViewModel Groups) OpenSpellbook(Setup setup)
    {
        setup.World.OpenSpellbookCommand.Execute(null);
        ManagementScreenViewModel screen = Assert.IsOfType<ManagementScreenViewModel>(setup.GameViewModel.TopScreen);

        Assert.That(screen.Tabs.Select(tab => tab.TabHeader), Is.EqualTo(new[] { "Spellbook", "Spell groups" }));

        return (screen, Assert.IsOfType<SpellbookTabViewModel>(screen.Tabs[0]), Assert.IsOfType<SpellGroupsTabViewModel>(screen.Tabs[1]));
    }

    private static SpellbookItemViewModel Item(SpellbookTabViewModel spellbook, WorldSpellDefinitionId id)
    {
        return spellbook.Levels.SelectMany(level => level.Spells).Single(item => item.SpellId == id);
    }

    private static void MakeGroup(SpellbookTabViewModel spellbook, string name, params WorldSpellDefinitionId[] spells)
    {
        spellbook.StartGroupCommand.Execute(null);
        spellbook.GroupName = name;

        foreach (WorldSpellDefinitionId spell in spells)
        {
            spellbook.SelectSpellCommand.Execute(Item(spellbook, spell));
        }

        spellbook.SaveGroupCommand.Execute(null);
    }

    [Test]
    public void CreateGroupTest()
    {
        using Setup setup = Create(LordCategory.Warrior);
        (ManagementScreenViewModel screen, SpellbookTabViewModel spellbook, SpellGroupsTabViewModel groups) = OpenSpellbook(setup);

        Assert.That(groups.Groups, Is.Empty);
        Assert.That(spellbook.SaveGroupCommand.CanExecute(null), Is.False);

        spellbook.StartGroupCommand.Execute(null);

        Assert.That(spellbook.IsPickingGroup, Is.True);
        Assert.That(spellbook.GroupName, Is.EqualTo("Group 1"));

        // Picking order is casting order; a second click takes a spell out again.
        spellbook.SelectSpellCommand.Execute(Item(spellbook, HasteId));
        spellbook.SelectSpellCommand.Execute(Item(spellbook, RestoreId));
        spellbook.SelectSpellCommand.Execute(Item(spellbook, HasteId));
        spellbook.SelectSpellCommand.Execute(Item(spellbook, HasteId));

        Assert.That(Item(spellbook, RestoreId).PickOrder, Is.EqualTo(1));
        Assert.That(Item(spellbook, HasteId).PickOrder, Is.EqualTo(2));
        Assert.That(spellbook.PickedCountText, Is.EqualTo("2 spells in the group"));
        Assert.That(spellbook.SelectedSpell, Is.Null);

        spellbook.GroupName = "  Speed  ";
        spellbook.SaveGroupCommand.Execute(null);

        Assert.That(spellbook.IsPickingGroup, Is.False);
        Assert.That(Item(spellbook, RestoreId).PickOrder, Is.Null);
        Assert.That(screen.SelectedTab, Is.SameAs(groups));
        Assert.That(SpellGroupStore.Load(WorldRace.Undead), Is.EqualTo(new[] { new SpellGroup("Speed", [RestoreId, HasteId]) }).Using<SpellGroup>(SameGroup));

        SpellGroupRowViewModel row = groups.Groups.Single();
        Assert.That(row.Name, Is.EqualTo("Speed"));
        Assert.That(row.SpellItems.Select(spell => (spell.Position, spell.Details.Name)), Is.EqualTo(new[] { (1, "Restoration"), (2, "Haste") }));
        Assert.That(row.ReadyText, Is.EqualTo("2 of 2 spells can be cast now"));
        Assert.That(row.Status.IsAvailable, Is.True);
        Assert.That(File.ReadAllText(tempPath), Does.Contain("\"Undead\""));
    }

    private static bool SameGroup(SpellGroup a, SpellGroup b)
    {
        return a.Name == b.Name && a.Spells.SequenceEqual(b.Spells);
    }

    [Test]
    public void CancelPickingTest()
    {
        using Setup setup = Create(LordCategory.Warrior);
        (ManagementScreenViewModel screen, SpellbookTabViewModel spellbook, _) = OpenSpellbook(setup);

        spellbook.StartGroupCommand.Execute(null);
        spellbook.SelectSpellCommand.Execute(Item(spellbook, HasteId));

        // Esc ends picking first, without closing the spellbook or saving anything.
        Assert.That(spellbook.TryCancel(), Is.True);
        Assert.That(spellbook.IsPickingGroup, Is.False);
        Assert.That(Item(spellbook, HasteId).PickOrder, Is.Null);
        Assert.That(setup.GameViewModel.TopScreen, Is.SameAs(screen));
        Assert.That(SpellGroupStore.Load(WorldRace.Undead), Is.Empty);
        Assert.That(spellbook.TryCancel(), Is.False);
    }

    [Test]
    public async Task CastGroupTest()
    {
        using Setup setup = Create(LordCategory.Warrior);
        (_, SpellbookTabViewModel spellbook, SpellGroupsTabViewModel groups) = OpenSpellbook(setup);
        int before = setup.MovementPoints;

        MakeGroup(spellbook, "Speed", RestoreId, HasteId);
        await groups.Groups.Single().CastCommand.ExecuteAsync(null);

        Assert.That(setup.GameViewModel.TopScreen, Is.SameAs(setup.World));
        Assert.That(setup.World.ActionPromptText, Is.EqualTo("Select a target for the spell group on the map."));

        await setup.World.HandleMapClickAsync(new WorldPosition(30, 0), null, 1);

        // Restoration (+5) and Haste (+10), both spells in one click.
        Assert.That(setup.MovementPoints, Is.EqualTo(before + 15));
        Assert.That(setup.World.Game.Alert.Current, Is.Null);
        Assert.That(setup.Session.World.State.Players[new WorldPlayerId(1)].TurnActions.SpellCasts.Keys, Is.EquivalentTo(new[] { RestoreId, HasteId }));

        // A warrior lord casts each spell once a turn, so nothing is left to cast and the cursor goes back to normal.
        Assert.That(setup.World.HasSelectedAction, Is.False);
    }

    [Test]
    public async Task CastGroupSkipsSpellsThatCantBeCastTest()
    {
        using Setup setup = Create(LordCategory.Warrior);
        (ManagementScreenViewModel screen, SpellbookTabViewModel spellbook, SpellGroupsTabViewModel groups) = OpenSpellbook(setup);
        int before = setup.MovementPoints;

        MakeGroup(spellbook, "Speed", RestoreId, HasteId);
        screen.CloseCommand.Execute(null);

        // Haste was already cast this turn: the group casts Restoration and reports Haste.
        setup.Session.World.DispatchCommand(new WorldCastSpellCommand(HasteId, new WorldSpellTarget(new WorldPosition(30, 0), 0)));
        int afterHaste = setup.MovementPoints;
        Assert.That(afterHaste, Is.EqualTo(before + 10));

        await setup.World.CastSpellGroupAsync([RestoreId, HasteId], new WorldPosition(30, 0));

        Assert.That(setup.MovementPoints, Is.EqualTo(afterHaste + 5));
        Alert alert = Assert.NotNull(setup.World.Game.Alert.Current);
        Assert.That(alert.Caption, Is.EqualTo("1 of 2 spells cast"));
        Assert.That(alert.Details, Does.StartWith("Haste: "));
    }

    [Test]
    public async Task SpellCursorStaysForSecondCastTest()
    {
        // A mage lord may cast each spell twice a turn: after the first cast the cursor stays on the spell.
        using Setup setup = Create(LordCategory.Mage);
        (_, SpellbookTabViewModel spellbook, _) = OpenSpellbook(setup);
        int before = setup.MovementPoints;

        spellbook.SelectSpellCommand.Execute(Item(spellbook, RestoreId));
        await spellbook.ActivateCommand.ExecuteAsync(null);
        await setup.World.HandleMapClickAsync(new WorldPosition(30, 0), null, 1);

        Assert.That(setup.MovementPoints, Is.EqualTo(before + 5));
        Assert.That(setup.World.HasSelectedAction, Is.True);
        Assert.That(setup.World.ActionPromptText, Is.EqualTo("Select a target for the spell on the map."));

        await setup.World.HandleMapClickAsync(new WorldPosition(30, 0), null, 1);

        Assert.That(setup.MovementPoints, Is.EqualTo(before + 10));
        Assert.That(setup.World.HasSelectedAction, Is.False);
        Assert.That(setup.World.Game.Alert.Current, Is.Null);
    }

    [Test]
    public async Task SpellCursorEndsWithEscapeTest()
    {
        using Setup setup = Create(LordCategory.Mage);
        (_, SpellbookTabViewModel spellbook, _) = OpenSpellbook(setup);

        spellbook.SelectSpellCommand.Execute(Item(spellbook, RestoreId));
        await spellbook.ActivateCommand.ExecuteAsync(null);
        await setup.World.HandleMapClickAsync(new WorldPosition(30, 0), null, 1);

        Assert.That(setup.World.HasSelectedAction, Is.True);

        setup.World.CancelSelectedActionCommand.Execute(null);

        Assert.That(setup.World.HasSelectedAction, Is.False);
    }

    [Test]
    public void DeleteGroupTest()
    {
        using Setup setup = Create(LordCategory.Warrior);
        (_, SpellbookTabViewModel spellbook, SpellGroupsTabViewModel groups) = OpenSpellbook(setup);

        MakeGroup(spellbook, "A", RestoreId);
        MakeGroup(spellbook, "B", HasteId);

        Assert.That(groups.Groups.Select(group => group.Name), Is.EqualTo(new[] { "A", "B" }));

        groups.Groups[0].DeleteCommand.Execute(null);

        Assert.That(groups.Groups.Select(group => group.Name), Is.EqualTo(new[] { "B" }));
        Assert.That(SpellGroupStore.Load(WorldRace.Undead).Select(group => group.Name), Is.EqualTo(new[] { "B" }));
        Assert.That(SpellGroupStore.Load(WorldRace.Human), Is.Empty);
    }
}
