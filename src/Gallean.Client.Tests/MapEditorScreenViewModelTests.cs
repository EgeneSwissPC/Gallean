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
        Assert.That(game.TopScreen, Is.SameAs(editor), "Capitals are blocked.");

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

        // The Army tool on a village opens the army stationed in it, owned by the village's owner.
        editor.SelectTool(MapEditorTool.Army);
        editor.SelectedArmyOwner = editor.ArmyOwners.Single(owner => owner.Value == WorldRace.Neutral);
        editor.BeginStroke(new WorldPosition(23, 23));
        MapEditorArmyScreenViewModel army = Assert.IsOfType<MapEditorArmyScreenViewModel>(game.TopScreen);
        Assert.That(army.Owner, Is.EqualTo(WorldRace.Human));
        army.ChooseLeaderCommand.Execute(null);
        Picker(game).Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge).PickCommand.Execute(null);
        army.ConfirmCommand.Execute(null);
        CustomMapStack stationed = editor.Map.Stacks.Single();
        Assert.That((stationed.Position, stationed.Owner), Is.EqualTo((new WorldPosition(25, 25), WorldRace.Human)));
        WorldCity withArmy = editor.State.Fortifications.Values.OfType<WorldCity>().Single();
        Assert.That(editor.State.Stacks.Values.Count(stack => stack.Position == withArmy.Entrance), Is.EqualTo(1));
        editor.ShowPositionInfo(new WorldPosition(23, 23));
        Assert.That(Assert.IsOfType<WorldFortificationInfoViewModel>(editor.PositionInfo).VisitingStack, Is.Not.Null, "The city info shows the stationed army.");
        editor.HidePositionInfo();
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
}
