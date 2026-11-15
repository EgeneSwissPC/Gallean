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

    private static void Select(MapEditorScreenViewModel editor, GridPosition position, Func<MapEditorUnitOption, bool> predicate)
    {
        MapEditorSlotViewModel slot = editor.Army.Slots.Single(candidate => candidate.Position == position);
        slot.SelectedOption = slot.Options.First(option => option.Unit is not null && predicate(option));
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

        // Neutral owner starts with a neutral leader in the front center; add a large unit and a soldier.
        editor.SelectTool(MapEditorTool.Army);
        Assert.That(editor.Army.Owner, Is.EqualTo(WorldRace.Neutral));
        Assert.That(editor.Army.Slots.Single(slot => slot.Position == GridPosition.FrontCenter).SelectedOption?.Unit, Is.Not.Null.And.Property(nameof(CustomMapUnit.IsLeader)).True);
        Assert.That(editor.Army.IsValid, Is.True);

        Select(editor, GridPosition.FrontTop, option => option.Unit is { IsLarge: true, IsLeader: false });
        MapEditorSlotViewModel backTop = editor.Army.Slots.Single(slot => slot.Position == GridPosition.BackTop);
        Assert.That(backTop.IsEnabled, Is.False, "A large unit fills both lines.");
        Assert.That(backTop.Options.Any(option => option.Unit is { IsLarge: true }), Is.False, "Large units only go in the front line.");
        Select(editor, GridPosition.BackBottom, option => option.Unit is { IsLarge: false, IsLeader: false });
        Assert.That(editor.Army.IsValid, Is.True, editor.Army.Status);

        editor.BeginStroke(new WorldPosition(24, 24));
        CustomMapStack placed = editor.Map.Stacks.Single();
        Assert.That(placed.Position, Is.EqualTo(new WorldPosition(24, 24)));
        Assert.That(editor.SelectedArmyId, Is.EqualTo(placed.Id));
        Assert.That(editor.RenderRequest.Cursor.StackId, Is.EqualTo(new WorldStackId("S600KC0101")));

        // Two leaders are refused.
        Select(editor, GridPosition.BackCenter, option => option.Unit is { IsLeader: true, IsLarge: false });
        Assert.That(editor.Army.IsValid, Is.False);
        editor.BeginStroke(new WorldPosition(30, 20));
        Assert.That(editor.Map.Stacks, Has.Count.EqualTo(1));

        // Select the army again by clicking it, give it to the Dwarves, apply.
        editor.BeginStroke(new WorldPosition(10, 30));
        editor.BeginStroke(new WorldPosition(24, 24));
        Assert.That(editor.Army.Owner, Is.EqualTo(WorldRace.Neutral));
        editor.Army.SelectedOwner = editor.Army.Owners.Single(owner => owner.Value == WorldRace.Dwarf);

        // The neutral leader is replaced by a Dwarf leader; neutral soldiers may stay.
        Assert.That(editor.Army.Slots.Select(slot => slot.SelectedOption?.Unit).Count(unit => unit is { IsLeader: true }), Is.EqualTo(1));
        Assert.That(editor.Army.Slots.Single(slot => slot.SelectedOption?.Unit is { IsLeader: true }).SelectedOption!.Race, Is.EqualTo(WorldRace.Dwarf));
        Assert.That(editor.Army.Slots.All(slot => slot.Options.All(option => option.Unit is not { IsLeader: true } || option.Race == WorldRace.Dwarf)), Is.True);
        editor.ApplyArmyCommand.Execute(null);
        Assert.That(editor.Map.Stacks.Single().Owner, Is.EqualTo(WorldRace.Dwarf));

        editor.SaveCommand.Execute(null);
        Assert.That(editor.IsDirty, Is.False);

        CustomMapStore store = loader.CreateMapStore()!;
        WorldState saved = store.BuildPreview(store.Open("S600SC0000", loader.CreateDefinitions()), loader.CreateDefinitions()).State;
        WorldStack army = saved.Stacks.Values.Single(stack => stack.Position == new WorldPosition(24, 24));
        Assert.That(saved.Players[army.OwnerId].Race, Is.EqualTo(WorldRace.Dwarf));

        editor.DeleteArmyCommand.Execute(null);
        Assert.That(editor.Map.Stacks, Is.Empty);
        Assert.That(editor.SelectedArmyId, Is.Null);

        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Stacks.Single().Owner, Is.EqualTo(WorldRace.Dwarf));
        editor.UndoCommand.Execute(null);
        Assert.That(editor.Map.Stacks.Single().Owner, Is.EqualTo(WorldRace.Neutral));
    }
}
