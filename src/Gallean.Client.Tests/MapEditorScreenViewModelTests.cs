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
}
