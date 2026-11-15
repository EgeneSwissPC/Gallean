using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
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

        // WASD moves the camera like in the game, also when a panel control has focus.
        view.GetVisualDescendants().OfType<Avalonia.Controls.ListBox>().First().Focus();
        Point before = view.Camera.Center;
        window.KeyPress(Key.S, RawInputModifiers.None, PhysicalKey.S, "s");
        await SkipFramesAsync(15);
        window.KeyRelease(Key.S, RawInputModifiers.None, PhysicalKey.S, "s");
        window.KeyPress(Key.D, RawInputModifiers.None, PhysicalKey.D, "d");
        await SkipFramesAsync(5);
        window.KeyRelease(Key.D, RawInputModifiers.None, PhysicalKey.D, "d");
        await SkipFramesAsync(10);
        Point moved = view.Camera.Center;
        TestContext.Out.WriteLine($"camera before {before} after {moved} zoom {view.Camera.Zoom} focused {Avalonia.Controls.TopLevel.GetTopLevel(view)?.FocusManager?.GetFocusedElement()}");
        Assert.That(moved.X, Is.GreaterThan(before.X), "D moves right.");
        Assert.That(moved.Y, Is.GreaterThan(before.Y), "S moves down.");
        window.KeyPress(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        window.KeyPress(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
        await SkipFramesAsync(5);
        window.KeyRelease(Key.A, RawInputModifiers.None, PhysicalKey.A, "a");
        window.KeyRelease(Key.W, RawInputModifiers.None, PhysicalKey.W, "w");
        await SkipFramesAsync(10);
        Assert.That(view.Camera.Center.X, Is.LessThan(moved.X), "A moves left.");
        Assert.That(view.Camera.Center.Y, Is.LessThan(moved.Y), "W moves up.");
        Assert.That(editor.SelectedTool.Value, Is.EqualTo(MapEditorTool.Water), "The tool list did not react to the keys.");
        view.Camera.LookAt(before);

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

    [AvaloniaTest]
    public async Task MapEditorArmyTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        await WaitForMapEditorAsync(window);

        editor.SelectTool(MapEditorTool.Army);
        MapEditorSlotViewModel top = editor.Army.Slots.Single(slot => slot.Position == GridPosition.FrontTop);
        top.SelectedOption = top.Options.First(option => option.Unit is { IsLarge: true, IsLeader: false });
        editor.BeginStroke(new WorldPosition(18, 18));
        editor.BeginStroke(new WorldPosition(14, 24));
        editor.Army.SelectedOwner = editor.Army.Owners.Single(owner => owner.Value == WorldRace.Human);
        Assert.That(editor.Army.IsValid, Is.False, "A level 1 human leader cannot lead the titan.");
        top.SelectedOption = top.Options[0];
        Assert.That(editor.Army.IsValid, Is.True, editor.Army.Status);
        editor.BeginStroke(new WorldPosition(16, 12));

        Assert.That(editor.Map.Stacks, Has.Count.EqualTo(3));

        MapEditorScreenView view = window.GetVisualDescendants().OfType<MapEditorScreenView>().First();
        await WaitUntilAsync(() => view.SnapshotWorker?.Current?.RenderRequest.State.Stacks.Count == 5);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(16, 18)));
        view.GetVisualDescendants().OfType<Avalonia.Controls.ScrollViewer>().First().ScrollToEnd();
        await SkipFramesAsync(10);

        await CaptureAsync(window, "1");

        view.Camera.SetZoom(1.5);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(17, 18)));
        await SkipFramesAsync(10);

        await CaptureAsync(window, "2");
    }
}
