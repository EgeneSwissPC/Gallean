using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

public sealed partial class VisualTests
{
    private (GameViewModel Game, GameView Window, GameLoader Loader) InitializeMapEditor(Sandbox sb)
    {
        GameLoader loader = new(new RandomProvider(0), sb.GetPath("maps"));
        NullAudioPlayer audioPlayer = new();
        GameViewModel gameViewModel = new TestGameViewModelBuilder()
            .WithAssets(assets)
            .WithAudioPlayer(audioPlayer)
            .WithGameLoader(loader)
            .WithSaveStore(new GameSaveStore(sb.GetPath("saves"), 10))
            .WithLeaderStore(new LeaderStore(sb.GetPath("leaders")))
            .WithSettingsStore(new GameSettingsStore(sb.GetPath("config")))
            .Build();

        GameView view = new()
        {
            DataContext = gameViewModel,
            WindowState = Avalonia.Controls.WindowState.Normal,
            SizeToContent = Avalonia.Controls.SizeToContent.Manual,
            Width = BaseWidth,
            Height = BaseHeight
        };

        AssetScope.SetAssets(view, assets);
        AudioScope.SetAudio(view, audioPlayer);
        RegisterWindow(view);
        cleanupList.Add(gameViewModel);
        view.Show();

        return (gameViewModel, view, loader);
    }

    private static CustomMap CreateSampleMap(string id, string name)
    {
        CustomMap map = CustomMap.Create(id, name, 48, WorldMapTerrain.Neutral, [WorldRace.Human, WorldRace.Undead]);
        Random random = new(3);

        map.Paint(new WorldPosition(24, 24), 5, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(12, 14), 3, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(16, 30), 2, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random);
        map.Paint(new WorldPosition(30, 12), 3, CustomMapBrush.Land, WorldMapTerrain.Undead, random);
        map.Paint(new WorldPosition(10, 10), 3, CustomMapBrush.Land, WorldMapTerrain.Human, random);
        map.PlaceCapital(WorldRace.Human, new WorldPosition(8, 8));
        map.PlaceCapital(WorldRace.Undead, new WorldPosition(34, 8));

        return map;
    }

    private static async Task<MapEditorScreenView> WaitForMapEditorAsync(GameView window)
    {
        await SkipFramesAsync(5);

        MapEditorScreenView view = window.GetVisualDescendants().OfType<MapEditorScreenView>().First();
        WorldScreenSnapshotWorker worker = Assert.NotNull(view.SnapshotWorker);

        await WaitUntilAsync(() => worker.Exception is not null || worker.Current is not null);

        Assert.That(worker.Exception, Is.Null);
        Assert.That(worker.Current, Is.Not.Null);

        return view;
    }

    [AvaloniaTest]
    public async Task MapEditorMenuTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());
        store.Save(CreateSampleMap("S600SC0000", "Two kingdoms"));
        store.Save(CreateSampleMap("S601SC0000", "Lakeside"));

        gameViewModel.ShowMapEditorCommand.Execute(null);

        await CaptureAsync(window);
    }

    [AvaloniaTest]
    public async Task MapEditorTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        await WaitForMapEditorAsync(window);

        await CaptureAsync(window, "1");

        // Paint with the mouse, as a player would: drag from tile (14, 22) to (22, 22).
        MapEditorScreenView view = window.GetVisualDescendants().OfType<MapEditorScreenView>().First();
        MapEditorVisual map = view.GetVisualDescendants().OfType<MapEditorVisual>().First();
        await SkipFramesAsync(10);

        Point ToWindow(int x, int y)
        {
            Point screen = WorldTransform.ToWorldPoint(new WorldPosition(x, y)).Transform(view.Camera.GetViewMatrix());
            return Assert.NotNull(map.TranslatePoint(screen, window));
        }

        editor.SelectTool(MapEditorTool.Water);
        editor.BrushRadius = 1;
        window.MouseMove(ToWindow(14, 22));
        window.MouseDown(ToWindow(14, 22), MouseButton.Left);

        for (int x = 15; x <= 22; x++)
        {
            window.MouseMove(ToWindow(x, 22));
        }

        window.MouseUp(ToWindow(22, 22), MouseButton.Left);

        Assert.That(editor.Map.GetCell(new WorldPosition(18, 22)).Terrain, Is.EqualTo(WorldMapTerrain.Water));
        Assert.That(editor.CanUndo, Is.True);

        await SkipFramesAsync(10);

        await CaptureAsync(window, "2");
    }

    [AvaloniaTest]
    public async Task MapEditorLargeTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ =>
        {
            CustomMap map = CustomMap.Create("S600SC0000", "Big", 512, WorldMapTerrain.Water, [WorldRace.Elf]);
            Random random = new(4);

            map.Paint(new WorldPosition(256, 256), 8, CustomMapBrush.Land, WorldMapTerrain.Neutral, random);
            map.Paint(new WorldPosition(250, 252), 3, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
            map.PlaceCapital(WorldRace.Elf, new WorldPosition(256, 256));

            return map;
        });
        await WaitForMapEditorAsync(window);

        await CaptureAsync(window);
    }

    [AvaloniaTest]
    public async Task MapEditorPlayedMapTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        Assert.NotNull(loader.CreateMapStore()).Save(CreateSampleMap("S600SC0000", "Two kingdoms"));

        ScenarioConfiguration configuration = new(
            [new ScenarioPlayerConfiguration(WorldRace.Human, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)],
            WorldDifficulty.Average,
            Leader: null);
        (GameSession session, IGameTextProvider texts) = loader.CreateScenario("S600SC0000", configuration, Language.English);
        GameModel gameModel = cleanupList.Add(new GameModel(session, texts, NullLog.Instance, SynchronousTaskScheduler.Instance));

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        gameViewModel.ShowScreen(new WorldScreenViewModel(gameModel, gameViewModel, new RandomProvider(0)));
        await WaitForWorldAsync(window);

        Assert.That(gameModel.ScenarioName, Is.EqualTo("Two kingdoms"));

        await CaptureAsync(window);
    }
}
