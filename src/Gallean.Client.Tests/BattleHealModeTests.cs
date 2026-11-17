using Gallean.Common;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;
using SkiaSharp;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the "Heal and defend" battle button (H) and instant resolve in that mode.
public sealed class BattleHealModeTests
{
    private static readonly BattleUnitId ClericId = new(1);
    private static readonly BattleUnitId FighterId = new(2);
    private static readonly BattleUnitId EnemyId = new(3);

    private sealed record Setup(GameModel GameModel, GameViewModel GameViewModel, BattleModel Battle, BattleScreenViewModel ViewModel, TestAssetProvider Assets) : IDisposable
    {
        public BattleState State => Assert.NotNull(GameModel.Snapshot.BattleState);

        public void Dispose()
        {
            Assets.Dispose();
        }
    }

    private static async Task<Setup> CreateBattleAsync(int enemyDamage)
    {
        UnitDefinition clericDefinition = TestHelpers.SimpleUnitDefinition("cleric", maxHitpoints: 100, initiative: 60, damage: 0) with
        {
            Attack = TestHelpers.SimpleAttack(new UnitAttackDefinitionId("cleric"), initiative: 60, damage: 0, accuracy: 100, reach: UnitAttackReach.Any) with
            {
                Type = UnitAttackType.Heal,
                Heal = 30
            }
        };
        UnitDefinition fighterDefinition = TestHelpers.SimpleUnitDefinition("fighter", maxHitpoints: 100, initiative: 50, damage: 10);
        UnitDefinition enemyDefinition = TestHelpers.SimpleUnitDefinition("enemy", maxHitpoints: 30, initiative: 30, damage: enemyDamage);

        GlobalVariables globals = new() { BattleInitiativeVariance = 0, BattleDamageVariance = 0 };
        WorldMap map = TestHelpers.SimpleMap(1, 1);
        WorldPlayer player = TestHelpers.SimplePlayer(new WorldPlayerId(1), WorldRace.Human, map);

        (GameSession session, IEngineContext engine) = new TestWorldEngineBuilder()
            .WithMap(map)
            .WithGlobals(globals)
            .WithDefinition(clericDefinition)
            .WithDefinition(fighterDefinition)
            .WithDefinition(enemyDefinition)
            .WithDefinition(TestHelpers.SimpleLord(player.LordId, player.Race, LordCategory.Warrior))
            .WithPlayer(player)
            .BuildSession();

        GameModel gameModel = new(session, TestGameTextProvider.Instance, NullLog.Instance, SynchronousTaskScheduler.Instance);

        BattleState battleState = new BattleStateBuilder(engine.Definitions)
            .Add(TestHelpers.SimpleBattleUnit(ClericId, null, clericDefinition.Id, BattleSide.Attacker, hitpoints: 100))
            .Add(TestHelpers.SimpleBattleUnit(FighterId, null, fighterDefinition.Id, BattleSide.Attacker, hitpoints: 40))
            .Add(TestHelpers.SimpleBattleUnit(EnemyId, null, enemyDefinition.Id, BattleSide.Defender, hitpoints: 30))
            .Build(BattleModifiers.None, TestHelpers.SimpleBattleInfo());

        EngineContext battleEngine = new(new RandomProvider(0), engine.Definitions, engine.UnitStats, GameScenarioProvider.Empty, globals);
        await gameModel.RunAsync(() =>
        {
            gameModel.Session.StartBattle(new BattleSession(
                battleEngine,
                battleState,
                NullBattleUnitUpgradeResolver.Instance,
                _ =>
                {
                },
                isAttackerAi: false,
                isDefenderAi: true,
                canAutoResolve: true,
                new WorldPosition(0, 0)));
        });
        BattleModel battle = Assert.NotNull(gameModel.ActiveBattle);

        TestAssetProviderBuilder assetBuilder = new();
        Sprite sprite = assetBuilder.CreateSprite(SKColors.Transparent, 80, 120);
        Animation animation = assetBuilder.CreateAnimation(new AssetAnimationMetadata(FrameCount: 1, Width: 80, Height: 120, OffsetX: 0, OffsetY: 0, TriggerFrame: null, SecondTriggerFrame: null), [sprite]);
        TestAssetProvider assets = assetBuilder
            .ResolveSprite(_ => sprite)
            .ResolveAnimation(_ => animation)
            .ResolveSound(_ => null)
            .Build();

        TestMetadataProviderBuilder metadataBuilder = new();
        AssetAnimationMetadata unitMetadata = new(FrameCount: 1, Width: 80, Height: 120, OffsetX: 375, OffsetY: 420, TriggerFrame: 1, SecondTriggerFrame: 0);

        foreach ((UnitDefinitionId id, BattleSide side) in new[] { (clericDefinition.Id, BattleSide.Attacker), (fighterDefinition.Id, BattleSide.Attacker), (enemyDefinition.Id, BattleSide.Defender) })
        {
            foreach (AssetAnimationKey.BattleUnitPose pose in new[] { AssetAnimationKey.BattleUnitPose.Attack, AssetAnimationKey.BattleUnitPose.Hit, AssetAnimationKey.BattleUnitPose.Idle })
            {
                metadataBuilder.WithAnimation(new AssetAnimationKey.BattleUnit(id, pose, side), unitMetadata);
            }
        }

        for (int variant = 0; variant < 4; variant++)
        {
            metadataBuilder.WithSprite(new AssetSpriteKey.BattleCorpse(IsLarge: false, Variant: variant), new AssetSpriteMetadata(Width: 44, Height: 28, PixelWidth: 44, PixelHeight: 28, OffsetX: -22, OffsetY: -20, Additive: false));
        }

        GameViewModel gameViewModel = new TestGameViewModelBuilder().WithAssets(assets).Build();
        BattleScreenViewModel viewModel = new(gameModel, battle, gameViewModel, metadataBuilder.Build(), NullAssetPreloader.Instance, new RandomProvider(0));
        gameViewModel.OpenScreen(viewModel);
        await viewModel.CompleteTransitionAsync();

        return new Setup(gameModel, gameViewModel, battle, viewModel, assets);
    }

    private static void Drive(Setup setup, Func<BattleState, bool> until)
    {
        for (int i = 0; i < 200 && !until(setup.State); i++)
        {
            setup.ViewModel.CompleteAnimation(TimeSpan.Zero);
        }
    }

    private static async Task FinishAsync(Setup setup, params Task[] tasks)
    {
        for (int i = 0; i < 200 && !tasks.All(task => task.IsCompleted); i++)
        {
            setup.ViewModel.CompleteAnimation(TimeSpan.Zero);
        }

        Assert.That(tasks.All(task => task.IsCompleted), Is.True);
        await Task.WhenAll(tasks);
    }

    [Test]
    public async Task HealModeLoopTest()
    {
        using Setup setup = await CreateBattleAsync(enemyDamage: 0);
        BattleScreenViewModel viewModel = setup.ViewModel;

        Assert.That(viewModel.IsPlayerTurn, Is.True);
        Assert.That(viewModel.ToggleHealModeCommand.CanExecute(null), Is.True);

        Task healTask = viewModel.ToggleHealModeCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsHealMode, Is.True);
        Assert.That(viewModel.DefendCommand.CanExecute(null), Is.False);
        Assert.That(setup.State.GetUnit(FighterId).Hitpoints, Is.EqualTo(70));

        Drive(setup, state => state.RoundNumber >= 3);

        // Round 2 healed the fighter to full; nobody attacked the enemy.
        Assert.That(setup.State.GetUnit(FighterId).Hitpoints, Is.EqualTo(100));
        Assert.That(setup.State.GetUnit(EnemyId).Hitpoints, Is.EqualTo(30));
        Assert.That(viewModel.IsHealMode, Is.True);

        List<BattleEvent> events = [.. setup.Battle.History.Steps.Select(step => step.Event)];

        Assert.That(events, Does.Contain(new BattleUnitDefendedEvent(FighterId)));
        Assert.That(events.OfType<BattleUnitHealedEvent>().Select(ev => ev.UnitId), Is.EqualTo(new[] { FighterId, FighterId }));

        await viewModel.ToggleHealModeCommand.ExecuteAsync(null);
        await FinishAsync(setup, healTask);

        Assert.That(viewModel.IsHealMode, Is.False);
        Assert.That(viewModel.IsPlayerTurn, Is.True);
        Assert.That(viewModel.DefendCommand.CanExecute(null), Is.True);
        Assert.That(setup.State.Outcome, Is.Null);
    }

    [Test]
    public async Task HealModeTurnsAutoPlayOffTest()
    {
        using Setup setup = await CreateBattleAsync(enemyDamage: 0);
        BattleScreenViewModel viewModel = setup.ViewModel;

        Task autoTask = viewModel.ToggleAutoBattleCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsAutoBattle, Is.True);

        Task healTask = viewModel.ToggleHealModeCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsHealMode, Is.True);
        Assert.That(viewModel.IsAutoBattle, Is.False);

        await viewModel.ToggleAutoBattleCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsAutoBattle, Is.True);
        Assert.That(viewModel.IsHealMode, Is.False);

        Task autoOffTask = viewModel.ToggleAutoBattleCommand.ExecuteAsync(null);
        await FinishAsync(setup, autoTask, healTask, autoOffTask);

        Assert.That(viewModel.IsAutoBattle, Is.False);
        Assert.That(viewModel.IsHealMode, Is.False);
        Assert.That(viewModel.IsPlayerTurn, Is.True);
    }

    [Test]
    public async Task InstantResolveInHealModeTest()
    {
        using Setup setup = await CreateBattleAsync(enemyDamage: 5);
        BattleScreenViewModel viewModel = setup.ViewModel;

        Task healTask = viewModel.ToggleHealModeCommand.ExecuteAsync(null);

        viewModel.AutoResolveCommand.Execute(null);

        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(setup.GameViewModel.TopScreen);
        Task confirmTask = prompt.ConfirmCommand.ExecuteAsync(null);

        await FinishAsync(setup, confirmTask, healTask);

        if (setup.GameViewModel.TopScreen is ErrorScreenViewModel error)
        {
            Assert.Fail(error.Message);
        }

        List<BattleEvent> events = [.. setup.Battle.History.Steps.Select(step => step.Event)];
        int firstHitOnEnemy = events.FindIndex(ev => ev is BattleUnitAttackHitEvent hit && hit.TargetId == EnemyId);
        int lastHealBeforeFight = events.FindLastIndex(firstHitOnEnemy, ev => ev is BattleUnitHealedEvent healed && healed.UnitId == FighterId && healed.HitpointsAfter == 100);

        Assert.That(setup.State.Outcome, Is.EqualTo(new BattleOutcome(BattleSide.Attacker)));
        Assert.That(viewModel.IsHealMode, Is.False);
        Assert.That(events, Does.Contain(new BattleUnitDefendedEvent(FighterId)));
        Assert.That(firstHitOnEnemy, Is.GreaterThan(0));
        Assert.That(lastHealBeforeFight, Is.GreaterThanOrEqualTo(0), "The fighter is healed to full before anyone attacks the enemy.");
    }
}
