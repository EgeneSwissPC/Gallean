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

    [AvaloniaTest]
    public async Task MapEditorRuinsAndSitesTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);

        MapEditorListPickerViewModel ListPicker() => Assert.IsOfType<MapEditorListPickerViewModel>(gameViewModel.TopScreen);
        MapEditorUnitPickerViewModel UnitPicker() => Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen);

        // A ruin, treasure on land and water, one site of each type and a village with an army stationed in it.
        editor.SelectTool(MapEditorTool.Ruin);
        editor.BeginStroke(new WorldPosition(5, 22));
        editor.SelectTool(MapEditorTool.Bag);
        editor.BeginStroke(new WorldPosition(9, 21));
        editor.BeginStroke(new WorldPosition(20, 24));
        editor.SelectTool(MapEditorTool.Site);
        (CustomMapSiteType Type, int X, int Y)[] sites =
        [
            (CustomMapSiteType.Merchant, 13, 22),
            (CustomMapSiteType.MageTower, 12, 27),
            (CustomMapSiteType.Mercenaries, 8, 27),
            (CustomMapSiteType.Trainer, 4, 27)
        ];

        foreach ((CustomMapSiteType type, int x, int y) in sites)
        {
            editor.SelectedSiteType = editor.SiteTypes.Single(option => option.Value == type);
            editor.BeginStroke(new WorldPosition(x, y));
        }

        editor.SelectTool(MapEditorTool.Village);
        editor.SelectedVillageOwner = editor.VillageOwners.Single(option => option.Value == WorldRace.Human);
        editor.BeginStroke(new WorldPosition(10, 16));
        editor.BeginStroke(new WorldPosition(10, 16));
        MapEditorVillageScreenViewModel village = Assert.IsOfType<MapEditorVillageScreenViewModel>(gameViewModel.TopScreen);
        village.ChooseArmyLeaderCommand.Execute(null);
        UnitPicker().Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge).PickCommand.Execute(null);
        village.ConfirmCommand.Execute(null);

        Assert.That((editor.Map.Ruins.Count, editor.Map.Bags.Count, editor.Map.Sites.Count, editor.Map.Stacks.Count), Is.EqualTo((1, 2, 4, 1)));
        await WaitUntilAsync(() => view.SnapshotWorker?.Current?.RenderRequest.State is { } shown && shown.Sites.Count == 4 && shown.Bags.Count == 2 && shown.Stacks.Count == 3);

        view.Camera.SetZoom(1);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(11, 22)));
        await SkipFramesAsync(10);
        await CaptureAsync(window, "1-map");

        // Ruin: reward and guards.
        editor.SelectTool(MapEditorTool.Ruin);
        editor.BeginStroke(new WorldPosition(5, 22));
        MapEditorRuinScreenViewModel ruin = Assert.IsOfType<MapEditorRuinScreenViewModel>(gameViewModel.TopScreen);
        ruin.GoldText = "750";
        ruin.ChooseItemCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "2-items");
        ListPicker().Options.First(option => option.Group == ListPicker().Filters[3]).PickCommand.Execute(null);
        ruin.AddUnit(GridPosition.FrontTop);
        UnitPicker().Options.First(option => option.Definition.IsLarge && !option.IsUnavailable).PickCommand.Execute(null);
        ruin.AddUnit(GridPosition.FrontCenter);
        UnitPicker().Options.First(option => !option.IsUnavailable).PickCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "3-ruin");
        ruin.ConfirmCommand.Execute(null);

        // Treasure.
        editor.SelectTool(MapEditorTool.Bag);
        editor.BeginStroke(new WorldPosition(9, 21));
        MapEditorBagScreenViewModel bag = Assert.IsOfType<MapEditorBagScreenViewModel>(gameViewModel.TopScreen);

        foreach (int index in new[] { 0, 40, 80, 120 })
        {
            bag.AddItemCommand.Execute(null);
            ListPicker().Options[index].PickCommand.Execute(null);
        }

        await SkipFramesAsync(10);
        await CaptureAsync(window, "4-treasure");
        bag.ConfirmCommand.Execute(null);

        // Merchant, mage tower, mercenary camp.
        editor.SelectTool(MapEditorTool.Site);
        editor.BeginStroke(new WorldPosition(13, 22));
        MapEditorSiteScreenViewModel merchant = Assert.IsOfType<MapEditorSiteScreenViewModel>(gameViewModel.TopScreen);

        foreach (int index in new[] { 10, 50, 90 })
        {
            merchant.AddCommand.Execute(null);
            ListPicker().Options[index].PickCommand.Execute(null);
        }

        merchant.Goods[0].IncreaseCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "5-merchant");
        merchant.ConfirmCommand.Execute(null);

        editor.BeginStroke(new WorldPosition(12, 27));
        MapEditorSiteScreenViewModel mage = Assert.IsOfType<MapEditorSiteScreenViewModel>(gameViewModel.TopScreen);
        mage.AddCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "6-spells");

        foreach (int index in new[] { 0, 7, 20 })
        {
            if (index > 0)
            {
                mage.AddCommand.Execute(null);
            }

            ListPicker().Options[index].PickCommand.Execute(null);
        }

        await SkipFramesAsync(10);
        await CaptureAsync(window, "7-mage");
        mage.ConfirmCommand.Execute(null);

        editor.BeginStroke(new WorldPosition(8, 27));
        MapEditorSiteScreenViewModel camp = Assert.IsOfType<MapEditorSiteScreenViewModel>(gameViewModel.TopScreen);

        for (int i = 0; i < 3; i++)
        {
            camp.AddCommand.Execute(null);
            UnitPicker().Options.Where(option => !option.IsUnavailable).ElementAt(i * 5).PickCommand.Execute(null);
        }

        await SkipFramesAsync(10);
        await CaptureAsync(window, "8-camp");
        camp.ConfirmCommand.Execute(null);

        Assert.That(editor.Map.Sites.Sum(site => site.Items.Length + site.Spells.Length + site.Units.Length), Is.EqualTo(9));
    }

    [AvaloniaTest]
    public async Task MapEditorLandmarksTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);

        // A few landmarks of each category around the human capital and the lake, one of them in the water.
        editor.SelectTool(MapEditorTool.Landmark);
        (WorldLandmarkType Category, int Size, int Index, int X, int Y)[] places =
        [
            (WorldLandmarkType.Misc, 3, 0, 4, 20),
            (WorldLandmarkType.Misc, 1, 0, 8, 19),
            (WorldLandmarkType.Misc, 1, 3, 9, 20),
            (WorldLandmarkType.Building, 4, 0, 16, 18),
            (WorldLandmarkType.Building, 2, 0, 14, 23),
            (WorldLandmarkType.Structure, 3, 0, 5, 25),
            (WorldLandmarkType.Structure, 1, 0, 11, 26),
            (WorldLandmarkType.Terrain, 3, 0, 9, 30),
            (WorldLandmarkType.Terrain, 1, 0, 22, 23),
            (WorldLandmarkType.Terrain, 2, 0, 18, 26)
        ];

        foreach ((WorldLandmarkType category, int size, int index, int x, int y) in places)
        {
            editor.SelectedLandmarkCategory = editor.LandmarkCategories.Single(option => option.Value == category);
            editor.SelectedLandmarkSize = editor.LandmarkSizes.Single(option => option.Value == size);
            editor.SelectedLandmark = editor.Landmarks[Math.Min(index, editor.Landmarks.Count - 1)];
            editor.BeginStroke(new WorldPosition(x, y));
        }

        Assert.That(editor.Map.Landmarks, Has.Count.EqualTo(places.Length));
        await WaitUntilAsync(() => view.SnapshotWorker?.Current?.RenderRequest.State is { } shown && shown.Landmarks.Count == places.Length);

        // The palette of buildings, any size.
        editor.SelectedLandmarkCategory = editor.LandmarkCategories.Single(option => option.Value == WorldLandmarkType.Building);
        editor.SelectedLandmarkSize = editor.LandmarkSizes[0];
        view.Camera.SetZoom(1);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(12, 23)));
        await SkipFramesAsync(10);
        await CaptureAsync(window, "1-map");

        // Terrain landmarks; the right-click info names a landmark.
        editor.SelectedLandmarkCategory = editor.LandmarkCategories.Single(option => option.Value == WorldLandmarkType.Terrain);
        editor.ShowPositionInfo(new WorldPosition(17, 19));
        await SkipFramesAsync(10);
        await CaptureAsync(window, "2-info");
        editor.HidePositionInfo();
    }

    [AvaloniaTest]
    public async Task MapEditorPlacementPreviewTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);
        view.Camera.SetZoom(1);

        async Task ShowAsync(MapEditorTool tool, WorldPosition tile, WorldPosition lookAt, string step)
        {
            editor.SelectTool(tool);
            editor.UpdateHover(null);
            editor.UpdateHover(tile);
            view.Camera.LookAt(WorldTransform.ToWorldPoint(lookAt));
            await SkipFramesAsync(10);
            await CaptureAsync(window, step);
        }

        // Green: a village on free land. Red: one overlapping the capital (the rest of it stays green).
        editor.SelectedVillageOwner = editor.VillageOwners.Single(owner => owner.Value == WorldRace.Human);
        await ShowAsync(MapEditorTool.Village, new WorldPosition(20, 18), new WorldPosition(17, 16), "1-fits");
        await ShowAsync(MapEditorTool.Village, new WorldPosition(14, 10), new WorldPosition(13, 12), "2-blocked");

        // Amber: a site on the mountain's corner removes the whole mountain; a ruin on the shore drains the water.
        await ShowAsync(MapEditorTool.Site, new WorldPosition(14, 27), new WorldPosition(15, 28), "3-changes");
        await ShowAsync(MapEditorTool.Ruin, new WorldPosition(19, 24), new WorldPosition(20, 23), "4-shore");

        // A Water brush at size 4 next to the capital: the capital's tiles are left alone.
        editor.BrushRadius = 4;
        await ShowAsync(MapEditorTool.Water, new WorldPosition(14, 13), new WorldPosition(13, 13), "5-brush");

        // Capital tool: placing it again moves it (its current place outlined), and over a capital the click opens it.
        editor.SelectedCapitalRace = editor.CapitalRaces[0];
        await ShowAsync(MapEditorTool.Capital, new WorldPosition(20, 11), new WorldPosition(15, 11), "6-move");
        await ShowAsync(MapEditorTool.Capital, new WorldPosition(36, 10), new WorldPosition(34, 12), "7-opens");
        Assert.That(editor.PlacementPreview.Glow, Is.Not.Null);
    }

    [AvaloniaTest]
    public async Task MapEditorRoadsTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);
        view.Camera.SetZoom(1);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(20, 16)));
        editor.SelectTool(MapEditorTool.Road);

        async Task ShowAsync(WorldPosition? click, WorldPosition hover, string step)
        {
            if (click is { } tile)
            {
                editor.BeginStroke(tile);
            }

            editor.UpdateHover(null);
            editor.UpdateHover(hover);
            await SkipFramesAsync(10);
            await CaptureAsync(window, step);
        }

        // Start next to the capital, then the road to the cursor: through the forest (amber, cleared), not laid yet.
        await ShowAsync(new WorldPosition(13, 13), new WorldPosition(13, 13), "1-start");
        await ShowAsync(null, new WorldPosition(22, 16), "2-path");

        // Laid; it carries on from there towards the lake shore.
        await ShowAsync(new WorldPosition(22, 16), new WorldPosition(30, 20), "3-continues");
        editor.BeginStroke(new WorldPosition(30, 20));
        editor.CancelRoad();

        // A second road across the first: junction pieces where they meet.
        await ShowAsync(new WorldPosition(18, 9), new WorldPosition(18, 21), "4-crossing");
        editor.BeginStroke(new WorldPosition(18, 21));

        // On the lake: red, no way there.
        await ShowAsync(null, new WorldPosition(23, 22), "5-water");
        Assert.That(editor.PlacementPreview.Fits, Is.False);
    }

    [AvaloniaTest]
    public async Task MapEditorRodsTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);
        view.Camera.SetZoom(1);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(22, 15)));

        async Task ShowAsync(WorldPosition hover, string step)
        {
            editor.UpdateHover(null);
            editor.UpdateHover(hover);
            await SkipFramesAsync(10);
            await CaptureAsync(window, step);
        }

        // A gold mine on free land near the Undead.
        editor.SelectTool(MapEditorTool.Resource);
        editor.BeginStroke(new WorldPosition(24, 10));
        await SkipFramesAsync(5);
        editor.SelectTool(MapEditorTool.Rod);

        // An Empire rod: its tile green, the 2 tiles it claims amber; placed, it is drawn with its land.
        await ShowAsync(new WorldPosition(20, 16), "1-hover");
        editor.BeginStroke(new WorldPosition(20, 16));

        // Another rod too close: both red.
        await ShowAsync(new WorldPosition(21, 17), "2-too-close");
        Assert.That(editor.PlacementPreview.Fits, Is.False);

        // An Undead rod whose land reaches the mine: the mine's 3x3 is amber too.
        editor.SelectedRodOwner = editor.RodOwners[1];
        await ShowAsync(new WorldPosition(24, 12), "3-takes-mine");
        Assert.That(editor.HoverText, Does.Contain("takes Gold mine from nobody"));
        editor.BeginStroke(new WorldPosition(24, 12));

        // Both placed; hovering a rod offers to remove it.
        await ShowAsync(new WorldPosition(24, 12), "4-placed");
        Assert.That(editor.Map.Rods.Count, Is.EqualTo(2));
    }

    [AvaloniaTest]
    public async Task MapEditorLocationsTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);
        view.Camera.SetZoom(1);
        // The list covers the left of the map: look left of the zones so they show to the right of it.
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(17, 17)));

        async Task ShowAsync(WorldPosition? hover, string step)
        {
            editor.UpdateHover(null);
            editor.UpdateHover(hover);
            await SkipFramesAsync(10);
            await CaptureAsync(window, step);
        }

        // The tool opens the empty list; hovering shows the 3x3 square it would place.
        editor.SelectTool(MapEditorTool.Location);
        await ShowAsync(new WorldPosition(20, 14), "1-hover");
        Assert.That(editor.IsLocationsOpen, Is.True);

        // Placed: its row opens for the name.
        editor.BeginStroke(new WorldPosition(20, 14));
        editor.EndStroke();
        await ShowAsync(null, "2-naming");
        Assert.That(editor.SelectedLocationRow!.IsEditing, Is.True);
        editor.SelectedLocationRow!.EditText = "Northern Pass";
        editor.SelectedLocationRow!.CommitEditCommand.Execute(null);

        // Grown to 7x7, and a small zone inside it: both drawn with their names, the selected one in gold.
        editor.ChangeLocationRadius(2);
        editor.SelectedLocationRow = null;
        editor.BeginStroke(new WorldPosition(22, 16));
        editor.EndStroke();
        Assert.That(editor.Map.Locations, Has.Count.EqualTo(1), "A click inside a zone selects it.");
        editor.SelectedLocationRow = null;
        editor.LocationRadius = 0;
        editor.PlaceLocationAt(new WorldPosition(22, 16));
        editor.SelectedLocationRow!.EditText = "Trap";
        editor.SelectedLocationRow!.CommitEditCommand.Execute(null);
        editor.BeginStroke(new WorldPosition(30, 8));
        editor.EndStroke();
        editor.SelectedLocationRow!.CommitEditCommand.Execute(null);
        await ShowAsync(new WorldPosition(22, 16), "3-overlay");
        Assert.That(editor.LocationRows.Select(row => (row.Name, row.Details)), Is.EqualTo(new[] { ("Location 1", "1x1 · 30, 8"), ("Northern Pass", "7x7 · 20, 14"), ("Trap", "1x1 · 22, 16") }));
        Assert.That(editor.HoverText, Does.EndWith("Locations here: Northern Pass, Trap"));

        // Dragging Northern Pass: the new place follows the cursor, the old one dashed; dropped, the X/Y boxes follow.
        editor.BeginStroke(new WorldPosition(19, 13));
        editor.ContinueStroke(new WorldPosition(23, 12));
        await ShowAsync(new WorldPosition(23, 12), "3b-dragging");
        Assert.That(editor.IsDraggingLocation, Is.True);
        editor.EndStroke();
        Assert.That(editor.Map.Locations.Single(location => location.Name == "Northern Pass").Center, Is.EqualTo(new WorldPosition(24, 13)));
        Assert.That((editor.LocationXText, editor.LocationYText), Is.EqualTo(("24", "13")));

        // Hovering a row in the list lights its zone up.
        editor.SetHoveredLocation(editor.LocationRows.Single(row => row.Name == "Northern Pass").Id);
        await ShowAsync(null, "4-row-hover");
        editor.SetHoveredLocation(null);

        // Another tool with the list closed: the zones aren't drawn.
        editor.SelectTool(MapEditorTool.Army);
        editor.IsLocationsOpen = false;
        await ShowAsync(new WorldPosition(22, 16), "5-hidden");
        Assert.That(editor.PlacementPreview.Locations, Is.Empty);
    }

    [AvaloniaTest]
    public async Task MapEditorEconomyTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ =>
        {
            CustomMap map = CreateSampleMap("S600SC0000", "Two kingdoms");
            map.PlaceResource(new WorldPosition(13, 11), WorldResourceType.Gold);
            map.PlaceResource(new WorldPosition(10, 13), WorldResourceType.LifeMana);
            map.PlaceResource(new WorldPosition(31, 12), WorldResourceType.DeathMana);
            map.PlaceResource(new WorldPosition(20, 16), WorldResourceType.Gold);
            map.PlaceResource(new WorldPosition(22, 18), WorldResourceType.RuneMana);
            map.PlaceRod(new WorldPosition(14, 14), WorldRace.Human);
            map.PlaceRod(new WorldPosition(30, 18), WorldRace.Undead);

            return map;
        });
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        MapEditorScreenView view = await WaitForMapEditorAsync(window);
        view.Camera.SetZoom(0.6);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(8, 19)));

        // E opens the panel: both races and the resources on nobody's land.
        window.KeyPress(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");
        window.KeyRelease(Key.E, RawInputModifiers.None, PhysicalKey.E, "e");
        await SkipFramesAsync(10);
        Assert.That(editor.IsEconomyOpen, Is.True);
        Assert.That(editor.EconomyRaces.Count, Is.EqualTo(2));
        await CaptureAsync(window, "1-panel");

        // Selecting the Empire outlines its mines and its rod.
        editor.SelectedEconomyRace = editor.EconomyRaces[0];
        await SkipFramesAsync(10);
        Assert.That(editor.PlacementPreview.Highlight, Is.Not.Empty);
        await CaptureAsync(window, "2-empire-selected");

        // The starting bank: the Empire's gold and mana, then the same for all races.
        editor.BankFields[0].Text = "800";
        editor.BankFields[2].Text = "300";
        editor.CopyBankToAllCommand.Execute(null);
        Avalonia.Controls.Button copyButton = view.GetVisualDescendants().OfType<Avalonia.Controls.Button>().First(button => button.Name == "CopyBankButton");
        copyButton.FindAncestorOfType<Avalonia.Controls.ScrollViewer>()!.ScrollToEnd();
        await SkipFramesAsync(10);
        Assert.That(editor.Map.Players[1].StartingBank.Gold, Is.EqualTo(800));
        await CaptureAsync(window, "3-starting-bank");

        // The land spread preview: the map and the numbers after 10 turns of the game's spread.
        copyButton.FindAncestorOfType<Avalonia.Controls.ScrollViewer>()!.ScrollToHome();
        editor.SpreadUpTenCommand.Execute(null);
        await editor.SpreadTask;
        await SkipFramesAsync(10);
        Assert.That(editor.SpreadTurnText, Is.EqualTo("After 10 turns"));
        await CaptureAsync(window, "4-after-10-turns");
        editor.SpreadUpTenCommand.Execute(null);
        editor.SpreadUpTenCommand.Execute(null);
        await editor.SpreadTask;
        await SkipFramesAsync(10);
        await CaptureAsync(window, "5-after-30-turns");
    }

    [AvaloniaTest]
    public async Task MapEditorGarrisonsAndLevelsTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ => CreateSampleMap("S600SC0000", "Two kingdoms"));
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        await WaitForMapEditorAsync(window);

        MapEditorUnitPickerViewModel UnitPicker() => Assert.IsOfType<MapEditorUnitPickerViewModel>(gameViewModel.TopScreen);
        void PickLeader() => UnitPicker().Options.First(option => !option.IsUnavailable && !option.Definition.IsLarge && option.Definition.Leadership >= 3).PickCommand.Execute(null);
        void PickSoldier(int skip = 0) => UnitPicker().Options.Where(option => !option.IsUnavailable && !option.Definition.IsLarge).ElementAt(skip).PickCommand.Execute(null);

        // A village: its garrison and the army stationed in it, one unit raised to level 12.
        editor.SelectTool(MapEditorTool.Village);
        editor.SelectedVillageOwner = editor.VillageOwners.Single(option => option.Value == WorldRace.Human);
        editor.SelectedVillageLevel = editor.VillageLevels.Single(option => option.Value == 3);
        editor.BeginStroke(new WorldPosition(10, 16));
        editor.BeginStroke(new WorldPosition(10, 16));
        MapEditorVillageScreenViewModel village = Assert.IsOfType<MapEditorVillageScreenViewModel>(gameViewModel.TopScreen);
        village.AddUnit(village.Garrison, GridPosition.FrontTop);
        PickSoldier();
        village.AddUnit(village.Garrison, GridPosition.FrontBottom);
        PickSoldier(3);
        village.AddUnit(village.Army, GridPosition.FrontCenter);
        PickLeader();
        village.AddUnit(village.Army, GridPosition.BackCenter);
        PickSoldier(6);
        village.SetSelectedLevel(12);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "1-village");
        village.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Stacks.Count(stack => stack.Position == editor.Map.Villages.Single().Entrance), Is.EqualTo(1));

        // A capital: the guardian, two more defenders and its army.
        CustomMapPlayer player = editor.Map.Players.First(candidate => candidate.Capital is not null);
        editor.SelectTool(MapEditorTool.Capital);
        editor.BeginStroke(new WorldPosition(player.Capital!.Value.X + 1, player.Capital.Value.Y + 1));
        MapEditorCapitalScreenViewModel capital = Assert.IsOfType<MapEditorCapitalScreenViewModel>(gameViewModel.TopScreen);
        capital.AddUnit(capital.Garrison, GridPosition.BackTop);
        PickSoldier();
        capital.SetSelectedLevel(5);
        capital.AddUnit(capital.Garrison, GridPosition.BackBottom);
        PickSoldier(2);
        capital.AddUnit(capital.Army, GridPosition.BackCenter);
        PickSoldier(4);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "2-capital");
        capital.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Players.First(candidate => candidate.Race == player.Race).Garrison.Count(unit => unit is not null), Is.EqualTo(3));
        editor.ShowPositionInfo(new WorldPosition(player.Capital.Value.X + 2, player.Capital.Value.Y + 2));
        Assert.IsOfType<WorldFortificationInfoViewModel>(editor.PositionInfo);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "2b-capital-info");
        editor.HidePositionInfo();

        // An army in the field with a level 30 soldier.
        UnitDefinition probeLeader = editor.Library.Units.First(unit => unit is { Category: UnitCategory.Leader, Race: WorldRace.Neutral, IsLarge: false });
        CustomMapUnit?[] probe = new CustomMapUnit?[GridPosition.Count];
        probe[GridPosition.FrontCenter.ToIndex()] = CustomMapUnit.From(probeLeader);
        WorldPosition field = Enumerable.Range(4, 40).SelectMany(y => Enumerable.Range(4, 40).Select(x => new WorldPosition(x, y)))
            .First(position => editor.Map.Clone().PlaceStack(position, WorldRace.Neutral, probe).Error is null);
        editor.SelectTool(MapEditorTool.Army);
        editor.SelectedArmyOwner = editor.ArmyOwners.Single(option => option.Value == WorldRace.Neutral);
        editor.BeginStroke(field);
        MapEditorArmyScreenViewModel army = Assert.IsOfType<MapEditorArmyScreenViewModel>(gameViewModel.TopScreen);
        army.ChooseLeaderCommand.Execute(null);
        PickLeader();
        army.AddUnit(GridPosition.BackCenter);
        PickSoldier();
        army.SetSelectedLevel(30);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "3-army");
        army.ConfirmCommand.Execute(null);
        Assert.That(editor.Map.Stacks.Single(stack => stack.Position == field).Slots.Max(unit => unit?.Level ?? 0), Is.EqualTo(30));
    }

    // Events: the list (V) with a saved and a broken event, the block editor with "or" groups, a message with a
    // translation and settings, and the message preview in the game's own popup.
    [AvaloniaTest]
    public async Task MapEditorEventsTest()
    {
        using Sandbox sb = Sandbox.Create();

        (GameViewModel gameViewModel, GameView window, GameLoader loader) = InitializeMapEditor(sb);
        CustomMapStore store = Assert.NotNull(loader.CreateMapStore());

        await gameViewModel.OpenMapEditorAsync(store, _ =>
        {
            CustomMap map = CreateSampleMap("S600SC0000", "Two kingdoms");
            map.PlaceLocation(new WorldPosition(20, 14), 1, "Northern Pass");
            map.PlaceLocation(new WorldPosition(30, 20), 2, "Silk Wood");
            Assert.That(map.PlaceRuin(new WorldPosition(14, 22), 0, "Old tower").Error, Is.Null);
            map.AddEvent(CustomMapEvent.Create("Welcome", WorldRace.Human) with
            {
                Groups = [[new CustomMapTurnCondition(1, Repeat: false)]],
                Effects = [new CustomMapMessageEffect(CustomMapText.Of("Welcome, my lord."), CustomMapMessageAudience.Triggerer, null, true)]
            });
            map.AddEvent(CustomMapEvent.Create("Lost scout", WorldRace.Undead) with { Groups = [[new CustomMapEnterZoneCondition(99)]] });

            return map;
        });
        MapEditorScreenViewModel editor = Assert.IsOfType<MapEditorScreenViewModel>(gameViewModel.RootScreen);
        await WaitForMapEditorAsync(window);

        editor.IsEventsOpen = true;
        await SkipFramesAsync(10);
        await CaptureAsync(window, "1-list");
        Assert.That(editor.EventRows.Select(row => (row.Name, row.HasProblem)), Is.EqualTo(new[] { ("Lost scout", true), ("Welcome", false) }));

        editor.NewEventCommand.Execute(null);
        MapEditorEventScreenViewModel screen = Assert.IsOfType<MapEditorEventScreenViewModel>(gameViewModel.TopScreen);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "2-new-event");

        MapEditorPaletteBlock Palette(MapEditorBlockKind kind) => screen.ConditionPalette.Concat(screen.EffectPalette).SelectMany(group => group.Blocks).Single(block => block.Kind == kind);

        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.EnterZone));
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.EveryTurns));
        screen.AddGroupCommand.Execute(null);
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.LootRuin));
        MapEditorEventBlockViewModel message = screen.InsertBlock(Palette(MapEditorBlockKind.Message), screen.Effects, 0)!;
        MapEditorBlockTextField text = message.Parts.OfType<MapEditorBlockTextField>().Single();
        text.Default = "Scout\n\"Riders in the pass! The Undead are coming.\"";
        text.LanguageToAdd = "ru";
        text.AddTranslationCommand.Execute(null);
        text.Translations.Single().Text = "Разведчик\n\"Всадники на перевале! Идёт Нежить.\"";
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.RevealZone));
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.SwitchEvent));
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.Win));
        screen.IsAdvancedOpen = true;
        await SkipFramesAsync(10);
        await CaptureAsync(window, "3-blocks");
        Assert.That(screen.HasProblems, Is.False);

        // An empty objective block is red and the settings say why the event isn't saved.
        screen.AddBlockCommand.Execute(Palette(MapEditorBlockKind.Objective));
        await SkipFramesAsync(10);
        await CaptureAsync(window, "4-red-block");
        Assert.That(screen.Effects[^1].HasProblem, Is.True);
        screen.RemoveBlockCommand.Execute(screen.Effects[^1]);

        message.Parts.OfType<MapEditorBlockPreview>().Single().PreviewCommand.Execute(null);
        await SkipFramesAsync(150);
        await CaptureAsync(window, "5-preview");
        Assert.IsOfType<ScenarioEventPopupViewModel>(gameViewModel.TopScreen).Close();

        screen.ConfirmCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "6-list-after");
        Assert.That(editor.EventRows.Select(row => row.Name), Is.EqualTo(new[] { "Event 3", "Lost scout", "Welcome" }));

        // Phase 3: pick the zone on the map (the zones glow gold, the screen steps aside), then the hover line names
        // the events using a zone, and deleting it asks first.
        MapEditorScreenView view = window.GetVisualDescendants().OfType<MapEditorScreenView>().First();
        view.Camera.SetZoom(1);
        view.Camera.LookAt(WorldTransform.ToWorldPoint(new WorldPosition(24, 16)));
        editor.IsEventsOpen = false;
        editor.OpenEvent(3);
        MapEditorBlockChoice zone = Assert.IsOfType<MapEditorEventScreenViewModel>(gameViewModel.TopScreen).Groups[0].Blocks[0].Parts.OfType<MapEditorBlockChoice>().First();
        zone.PickOnMapCommand.Execute(null);
        editor.UpdateHover(new WorldPosition(30, 20));
        await SkipFramesAsync(10);
        await CaptureAsync(window, "7-pick-zone");
        Assert.That(editor.IsPickingEventTarget, Is.True);
        editor.BeginStroke(new WorldPosition(30, 20));
        editor.EndStroke();
        Assert.That(zone.Value, Is.EqualTo(2));
        Assert.IsOfType<MapEditorEventScreenViewModel>(gameViewModel.TopScreen).ConfirmCommand.Execute(null);

        editor.UpdateHover(null);
        editor.UpdateHover(new WorldPosition(30, 20));
        Assert.That(editor.HoverText, Does.EndWith("Used by events: Event 3"));
        editor.DeleteLocation(2);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "8-delete-warning");
        Assert.IsOfType<ConfirmPromptViewModel>(gameViewModel.TopScreen).Close();

        // Phase 4: a recipe adds ready-made events (here one per race plus the conquest events) and opens the main one.
        editor.IsEventsOpen = true;
        editor.SelectedRecipe = editor.Recipes.Single(option => option.Value == MapEditorRecipe.HoldOut);
        editor.AddRecipeCommand.Execute(null);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "9-recipe-event");
        Assert.IsOfType<MapEditorEventScreenViewModel>(gameViewModel.TopScreen).Close();
        await SkipFramesAsync(10);
        await CaptureAsync(window, "10-recipe-list");
        Assert.That(editor.EventRows.Select(row => row.Name), Is.SupersetOf(new[] { "Empire holds out 30 turns", "Undead takes the Empire capital", "Empire conquers all capitals", "Undead conquers all capitals" }));
    }
}
