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

    private static readonly ItemDefinitionId BannerOfStriking = new("G000IG1003");
    private static readonly UnitModifierDefinitionId BannerBearer = new("g000um4003");

    private static MapEditorListPickerViewModel ItemPicker(GameViewModel game) => Assert.IsOfType<MapEditorListPickerViewModel>(game.TopScreen);

    [Test]
    public async Task LeaderItemsTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);
        BattleSandboxSideViewModel side = screen.Attacker;

        Assert.That(side.CanEquip, Is.True);
        Assert.That(side.EquipmentSlots.Select(slot => slot.Slot), Is.EqualTo(new[]
        {
            LeaderEquipmentSlot.Banner, LeaderEquipmentSlot.Tome, LeaderEquipmentSlot.Battle1, LeaderEquipmentSlot.Battle2,
            LeaderEquipmentSlot.Artifact1, LeaderEquipmentSlot.Artifact2, LeaderEquipmentSlot.Boots
        }));

        // The banner slot lists banners only; the leader can't carry one yet, so it's marked.
        side.EquipmentSlots[0].ClickCommand.Execute(null);
        MapEditorListPickerViewModel picker = ItemPicker(game);
        Assert.That(picker.Options, Is.Not.Empty);
        Assert.That(picker.Options.All(option => option.Group == picker.Options[0].Group), Is.True, "banners only");
        MapEditorListPickerItemViewModel banner = picker.Options.Single(option => Equals(option.Value, BannerOfStriking));
        Assert.That(banner.Warning, Is.Not.Null);
        banner.PickCommand.Execute(null);

        Assert.That(side.EquipmentSlots[0].ItemId, Is.EqualTo(BannerOfStriking));
        Assert.That(side.EquipmentSlots[0].HasProblem, Is.True);
        Assert.That(screen.IsValid, Is.False);
        Assert.That(screen.Problem, Does.StartWith("Attacker"));

        // Level 3 and Banner Bearer make it legal; the upgrade list allows two upgrades at level 3.
        await side.UnitGrid.Slots.First(slot => slot.Position == GridPosition.FrontCenter).SelectCommand.ExecuteAsync(null);
        screen.LevelUpCommand.Execute(null);
        screen.LevelUpCommand.Execute(null);
        side.ChooseUpgradesCommand.Execute(null);
        BattleSandboxUpgradesViewModel upgrades = Assert.IsOfType<BattleSandboxUpgradesViewModel>(game.TopScreen);
        Assert.That(upgrades.MaxUpgrades, Is.EqualTo(2));
        upgrades.Options.Single(option => option.ModifierId == BannerBearer).SelectCommand.Execute(null);
        upgrades.ConfirmCommand.Execute(null);

        Assert.That(side.LeaderUpgrades, Is.EqualTo(new[] { BannerBearer }));
        Assert.That(side.UpgradesText, Is.EqualTo("1 / 2"));
        Assert.That(side.EquipmentSlots[0].HasProblem, Is.False);
        Assert.That(screen.IsValid, Is.True, screen.Problem);
        BattleArmy army = screen.ToArmy(side);
        Assert.That(army.Equipment, Is.EqualTo(new[] { new BattleArmyItem(LeaderEquipmentSlot.Banner, BannerOfStriking) }));
        Assert.That(army.LeaderUpgrades, Is.EqualTo(new[] { BannerBearer }));

        // Saved and loaded with the army, swapped with the sides.
        side.PresetName = "Bannermen";
        side.SavePresetCommand.Execute(null);
        screen.Defender.SelectedPreset = "Bannermen";
        screen.Defender.LoadPresetCommand.Execute(null);
        Assert.That(screen.ToArmy(screen.Defender).Equipment, Is.EqualTo(army.Equipment));
        Assert.That(screen.ToArmy(screen.Defender).LeaderUpgrades, Is.EqualTo(army.LeaderUpgrades));

        // "No item" empties the slot.
        side.EquipmentSlots[0].ClickCommand.Execute(null);
        ItemPicker(game).Options[0].PickCommand.Execute(null);
        Assert.That(side.EquipmentSlots[0].HasItem, Is.False);
        Assert.That(screen.ToArmy(side).Equipment, Is.Empty);
    }

    [Test]
    public async Task UpgradeLimitsTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);
        BattleSandboxSideViewModel side = screen.Attacker;

        // A level 1 leader has no upgrades; every row says why.
        side.ChooseUpgradesCommand.Execute(null);
        BattleSandboxUpgradesViewModel upgrades = Assert.IsOfType<BattleSandboxUpgradesViewModel>(game.TopScreen);
        Assert.That(upgrades.MaxUpgrades, Is.Zero);
        Assert.That(upgrades.Options.All(option => !option.SelectCommand.CanExecute(null) && option.Status.UnavailableReason is not null), Is.True);
        upgrades.CloseCommand.Execute(null);

        // With "Anything goes" every upgrade can be ticked.
        side.AllowsAnything = true;
        side.ChooseUpgradesCommand.Execute(null);
        upgrades = Assert.IsOfType<BattleSandboxUpgradesViewModel>(game.TopScreen);
        upgrades.Options[0].SelectCommand.Execute(null);
        upgrades.Options[1].SelectCommand.Execute(null);
        upgrades.ConfirmCommand.Execute(null);
        Assert.That(side.LeaderUpgrades, Has.Length.EqualTo(2));
        Assert.That(screen.IsValid, Is.True, screen.Problem);

        // Without it, the screen reports too many upgrades.
        side.AllowsAnything = false;
        Assert.That(screen.IsValid, Is.False);
        Assert.That(screen.Problem, Does.Contain("upgrades"));
    }

    [Test]
    public async Task GarrisonHasNoItemsTest()
    {
        (GameViewModel game, BattleSandboxScreenViewModel screen) = await OpenAsync();
        await BuildArmiesAsync(game, screen);
        screen.Defender.Equipment = screen.Defender.Equipment.SetItem(LeaderEquipmentSlot.Banner, BannerOfStriking);

        screen.SetPlaceCommand.Execute(screen.Places.Single(option => option.Value == BattleBenchPlace.City));
        screen.AsGarrison = true;

        Assert.That(screen.Defender.CanEquip, Is.False);
        Assert.That(screen.ToArmy(screen.Defender).Equipment, Is.Empty);
    }
}
