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
}
