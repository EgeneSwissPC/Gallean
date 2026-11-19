using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

public sealed class MapEditorScreenViewModelTests
{
    private static (GameViewModel Game, GameLoader Loader) Build(Sandbox sb)
    {
        GameLoader loader = new(new RandomProvider(0), sb.GetPath("maps"));
        GameViewModel game = new TestGameViewModelBuilder()
            .WithGameLoader(loader)
            .WithSaveStore(new GameSaveStore(sb.GetPath("saves"), 10))
            .Build();

        return (game, loader);
    }

    private static async Task<MapEditorScreenViewModel> CreateMapAsync(GameViewModel game, params WorldRace[] races)
    {
        game.ShowMapEditorCommand.Execute(null);
        MapEditorMenuScreenViewModel menu = Assert.IsOfType<MapEditorMenuScreenViewModel>(game.RootScreen);

        menu.Name = "Two kingdoms";
        menu.SelectedSize = menu.Sizes.Single(size => size.Value == 48);

        foreach (MapEditorRaceOption race in menu.Races)
        {
            race.IsSelected = races.Contains(race.Race);
        }

        await menu.CreateCommand.ExecuteAsync(null);

        return Assert.IsOfType<MapEditorScreenViewModel>(game.RootScreen);
    }

    [Test]
    public async Task CreateTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, _) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf);

        Assert.That(editor.Map.Id, Is.EqualTo("S600SC0000"));
        Assert.That(editor.Map.Size, Is.EqualTo(48));
        Assert.That(editor.State.Map.Width, Is.EqualTo(48));
        Assert.That(editor.CapitalRaces.Select(race => race.Value), Is.EqualTo(new[] { WorldRace.Human, WorldRace.Dwarf }));
        Assert.That(editor.HasProblems, Is.True, "Capitals are missing.");
        Assert.That(editor.RenderRequest.FogEnabled, Is.False);
    }

    [Test]
    public async Task PaintAndUndoTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, _) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf);
        WorldRenderRequest initial = editor.RenderRequest;

        editor.SelectTool(MapEditorTool.Water);
        editor.BrushRadius = 0;
        editor.BeginStroke(new WorldPosition(10, 10));
        editor.ContinueStroke(new WorldPosition(11, 10));
        editor.ContinueStroke(new WorldPosition(12, 10));
        editor.EndStroke();

        Assert.That(editor.RenderRequest, Is.Not.SameAs(initial));
        Assert.That(editor.State.Map.GetCellAt(new WorldPosition(11, 10)).Terrain, Is.EqualTo(WorldMapTerrain.Water));
        Assert.That(editor.IsDirty, Is.True);
        Assert.That(editor.CanUndo, Is.True);

        editor.SelectTool(MapEditorTool.Mountain);
        editor.BrushRadius = 1;
        editor.BeginStroke(new WorldPosition(30, 30));
        editor.EndStroke();

        Assert.That(editor.State.Mountains, Has.Count.EqualTo(1));

        editor.UndoCommand.Execute(null);

        Assert.That(editor.State.Mountains, Is.Empty);
        Assert.That(editor.State.Map.GetCellAt(new WorldPosition(11, 10)).Terrain, Is.EqualTo(WorldMapTerrain.Water), "Only the last stroke is undone.");

        editor.UndoCommand.Execute(null);

        Assert.That(editor.State.Map.GetCellAt(new WorldPosition(11, 10)).Terrain, Is.EqualTo(WorldMapTerrain.Neutral));
        Assert.That(editor.CanUndo, Is.False);
    }

    [Test]
    public async Task SaveAndPlayTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf);

        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.List(), Is.Empty, "Cannot save without capitals.");

        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(8, 8));
        editor.SelectedCapitalRace = editor.CapitalRaces[1];
        editor.BeginStroke(new WorldPosition(38, 38));

        Assert.That(editor.State.Fortifications.Values.OfType<WorldCapital>().Count(), Is.EqualTo(2));
        Assert.That(editor.HasProblems, Is.False);

        editor.Name = "Two kingdoms, final";
        editor.SaveCommand.Execute(null);

        Assert.That(editor.IsDirty, Is.False);
        Assert.That(loader.CreateMapStore()!.List().Select(info => info.Name), Is.EqualTo(new[] { "Two kingdoms, final" }));

        (IReadOnlyList<GameCampaign> campaigns, _, _) = loader.LoadCampaigns(Language.English);
        GameScenario scenario = campaigns.Single(campaign => campaign.IsSkirmish).Scenarios.Single(candidate => candidate.Id == "S600SC0000");
        Assert.That(scenario.Name, Is.EqualTo("Two kingdoms, final"));
        Assert.That(scenario.MapSize, Is.EqualTo(48));

        editor.PlayCommand.Execute(null);

        BriefingScreenViewModel briefing = Assert.IsOfType<BriefingScreenViewModel>(game.RootScreen);
        Assert.That(briefing, Is.Not.Null);
    }

    [Test]
    public async Task ReopenTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, _) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Elf);
        editor.SelectTool(MapEditorTool.Forest);
        editor.BeginStroke(new WorldPosition(20, 20));
        editor.EndStroke();
        editor.SelectTool(MapEditorTool.Capital);
        editor.BeginStroke(new WorldPosition(5, 5));
        editor.SaveCommand.Execute(null);
        editor.BackCommand.Execute(null);

        MapEditorMenuScreenViewModel menu = Assert.IsOfType<MapEditorMenuScreenViewModel>(game.RootScreen);
        Assert.That(menu.Maps.Select(map => map.Name), Is.EqualTo(new[] { "Two kingdoms" }));

        await menu.OpenCommand.ExecuteAsync(null);

        MapEditorScreenViewModel reopened = Assert.IsOfType<MapEditorScreenViewModel>(game.RootScreen);
        Assert.That(reopened.Map.Cells, Is.EqualTo(editor.Map.Cells));
        Assert.That(reopened.Map.GetCell(new WorldPosition(20, 20)).Feature, Is.EqualTo(WorldMapFeature.Forest));
        Assert.That(reopened.IsDirty, Is.False);
    }

    private static MapEditorUnitPickerViewModel Picker(GameViewModel game)
    {
        return Assert.IsOfType<MapEditorUnitPickerViewModel>(game.TopScreen);
    }

    [Test]
    public async Task ArmyTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf);
        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(8, 8));
        editor.SelectedCapitalRace = editor.CapitalRaces[1];
        editor.BeginStroke(new WorldPosition(38, 38));

        // Clicking water does not open the army screen.
        editor.SelectTool(MapEditorTool.Army);
        editor.SelectedArmyOwner = editor.ArmyOwners.Single(owner => owner.Value == WorldRace.Neutral);
        editor.BeginStroke(new WorldPosition(9, 9));
        Assert.IsOfType<MapEditorCapitalScreenViewModel>(game.TopScreen).CloseCommand.Execute(null);
        Assert.That(game.TopScreen, Is.SameAs(editor));

        // Free land opens an empty army; its first empty slot asks for a leader.
        editor.BeginStroke(new WorldPosition(24, 24));
        MapEditorArmyScreenViewModel army = Assert.IsOfType<MapEditorArmyScreenViewModel>(game.TopScreen);
        Assert.That(army.IsNew, Is.True);
        Assert.That(army.HasLeader, Is.False);
        Assert.That(army.ConfirmCommand.CanExecute(null), Is.False);

        await army.UnitGrid.Slots.First(slot => slot.Position == GridPosition.BackTop).SelectCommand.ExecuteAsync(null);
        MapEditorUnitPickerViewModel leaders = Picker(game);
        Assert.That(leaders.SelectedFilter.Race, Is.EqualTo(WorldRace.Neutral), "Starts with the owner's race.");
        Assert.That(leaders.Options.All(option => option.Definition.Leadership > 0 && option.Definition.Race == WorldRace.Neutral), Is.True);
        leaders.SelectedFilter = leaders.Filters.Single(filter => filter.Race is null);
        Assert.That(leaders.Options.Select(option => option.Definition.Race).Distinct().Count(), Is.GreaterThan(3), "Any leader can be chosen.");
        leaders.SelectedFilter = leaders.Filters.Single(filter => filter.Race == WorldRace.Neutral);
        MapEditorUnitPickerItemViewModel leader = leaders.Options.First(option => !option.Definition.IsLarge && option.Definition.Leadership >= 4);
        Assert.That(leader.Details, Is.Not.Null, "Right-click shows the unit details.");
        leader.PickCommand.Execute(null);

        Assert.That(game.TopScreen, Is.SameAs(army));
        Assert.That(army.HasLeader, Is.True);
        Assert.That(army.Slots[GridPosition.FrontCenter.ToIndex()]!.TypeId, Is.EqualTo(leader.Definition.Id));
        Assert.That(army.ConfirmCommand.CanExecute(null), Is.True);

        // Units: a large one takes its whole row; a full row cannot take another.
        await army.UnitGrid.Slots.First(slot => slot.Position == GridPosition.BackTop).SelectCommand.ExecuteAsync(null);
        MapEditorUnitPickerViewModel soldiers = Picker(game);
        Assert.That(soldiers.Options.All(option => option.Definition.Leadership is not > 0), Is.True);
        soldiers.Options.First(option => option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        Assert.That(army.Slots[GridPosition.FrontTop.ToIndex()], Is.Not.Null.And.Property(nameof(CustomMapUnit.IsLarge)).True);

        army.AddUnit(GridPosition.FrontCenter);
        Assert.That(Picker(game).Options.All(option => option.IsUnavailable), Is.True, "The slot is taken.");
        Picker(game).CloseCommand.Execute(null);

        army.AddUnit(GridPosition.BackBottom);
        Picker(game).Options.First(option => !option.Definition.IsLarge).PickCommand.Execute(null);
        Assert.That(army.Slots[GridPosition.BackBottom.ToIndex()], Is.Not.Null);

        // Rearranging works like in the game: the large unit swaps rows.
        army.MoveUnit(GridPosition.BackTop, GridPosition.FrontBottom);
        Assert.That(army.Slots[GridPosition.FrontBottom.ToIndex()], Is.Not.Null.And.Property(nameof(CustomMapUnit.IsLarge)).True);
        Assert.That(army.Slots[GridPosition.BackTop.ToIndex()], Is.Not.Null.And.Property(nameof(CustomMapUnit.IsLarge)).False);
        army.RemoveUnit(GridPosition.BackTop);
        Assert.That(army.Slots.Count(unit => unit is not null), Is.EqualTo(2));

        army.ConfirmCommand.Execute(null);
        Assert.That(game.TopScreen, Is.SameAs(editor));
        CustomMapStack placed = editor.Map.Stacks.Single();
        Assert.That(placed.Position, Is.EqualTo(new WorldPosition(24, 24)));
        Assert.That(placed.Owner, Is.EqualTo(WorldRace.Neutral));

        // Right-click shows the game's army info.
        editor.ShowPositionInfo(new WorldPosition(24, 24));
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldStackInfoViewModel>());
        editor.HidePositionInfo();
        Assert.That(editor.PositionInfo, Is.Null);
        editor.ShowPositionInfo(new WorldPosition(9, 9));
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldFortificationInfoViewModel>());
        editor.HidePositionInfo();

        // Clicking the army opens it again; change the leader and save.
        editor.BeginStroke(new WorldPosition(24, 24));
        army = Assert.IsOfType<MapEditorArmyScreenViewModel>(game.TopScreen);
        Assert.That(army.IsNew, Is.False);
        Assert.That(army.Slots, Is.EqualTo(placed.Slots));
        army.ChooseLeaderCommand.Execute(null);
        Picker(game).SelectedFilter = Picker(game).Filters.Single(filter => filter.Race == WorldRace.Dwarf);
        MapEditorUnitPickerItemViewModel dwarf = Picker(game).Options.First(option => !option.IsUnavailable);
        dwarf.PickCommand.Execute(null);
        Assert.That(army.Slots.Count(unit => unit is { IsLeader: true }), Is.EqualTo(1));
        Assert.That(army.Slots.Single(unit => unit is { IsLeader: true })!.TypeId, Is.EqualTo(dwarf.Definition.Id));

        if (army.ConfirmCommand.CanExecute(null))
        {
            army.ConfirmCommand.Execute(null);
            Assert.That(editor.Map.Stacks.Single().Leader.TypeId, Is.EqualTo(dwarf.Definition.Id));
        }
        else
        {
            Assert.That(army.Problem, Is.Not.Null, "A weaker leader may not lead the large unit.");
            army.CloseCommand.Execute(null);
        }

        editor.SaveCommand.Execute(null);
        Assert.That(editor.IsDirty, Is.False);
        WorldState saved = loader.CreateMapStore()!.BuildPreview(loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions()), loader.CreateDefinitions()).State;
        Assert.That(saved.Stacks.Values.Count(stack => stack.Position == new WorldPosition(24, 24)), Is.EqualTo(1));

        // Delete from the army screen, then undo.
        editor.BeginStroke(new WorldPosition(24, 24));
        Assert.IsOfType<MapEditorArmyScreenViewModel>(game.TopScreen).DeleteCommand.Execute(null);
        Assert.That(editor.Map.Stacks, Is.Empty);
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Stacks, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ResourceTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf);
        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(8, 8));
        editor.SelectedCapitalRace = editor.CapitalRaces[1];
        editor.BeginStroke(new WorldPosition(38, 38));

        editor.SelectTool(MapEditorTool.Resource);
        Assert.That(editor.IsResourceTool, Is.True);
        Assert.That(editor.HasBrush, Is.False);
        Assert.That(editor.ResourceTypes.Select(type => type.Value), Is.EquivalentTo(Enum.GetValues<WorldResourceType>()));

        // Click land: a gold mine; click it again: gone; another type changes it.
        editor.SelectedResourceType = editor.ResourceTypes.Single(type => type.Value == WorldResourceType.Gold);
        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.That(editor.Map.Resources.Single().Type, Is.EqualTo(WorldResourceType.Gold));
        Assert.That(editor.State.ResourceNodes.Values.Single().Position, Is.EqualTo(new WorldPosition(20, 20)));
        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.That(editor.Map.Resources, Is.Empty);
        Assert.That(editor.State.ResourceNodes, Is.Empty);
        editor.BeginStroke(new WorldPosition(20, 20));
        editor.SelectedResourceType = editor.ResourceTypes.Single(type => type.Value == WorldResourceType.LifeMana);
        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.That(editor.Map.Resources.Single().Type, Is.EqualTo(WorldResourceType.LifeMana));

        // Capitals are blocked; the hover line names the resource.
        editor.BeginStroke(new WorldPosition(9, 9));
        Assert.That(editor.Map.Resources, Has.Count.EqualTo(1));
        editor.UpdateHover(new WorldPosition(20, 20));
        Assert.That(editor.HoverText, Does.Contain("Life mana crystal"));

        // Right-click shows the game's resource info.
        editor.ShowPositionInfo(new WorldPosition(20, 20));
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldTileInfoViewModel>());
        editor.HidePositionInfo();

        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Resources.Single().Type, Is.EqualTo(WorldResourceType.Gold));
        Assert.That(editor.State.ResourceNodes.Values.Single().ResourceType, Is.EqualTo(WorldResourceType.Gold));

        editor.SaveCommand.Execute(null);
        Assert.That(editor.IsDirty, Is.False);
        CustomMap saved = loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions());
        Assert.That(saved.Resources.Select(resource => (resource.Position, resource.Type)), Is.EqualTo(new[] { (new WorldPosition(20, 20), WorldResourceType.Gold) }));
    }

    [Test]
    public async Task VillageTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf);
        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(8, 8));
        editor.SelectedCapitalRace = editor.CapitalRaces[1];
        editor.BeginStroke(new WorldPosition(38, 38));

        // Click free land: a neutral level 2 village centred on the tile, named from the game's city names.
        editor.SelectTool(MapEditorTool.Village);
        Assert.That(editor.IsVillageTool, Is.True);
        Assert.That(editor.VillageOwners.Select(owner => owner.Value), Is.EqualTo(new[] { WorldRace.Neutral, WorldRace.Human, WorldRace.Dwarf }));
        editor.SelectedVillageOwner = editor.VillageOwners[0];
        editor.SelectedVillageLevel = editor.VillageLevels.Single(level => level.Value == 2);
        editor.BeginStroke(new WorldPosition(24, 24));
        CustomMapVillage village = editor.Map.Villages.Single();
        Assert.That((village.TopLeft, village.Level, village.Owner), Is.EqualTo((new WorldPosition(22, 22), 2, WorldRace.Neutral)));
        Assert.That(village.Name, Is.Not.Empty);
        Assert.That(MapEditorNames.Load().Villages.Select(name => name.Name), Does.Contain(village.Name));
        Assert.That(editor.State.Fortifications.Values.OfType<WorldCity>().Single().Footprint.TopLeft, Is.EqualTo(new WorldPosition(22, 22)));
        Assert.That(game.TopScreen, Is.SameAs(editor), "Placing does not open the village screen.");

        // Overlapping another village or a capital is refused.
        editor.BeginStroke(new WorldPosition(10, 12));
        Assert.That(editor.Map.Villages, Has.Count.EqualTo(1));

        // Clicking the village opens it: garrison of level-many units, no leaders.
        editor.BeginStroke(new WorldPosition(25, 25));
        MapEditorVillageScreenViewModel screen = Assert.IsOfType<MapEditorVillageScreenViewModel>(game.TopScreen);
        Assert.That(screen.Name, Is.EqualTo(village.Name));
        Assert.That(screen.Capacity, Is.EqualTo("0 / 2"));
        Assert.That(screen.ConfirmCommand.CanExecute(null), Is.True, "An empty garrison is fine.");
        screen.SelectedLevel = screen.Levels.Single(level => level.Value == 3);

        await screen.UnitGrid.Slots.First(slot => slot.Position == GridPosition.FrontCenter).SelectCommand.ExecuteAsync(null);
        MapEditorUnitPickerViewModel soldiers = Picker(game);
        Assert.That(soldiers.SelectedFilter.Race, Is.EqualTo(WorldRace.Neutral));
        Assert.That(soldiers.Options.All(option => option.Definition.Leadership is not > 0), Is.True, "Garrisons take no leaders.");
        soldiers.Options.First(option => !option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        screen.AddUnit(GridPosition.FrontTop);
        Picker(game).Options.First(option => option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        Assert.That(screen.Capacity, Is.EqualTo("3 / 3"));
        screen.AddUnit(GridPosition.BackBottom);
        Assert.That(Picker(game).Options.All(option => option.IsUnavailable), Is.True, "The garrison is full.");
        Picker(game).CloseCommand.Execute(null);

        // Lowering the level below the garrison is flagged.
        screen.SelectedLevel = screen.Levels.Single(level => level.Value == 2);
        Assert.That(screen.Problem, Is.Not.Null);
        Assert.That(screen.ConfirmCommand.CanExecute(null), Is.False);
        screen.SelectedLevel = screen.Levels.Single(level => level.Value == 3);
        Assert.That(screen.ConfirmCommand.CanExecute(null), Is.True);

        screen.Name = "Ford";
        screen.SelectedOwner = screen.Owners.Single(owner => owner.Value == WorldRace.Human);
        screen.ConfirmCommand.Execute(null);
        Assert.That(game.TopScreen, Is.SameAs(editor));
        village = editor.Map.Villages.Single();
        Assert.That((village.Name, village.Level, village.Owner, village.Garrison.Count(unit => unit is not null)), Is.EqualTo(("Ford", 3, WorldRace.Human, 2)));
        WorldCity city = editor.State.Fortifications.Values.OfType<WorldCity>().Single();
        Assert.That(editor.State.Players[city.OwnerId].Race, Is.EqualTo(WorldRace.Human));
        Assert.That(editor.Map.GetCell(new WorldPosition(23, 23)).Terrain, Is.EqualTo(WorldMapTerrain.Human), "The owner's land.");

        // Hover and right-click show the village.
        editor.UpdateHover(new WorldPosition(23, 23));
        Assert.That(editor.HoverText, Does.Contain("village Ford"));
        editor.ShowPositionInfo(new WorldPosition(23, 23));
        WorldFortificationInfoViewModel info = Assert.IsOfType<WorldFortificationInfoViewModel>(editor.PositionInfo);
        Assert.That(info.Title, Does.Contain("Ford"));
        editor.HidePositionInfo();

        editor.SaveCommand.Execute(null);
        CustomMap saved = loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions());
        Assert.That(saved.Villages, Is.EqualTo(editor.Map.Villages));

        // Delete from the village screen, then undo.
        editor.BeginStroke(new WorldPosition(23, 23));
        Assert.IsOfType<MapEditorVillageScreenViewModel>(game.TopScreen).DeleteCommand.Execute(null);
        Assert.That(editor.Map.Villages, Is.Empty);
        Assert.That(editor.State.Fortifications.Values.OfType<WorldCity>(), Is.Empty);
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Villages.Single().Name, Is.EqualTo("Ford"));

        // The Army tool on a village opens the village screen too: garrison and stationed army side by side. The
        // army serves the village's owner.
        editor.SelectTool(MapEditorTool.Army);
        editor.SelectedArmyOwner = editor.ArmyOwners.Single(owner => owner.Value == WorldRace.Neutral);
        editor.BeginStroke(new WorldPosition(23, 23));
        MapEditorVillageScreenViewModel withArmy = Assert.IsOfType<MapEditorVillageScreenViewModel>(game.TopScreen);
        Assert.That((withArmy.HasArmy, withArmy.Army.IsEmpty, withArmy.HasSelectedUnit), Is.EqualTo((false, true, false)));
        withArmy.AddUnit(withArmy.Army, GridPosition.BackCenter);
        MapEditorUnitPickerViewModel leaders = Picker(game);
        Assert.That(leaders.SelectedFilter.Race, Is.EqualTo(WorldRace.Human));
        MapEditorUnitPickerItemViewModel leader = leaders.Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge && option.Definition.Leadership >= 3);
        leader.PickCommand.Execute(null);
        Assert.That(withArmy.HasArmy, Is.True);
        Assert.That(withArmy.Army.Slots[GridPosition.FrontCenter.ToIndex()]!.IsLeader, Is.True);
        Assert.That(withArmy.SelectedUnitName, Is.Not.Null, "The new leader is selected.");
        Assert.That(withArmy.LevelDownCommand.CanExecute(null), Is.False, "Not below its own level.");

        // Raise the leader's level; hit points grow with it.
        withArmy.LevelUpTenCommand.Execute(null);
        withArmy.LevelUpCommand.Execute(null);
        CustomMapUnit levelled = withArmy.Army.Slots[GridPosition.FrontCenter.ToIndex()]!;
        Assert.That(levelled.Level, Is.EqualTo(leader.Definition.Level + 11));
        Assert.That(levelled.Hitpoints, Is.GreaterThan(leader.Definition.MaxHitpoints));
        Assert.That(withArmy.SelectedUnitLevel, Is.EqualTo($"Level {levelled.Level}"));

        // Units move between the garrison and the army; leaders stay out of the garrison.
        Assert.That(withArmy.MoveUnit(withArmy.Garrison, GridPosition.FrontCenter, withArmy.Army, GridPosition.FrontBottom), Is.True);
        Assert.That(withArmy.Garrison.Slots.Count(unit => unit is not null), Is.EqualTo(1));
        Assert.That(withArmy.MoveUnit(withArmy.Army, GridPosition.FrontCenter, withArmy.Garrison, GridPosition.BackBottom), Is.False);
        withArmy.ConfirmCommand.Execute(null);
        Assert.That(game.TopScreen, Is.SameAs(editor));
        CustomMapStack stationed = editor.Map.Stacks.Single();
        Assert.That((stationed.Position, stationed.Owner, stationed.Slots.Count(unit => unit is not null)), Is.EqualTo((new WorldPosition(25, 25), WorldRace.Human, 2)));
        Assert.That(editor.Map.Villages.Single().Garrison.Count(unit => unit is not null), Is.EqualTo(1));
        WorldCity cityWithArmy = editor.State.Fortifications.Values.OfType<WorldCity>().Single();
        WorldStack stationedState = editor.State.Stacks.Values.Single(stack => stack.Position == cityWithArmy.Entrance);
        Assert.That(stationedState.Units.GetUnits().Max(entry => entry.Unit.Level), Is.EqualTo(levelled.Level));
        editor.ShowPositionInfo(new WorldPosition(23, 23));
        Assert.That(Assert.IsOfType<WorldFortificationInfoViewModel>(editor.PositionInfo).VisitingStack, Is.Not.Null, "The city info shows the stationed army.");
        editor.HidePositionInfo();

        // Removing the army on the village screen takes it off the map.
        editor.SelectTool(MapEditorTool.Village);
        editor.BeginStroke(new WorldPosition(23, 23));
        MapEditorVillageScreenViewModel again = Assert.IsOfType<MapEditorVillageScreenViewModel>(game.TopScreen);
        Assert.That(again.Army.Slots, Is.EqualTo(stationed.Slots));
        again.RemoveArmyCommand.Execute(null);
        again.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Stacks, Is.Empty);
    }

    [Test]
    public async Task CapitalScreenTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        Assert.That(editor.Map.Players[0].Capital, Is.EqualTo(new WorldPosition(6, 6)));

        // Clicking a capital with the Capital tool opens it instead of moving it.
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(7, 7));
        MapEditorCapitalScreenViewModel screen = Assert.IsOfType<MapEditorCapitalScreenViewModel>(game.TopScreen);
        Assert.That(editor.Map.Players[0].Capital, Is.EqualTo(new WorldPosition(6, 6)));
        int guardianIndex = Array.FindIndex(screen.Garrison.Slots, unit => unit is { IsGuardian: true });
        Assert.That(guardianIndex, Is.Not.Negative, "The race's guardian is in the garrison.");
        Assert.That(screen.Garrison.CapacityText, Is.EqualTo("1 / 6"));
        Assert.That(screen.Army.HasLeader, Is.True, "The race's first leader waits in the capital.");

        // The guardian can be neither removed nor moved to the army.
        GridPosition guardianPosition = GridPosition.FromIndex(guardianIndex);
        screen.RemoveUnit(screen.Garrison, guardianPosition);
        Assert.That(screen.Garrison.Slots[guardianIndex], Is.Not.Null);
        Assert.That(screen.MoveUnit(screen.Garrison, guardianPosition, screen.Army, GridPosition.BackTop), Is.False);

        // A soldier joins the garrison at level 7, another the army.
        screen.AddUnit(screen.Garrison, GridPosition.BackTop);
        MapEditorUnitPickerViewModel soldiers = Picker(game);
        Assert.That(soldiers.Options.All(option => option.Definition.Leadership is not > 0), Is.True);
        soldiers.Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge && option.Definition.Level <= 7).PickCommand.Execute(null);
        screen.SetSelectedLevel(7);
        Assert.That(screen.Garrison.Slots[GridPosition.BackTop.ToIndex()]!.Level, Is.EqualTo(7));
        screen.AddUnit(screen.Army, GridPosition.BackCenter);
        Picker(game).Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge).PickCommand.Execute(null);
        screen.ConfirmCommand.Execute(null);
        Assert.That(game.TopScreen, Is.SameAs(editor));

        CustomMapPlayer human = editor.Map.Players.Single(player => player.Race == WorldRace.Human);
        Assert.That(human.Garrison.Count(unit => unit is not null), Is.EqualTo(2));
        CustomMapStack army = editor.Map.Stacks.Single();
        Assert.That((army.Position, army.Owner, army.Slots.Count(unit => unit is not null)), Is.EqualTo((new WorldPosition(10, 10), WorldRace.Human, 2)));
        WorldPlayerId humanId = editor.State.Players.Values.Single(player => player.Race == WorldRace.Human).Id;
        WorldCapital capital = editor.State.Fortifications.Values.OfType<WorldCapital>().Single(fortification => fortification.OwnerId == humanId);
        Assert.That(capital.Garrison.GetUnits().Select(entry => entry.Unit.Level), Does.Contain(7));
        Assert.That(editor.State.Stacks.Values.Count(stack => stack.OwnerId == humanId), Is.EqualTo(1));

        // Right-click shows the capital as its owner sees it: garrison and stationed army, no spying needed.
        editor.ShowPositionInfo(new WorldPosition(7, 7));
        WorldFortificationInfoViewModel capitalInfo = Assert.IsOfType<WorldFortificationInfoViewModel>(editor.PositionInfo);
        Assert.That(capitalInfo.Note, Is.Null);
        Assert.That((capitalInfo.Defenses.Units.Count, capitalInfo.VisitingStack.Units.Count), Is.EqualTo((2, 2)));
        editor.HidePositionInfo();

        editor.SaveCommand.Execute(null);
        CustomMap saved = loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions());
        Assert.That(saved.Players, Is.EqualTo(editor.Map.Players));
        Assert.That(saved.Stacks, Is.EquivalentTo(editor.Map.Stacks));

        // The Army tool opens the same screen, with what was saved.
        editor.SelectTool(MapEditorTool.Army);
        editor.BeginStroke(new WorldPosition(8, 8));
        MapEditorCapitalScreenViewModel reopened = Assert.IsOfType<MapEditorCapitalScreenViewModel>(game.TopScreen);
        Assert.That(reopened.Army.Slots, Is.EqualTo(army.Slots));
        reopened.CloseCommand.Execute(null);

        // Moving the capital takes its garrison and army along.
        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.That(editor.Map.Players[0].Capital, Is.EqualTo(new WorldPosition(18, 18)));
        Assert.That(editor.Map.Stacks.Single().Position, Is.EqualTo(new WorldPosition(22, 22)));
        Assert.That(editor.Map.Players[0].Garrison, Is.EqualTo(human.Garrison));
    }

    [Test]
    public async Task ArmyLevelTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Army);
        editor.SelectedArmyOwner = editor.ArmyOwners.Single(owner => owner.Value == WorldRace.Neutral);
        editor.BeginStroke(new WorldPosition(20, 30));
        MapEditorArmyScreenViewModel screen = Assert.IsOfType<MapEditorArmyScreenViewModel>(game.TopScreen);
        screen.ChooseLeaderCommand.Execute(null);
        Picker(game).Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge && option.Definition.Leadership >= 2).PickCommand.Execute(null);
        screen.AddUnit(GridPosition.BackCenter);
        MapEditorUnitPickerItemViewModel soldier = Picker(game).Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge);
        soldier.PickCommand.Execute(null);

        // The new unit is selected; levels go up to 99.
        Assert.That(screen.HasSelectedUnit, Is.True);
        screen.SetSelectedLevel(500);
        Assert.That(screen.Slots[GridPosition.BackCenter.ToIndex()]!.Level, Is.EqualTo(CustomMapUnit.MaxLevel));
        Assert.That(screen.LevelUpCommand.CanExecute(null), Is.False);
        screen.LevelDownTenCommand.Execute(null);
        Assert.That(screen.Slots[GridPosition.BackCenter.ToIndex()]!.Level, Is.EqualTo(89));
        screen.ConfirmCommand.Execute(null);

        WorldUnit placed = editor.State.Stacks.Values.Single(stack => stack.Position == new WorldPosition(20, 30)).Units.GetUnits()
            .Single(entry => entry.Unit.DefinitionId == soldier.Definition.Id).Unit;
        Assert.That(placed.Level, Is.EqualTo(89));
        Assert.That(placed.Hitpoints, Is.EqualTo(editor.Map.Stacks.Single().Slots[GridPosition.BackCenter.ToIndex()]!.Hitpoints), "Full health at that level.");
        Assert.That(placed.Hitpoints, Is.GreaterThan(soldier.Definition.MaxHitpoints));
    }

    [Test]
    public async Task PlacementPreviewTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, _) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        Assert.That((editor.IsPreviewEnabled, editor.PlacementPreview.IsEmpty), Is.EqualTo((true, true)), "On by default; nothing while the mouse is off the map.");

        // A village on free land: its 4x4 footprint in green; the land it claims is only mentioned.
        editor.SelectTool(MapEditorTool.Village);
        editor.SelectedVillageOwner = editor.VillageOwners.Single(owner => owner.Value == WorldRace.Human);
        editor.UpdateHover(new WorldPosition(24, 24));
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(16));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(22, 22)), Is.EqualTo(MapEditorTileState.Fits));
        Assert.That(editor.HoverText, Is.EqualTo("Village (4x4): fits · claims 16 tiles for Empire\n24, 24: Neutral land"));

        // Next to the capital: the overlapping column is red, the rest green, and the hover line names the first
        // blocking tile. The click agrees.
        editor.UpdateHover(new WorldPosition(12, 8));
        Assert.That((editor.PlacementPreview.Count(MapEditorTileState.Blocked), editor.PlacementPreview.Count(MapEditorTileState.Fits)), Is.EqualTo((4, 12)));
        Assert.That(editor.HoverText, Does.StartWith("Village (4x4): blocked by a capital at 10, 6\n"));
        editor.BeginStroke(new WorldPosition(12, 8));
        Assert.That(editor.Map.Villages, Is.Empty);

        // Brushes show their disk at the current size, at once when the size changes.
        editor.SelectTool(MapEditorTool.Water);
        editor.BrushRadius = 2;
        editor.UpdateHover(new WorldPosition(30, 20));
        int disk = CustomMap.GetBrushArea(new WorldPosition(30, 20), 2).Count();
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(disk));
        Assert.That(editor.HoverText, Does.StartWith($"Water brush, size 2: paints {disk} tiles\n"));
        editor.BrushRadius = 3;
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(CustomMap.GetBrushArea(new WorldPosition(30, 20), 3).Count()));
        editor.BeginStroke(new WorldPosition(30, 20));
        editor.EndStroke();

        // A Water brush touching a mountain removes all of it: its tiles are amber, the painted tile is still counted.
        editor.SelectTool(MapEditorTool.Mountain);
        editor.BrushRadius = 1;
        editor.BeginStroke(new WorldPosition(30, 34));
        editor.EndStroke();
        editor.SelectTool(MapEditorTool.Water);
        editor.BrushRadius = 0;
        editor.UpdateHover(new WorldPosition(29, 33));
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Changes), Is.EqualTo(9));
        Assert.That(editor.HoverText, Does.StartWith("Water brush, size 0: paints 1 tile · removes 1 mountain\n"));

        // Over the capital, the brush leaves its tiles alone (red).
        editor.SelectTool(MapEditorTool.Land);
        editor.BrushRadius = 1;
        editor.UpdateHover(new WorldPosition(6, 8));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(6, 8)), Is.EqualTo(MapEditorTileState.Blocked));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(5, 8)), Is.EqualTo(MapEditorTileState.Fits));
        Assert.That(editor.HoverText, Does.StartWith("Land brush, size 1: paints 3 tiles · leaves 6 tiles alone\n"));

        // A ruin on the shore turns water into land: those tiles are amber.
        editor.SelectTool(MapEditorTool.Ruin);
        editor.UpdateHover(new WorldPosition(27, 20));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(27, 20)), Is.EqualTo(MapEditorTileState.Changes));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(26, 20)), Is.EqualTo(MapEditorTileState.Fits));
        Assert.That(editor.HoverText, Does.Match(@"^Ruin \(3x3\): fits · turns \d+ water tiles into land\n"));

        // Capital tool: over a capital the gold outline shows the click opens it; elsewhere placing again moves it.
        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.UpdateHover(new WorldPosition(7, 7));
        Assert.That(editor.PlacementPreview.Glow, Is.EqualTo(editor.Map.Players[0].Footprint));
        Assert.That(editor.HoverText, Does.StartWith("Capital of Empire: click opens it\n"));
        editor.UpdateHover(new WorldPosition(20, 8));
        Assert.That(editor.PlacementPreview.MovesFrom, Is.EqualTo(editor.Map.Players[0].Footprint));
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(25));
        Assert.That(editor.HoverText, Does.StartWith("Capital (5x5): fits · claims 25 tiles for Empire · moves it here with its garrison and army\n"));

        // One-tile objects get the game's hover cursor: the render request carries it.
        editor.SelectTool(MapEditorTool.Bag);
        editor.BeginStroke(new WorldPosition(14, 30));
        editor.UpdateHover(new WorldPosition(15, 30));
        editor.UpdateHover(new WorldPosition(14, 30));
        Assert.That(editor.PlacementPreview.HoverObject, Is.EqualTo(new WorldPosition(14, 30)));
        Assert.That(editor.RenderRequest.Cursor.HoverObject, Is.EqualTo(new WorldPosition(14, 30)));
        Assert.That(editor.HoverText, Does.StartWith("Treasure: click opens it\n"));

        // Resources: the same type is removed by a click, another type is changed; a new one names who earns from it.
        editor.SelectTool(MapEditorTool.Resource);
        editor.SelectedResourceType = editor.ResourceTypes.Single(type => type.Value == WorldResourceType.Gold);
        editor.UpdateHover(new WorldPosition(9, 12));
        Assert.That(editor.HoverText, Does.StartWith("Gold mine: fits · income goes to nobody\n"));
        editor.UpdateHover(new WorldPosition(14, 30));
        Assert.That((editor.PlacementPreview.Count(MapEditorTileState.Blocked), editor.HoverText), Is.EqualTo((1, "Gold mine: blocked by treasure at 14, 30\n14, 30: Neutral land · treasure (0 items)")));
        editor.UpdateHover(new WorldPosition(9, 11));
        editor.BeginStroke(new WorldPosition(9, 11));
        editor.UpdateHover(new WorldPosition(9, 12));
        editor.UpdateHover(new WorldPosition(9, 11));
        Assert.That(editor.HoverText, Does.StartWith("Gold mine: click removes it\n"));
        editor.SelectedResourceType = editor.ResourceTypes.Single(type => type.Value == WorldResourceType.LifeMana);
        Assert.That(editor.HoverText, Does.StartWith("Gold mine: click changes it to Life mana crystal\n"));

        // Landmarks: the footprint follows the palette selection at once; over a landmark the click removes it.
        editor.SelectTool(MapEditorTool.Landmark);
        editor.UpdateHover(new WorldPosition(20, 26));
        editor.SelectedLandmarkSize = editor.LandmarkSizes.Single(size => size.Value == 3);
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(9));
        editor.SelectedLandmarkSize = editor.LandmarkSizes.Single(size => size.Value == 2);
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(4));
        editor.UpdateHover(new WorldPosition(10, 12));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(9, 11)), Is.EqualTo(MapEditorTileState.Blocked));
        Assert.That(editor.HoverText, Does.Contain(": blocked by a resource at 9, 11\n"));
        editor.BeginStroke(new WorldPosition(10, 12));
        Assert.That(editor.Map.Landmarks, Is.Empty, "Blocked, as the preview said.");
        editor.UpdateHover(new WorldPosition(30, 30));
        editor.BeginStroke(new WorldPosition(30, 30));
        Assert.That(editor.Map.Landmarks, Has.Count.EqualTo(1));
        editor.UpdateHover(new WorldPosition(29, 29));
        Assert.That((editor.PlacementPreview.Glow, editor.HoverText), Is.EqualTo((editor.Map.Landmarks[0].Footprint, $"{editor.SelectedLandmark!.Name}: click removes it\n29, 29: Neutral land · {editor.SelectedLandmark.Name}")));

        // The preview goes away with the right-click popup, while dragging the map, when switched off and when the
        // mouse leaves the map; the tile description stays.
        editor.ShowPositionInfo(new WorldPosition(29, 29));
        Assert.That((editor.PlacementPreview.IsEmpty, editor.HoverText), Is.EqualTo((true, $"29, 29: Neutral land · {editor.SelectedLandmark.Name}")));
        editor.HidePositionInfo();
        Assert.That(editor.PlacementPreview.IsEmpty, Is.False);
        editor.SetPreviewSuspended(true);
        Assert.That(editor.PlacementPreview.IsEmpty, Is.True);
        editor.SetPreviewSuspended(false);
        editor.IsPreviewEnabled = false;
        Assert.That((editor.PlacementPreview.IsEmpty, editor.RenderRequest.Cursor.HoverObject), Is.EqualTo((true, (WorldPosition?)null)));
        editor.IsPreviewEnabled = true;
        editor.UpdateHover(null);
        Assert.That((editor.PlacementPreview.IsEmpty, editor.HoverText), Is.EqualTo((true, string.Empty)));
    }

    // The locations tool and list: hover preview (snapping near the edge), place + name in the row, select on the
    // map or in the list (the list moves the camera), resize with +/-, search, sorting, Shift+click, undo.
    [Test]
    public async Task LocationToolTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Landmark);
        Assert.That(editor.ShowLandmarkPalette, Is.True);
        editor.SelectTool(MapEditorTool.Location);
        Assert.That((editor.IsLocationTool, editor.HasBrush, editor.IsLocationsOpen, editor.HasNoLocationRows), Is.EqualTo((true, false, true, true)), "Picking the tool opens the list.");
        Assert.That(editor.LocationsTitle, Is.EqualTo("Locations (0)"));

        // Hover: a green 3x3 square and the name it will get; at the edge it moves inside the map (amber).
        WorldPosition first = new(20, 20);
        editor.UpdateHover(first);
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(21, 21)), Is.EqualTo(MapEditorTileState.Fits));
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(9));
        Assert.That(editor.HoverText, Does.StartWith("Location (3x3): click places \"Location 1\"\n"));
        editor.UpdateHover(new WorldPosition(0, 0));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(2, 2)), Is.EqualTo(MapEditorTileState.Changes));
        Assert.That(editor.HoverText, Does.StartWith("Location (3x3): click places \"Location 1\" at 1, 1 (moved inside the map)"));

        // Click: placed, selected, and its row opens for the name; Enter keeps the typed one.
        editor.BeginStroke(first);
        editor.EndStroke();
        Assert.That(editor.Map.Locations.Select(location => (location.Id, location.Name, location.Center, location.Radius)), Is.EqualTo(new[] { (1, "Location 1", first, 1) }));
        MapEditorLocationRowViewModel row = editor.SelectedLocationRow!;
        Assert.That((row.Id, row.IsEditing, row.EditText, editor.CanUndo), Is.EqualTo((1, true, "Location 1", true)));
        row.EditText = "  Northern Pass ";
        row.CommitEditCommand.Execute(null);
        Assert.That(editor.Map.Locations.Single().Name, Is.EqualTo("Northern Pass"));
        Assert.That(editor.SelectedLocationRow!.Details, Is.EqualTo("3x3 · 20, 20"));
        Assert.That(editor.PlacementPreview.Locations.Single(), Is.EqualTo(new MapEditorLocationMark(new WorldFootprint(new WorldPosition(19, 19), 3, 3), first, "Northern Pass", Marked: true)));

        // + resizes the selected location; the radius stays 0-3.
        editor.ChangeLocationRadius(1);
        Assert.That((editor.Map.Locations.Single().Radius, editor.LocationSizeText), Is.EqualTo((2, "5x5")));
        editor.ChangeLocationRadius(1);
        editor.ChangeLocationRadius(1);
        Assert.That((editor.Map.Locations.Single().Radius, editor.LocationRadiusUpCommand.CanExecute(null)), Is.EqualTo((3, false)));
        editor.ChangeLocationRadius(-2);

        // A second location, overlapping: the list is sorted by name, a blank name keeps the default.
        editor.BeginStroke(new WorldPosition(30, 30));
        editor.EndStroke();
        editor.SelectedLocationRow!.EditText = " ";
        editor.SelectedLocationRow!.CommitEditCommand.Execute(null);
        editor.BeginStroke(new WorldPosition(21, 21));
        editor.EndStroke();
        Assert.That(editor.SelectedLocationRow!.Name, Is.EqualTo("Northern Pass"), "A click inside a location selects it.");
        editor.SelectedLocationRow!.CancelEditCommand.Execute(null);
        editor.SelectTool(MapEditorTool.Army);
        editor.SelectTool(MapEditorTool.Location);
        editor.LocationRadius = 0;
        editor.SelectedLocationRow = null;
        editor.BeginStroke(new WorldPosition(21, 20));
        editor.EndStroke();
        Assert.That(editor.Map.Locations, Has.Count.EqualTo(2), "A click inside a location never places another.");
        Assert.That(editor.LocationRows.Select(item => item.Name), Is.EqualTo(new[] { "Location 1", "Northern Pass" }));
        Assert.That(editor.LocationsTitle, Is.EqualTo("Locations (2)"));
        Assert.That(editor.TakeCameraTarget(), Is.Null, "Selecting on the map doesn't move the camera.");

        // Selecting a row moves the camera to the location.
        editor.SelectedLocationRow = editor.LocationRows[0];
        Assert.That(editor.TakeCameraTarget(), Is.EqualTo(new WorldPosition(30, 30)));
        Assert.That(editor.TakeCameraTarget(), Is.Null);

        // Search filters by name, ignoring case.
        editor.LocationSearch = "NORTH";
        Assert.That(editor.LocationRows.Select(item => item.Name), Is.EqualTo(new[] { "Northern Pass" }));
        editor.LocationSearch = string.Empty;

        // Hovering a row lights its square up; the hover line lists every location on a tile, with any tool.
        editor.SetHoveredLocation(1);
        Assert.That(editor.PlacementPreview.Locations.Count(mark => mark.Marked), Is.EqualTo(2));
        editor.SetHoveredLocation(null);
        editor.SelectTool(MapEditorTool.Army);
        editor.UpdateHover(new WorldPosition(21, 21));
        Assert.That(editor.HoverText, Does.EndWith("\nLocations here: Northern Pass"));

        // Shift+click removes the smallest location under the cursor (here the selected one wins); Undo brings it back.
        editor.SelectTool(MapEditorTool.Location);
        editor.SelectedLocationRow = editor.LocationRows.Single(item => item.Name == "Northern Pass");
        editor.BeginStroke(new WorldPosition(21, 21), alternate: true);
        Assert.That(editor.Map.Locations.Select(location => location.Name), Is.EqualTo(new[] { "Location 1" }));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.LocationRows.Select(item => item.Name), Is.EqualTo(new[] { "Location 1", "Northern Pass" }));

        // The row's delete button; the economy panel and the list share the left side.
        editor.LocationRows[0].DeleteCommand.Execute(null);
        Assert.That(editor.Map.Locations, Has.Count.EqualTo(1));
        editor.IsEconomyOpen = true;
        Assert.That(editor.IsLocationsOpen, Is.False);
        editor.IsLocationsOpen = true;
        Assert.That(editor.IsEconomyOpen, Is.False);

        // With another tool and the list closed, nothing is drawn.
        editor.SelectTool(MapEditorTool.Land);
        editor.IsLocationsOpen = false;
        Assert.That(editor.PlacementPreview.Locations, Is.Empty);
    }

    // Dragging a location: the grabbed tile stays under the cursor, the drop snaps onto the map, Esc and losing the
    // drag put it back, a drag from empty ground does nothing, and the X/Y boxes move it exactly.
    [Test]
    public async Task LocationDragTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Location);
        editor.LocationRadius = 2;
        editor.BeginStroke(new WorldPosition(20, 20));
        editor.EndStroke();
        editor.SelectedLocationRow!.CommitEditCommand.Execute(null);
        editor.SelectedLocationRow = null;

        // Grab the zone off-centre and carry it 5 right, 3 down: the preview follows, the old place is dashed.
        editor.BeginStroke(new WorldPosition(21, 19));
        Assert.That(editor.IsDraggingLocation, Is.False, "A press alone doesn't drag.");
        editor.ContinueStroke(new WorldPosition(26, 22));
        editor.UpdateHover(new WorldPosition(26, 22));
        Assert.That(editor.IsDraggingLocation, Is.True);
        Assert.That(editor.PlacementPreview.Footprint, Is.EqualTo(new WorldFootprint(new WorldPosition(23, 21), 5, 5)));
        Assert.That(editor.PlacementPreview.MovesFrom, Is.EqualTo(new WorldFootprint(new WorldPosition(18, 18), 5, 5)));
        Assert.That(editor.HoverText, Does.StartWith("Location 1: 20, 20 -> 25, 23 · Esc puts it back"));
        Assert.That(editor.Map.Locations.Single().Center, Is.EqualTo(new WorldPosition(20, 20)), "Nothing moves before the drop.");
        editor.EndStroke();
        Assert.That(editor.Map.Locations.Single(), Is.EqualTo(new CustomMapLocation(1, "Location 1", new WorldPosition(25, 23), 2)));
        Assert.That((editor.SelectedLocationRow!.Id, editor.LocationXText, editor.LocationYText), Is.EqualTo((1, "25", "23")));
        Assert.That(editor.SelectedLocationRow!.IsEditing, Is.False, "A drop doesn't open the name.");

        // Past the edge: amber at the snapped place, dropped there.
        editor.BeginStroke(new WorldPosition(25, 23));
        editor.ContinueStroke(new WorldPosition(60, 23));
        editor.UpdateHover(new WorldPosition(47, 23));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(47, 23)), Is.EqualTo(MapEditorTileState.Changes));
        Assert.That(editor.HoverText, Does.Contain("-> 45, 23 (snapped to the map edge)"));
        editor.EndStroke();
        Assert.That(editor.Map.Locations.Single().Center, Is.EqualTo(new WorldPosition(45, 23)));

        // Esc puts it back; so does losing the drag.
        editor.BeginStroke(new WorldPosition(45, 23));
        editor.ContinueStroke(new WorldPosition(30, 30));
        Assert.That(editor.CancelLocationDrag(), Is.True);
        editor.EndStroke();
        Assert.That(editor.Map.Locations.Single().Center, Is.EqualTo(new WorldPosition(45, 23)));
        Assert.That(editor.CancelLocationDrag(), Is.False);

        // A drag from empty ground places nothing; a press and release on empty ground places.
        editor.BeginStroke(new WorldPosition(10, 30));
        editor.ContinueStroke(new WorldPosition(12, 30));
        editor.EndStroke();
        Assert.That(editor.Map.Locations, Has.Count.EqualTo(1));

        // One undo step per drop: Undo takes the zone back to its previous place.
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Locations.Single().Center, Is.EqualTo(new WorldPosition(25, 23)));

        // X/Y boxes: exact move, snapped; junk text is put back.
        editor.SelectedLocationRow = editor.LocationRows.Single();
        editor.LocationXText = "3";
        editor.LocationYText = "40";
        editor.ApplyLocationPosition();
        Assert.That(editor.Map.Locations.Single().Center, Is.EqualTo(new WorldPosition(3, 40)));
        editor.LocationXText = "0";
        editor.ApplyLocationPosition();
        Assert.That((editor.Map.Locations.Single().Center, editor.LocationXText), Is.EqualTo((new WorldPosition(2, 40), "2")));
        editor.LocationYText = "abc";
        editor.ApplyLocationPosition();
        Assert.That((editor.Map.Locations.Single().Center, editor.LocationYText), Is.EqualTo((new WorldPosition(2, 40), "40")));
    }

    private static MapEditorScreenViewModel PlaceCapitals(MapEditorScreenViewModel editor)
    {
        editor.SelectTool(MapEditorTool.Capital);
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        editor.BeginStroke(new WorldPosition(8, 8));
        editor.SelectedCapitalRace = editor.CapitalRaces[1];
        editor.BeginStroke(new WorldPosition(38, 38));

        return editor;
    }

    private static MapEditorListPickerViewModel ListPicker(GameViewModel game)
    {
        return Assert.IsOfType<MapEditorListPickerViewModel>(game.TopScreen);
    }

    [Test]
    public async Task RuinTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));

        editor.SelectTool(MapEditorTool.Ruin);
        Assert.That(editor.IsRuinTool, Is.True);
        editor.BeginStroke(new WorldPosition(20, 20));
        CustomMapRuin ruin = editor.Map.Ruins.Single();
        Assert.That(ruin.TopLeft, Is.EqualTo(new WorldPosition(19, 19)));
        Assert.That(MapEditorNames.Load().Ruins.Select(name => name.Name), Does.Contain(ruin.Name));
        Assert.That(editor.State.Fortifications.Values.OfType<WorldRuin>().Count(), Is.EqualTo(1));

        editor.BeginStroke(new WorldPosition(21, 21));
        MapEditorRuinScreenViewModel screen = Assert.IsOfType<MapEditorRuinScreenViewModel>(game.TopScreen);
        Assert.That(screen.Capacity, Is.EqualTo("0 / 6"));
        screen.GoldText = "abc";
        Assert.That(screen.ConfirmCommand.CanExecute(null), Is.False);
        screen.GoldText = "800";
        Assert.That(screen.ConfirmCommand.CanExecute(null), Is.True);
        screen.SelectedLook = screen.Looks[7];
        screen.ChooseItemCommand.Execute(null);
        MapEditorListPickerViewModel items = ListPicker(game);
        Assert.That(items.Options, Has.Count.GreaterThan(100), "Every item of the game.");
        items.SelectedFilter = items.Filters[1];
        Assert.That(items.Options.Select(option => option.Group).Distinct(), Is.EqualTo(new[] { items.Filters[1] }));
        Assert.That(items.Options[0].Details, Is.Not.Null, "Right-click shows the item details.");
        ItemDefinitionId chosen = (ItemDefinitionId)items.Options[0].Value;
        items.Options[0].PickCommand.Execute(null);
        Assert.That(screen.Item, Is.EqualTo(chosen));
        screen.AddUnit(GridPosition.FrontTop);
        Picker(game).Options.First(option => option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        Assert.That(screen.Capacity, Is.EqualTo("2 / 6"));
        screen.Name = "Odense";
        screen.ConfirmCommand.Execute(null);

        ruin = editor.Map.Ruins.Single();
        Assert.That((ruin.Name, ruin.Gold, ruin.Item, ruin.Variant, ruin.Guards.Count(unit => unit is not null)), Is.EqualTo(("Odense", 800, (ItemDefinitionId?)chosen, 7, 1)));
        WorldRuin loaded = editor.State.Fortifications.Values.OfType<WorldRuin>().Single();
        Assert.That((loaded.Resources.Gold, loaded.Item, loaded.Garrison.IsEmpty), Is.EqualTo((800, (ItemDefinitionId?)chosen, false)));
        editor.UpdateHover(new WorldPosition(20, 20));
        Assert.That(editor.HoverText, Does.Contain("ruin Odense"));
        editor.ShowPositionInfo(new WorldPosition(20, 20));
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldTileInfoViewModel>());
        editor.HidePositionInfo();

        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions()).Ruins, Is.EqualTo(editor.Map.Ruins));

        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.IsOfType<MapEditorRuinScreenViewModel>(game.TopScreen).DeleteCommand.Execute(null);
        Assert.That(editor.Map.Ruins, Is.Empty);
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Ruins.Single().Name, Is.EqualTo("Odense"));
    }

    [Test]
    public async Task BagTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));

        editor.SelectTool(MapEditorTool.Bag);
        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.That(editor.Map.Bags.Single().Position, Is.EqualTo(new WorldPosition(20, 20)));
        Assert.That(editor.State.Bags, Has.Count.EqualTo(1));

        editor.BeginStroke(new WorldPosition(20, 20));
        MapEditorBagScreenViewModel screen = Assert.IsOfType<MapEditorBagScreenViewModel>(game.TopScreen);
        Assert.That(screen.CountText, Is.EqualTo("0 / 16"));
        screen.AddItemCommand.Execute(null);
        ItemDefinitionId first = (ItemDefinitionId)ListPicker(game).Options[0].Value;
        ListPicker(game).Options[0].PickCommand.Execute(null);
        screen.AddItemCommand.Execute(null);
        ItemDefinitionId second = (ItemDefinitionId)ListPicker(game).Options[5].Value;
        ListPicker(game).Options[5].PickCommand.Execute(null);
        screen.AddItemCommand.Execute(null);
        ListPicker(game).Options[9].PickCommand.Execute(null);
        screen.Items[2].RemoveCommand.Execute(null);
        Assert.That(screen.Items.Select(row => row.Value), Is.EqualTo(new object[] { first, second }));
        screen.SelectedLook = screen.Looks[3];
        screen.ConfirmCommand.Execute(null);

        CustomMapBag bag = editor.Map.Bags.Single();
        Assert.That(bag.Variant, Is.EqualTo(3));
        Assert.That(bag.Items, Is.EqualTo(new[] { first, second }));
        Assert.That(editor.State.Bags.Values.Single().Inventory.Items.Select(item => item.DefinitionId), Is.EqualTo(new[] { first, second }));
        editor.UpdateHover(new WorldPosition(20, 20));
        Assert.That(editor.HoverText, Does.Contain("treasure (2 items)"));

        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions()).Bags, Is.EqualTo(editor.Map.Bags));
    }

    [Test]
    public async Task LandmarkTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Landmark);
        Assert.That((editor.IsLandmarkTool, editor.HasBrush), Is.EqualTo((true, false)));
        Assert.That(editor.LandmarkCategories.Select(category => category.Value), Is.EqualTo(Enum.GetValues<WorldLandmarkType>()));
        Assert.That(editor.LandmarkSizes.Select(size => size.Value), Is.EqualTo(new[] { 0, 1, 2, 3, 4, 5 }));

        // The palette shows one category at a time, filtered by size; every landmark of the game is in one.
        int total = 0;

        foreach (MapEditorOption<WorldLandmarkType> category in editor.LandmarkCategories)
        {
            editor.SelectedLandmarkCategory = category;
            Assert.That(editor.Landmarks.All(option => option.Definition.Type == category.Value), Is.True);
            Assert.That(editor.SelectedLandmark, Is.EqualTo(editor.Landmarks[0]));
            total += editor.Landmarks.Count;
        }

        Assert.That(total, Is.EqualTo(156));
        editor.SelectedLandmarkCategory = editor.LandmarkCategories[0];
        editor.SelectedLandmarkSize = editor.LandmarkSizes.Single(size => size.Value == 3);
        Assert.That(editor.Landmarks.All(option => option.Definition.Width == 3), Is.True);
        MapEditorLandmarkOption chosen = editor.Landmarks[^1];
        editor.SelectedLandmark = chosen;
        Assert.That(chosen.Caption, Does.EndWith("· 3x3"));

        // Click: placed centred on the tile and shown at once; the hover line and right-click name it.
        editor.BeginStroke(new WorldPosition(20, 20));
        CustomMapLandmark landmark = editor.Map.Landmarks.Single();
        Assert.That((landmark.Type, landmark.TopLeft), Is.EqualTo((chosen.Definition.Id, new WorldPosition(19, 19))));
        Assert.That(editor.State.Landmarks.Values.Single().Footprint, Is.EqualTo(landmark.Footprint));
        editor.UpdateHover(new WorldPosition(21, 21));
        Assert.That(editor.HoverText, Does.Contain(chosen.Name));
        editor.ShowPositionInfo(new WorldPosition(21, 21));
        Assert.That(Assert.IsOfType<WorldTileInfoViewModel>(editor.PositionInfo).Title, Is.EqualTo(chosen.Name));
        editor.HidePositionInfo();

        // Not on a capital or another landmark; a click on a landmark removes it, undo brings it back.
        editor.BeginStroke(new WorldPosition(9, 9));
        editor.BeginStroke(new WorldPosition(22, 22));
        Assert.That(editor.Map.Landmarks, Has.Count.EqualTo(1));
        editor.BeginStroke(new WorldPosition(19, 21));
        Assert.That((editor.Map.Landmarks.Count, editor.State.Landmarks.Count), Is.EqualTo((0, 0)));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Landmarks.Single(), Is.EqualTo(landmark));

        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions()).Landmarks, Is.EqualTo(editor.Map.Landmarks));
    }

    [Test]
    public async Task RoadTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Road);
        Assert.That((editor.IsRoadTool, editor.HasBrush), Is.EqualTo((true, false)));

        bool IsRoad(WorldMap shown, int x, int y) => shown.GetCellAt(new WorldPosition(x, y)).Feature == WorldMapFeature.Road;
        int CountRoads(WorldMap shown) => Enumerable.Range(0, shown.Width * shown.Height).Count(i => IsRoad(shown, i % shown.Width, i / shown.Width));

        // Hover: a road can start on free land, not on a capital (a click there shows why).
        editor.UpdateHover(new WorldPosition(20, 20));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(20, 20)), Is.EqualTo(MapEditorTileState.Fits));
        Assert.That(editor.HoverText, Does.StartWith("Road: click starts a road here"));
        editor.UpdateHover(new WorldPosition(8, 8));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(8, 8)), Is.EqualTo(MapEditorTileState.Blocked));
        editor.BeginStroke(new WorldPosition(8, 8));
        Assert.That((editor.RoadStart, game.Alert.Current?.Caption), Is.EqualTo(((WorldPosition?)null, "A road cannot go here: a capital at 8, 8.")));

        // First click: the start shows as a road piece, nothing is laid yet.
        editor.BeginStroke(new WorldPosition(20, 20));
        Assert.That(editor.RoadStart, Is.EqualTo(new WorldPosition(20, 20)));
        Assert.That((IsRoad(editor.RenderRequest.State.Map, 20, 20), CountRoads(editor.Map.ToWorldMap())), Is.EqualTo((true, 0)));

        // Moving the mouse shows the whole road to the cursor, joined edge to edge, still not laid.
        editor.UpdateHover(new WorldPosition(26, 24));
        WorldMap ghost = editor.RenderRequest.State.Map;
        Assert.That(CountRoads(ghost), Is.EqualTo(11));
        Assert.That(CountRoads(editor.Map.ToWorldMap()), Is.Zero);
        Assert.That(editor.PlacementPreview.Count(MapEditorTileState.Fits), Is.EqualTo(11));
        Assert.That(editor.HoverText, Does.StartWith("Road from 20, 20: 11 tiles long · 11 new road tiles · click lays it"));

        // Second click lays exactly what was shown, and the road carries on from there.
        editor.BeginStroke(new WorldPosition(26, 24));
        Assert.That(editor.Map.ToWorldMap().Cells, Is.EqualTo(ghost.Cells));
        Assert.That(editor.State.Map.Cells, Is.EqualTo(ghost.Cells));
        Assert.That((editor.RoadStart, editor.CanUndo), Is.EqualTo(((WorldPosition?)new WorldPosition(26, 24), true)));

        // Esc stops; a road across the first one joins it with junction pieces (three or four road neighbours).
        Assert.That((editor.CancelRoad(), editor.CancelRoad(), editor.RoadStart), Is.EqualTo((true, false, (WorldPosition?)null)));
        editor.BeginStroke(new WorldPosition(23, 16));
        editor.UpdateHover(new WorldPosition(23, 28));
        WorldMap crossing = editor.RenderRequest.State.Map;
        Assert.That(Enumerable.Range(16, 13).All(y => IsRoad(crossing, 23, y)), Is.True, "Straight down the column.");
        int Neighbours(WorldPosition tile) => new[] { (0, -1), (1, 0), (0, 1), (-1, 0) }.Count(step => IsRoad(crossing, tile.X + step.Item1, tile.Y + step.Item2));
        WorldPosition[] junctions = [.. Enumerable.Range(17, 11).Select(y => new WorldPosition(23, y)).Where(tile => Neighbours(tile) >= 3)];
        Assert.That(junctions, Is.Not.Empty);
        Assert.That(junctions.All(tile => !IsRoad(editor.Map.ToWorldMap(), tile.X, tile.Y) || Neighbours(tile) >= 3), Is.True);
        editor.BeginStroke(new WorldPosition(23, 28));
        Assert.That(CountRoads(editor.Map.ToWorldMap()), Is.EqualTo(CountRoads(crossing)));

        // No way to a tile ringed by water; a click there shows why and keeps the start.
        editor.SelectTool(MapEditorTool.Water);
        editor.BrushRadius = 1;
        editor.BeginStroke(new WorldPosition(32, 12));
        editor.EndStroke();
        editor.SelectTool(MapEditorTool.Land);
        editor.BrushRadius = 0;
        editor.BeginStroke(new WorldPosition(32, 12));
        editor.EndStroke();
        editor.SelectTool(MapEditorTool.Road);
        editor.BeginStroke(new WorldPosition(30, 20));
        editor.UpdateHover(new WorldPosition(32, 12));
        Assert.That(editor.HoverText, Does.StartWith("Road from 30, 20: no way to here"));
        Assert.That(CountRoads(editor.RenderRequest.State.Map), Is.EqualTo(CountRoads(editor.Map.ToWorldMap()) + 1), "Only the start is shown.");
        editor.BeginStroke(new WorldPosition(32, 12));
        Assert.That((editor.RoadStart, game.Alert.Current?.Caption), Is.EqualTo(((WorldPosition?)new WorldPosition(30, 20), "No way for a road from 30, 20 to 32, 12.")));

        // Shift+click removes a road tile; undo puts it back, then takes the crossing road away.
        int roads = CountRoads(editor.Map.ToWorldMap());
        editor.BeginStroke(new WorldPosition(23, 16), alternate: true);
        Assert.That((CountRoads(editor.Map.ToWorldMap()), editor.RoadStart), Is.EqualTo((roads - 1, (WorldPosition?)null)));
        editor.UndoCommand.Execute(null);
        Assert.That(CountRoads(editor.Map.ToWorldMap()), Is.EqualTo(roads));
        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        Assert.That(CountRoads(editor.Map.ToWorldMap()), Is.EqualTo(11));

        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions()).Cells, Is.EqualTo(editor.Map.Cells));
    }

    [Test]
    public async Task RodTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Rod);
        Assert.That((editor.IsRodTool, editor.HasBrush), Is.EqualTo((true, false)));
        Assert.That(editor.RodOwners.Select(option => option.Value), Is.EqualTo(new[] { WorldRace.Human, WorldRace.Dwarf }), "Only the races on the map.");
        Assert.That(editor.SelectedRodOwner!.Value, Is.EqualTo(WorldRace.Human));

        // Hover: the rod's tile is green, the 2 tiles it claims amber.
        WorldPosition first = new(20, 20);
        editor.UpdateHover(first);
        Assert.That(editor.PlacementPreview.GetState(first), Is.EqualTo(MapEditorTileState.Fits));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(20, 21)), Is.EqualTo(MapEditorTileState.Changes));
        Assert.That(editor.PlacementPreview.GetState(new WorldPosition(20, 19)), Is.EqualTo(MapEditorTileState.Changes));
        Assert.That(editor.HoverText, Does.StartWith("Rod (Empire): fits · turns 2 tiles into Empire land\n"));

        // Click: placed, drawn at once (no preview rebuild needed) and undoable.
        editor.BeginStroke(first);
        Assert.That(editor.Map.Rods.Select(rod => (rod.Position, rod.Owner)), Is.EqualTo(new[] { (first, WorldRace.Human) }));
        Assert.That(editor.RenderRequest.State.Rods.Values.Select(rod => rod.Position), Is.EqualTo(new[] { first }));
        Assert.That(editor.RenderRequest.State.Map.GetCellAt(new WorldPosition(20, 21)).Terrain, Is.EqualTo(WorldMapTerrain.Human));
        Assert.That(editor.CanUndo, Is.True);

        // Too close: both rods red, and the click says which rod is in the way.
        WorldPosition near = new(21, 22);
        editor.UpdateHover(near);
        Assert.That(editor.PlacementPreview.GetState(near), Is.EqualTo(MapEditorTileState.Blocked));
        Assert.That(editor.PlacementPreview.GetState(first), Is.EqualTo(MapEditorTileState.Blocked));
        const string tooClose = "Rod (Empire): another rod at 20, 20 is too close (rods need 2 free tiles between them)";
        Assert.That(editor.HoverText, Does.StartWith(tooClose));
        editor.BeginStroke(near);
        Assert.That((editor.Map.Rods.Count, game.Alert.Current?.Caption), Is.EqualTo((1, tooClose)));
        editor.UpdateHover(new WorldPosition(8, 8));
        Assert.That(editor.HoverText, Does.StartWith("Rod (Empire): blocked by a capital at 8, 8"));

        // A rod whose land reaches a mine takes it: the hover line says from whom.
        editor.SelectTool(MapEditorTool.Resource);
        editor.BeginStroke(new WorldPosition(30, 18));
        editor.SelectTool(MapEditorTool.Rod);
        editor.SelectedRodOwner = editor.RodOwners[1];
        WorldPosition second = new(30, 20);
        editor.UpdateHover(second);
        Assert.That(editor.HoverText, Does.StartWith("Rod (Mountain Clans): fits · turns 10 tiles into Mountain Clans land · takes Gold mine from nobody\n"));
        editor.BeginStroke(second);
        Assert.That(editor.Map.GetLandOwner(new WorldPosition(30, 18)), Is.EqualTo(WorldRace.Dwarf));

        // Other tools see the rod in the way.
        editor.SelectTool(MapEditorTool.Army);
        editor.UpdateHover(second);
        Assert.That(editor.HoverText, Does.StartWith("Army: blocked by a rod at 30, 20"));

        // Hovering a rod: a click removes it. Shift+click and a plain click both do; the land stays.
        editor.SelectTool(MapEditorTool.Rod);
        editor.UpdateHover(null);
        editor.UpdateHover(second);
        Assert.That(editor.HoverText, Does.StartWith("Rod of Mountain Clans: click removes it (its land stays)"));
        Assert.That(editor.PlacementPreview.HoverObject, Is.EqualTo(second));
        editor.BeginStroke(second, alternate: true);
        editor.BeginStroke(first);
        Assert.That(editor.Map.Rods, Is.Empty);
        Assert.That(editor.RenderRequest.State.Rods, Is.Empty);
        Assert.That(editor.Map.GetLandOwner(new WorldPosition(30, 18)), Is.EqualTo(WorldRace.Dwarf));

        // Shift+click on free land does nothing; undo brings the rods back one by one.
        editor.BeginStroke(new WorldPosition(24, 30), alternate: true);
        Assert.That(editor.Map.Rods, Is.Empty);
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Rods.Select(rod => rod.Position), Is.EqualTo(new[] { first }));
        Assert.That(editor.RenderRequest.State.Rods.Count, Is.EqualTo(1));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Rods.Count, Is.EqualTo(2));

        // Saved and opened again, the rods are still there.
        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open(editor.Map.Id, editor.Definitions).Rods, Is.EquivalentTo(editor.Map.Rods));
    }

    [Test]
    public async Task EconomyPanelTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        Assert.That((editor.IsEconomyOpen, editor.EconomyRaces.Count), Is.EqualTo((false, 0)), "Computed only while open.");

        editor.ToggleEconomyCommand.Execute(null);
        Assert.That(editor.EconomyRaces.Select(race => race.Title), Is.EqualTo(new[] { "Empire", "Mountain Clans" }));
        MapEditorEconomyRaceViewModel empire = editor.EconomyRaces[0];
        Assert.That(empire.Rows.Select(row => (row.Income.Name, row.Count, row.Income.CapitalIncome, row.Income.ResourceNodesIncome, row.Income.IncomeText)), Is.EqualTo(new[]
        {
            ("Gold", 0, 100, 0, "+100"),
            ("Life mana", 0, 25, 0, "+25")
        }));
        Assert.That(empire.Summary, Is.EqualTo("Land: 25 tiles (1%) · 0 rods"), "The capital's 5x5.");
        Assert.That(editor.HasUnclaimed, Is.False);

        // Live: Empire land with a gold mine, and a mine on neutral land.
        editor.SelectTool(MapEditorTool.Land);
        editor.SelectedLandTerrain = editor.LandTerrains.Single(option => option.Value == WorldMapTerrain.Human);
        editor.BrushRadius = 1;
        editor.BeginStroke(new WorldPosition(20, 20));
        editor.EndStroke();
        editor.SelectTool(MapEditorTool.Resource);
        editor.BeginStroke(new WorldPosition(20, 20));
        editor.BeginStroke(new WorldPosition(26, 30));
        empire = editor.EconomyRaces[0];
        Assert.That(empire.Rows[0].Count, Is.EqualTo(1));
        Assert.That(empire.Rows[0].Income.IncomeText, Is.EqualTo("+150"));
        Assert.That(editor.UnclaimedResources.Select(row => (row.Income.Name, row.Count)), Is.EqualTo(new[] { ("Gold", 1) }));

        // A rod counts, and its land can take the neutral mine.
        editor.SelectTool(MapEditorTool.Rod);
        editor.BeginStroke(new WorldPosition(26, 32));
        Assert.That(editor.EconomyRaces[0].Summary, Does.EndWith("· 1 rod"));
        Assert.That(editor.EconomyRaces[0].Rows[0].Count, Is.EqualTo(2), "Its 2 tiles reached the mine at 26, 30.");
        Assert.That(editor.HasUnclaimed, Is.False);

        // Selecting a race outlines its resources and rods on the map, whatever the tool; toggling clears it.
        editor.SelectedEconomyRace = editor.EconomyRaces[0];
        Assert.That(editor.PlacementPreview.Highlight, Is.EquivalentTo(new[] { new WorldPosition(20, 20), new WorldPosition(26, 30), new WorldPosition(26, 32) }));
        editor.SelectedEconomyRace = editor.EconomyRaces[1];
        Assert.That(editor.PlacementPreview.Highlight, Is.Empty, "The Clans have nothing yet.");

        // Undo follows, and the selection survives the refresh.
        editor.UndoCommand.Execute(null);
        Assert.That(editor.EconomyRaces[0].Rows[0].Count, Is.EqualTo(1));
        Assert.That(editor.SelectedEconomyRace?.Race, Is.EqualTo(WorldRace.Dwarf));

        // The landmark palette makes room while the panel is open; closing clears the outline.
        editor.SelectTool(MapEditorTool.Landmark);
        Assert.That(editor.ShowLandmarkPalette, Is.False);
        editor.SelectedEconomyRace = editor.EconomyRaces[0];
        editor.ToggleEconomyCommand.Execute(null);
        Assert.That((editor.IsEconomyOpen, editor.ShowLandmarkPalette, editor.SelectedEconomyRace), Is.EqualTo((false, true, (MapEditorEconomyRaceViewModel?)null)));
        Assert.That(editor.PlacementPreview.Highlight, Is.Empty);
    }

    [Test]
    public async Task StartingBankTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.ToggleEconomyCommand.Execute(null);

        // The first race's bank: gold, its own mana, then the other four.
        Assert.That(editor.SelectedBankRace!.Value, Is.EqualTo(WorldRace.Human));
        Assert.That(editor.BankFields.Select(field => (field.Name, field.Text)), Is.EqualTo(new[]
        {
            ("Gold", "500"), ("Life mana", "1500"), ("Infernal mana", "0"), ("Death mana", "0"), ("Rune mana", "0"), ("Grove mana", "0")
        }));
        Assert.That(editor.EconomyRaces[0].StartText, Is.EqualTo("Start: 500 Gold · 1500 Life mana"));

        // Typing: the map and the race block follow; one field typed into keeps one undo step; too much is 9999.
        bool couldUndo = editor.CanUndo;
        editor.BankFields[0].Text = "7";
        editor.BankFields[0].Text = "75";
        editor.BankFields[0].Text = "750";
        editor.BankFields[4].Text = "";
        editor.BankFields[4].Text = "250";
        editor.BankFields[2].Text = "12345";
        editor.BankFields[3].Text = "abc";
        Assert.That(editor.Map.Players[0].StartingBank, Is.EqualTo(new WorldResourceSet(750, 0, 250, 1500, 9999, 0)));
        Assert.That(editor.EconomyRaces[0].StartText, Is.EqualTo("Start: 750 Gold · 1500 Life mana · 9999 Infernal mana · 250 Rune mana"));
        Assert.That((couldUndo, editor.IsDirty), Is.EqualTo((true, true)));

        // Same for all: the Clans get the same, with the Empire's life mana as their rune mana (the two swap).
        editor.CopyBankToAllCommand.Execute(null);
        Assert.That(editor.Map.Players[1].StartingBank, Is.EqualTo(new WorldResourceSet(750, 0, 1500, 250, 9999, 0)));
        editor.SelectedBankRace = editor.BankRaces[1];
        Assert.That(editor.BankFields.Select(field => field.Name).Take(2), Is.EqualTo(new[] { "Gold", "Rune mana" }));
        Assert.That(editor.BankFields[1].Text, Is.EqualTo("1500"));

        // Undo steps: same for all, the infernal field, the rune field, the gold field.
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Players[1].StartingBank, Is.EqualTo(CustomMapPlayer.GetDefaultStartingBank(WorldRace.Dwarf)));
        Assert.That(editor.BankFields[1].Text, Is.EqualTo("1500"), "The Clans' own default.");
        editor.UndoCommand.Execute(null);
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Players[0].StartingBank, Is.EqualTo(new WorldResourceSet(750, 0, 0, 1500, 0, 0)));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Players[0].StartingBank, Is.EqualTo(CustomMapPlayer.GetDefaultStartingBank(WorldRace.Human)));

        // Saved and opened again.
        editor.SelectedBankRace = editor.BankRaces[0];
        editor.BankFields[0].Text = "1234";
        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open(editor.Map.Id, editor.Definitions).Players[0].StartingBank.Gold, Is.EqualTo(1234));
    }

    [Test]
    public async Task SpreadPreviewTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Resource);
        editor.BeginStroke(new WorldPosition(14, 8));
        editor.ToggleEconomyCommand.Execute(null);

        int EmpireLand(WorldState shown) => shown.Map.Cells.Count(cell => cell.Terrain == WorldMapTerrain.Human);
        int now = EmpireLand(editor.RenderRequest.State);
        Assert.That((editor.SpreadTurns, editor.SpreadTurnText, editor.SpreadDownCommand.CanExecute(null)), Is.EqualTo((0, "The map now", false)));
        Assert.That(editor.EconomyRaces[0].Rows[0].Count, Is.Zero, "The mine at 14, 8 is on neutral land now.");

        // +1, +10, -: the map view and the panel show the land after that many turns; the map itself is untouched.
        editor.SpreadUpCommand.Execute(null);
        Assert.That(editor.SpreadTurnText, Is.EqualTo("After 1 turn"));
        int afterOne = EmpireLand(editor.RenderRequest.State);
        Assert.That(afterOne, Is.EqualTo(now + 10), "A capital grows 10 tiles a turn.");
        editor.SpreadUpTenCommand.Execute(null);
        Assert.That(editor.SpreadTurnText, Is.EqualTo("After 11 turns"));
        Assert.That(EmpireLand(editor.RenderRequest.State), Is.GreaterThan(afterOne));
        Assert.That(editor.EconomyRaces[0].Rows[0].Count, Is.EqualTo(1), "By then the Empire's land has reached the mine.");
        Assert.That(editor.EconomyRaces[0].Summary, Does.StartWith($"Land: {EmpireLand(editor.RenderRequest.State)} tiles"));
        Assert.That(editor.Map.Cells.Count(cell => cell.Terrain == WorldMapTerrain.Human), Is.EqualTo(now), "Only a preview.");
        editor.SpreadDownCommand.Execute(null);
        Assert.That(editor.SpreadTurns, Is.EqualTo(10));

        // The selected race's outline follows the land of the turn shown.
        editor.SelectedEconomyRace = editor.EconomyRaces[0];
        Assert.That(editor.PlacementPreview.Highlight, Does.Contain(new WorldPosition(14, 8)));

        // Nothing is edited while a later turn is shown: the hover line says so, a click goes back to the map.
        editor.SelectTool(MapEditorTool.Land);
        editor.UpdateHover(new WorldPosition(30, 30));
        Assert.That(editor.HoverText, Does.StartWith("After 10 turns (preview, nothing is edited): click goes back to the map"));
        bool couldUndo = editor.CanUndo;
        editor.BeginStroke(new WorldPosition(30, 30));
        editor.ContinueStroke(new WorldPosition(31, 30));
        editor.EndStroke();
        Assert.That((editor.SpreadTurns, EmpireLand(editor.RenderRequest.State)), Is.EqualTo((0, now)));
        Assert.That(editor.Map.GetCell(new WorldPosition(30, 30)).Terrain, Is.EqualTo(WorldMapTerrain.Neutral));
        Assert.That(editor.EconomyRaces[0].Rows[0].Count, Is.Zero);

        // After an edit the preview starts from the new map; -10 and Back to the map return to it; closing the panel too.
        editor.SelectedLandTerrain = editor.LandTerrains.Single(option => option.Value == WorldMapTerrain.Human);
        editor.BrushRadius = 0;
        editor.BeginStroke(new WorldPosition(30, 30));
        editor.EndStroke();
        editor.SpreadUpCommand.Execute(null);
        Assert.That(EmpireLand(editor.RenderRequest.State), Is.GreaterThan(now + 10), "The new tile counts, and nothing spreads from it.");
        editor.SpreadDownTenCommand.Execute(null);
        Assert.That(editor.SpreadTurns, Is.Zero);
        editor.SpreadUpTenCommand.Execute(null);
        editor.BackToMapCommand.Execute(null);
        Assert.That(editor.SpreadTurns, Is.Zero);
        editor.SpreadUpTenCommand.Execute(null);
        editor.ToggleEconomyCommand.Execute(null);
        Assert.That((editor.SpreadTurns, EmpireLand(editor.RenderRequest.State)), Is.EqualTo((0, now + 1)));
        Assert.That(couldUndo, Is.True);
    }

    [Test]
    public async Task SiteTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Site);
        Assert.That(editor.SiteTypes.Select(type => type.Value), Is.EqualTo(Enum.GetValues<CustomMapSiteType>()));

        // One site of each type, named and greeting from the game's lists.
        (CustomMapSiteType Type, WorldPosition Tile)[] places =
        [
            (CustomMapSiteType.Merchant, new WorldPosition(20, 20)),
            (CustomMapSiteType.MageTower, new WorldPosition(26, 20)),
            (CustomMapSiteType.Mercenaries, new WorldPosition(20, 26)),
            (CustomMapSiteType.Trainer, new WorldPosition(26, 26))
        ];

        foreach ((CustomMapSiteType type, WorldPosition tile) in places)
        {
            editor.SelectedSiteType = editor.SiteTypes.Single(option => option.Value == type);
            editor.BeginStroke(tile);
        }

        Assert.That(editor.Map.Sites.Select(site => site.Type), Is.EqualTo(Enum.GetValues<CustomMapSiteType>()));
        Assert.That(editor.Map.Sites.All(site => site.Name.Length > 0 && site.Greeting.Length > 0), Is.True);
        Assert.That(editor.State.Sites, Has.Count.EqualTo(4));

        // Merchant: picking an item twice raises its stock.
        editor.BeginStroke(new WorldPosition(20, 20));
        MapEditorSiteScreenViewModel merchant = Assert.IsOfType<MapEditorSiteScreenViewModel>(game.TopScreen);
        merchant.AddCommand.Execute(null);
        ItemDefinitionId item = (ItemDefinitionId)ListPicker(game).Options[3].Value;
        ListPicker(game).Options[3].PickCommand.Execute(null);
        merchant.AddCommand.Execute(null);
        ListPicker(game).Options[3].PickCommand.Execute(null);
        Assert.That(merchant.Goods.Single().Amount, Is.EqualTo(2));
        merchant.Goods.Single().IncreaseCommand.Execute(null);
        merchant.Name = "Ebhon's Shop";
        merchant.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Sites[0].Items.Single(), Is.EqualTo(new CustomMapMerchantItem(item, 3)));
        Assert.That(editor.State.Sites.Values.OfType<WorldMerchantSite>().Single().Items.Single(), Is.EqualTo(new WorldMerchantItem(item, 3)));

        // Mage tower: spells, no duplicates.
        editor.BeginStroke(new WorldPosition(26, 20));
        MapEditorSiteScreenViewModel mage = Assert.IsOfType<MapEditorSiteScreenViewModel>(game.TopScreen);
        mage.AddCommand.Execute(null);
        Assert.That(ListPicker(game).Options, Has.Count.GreaterThan(50), "Every spell of the game.");
        WorldSpellDefinitionId spell = (WorldSpellDefinitionId)ListPicker(game).Options[0].Value;
        ListPicker(game).Options[0].PickCommand.Execute(null);
        mage.AddCommand.Execute(null);
        ListPicker(game).Options[0].PickCommand.Execute(null);
        mage.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Sites[1].Spells, Is.EqualTo(new[] { spell }));

        // Mercenary camp: units with a level from their base level up.
        editor.BeginStroke(new WorldPosition(20, 26));
        MapEditorSiteScreenViewModel camp = Assert.IsOfType<MapEditorSiteScreenViewModel>(game.TopScreen);
        camp.AddCommand.Execute(null);
        MapEditorUnitPickerItemViewModel unit = Picker(game).Options.First(option => !option.IsUnavailable);
        unit.PickCommand.Execute(null);
        Assert.That(camp.Goods.Single().DecreaseCommand.CanExecute(null), Is.False, "Not below the unit's own level.");
        camp.Goods.Single().IncreaseCommand.Execute(null);
        camp.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Sites[2].Units.Single(), Is.EqualTo(new CustomMapMercenary(unit.Definition.Id, unit.Definition.Level + 1)));

        // Trainer: nothing to sell.
        editor.BeginStroke(new WorldPosition(26, 26));
        MapEditorSiteScreenViewModel trainer = Assert.IsOfType<MapEditorSiteScreenViewModel>(game.TopScreen);
        Assert.That((trainer.IsTrainer, trainer.HasGoods, trainer.AddCommand.CanExecute(null)), Is.EqualTo((true, false, false)));
        trainer.CloseCommand.Execute(null);

        editor.ShowPositionInfo(new WorldPosition(20, 20));
        Assert.That(Assert.IsOfType<WorldTileInfoViewModel>(editor.PositionInfo).Title, Does.Contain("Ebhon's Shop"));
        editor.HidePositionInfo();

        editor.SaveCommand.Execute(null);
        Assert.That(loader.CreateMapStore()!.Open("S600SC0000", loader.CreateDefinitions()).Sites, Is.EqualTo(editor.Map.Sites));
    }

    // Events list (V) and the block editor: palette clicks and drops, "or" groups, red blocks, translations, the
    // game's popup as preview, then rename, on/off, duplicate, search, delete and undo in the list.
    [Test]
    public async Task EventScreenTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.Map.PlaceLocation(new WorldPosition(20, 20), 1, "North Gate");
        editor.IsEconomyOpen = true;
        editor.IsEventsOpen = true;
        Assert.That((editor.IsEconomyOpen, editor.HasNoEventRows, editor.EventsTitle), Is.EqualTo((false, true, "Events (0)")), "The lists on the left take turns.");

        editor.NewEventCommand.Execute(null);
        MapEditorEventScreenViewModel screen = Assert.IsOfType<MapEditorEventScreenViewModel>(game.TopScreen);
        Assert.That((editor.Map.Events.Single().Name, editor.CanUndo), Is.EqualTo(("Event 1", true)));
        Assert.That(screen.ProblemText, Does.Contain("add a condition"));
        Assert.That(screen.RaceToggles.Select(toggle => (toggle.Race, toggle.IsChecked)), Is.EqualTo(new[] { (WorldRace.Human, true), (WorldRace.Dwarf, false), (WorldRace.Neutral, false) }));

        MapEditorPaletteBlock Palette(MapEditorBlockKind kind) => screen.ConditionPalette.Concat(screen.EffectPalette).SelectMany(group => group.Blocks).Single(block => block.Kind == kind);

        // Click: conditions go to the last group. "or" opens a second group; a drop puts a block where it lands.
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.EnterZone));
        screen.AddGroupCommand.Execute(null);
        Assert.That(screen.InsertBlock(Palette(MapEditorBlockKind.OnTurn), screen.Groups[1].Blocks, 0), Is.Not.Null);
        Assert.That(screen.InsertBlock(Palette(MapEditorBlockKind.OnTurn), screen.Effects, 0), Is.Null, "A condition can't go under Then.");
        MapEditorEventBlockViewModel message = screen.InsertBlock(Palette(MapEditorBlockKind.Message), screen.Effects, 0)!;
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.Win));
        Assert.That(screen.Groups.Select(group => group.Blocks.Count), Is.EqualTo(new[] { 1, 1 }));
        Assert.That(screen.Effects.Select(block => block.Kind), Is.EqualTo(new[] { MapEditorBlockKind.Message, MapEditorBlockKind.Win }));

        // An empty message is red until it has text; a translation adds a box.
        Assert.That((message.HasProblem, screen.HasProblems), Is.EqualTo((true, true)));
        MapEditorBlockTextField text = message.Parts.OfType<MapEditorBlockTextField>().Single();
        text.Default = "The gates!";
        Assert.That((message.HasProblem, screen.HasProblems), Is.EqualTo((false, false)));
        text.LanguageToAdd = "ru";
        text.AddTranslationCommand.Execute(null);
        text.Translations.Single().Text = "Ворота!";
        Assert.That(text.FreeLanguages, Does.Not.Contain("ru"));

        // Drag Win above the message; settings in plain words.
        Assert.That(screen.MoveBlock(screen.Effects[1], screen.Effects, 0), Is.True);
        Assert.That(screen.MoveBlock(screen.Effects[0], screen.Groups[0].Blocks, 0), Is.False, "An effect can't go under When.");
        screen.Once = false;
        screen.ChanceText = "50";

        // Preview: the game's own story popup, in the chosen language.
        message.Parts.OfType<MapEditorBlockPreview>().Single().PreviewCommand.Execute(null);
        ScenarioEventPopupViewModel popup = Assert.IsOfType<ScenarioEventPopupViewModel>(game.TopScreen);
        Assert.That(string.Concat(popup.Messages.Single().Message.Spans.Select(span => span.Text)), Is.EqualTo("The gates!"));
        popup.Close();
        text.Translations.Single().PreviewCommand.Execute(null);
        popup = Assert.IsOfType<ScenarioEventPopupViewModel>(game.TopScreen);
        Assert.That(string.Concat(popup.Messages.Single().Message.Spans.Select(span => span.Text)), Is.EqualTo("Ворота!"));
        popup.Close();
        Assert.That(game.TopScreen, Is.SameAs(screen));

        screen.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Events.Single(), Is.EqualTo(new CustomMapEvent(
            1,
            "Event 1",
            [WorldRace.Human],
            null,
            Once: false,
            Enabled: true,
            Chance: 50,
            Order: 0,
            [[new CustomMapEnterZoneCondition(1)], [new CustomMapTurnCondition(5, Repeat: false)]],
            [new CustomMapWinEffect(WorldRace.Human), new CustomMapMessageEffect(CustomMapText.Of("The gates!").WithTranslation("ru", "Ворота!"), CustomMapMessageAudience.Triggerer, null, true)])));

        // The list row: details, rename, on/off, duplicate, search, delete, undo.
        MapEditorEventRowViewModel row = editor.EventRows.Single();
        Assert.That((row.Name, row.Details, row.HasProblem, row.IsEnabled), Is.EqualTo(("Event 1", "Empire · 2 conditions · 2 effects", false, true)));
        row.BeginEditCommand.Execute(null);
        row.EditText = " Gates ";
        row.CommitEditCommand.Execute(null);
        Assert.That(editor.Map.Events.Single().Name, Is.EqualTo("Gates"));
        editor.EventRows.Single().IsEnabled = false;
        Assert.That(editor.Map.Events.Single().Enabled, Is.False);
        editor.EventRows.Single().DuplicateCommand.Execute(null);
        Assert.That(editor.EventRows.Select(r => r.Name), Is.EqualTo(new[] { "Gates", "Gates (copy)" }));
        editor.EventSearch = "copy";
        Assert.That(editor.EventRows.Select(r => r.Name), Is.EqualTo(new[] { "Gates (copy)" }));
        editor.EventSearch = string.Empty;
        editor.UndoCommand.Execute(null);
        Assert.That(editor.EventRows.Select(r => r.Name), Is.EqualTo(new[] { "Gates" }), "Undo removes the copy.");
        editor.EventRows.Single().DeleteCommand.Execute(null);
        Assert.That((editor.Map.Events.Count, editor.HasNoEventRows), Is.EqualTo((0, true)));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.EventRows.Single().Name, Is.EqualTo("Gates"));

        // A removed zone turns the event red in the list; the map is saved without it but keeps it for later.
        editor.Map.RemoveLocation(1);
        editor.EventSearch = " ";
        Assert.That(editor.EventRows.Single().HasProblem, Is.True);
        editor.SaveCommand.Execute(null);
        CustomMap reopened = loader.CreateMapStore()!.Open(editor.Map.Id, loader.CreateDefinitions());
        Assert.That(reopened.Events.Single().Name, Is.EqualTo("Gates"));
        Assert.That(reopened.GetSaveableEvents(), Is.Empty);
    }

    // Phase 3: "used by" in the hover line, picking a block's target on the map, a warning before deleting what
    // events use (once; then the event turns red and is left out of the save), and the save note.
    [Test]
    public async Task EventSafetyTest()
    {
        using Sandbox sb = Sandbox.Create();
        (GameViewModel game, GameLoader loader) = Build(sb);
        using GameViewModel disposeGame = game;

        MapEditorScreenViewModel editor = PlaceCapitals(await CreateMapAsync(game, WorldRace.Human, WorldRace.Dwarf));
        editor.SelectTool(MapEditorTool.Village);
        editor.BeginStroke(new WorldPosition(24, 24));
        CustomMapVillage village = editor.Map.Villages.Single();
        editor.Map.PlaceLocation(new WorldPosition(20, 14), 1, "North Gate");
        editor.Map.PlaceLocation(new WorldPosition(30, 14), 0, "Trap");
        editor.Map.AddEvent(CustomMapEvent.Create("Take the village", WorldRace.Human) with
        {
            Groups = [[new CustomMapOwnCityCondition(new CustomMapCity.Village(village.Id))]],
            Effects = [new CustomMapWinEffect(WorldRace.Human)]
        });
        editor.Map.AddEvent(CustomMapEvent.Create("Gate", WorldRace.Human) with
        {
            Groups = [[new CustomMapEnterZoneCondition(1)]],
            Effects = [new CustomMapSwitchEventEffect(1, On: true)]
        });
        editor.SelectTool(MapEditorTool.Land);

        // Hover: what the objects under the cursor are used by.
        editor.UpdateHover(new WorldPosition(20, 14));
        Assert.That(editor.HoverText, Does.EndWith("Locations here: North Gate\nUsed by events: Gate"));
        editor.UpdateHover(new WorldPosition(village.TopLeft.X + 1, village.TopLeft.Y + 1));
        Assert.That(editor.HoverText, Does.EndWith("Used by events: Take the village"));

        // Pick on the map: the screen steps aside, the zones glow, a miss keeps picking, a hit comes back.
        editor.IsEventsOpen = true;
        editor.OpenEvent(2);
        MapEditorEventScreenViewModel screen = Assert.IsOfType<MapEditorEventScreenViewModel>(game.TopScreen);
        MapEditorBlockChoice zone = screen.Groups[0].Blocks[0].Parts.OfType<MapEditorBlockChoice>().Single();
        Assert.That((zone.CanPick, zone.Value), Is.EqualTo((true, (object)1)));
        zone.PickOnMapCommand.Execute(null);
        Assert.That((game.TopScreen, editor.IsPickingEventTarget, editor.ShowLocations, editor.ShowEventsList), Is.EqualTo(((object)editor, true, true, false)));
        editor.UpdateHover(new WorldPosition(5, 40));
        Assert.That(editor.PlacementPreview.Highlight, Is.EquivalentTo(editor.Map.Locations.SelectMany(location => location.Footprint.EnumerateTiles())));
        Assert.That(editor.HoverText, Does.StartWith("Pick a zone: click one of the gold squares"));
        editor.BeginStroke(new WorldPosition(5, 40));
        editor.EndStroke();
        Assert.That((editor.IsPickingEventTarget, editor.Map.Locations.Count), Is.EqualTo((true, 2)), "A miss places nothing and keeps picking.");
        editor.BeginStroke(new WorldPosition(30, 14));
        editor.EndStroke();
        Assert.That((game.TopScreen, editor.IsPickingEventTarget, zone.Value), Is.EqualTo(((object)screen, false, (object)2)));

        // Esc cancels: the screen comes back unchanged.
        zone.PickOnMapCommand.Execute(null);
        Assert.That(editor.CancelEventPick(), Is.True);
        Assert.That((game.TopScreen, zone.Value), Is.EqualTo(((object)screen, (object)2)));
        screen.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.GetEvent(2)!.Groups[0][0], Is.EqualTo(new CustomMapEnterZoneCondition(2)));

        // Deleting an unused zone asks nothing; a used one asks once, and No keeps it.
        editor.DeleteLocation(1);
        Assert.That((editor.Map.Locations.Single().Name, game.TopScreen), Is.EqualTo(("Trap", (object)editor)));
        editor.DeleteLocation(2);
        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(game.TopScreen);
        Assert.That(prompt.Message.Text, Does.Contain("\"Trap\" is used by the event Gate"));
        prompt.Close();
        Assert.That(editor.Map.Locations, Has.Count.EqualTo(1));

        // The village from its screen: Delete anyway removes it and the event turns red.
        editor.SelectTool(MapEditorTool.Village);
        editor.BeginStroke(new WorldPosition(village.TopLeft.X + 1, village.TopLeft.Y + 1));
        Assert.IsOfType<MapEditorVillageScreenViewModel>(game.TopScreen).DeleteCommand.Execute(null);
        prompt = Assert.IsOfType<ConfirmPromptViewModel>(game.TopScreen);
        await prompt.ConfirmCommand.ExecuteAsync(null);
        Assert.That(editor.Map.Villages, Is.Empty);
        Assert.That(editor.EventRows.Select(row => (row.Name, row.HasProblem)), Is.EquivalentTo(new[] { ("Take the village", true), ("Gate", true) }), "Gate switches it, so Gate is left out too.");
        editor.OpenEvent(2);
        Assert.That(Assert.IsOfType<MapEditorEventScreenViewModel>(game.TopScreen).ProblemText, Is.EqualTo("Not saved with the map yet: it switches an event that is left out."));
        ((MapEditorEventScreenViewModel)game.TopScreen).Close();

        // Deleting an event that another event switches asks too.
        editor.DeleteEvent(1);
        Assert.That(Assert.IsOfType<ConfirmPromptViewModel>(game.TopScreen).Message.Text, Does.Contain("is used by the event Gate"));
        ((ConfirmPromptViewModel)game.TopScreen).Close();

        // Saving says which events were left out; Undo brings the village back and the event is fine again.
        editor.SaveCommand.Execute(null);
        Assert.That(editor.Status, Is.EqualTo("Saved. Play it from New quest. 2 events were left out of the game until they are fixed: Take the village, Gate."));
        Assert.That(game.Alert.Current!.Details, Is.EqualTo("2 events were left out of the game until they are fixed: Take the village, Gate."));
        editor.UndoCommand.Execute(null);
        Assert.That((editor.Map.Villages.Count, editor.EventRows.Single(row => row.Name == "Take the village").HasProblem), Is.EqualTo((1, false)));
    }
}
