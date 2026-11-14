using System.Diagnostics;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Measures memory across repeated play cycles with real assets and real (headless Skia) rendering.
// Each cycle: start a scenario with a briefing and leave it, load a save into the world screen, fight a battle,
// return to the main menu. Writes one CSV row per cycle to $GALLEAN_SOAK_CSV.
[Explicit]
[NonParallelizable]
[Category("Soak")]
public sealed class MemorySoakTests
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(60);

    [AvaloniaTest]
    public async Task PlayCyclesTest()
    {
        int cycles = int.Parse(Environment.GetEnvironmentVariable("GALLEAN_SOAK_CYCLES") ?? "12", CultureInfo.InvariantCulture);
        string csvPath = Environment.GetEnvironmentVariable("GALLEAN_SOAK_CSV") ?? Path.Combine(Path.GetTempPath(), "gallean-soak.csv");

        using AssetProvider assets = TestAssets.LoadOrIgnore();
        using Sandbox sb = Sandbox.Create();

        GameLoader loader = new(new RandomProvider(0));
        (GameSession session, _) = loader.LoadScenario("S107SC0000", worldState: null, Language.English);
        GameSaveMetadata metadata = new("Soak", GameSaveType.Manual, "S107SC0000", "Soak", 1, DateTimeOffset.UnixEpoch, "0.1.0+2d40300");
        GameSaveStore saveStore = new(sb.GetPath("saves"), 10);
        await saveStore.SaveAsync(new GameSaveId(1), metadata, session.World.State);
        GameSaveEntry saveEntry = new(new GameSaveId(1), Result<GameSaveMetadata>.FromValue(metadata));

        NullAudioPlayer audioPlayer = new();
        using GameViewModel game = new TestGameViewModelBuilder()
            .WithAssets(assets)
            .WithGameLoader(loader)
            .WithAudioPlayer(audioPlayer)
            .WithSaveStore(saveStore)
            .WithLeaderStore(new LeaderStore(sb.GetPath("leaders")))
            .WithSettingsStore(new GameSettingsStore(sb.GetPath("config")))
            .Build();

        GameView window = new()
        {
            DataContext = game,
            WindowState = WindowState.Normal,
            SizeToContent = SizeToContent.Manual,
            Width = 1366,
            Height = 768
        };

        AssetScope.SetAssets(window, assets);
        AudioScope.SetAudio(window, audioPlayer);
        window.Show();

        List<string> rows = ["cycle,seconds,managed_mb,rss_mb,gen2_collections"];
        Stopwatch total = Stopwatch.StartNew();

        await game.ShowMainMenuCommand.ExecuteAsync(null);
        rows.Add(Measure(0, total));

        for (int cycle = 1; cycle <= cycles; cycle++)
        {
            // 1. Start a scenario that opens with a briefing, then leave the briefing.
            await game.StartGameCommand.ExecuteAsync(new GameConfiguration("S137SC0000", Configuration: null));
            await SkipFramesAsync(10);
            await game.ShowMainMenuCommand.ExecuteAsync(null);
            await SkipFramesAsync(5);

            // 2. Load the save into the world screen and render it.
            await game.LoadGameCommand.ExecuteAsync(saveEntry);

            if (game.TopScreen is ConfirmPromptViewModel prompt)
            {
                await prompt.ConfirmCommand.ExecuteAsync(null);
            }

            await WaitForWorldAsync(window);
            WorldScreenViewModel world = Assert.IsOfType<WorldScreenViewModel>(game.RootScreen);
            world.CompleteGameStart();
            await SkipFramesAsync(60);

            // 3. Fight a battle in the loaded game and render it.
            GameModel gameModel = world.GameModel;
            await StartBattleAsync(gameModel);
            BattleModel battle = Assert.NotNull(gameModel.ActiveBattle);
            game.ShowScreen(new BattleScreenViewModel(gameModel, battle, game, assets, NullAssetPreloader.Instance, new RandomProvider(cycle)));
            await WaitForBattleAsync(window);
            await SkipFramesAsync(60);

            // 4. Back to the main menu.
            await game.ShowMainMenuCommand.ExecuteAsync(null);
            await SkipFramesAsync(30);

            rows.Add(Measure(cycle, total));
            TestContext.Out.WriteLine(rows[^1]);
        }

        window.Close();
        Dispatcher.UIThread.RunJobs();

        await File.WriteAllLinesAsync(csvPath, rows);
    }

    private static async Task StartBattleAsync(GameModel gameModel)
    {
        WorldRaceDefinition attackers = gameModel.Definitions.GetRace(WorldRace.Human);
        WorldRaceDefinition defenders = gameModel.Definitions.GetRace(WorldRace.Undead);
        BattleStateBuilder builder = new(gameModel.Definitions);

        builder.Add(MakeBattleUnit(0, attackers.Leaders[0], BattleSide.Attacker, gameModel));
        builder.Add(MakeBattleUnit(1, attackers.Units[0], BattleSide.Attacker, gameModel));
        builder.Add(MakeBattleUnit(2, attackers.Units[1], BattleSide.Attacker, gameModel));
        builder.Add(MakeBattleUnit(3, defenders.Leaders[0], BattleSide.Defender, gameModel));
        builder.Add(MakeBattleUnit(4, defenders.Units[0], BattleSide.Defender, gameModel));
        builder.Add(MakeBattleUnit(5, defenders.Units[1], BattleSide.Defender, gameModel));

        EngineContext engine = new(new RandomProvider(0), gameModel.Definitions, gameModel.Stats, GameScenarioProvider.Empty, new GlobalVariables());

        await gameModel.RunAsync(() =>
        {
            gameModel.Session.StartBattle(new BattleSession(
                engine,
                builder.Build(BattleModifiers.None, TestHelpers.SimpleBattleInfo()),
                NullBattleUnitUpgradeResolver.Instance,
                _ => { },
                isAttackerAi: false,
                isDefenderAi: false,
                canAutoResolve: true,
                new WorldPosition(0, 0)));
        });
    }

    private static BattleUnit MakeBattleUnit(int id, UnitDefinitionId definitionId, BattleSide side, GameModel gameModel)
    {
        UnitDefinition definition = gameModel.Definitions.GetUnit(definitionId);

        return new BattleUnit(
            new BattleUnitId(id),
            null,
            definitionId,
            side,
            definition.MaxHitpoints,
            Experience: 0,
            ExperienceGained: 0,
            definition.Level,
            Modifiers: [],
            StatusEffects: [],
            SummonerId: null,
            EquipmentAttackIds: [],
            Name: null,
            IsTypeLocked: false);
    }

    private static string Measure(int cycle, Stopwatch total)
    {
        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        GC.Collect();

        double managed = GC.GetTotalMemory(forceFullCollection: true) / 1048576.0;
        double rss = ReadRssMb();

        return string.Create(CultureInfo.InvariantCulture, $"{cycle},{total.Elapsed.TotalSeconds:F1},{managed:F1},{rss:F1},{GC.CollectionCount(2)}");
    }

    private static double ReadRssMb()
    {
        foreach (string line in File.ReadLines("/proc/self/status"))
        {
            if (line.StartsWith("VmRSS:", StringComparison.Ordinal))
            {
                string[] parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return double.Parse(parts[1], CultureInfo.InvariantCulture) / 1024.0;
            }
        }

        return double.NaN;
    }

    private static async Task WaitForWorldAsync(GameView window)
    {
        await SkipFramesAsync(5);

        GameViewModel game = Assert.IsOfType<GameViewModel>(window.DataContext);
        WorldScreenView view = window.GetVisualDescendants().OfType<WorldScreenView>().First();
        WorldScreenSnapshotWorker worker = Assert.NotNull(view.SnapshotWorker);

        await WaitUntilAsync(() => worker.Exception is not null || (worker.Current is not null && !game.Screens.OfType<LoadingScreenViewModel>().Any()));

        Assert.That(worker.Exception, Is.Null);
        Assert.That(worker.Current, Is.Not.Null);
    }

    private static async Task WaitForBattleAsync(GameView window)
    {
        await SkipFramesAsync(5);

        GameViewModel game = Assert.IsOfType<GameViewModel>(window.DataContext);
        BattleScreenViewModel battle = Assert.IsOfType<BattleScreenViewModel>(game.RootScreen);

        await WaitUntilAsync(() => battle.TransitionState is null);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (!condition() && stopwatch.Elapsed < WaitTimeout)
        {
            await SkipFramesAsync(5);
        }
    }

    private static async Task SkipFramesAsync(int count)
    {
        Dispatcher.UIThread.RunJobs();

        for (int frame = 0; frame < count; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();

            await Task.Delay(TimeSpan.FromMilliseconds(16));
        }
    }
}
