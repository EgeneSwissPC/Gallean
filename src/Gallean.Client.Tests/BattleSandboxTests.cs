using Gallean.Common;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the battle sandbox setup screen (docs/battle-ai-designer.md, phase 3).
[NonParallelizable]
public sealed class BattleSandboxTests
{
    private static readonly UnitDefinitionId PegasusKnight = new("g000uu0019");
    private static readonly UnitDefinitionId Squire = new("g000uu0001");
    private static readonly UnitDefinitionId DeathKnight = new("g000uu0096");
    private static readonly UnitDefinitionId Fighter = new("g000uu0086");

    private string previousDirectory = "";
    private Sandbox? sandbox;

    [SetUp]
    public void SetUp()
    {
        sandbox = Sandbox.Create();
        previousDirectory = BattleSandboxScreenViewModel.ArmiesDirectory;
        BattleSandboxScreenViewModel.ArmiesDirectory = sandbox.GetPath("Battles/armies");
    }

    [TearDown]
    public void TearDown()
    {
        BattleSandboxScreenViewModel.ArmiesDirectory = previousDirectory;
        sandbox?.Dispose();
    }

    private async Task<(GameViewModel Game, BattleSandboxScreenViewModel Screen)> OpenAsync()
    {
        return await OpenAsync(sandbox!);
    }

    // The sandbox reuses the map editor's map store (for its preview game), so it needs a maps folder.
    internal static async Task<(GameViewModel Game, BattleSandboxScreenViewModel Screen)> OpenAsync(Sandbox sb)
    {
        GameViewModel game = new TestGameViewModelBuilder()
            .WithGameLoader(new GameLoader(new RandomProvider(0), sb.GetPath("maps")))
            .Build();
        await game.ShowMainMenuCommand.ExecuteAsync(null);
        await game.ShowBattleSandboxCommand.ExecuteAsync(null);

        return (game, Assert.IsOfType<BattleSandboxScreenViewModel>(game.RootScreen));
    }

    private static MapEditorUnitPickerViewModel Picker(GameViewModel game) => Assert.IsOfType<MapEditorUnitPickerViewModel>(game.TopScreen);

    // Picks a unit for an empty slot through the same unit list the player uses.
    internal static async Task AddAsync(GameViewModel game, BattleSandboxSideViewModel side, GridPosition position, UnitDefinitionId unit)
    {
        await side.UnitGrid.Slots.First(slot => slot.Position == position).SelectCommand.ExecuteAsync(null);
        MapEditorUnitPickerViewModel picker = Picker(game);
        picker.SelectedFilter = picker.Filters.Single(filter => filter.Race is null);
        picker.Options.Single(option => option.Definition.Id == unit).PickCommand.Execute(null);
    }

    internal static async Task BuildArmiesAsync(GameViewModel game, BattleSandboxScreenViewModel screen)
    {
        // The first unit of an army is its leader, placed front center like in the map editor.
        await AddAsync(game, screen.Attacker, GridPosition.BackCenter, PegasusKnight);
        await AddAsync(game, screen.Attacker, GridPosition.FrontBottom, Squire);
        await AddAsync(game, screen.Attacker, GridPosition.BackBottom, Squire);
        await AddAsync(game, screen.Defender, GridPosition.BackCenter, DeathKnight);
        await AddAsync(game, screen.Defender, GridPosition.FrontBottom, Fighter);
    }

    [Test]
    public async Task OpensFromTheMainMenuTest()
    {
        (_, BattleSandboxScreenViewModel screen) = await OpenAsync();

        Assert.That(screen.Attacker.Owner, Is.EqualTo(WorldRace.Human));
        Assert.That(screen.Defender.Owner, Is.EqualTo(WorldRace.Undead));
        Assert.That(screen.Attacker.Owners.Select(owner => owner.Race), Is.EqualTo(BattleSandboxScreenViewModel.OwnerRaces));
        Assert.That(screen.Attacker.Owners.Single(owner => owner.IsSelected).Race, Is.EqualTo(WorldRace.Human));
        Assert.That(screen.Attacker.Ais.Select(option => option.Value), Is.EqualTo(AiBrainRegistry.BattleBrains.Select(info => info.Id)));
        Assert.That(screen.SelectedDifficulty.Value, Is.EqualTo(WorldDifficulty.Average));
        Assert.That(screen.Place, Is.EqualTo(BattleBenchPlace.Open));
        Assert.That(screen.IsValid, Is.False);
        Assert.That(screen.Problem, Is.Not.Null.And.Not.Empty, "an empty army is reported");
    }

    [Test]
    public async Task BuildArmiesTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();

        // The first click on an empty army asks for its leader (game rules), like the map editor.
        await screen.Attacker.UnitGrid.Slots.First(slot => slot.Position == GridPosition.FrontTop).SelectCommand.ExecuteAsync(null);
        Assert.That(Picker(game).Options.All(option => option.Definition.Leadership > 0), Is.True);
        Picker(game).CloseCommand.Execute(null);

        await BuildArmiesAsync(game, screen);

        Assert.That(screen.IsValid, Is.True, screen.Problem);
        BattleArmy attacker = screen.ToArmy(screen.Attacker);
        Assert.That(attacker.Units.Select(unit => (unit.TypeId, unit.Position)), Is.EquivalentTo(new[]
        {
            (PegasusKnight, GridPosition.FrontCenter), (Squire, GridPosition.FrontBottom), (Squire, GridPosition.BackBottom)
        }));
        Assert.That(screen.ToSide(screen.Defender).Race, Is.EqualTo(WorldRace.Undead));

        // Levels go through the shared level bar.
        await screen.Attacker.UnitGrid.Slots.First(slot => slot.Position == GridPosition.FrontCenter).SelectCommand.ExecuteAsync(null);
        screen.LevelUpTenCommand.Execute(null);
        Assert.That(screen.ToArmy(screen.Attacker).Units.Single(unit => unit.TypeId == PegasusKnight).Level, Is.EqualTo(11));
    }

    [Test]
    public async Task AnythingGoesTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);

        // A Pegasus Knight leads 4: a third soldier fits, a fourth breaks the rule and the list says why.
        await AddAsync(game, screen.Attacker, GridPosition.FrontTop, Squire);
        await screen.Attacker.UnitGrid.Slots.First(slot => slot.Position == GridPosition.BackTop).SelectCommand.ExecuteAsync(null);
        Picker(game).SelectedFilter = Picker(game).Filters.Single(filter => filter.Race is null);
        Assert.That(Picker(game).Options.Single(option => option.Definition.Id == Squire).UnavailableReason, Is.Not.Null);
        Picker(game).CloseCommand.Execute(null);

        // With "Anything goes" it is allowed, and so is an army without a leader.
        screen.Attacker.AllowsAnything = true;
        await AddAsync(game, screen.Attacker, GridPosition.BackTop, Squire);
        Assert.That(screen.IsValid, Is.True, screen.Problem);
        Assert.That(screen.ToArmy(screen.Attacker).AllowAnyArmy, Is.True);
        Assert.That(screen.ToArmy(screen.Attacker).Units, Has.Length.EqualTo(5));

        // A new army with "Anything goes" doesn't need a leader first.
        screen.Defender.AllowsAnything = true;
        await AddAsync(game, screen.Defender, GridPosition.BackTop, Squire);
        Assert.That(screen.ToArmy(screen.Defender).Units.Select(unit => unit.Position), Does.Contain(GridPosition.BackTop));

        screen.Attacker.AllowsAnything = false;
        Assert.That(screen.IsValid, Is.False);
        Assert.That(screen.Attacker.Group.Problem, Is.Not.Null);
    }

    [Test]
    public async Task BattlefieldTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);

        screen.SetPlaceCommand.Execute(screen.Places.Single(option => option.Value == BattleBenchPlace.City));
        Assert.That(screen.PlaceImageKey, Is.EqualTo(new AssetSpriteKey.CityImage(1)));
        screen.CityLevelUpCommand.Execute(null);
        screen.CityLevelUpCommand.Execute(null);
        Assert.That(screen.Setup, Is.EqualTo(new BattleBenchSetup(BattleBenchPlace.City, 3) { Difficulty = WorldDifficulty.Average }));
        Assert.That(screen.IsValid, Is.True, screen.Problem);

        // As the garrison, the defender may not keep a leader.
        screen.AsGarrison = true;
        Assert.That(screen.Defender.Group.Kind, Is.EqualTo(MapEditorUnitGroupKind.Garrison));
        Assert.That(screen.IsValid, Is.False);

        // A capital garrison gets the race's guardian, like in the game; the guardian leaves with the garrison.
        screen.SetPlaceCommand.Execute(screen.Places.Single(option => option.Value == BattleBenchPlace.Capital));
        Assert.That(screen.Defender.Group.Kind, Is.EqualTo(MapEditorUnitGroupKind.CapitalGarrison));
        Assert.That(screen.Defender.Group.Slots.Count(unit => unit is { IsGuardian: true }), Is.EqualTo(1));
        Assert.That(screen.PlaceImageKey, Is.EqualTo(new AssetSpriteKey.CapitalImage(WorldRace.Undead)));
        screen.AsGarrison = false;
        Assert.That(screen.Defender.Group.Slots.Count(unit => unit is { IsGuardian: true }), Is.Zero);
        Assert.That(screen.IsValid, Is.True, screen.Problem);

        // Neutrals have no capital.
        screen.Defender.Owners.Single(owner => owner.Race == WorldRace.Neutral).SelectCommand.Execute(null);
        Assert.That(screen.IsValid, Is.False);
        Assert.That(screen.Problem, Does.Contain("capital"));
        screen.AsGarrison = true;
        Assert.That(screen.Problem, Does.Contain("capital"), "the capital problem comes before the garrison's own");
        screen.AsGarrison = false;

        // Elves are owners too (the generated host map has every race).
        screen.Defender.Owners.Single(owner => owner.Race == WorldRace.Elf).SelectCommand.Execute(null);
        Assert.That(screen.IsValid, Is.True, screen.Problem);

        screen.SelectedDifficulty = screen.Difficulties.Single(option => option.Value == WorldDifficulty.VeryHard);
        Assert.That(screen.Setup.Difficulty, Is.EqualTo(WorldDifficulty.VeryHard));
    }

    [Test]
    public async Task SwapSidesTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);
        screen.Attacker.SelectedAi = screen.Attacker.Ais.Single(option => option.Value == "ruthless");

        screen.SwapSidesCommand.Execute(null);

        Assert.That(screen.Attacker.Owner, Is.EqualTo(WorldRace.Undead));
        Assert.That(screen.Defender.Owner, Is.EqualTo(WorldRace.Human));
        Assert.That(screen.Defender.AiId, Is.EqualTo("ruthless"));
        Assert.That(screen.ToArmy(screen.Attacker).Units.Select(unit => unit.TypeId), Does.Contain(DeathKnight));
        Assert.That(screen.ToArmy(screen.Defender).Units.Select(unit => unit.TypeId), Does.Contain(PegasusKnight));
        Assert.That(screen.Attacker.Owners.Single(owner => owner.IsSelected).Race, Is.EqualTo(WorldRace.Undead));
        Assert.That(screen.IsValid, Is.True, screen.Problem);
    }

    [Test]
    public async Task PresetsTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);

        screen.Attacker.PresetName = "Empire raiders";
        screen.Attacker.SavePresetCommand.Execute(null);
        Assert.That(screen.Presets, Is.EqualTo(new[] { "Empire raiders" }));
        Assert.That(File.Exists(Path.Combine(BattleSandboxScreenViewModel.ArmiesDirectory, "Empire raiders.json")), Is.True);

        // Load it on the other side: the same units in the same slots.
        screen.Defender.SelectedPreset = "Empire raiders";
        screen.Defender.LoadPresetCommand.Execute(null);
        Assert.That(screen.ToArmy(screen.Defender).Units, Is.EqualTo(screen.ToArmy(screen.Attacker).Units));
        Assert.That(screen.Defender.PresetName, Is.EqualTo("Empire raiders"));

        // A new screen lists it too; Delete removes the file.
        (_, BattleSandboxScreenViewModel again) = await OpenAsync();
        Assert.That(again.Presets, Is.EqualTo(new[] { "Empire raiders" }));
        again.Attacker.SelectedPreset = "Empire raiders";
        again.Attacker.DeletePresetCommand.Execute(null);
        Assert.That(again.Presets, Is.Empty);
        Assert.That(Directory.GetFiles(BattleSandboxScreenViewModel.ArmiesDirectory), Is.Empty);
    }
}
