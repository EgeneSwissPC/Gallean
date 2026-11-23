using Gallean.Common;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;
using Gallean.Engine;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the "Refuse to flee" battle button (F) and instant resolve in that mode.
// The battle: a cleric and a wounded leader against an enemy far too strong for them, so the
// computer flees with the leader as soon as it plays the leader's turn.
public sealed class BattleNoFleeModeTests
{
    private static Task<BattleHealModeTests.Setup> CreateBattleAsync()
    {
        return BattleHealModeTests.CreateBattleAsync(enemyDamage: 50, enemyHitpoints: 5000, fighterIsLeader: true);
    }

    // GameModel.Snapshot drops the battle state when the battle ends, so read the battle model.
    private static void DriveToEnd(BattleHealModeTests.Setup setup)
    {
        for (int i = 0; i < 500 && setup.Battle.State.Outcome is null; i++)
        {
            setup.ViewModel.CompleteAnimation(TimeSpan.Zero);
        }
    }

    private static List<BattleEvent> Events(BattleHealModeTests.Setup setup)
    {
        return [.. setup.Battle.History.Steps.Select(step => step.Event)];
    }

    private static void AssertNoError(BattleHealModeTests.Setup setup)
    {
        if (setup.GameViewModel.TopScreen is ErrorScreenViewModel error)
        {
            Assert.Fail(error.Message);
        }
    }

    [Test]
    public async Task AutoPlayFleesTest()
    {
        // Checks the setup: plain Auto-play flees with the leader.
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();

        Task autoTask = setup.ViewModel.ToggleAutoBattleCommand.ExecuteAsync(null);
        DriveToEnd(setup);
        await BattleHealModeTests.FinishAsync(setup, autoTask);

        AssertNoError(setup);
        Assert.That(Events(setup).OfType<BattleUnitRetreatingEvent>().Select(ev => ev.UnitId), Does.Contain(BattleHealModeTests.FighterId));
    }

    [Test]
    public async Task NoFleeModeLoopTest()
    {
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();
        BattleScreenViewModel viewModel = setup.ViewModel;

        Assert.That(viewModel.ToggleNoFleeModeCommand.CanExecute(null), Is.True);

        Task noFleeTask = viewModel.ToggleNoFleeModeCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsNoFleeMode, Is.True);
        Assert.That(viewModel.DefendCommand.CanExecute(null), Is.False);

        DriveToEnd(setup);
        await BattleHealModeTests.FinishAsync(setup, noFleeTask);

        AssertNoError(setup);
        List<BattleEvent> events = Events(setup);

        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
        Assert.That(events.OfType<BattleUnitAttackEvent>().Select(ev => ev.AttackerId), Does.Contain(BattleHealModeTests.FighterId));
        Assert.That(setup.Battle.State.Outcome, Is.EqualTo(new BattleOutcome(BattleSide.Defender)));
    }

    [Test]
    public async Task NoFleeModeTurnsOtherModesOffTest()
    {
        using BattleHealModeTests.Setup setup = await BattleHealModeTests.CreateBattleAsync(enemyDamage: 0);
        BattleScreenViewModel viewModel = setup.ViewModel;

        Task autoTask = viewModel.ToggleAutoBattleCommand.ExecuteAsync(null);
        Task noFleeTask = viewModel.ToggleNoFleeModeCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsNoFleeMode, Is.True);
        Assert.That(viewModel.IsAutoBattle, Is.False);

        Task healTask = viewModel.ToggleHealModeCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsHealMode, Is.True);
        Assert.That(viewModel.IsNoFleeMode, Is.False);

        Task noFleeAgainTask = viewModel.ToggleNoFleeModeCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsNoFleeMode, Is.True);
        Assert.That(viewModel.IsHealMode, Is.False);

        await viewModel.ToggleAutoBattleCommand.ExecuteAsync(null);

        Assert.That(viewModel.IsAutoBattle, Is.True);
        Assert.That(viewModel.IsNoFleeMode, Is.False);

        Task autoOffTask = viewModel.ToggleAutoBattleCommand.ExecuteAsync(null);
        await BattleHealModeTests.FinishAsync(setup, autoTask, noFleeTask, healTask, noFleeAgainTask, autoOffTask);

        AssertNoError(setup);
        Assert.That(viewModel.IsAutoBattle || viewModel.IsHealMode || viewModel.IsNoFleeMode, Is.False);
        Assert.That(viewModel.IsPlayerTurn, Is.True);
    }

    [Test]
    public async Task InstantResolveWithoutModeFleesTest()
    {
        // Checks the setup: the normal instant resolve flees with the leader.
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();

        setup.ViewModel.AutoResolveCommand.Execute(null);

        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(setup.GameViewModel.TopScreen);
        Task confirmTask = prompt.ConfirmCommand.ExecuteAsync(null);

        await BattleHealModeTests.FinishAsync(setup, confirmTask);

        AssertNoError(setup);
        Assert.That(Events(setup).OfType<BattleUnitRetreatingEvent>(), Is.Not.Empty);
    }

    [Test]
    public async Task InstantResolveInNoFleeModeTest()
    {
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();
        BattleScreenViewModel viewModel = setup.ViewModel;

        Task noFleeTask = viewModel.ToggleNoFleeModeCommand.ExecuteAsync(null);

        viewModel.AutoResolveCommand.Execute(null);

        ConfirmPromptViewModel prompt = Assert.IsOfType<ConfirmPromptViewModel>(setup.GameViewModel.TopScreen);
        Task confirmTask = prompt.ConfirmCommand.ExecuteAsync(null);

        await BattleHealModeTests.FinishAsync(setup, confirmTask, noFleeTask);

        AssertNoError(setup);
        List<BattleEvent> events = Events(setup);

        Assert.That(events.OfType<BattleUnitRetreatingEvent>(), Is.Empty);
        Assert.That(setup.Battle.State.Outcome, Is.EqualTo(new BattleOutcome(BattleSide.Defender)));
        Assert.That(viewModel.IsNoFleeMode, Is.False);
    }
}
