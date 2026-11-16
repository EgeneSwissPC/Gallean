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
        MapEditorScreenView view = await WaitForMapEditorAsync(window);

        // The minimap frame must stay on the minimap image on large maps.
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(256, 256)));
        await SkipFramesAsync(10);
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
        MapEditorScreenView view = await WaitForMapEditorAsync(window);

        // Click free land with the Army tool: the army screen opens and asks for a leader.
        editor.SelectTool(MapEditorTool.Army);
        editor.BeginStroke(new WorldPosition(18, 18));
        MapEditorArmyScreenViewModel army = Assert.IsOfType<MapEditorArmyScreenViewModel>(gameViewModel.TopScreen);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "1-empty");

        army.ChooseLeaderCommand.Execute(null);
        MapEditorUnitPickerViewModel leaders = Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "2-leaders");
        leaders.Options.First(option => option.Definition is { IsLarge: false, Leadership: >= 4 }).PickCommand.Execute(null);

        army.AddUnit(GridPosition.FrontTop);
        MapEditorUnitPickerViewModel soldiers = Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "3-units");
        soldiers.Options.First(option => option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        army.AddUnit(GridPosition.BackCenter);
        Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen).Options.First(option => !option.IsUnavailable).PickCommand.Execute(null);
        army.AddUnit(GridPosition.FrontBottom);
        Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen).Options.Last(option => !option.IsUnavailable && !option.Definition.IsLarge).PickCommand.Execute(null);

        // Drag the large unit from the top row to the bottom row with the mouse, as in the game.
        MapEditorArmyScreenView armyView = window.GetVisualDescendants().OfType<MapEditorArmyScreenView>().First();
        await SkipFramesAsync(10);
        Avalonia.Controls.ItemsControl grid = armyView.GetVisualDescendants().OfType<Avalonia.Controls.ItemsControl>().First(control => control.Name == "PartyGrid");
        Point SlotCenter(GridPosition position)
        {
            UnitGridSlotViewModel slot = army.UnitGrid.Slots.Single(candidate => candidate.Position == position);
            Avalonia.Controls.Control container = Assert.NotNull(grid.ContainerFromItem(slot));
            return Assert.NotNull(container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window));
        }

        Point from = SlotCenter(GridPosition.FrontTop);
        Point to = SlotCenter(GridPosition.FrontBottom);
        window.MouseMove(from);
        window.MouseDown(from, MouseButton.Left);
        for (int step = 1; step <= 10; step++)
        {
            window.MouseMove(new Point(from.X + ((to.X - from.X) * step / 10), from.Y + ((to.Y - from.Y) * step / 10)));
            await SkipFramesAsync(1);
        }
        window.MouseUp(to, MouseButton.Left);
        await SkipFramesAsync(5);
        Assert.That(army.Slots[GridPosition.FrontBottom.ToIndex()], Is.Not.Null.And.Property(nameof(CustomMapUnit.IsLarge)).True, "Dragging moved the large unit to the bottom row.");

        await SkipFramesAsync(10);
        await CaptureAsync(window, "4-party");

        Assert.That(army.ConfirmCommand.CanExecute(null), Is.True, army.Problem);
        army.ConfirmCommand.Execute(null);
        await WaitUntilAsync(() => view.SnapshotWorker?.Current?.RenderRequest.State.Stacks.Count == 3);

        // Right-click the army on the map: the game's army info popup.
        view.Camera.SetZoom(1.25);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(16, 18)));
        await SkipFramesAsync(10);
        MapEditorVisual map = view.GetVisualDescendants().OfType<MapEditorVisual>().First();
        Point armyPoint = Assert.NotNull(map.TranslatePoint(WorldTransform.ToWorldPoint(new WorldPosition(18, 18)).Transform(view.Camera.GetViewMatrix()), window));
        window.MouseMove(armyPoint);
        window.MouseDown(armyPoint, MouseButton.Right);
        await SkipFramesAsync(10);
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldStackInfoViewModel>());
        await CaptureAsync(window, "5-info");
        window.MouseUp(armyPoint, MouseButton.Right);
        Assert.That(editor.PositionInfo, Is.Null);

        // Moving the mouse to the edge of the map view scrolls, as in the game (checked below).
        Point center = view.Camera.Center;
        Point leftEdge = Assert.NotNull(map.TranslatePoint(new Point(4, map.Bounds.Height / 2), window));
        window.MouseMove(new Point(leftEdge.X + 40, leftEdge.Y));
        window.MouseMove(leftEdge);
        await SkipFramesAsync(15);
        Assert.That(view.Camera.Center.X, Is.LessThan(center.X), "The left edge scrolls left.");
        window.MouseMove(Assert.NotNull(map.TranslatePoint(new Point(map.Bounds.Width / 2, map.Bounds.Height / 2), window)));
    }

    [AvaloniaTest]
    public async Task MapEditorVillagesTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);
        MapEditorVisual map = view.GetVisualDescendants().OfType<MapEditorVisual>().First();

        // One village of each level: neutral ones in a row, the last two owned by the players.
        editor.SelectTool(MapEditorTool.Village);
        (int X, int Y, int Level, WorldRace Owner)[] villages =
        [
            (4, 22, 1, WorldRace.Neutral),
            (9, 22, 2, WorldRace.Neutral),
            (14, 22, 3, WorldRace.Neutral),
            (8, 16, 4, WorldRace.Human),
            (38, 18, 5, WorldRace.Undead)
        ];

        foreach ((int x, int y, int level, WorldRace owner) in villages)
        {
            editor.SelectedVillageLevel = editor.VillageLevels.Single(option => option.Value == level);
            editor.SelectedVillageOwner = editor.VillageOwners.Single(option => option.Value == owner);
            editor.BeginStroke(new WorldPosition(x, y));
        }

        Assert.That(editor.Map.Villages.Select(village => village.Level), Is.EqualTo(new[] { 1, 2, 3, 4, 5 }));

        // Every resource type next to the villages.
        editor.SelectTool(MapEditorTool.Resource);
        WorldResourceType[] types = [WorldResourceType.Gold, WorldResourceType.LifeMana, WorldResourceType.InfernalMana, WorldResourceType.DeathMana, WorldResourceType.RuneMana, WorldResourceType.GroveMana];

        for (int i = 0; i < types.Length; i++)
        {
            editor.SelectedResourceType = editor.ResourceTypes.Single(option => option.Value == types[i]);
            editor.BeginStroke(new WorldPosition(4 + (i * 2), 26));
        }

        Assert.That(editor.Map.Resources, Has.Count.EqualTo(6));
        await WaitUntilAsync(() => view.SnapshotWorker?.Current?.RenderRequest.State.Fortifications.Values.OfType<WorldCity>().Count() == 5 &&
                                   view.SnapshotWorker.Current.RenderRequest.State.ResourceNodes.Count == 6);

        view.Camera.SetZoom(1);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(12, 22)));
        editor.UpdateHover(new WorldPosition(15, 23));
        await SkipFramesAsync(10);
        await CaptureAsync(window, "1-map");

        // Right-click a village: the game's city info.
        Point villagePoint = Assert.NotNull(map.TranslatePoint(WorldTransform.ToWorldPoint(new WorldPosition(15, 23)).Transform(view.Camera.GetViewMatrix()), window));
        window.MouseMove(villagePoint);
        window.MouseDown(villagePoint, MouseButton.Right);
        await SkipFramesAsync(10);
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldFortificationInfoViewModel>());
        await CaptureAsync(window, "2-village-info");
        window.MouseUp(villagePoint, MouseButton.Right);

        // Right-click a mine.
        Point minePoint = Assert.NotNull(map.TranslatePoint(WorldTransform.ToWorldPoint(new WorldPosition(4, 26)).Transform(view.Camera.GetViewMatrix()), window));
        window.MouseMove(minePoint);
        window.MouseDown(minePoint, MouseButton.Right);
        await SkipFramesAsync(10);
        Assert.That(editor.PositionInfo, Is.InstanceOf<WorldTileInfoViewModel>());
        await CaptureAsync(window, "3-mine-info");
        window.MouseUp(minePoint, MouseButton.Right);

        // Click the level 3 village: the village screen; fill its garrison.
        editor.SelectTool(MapEditorTool.Village);
        editor.BeginStroke(new WorldPosition(15, 23));
        MapEditorVillageScreenViewModel screen = Assert.IsOfType<MapEditorVillageScreenViewModel>(gameViewModel.TopScreen);
        screen.AddUnit(GridPosition.FrontTop);
        Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen).Options.First(option => option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        screen.AddUnit(GridPosition.FrontCenter);
        Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen).Options.First(option => !option.IsUnavailable).PickCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "4-village-screen");

        screen.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Villages.Single(village => village.Level == 3).Garrison.Count(unit => unit is not null), Is.EqualTo(2));
    }
}
