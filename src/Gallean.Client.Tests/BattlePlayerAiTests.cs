using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade (battle AI designer phase 10): the battle AI picked on the battle screen plays the player's own
// Auto-play and Instant resolve. The battle (from BattleNoFleeModeTests): Classic flees with the player's leader.
[NonParallelizable]
public sealed class BattlePlayerAiTests
{
    private const string NeverFleeId = "custom-battle:stand";

    [SetUp]
    public void SetUp()
    {
        AiBrainRegistry.SetDesignedBattleAis([new DesignedBattleAi(NeverFleeId, "Stand and fight", [DesignedBattleStage.Classic with { Tactics = DesignedBattleTactics.Classic with { NeverFlee = true } }])]);
    }

    [TearDown]
    public void TearDown()
    {
        AiBrainRegistry.SetDesignedBattleAis([]);
    }

    private static Task<BattleHealModeTests.Setup> CreateBattleAsync()
    {
        return BattleHealModeTests.CreateBattleAsync(enemyDamage: 50, enemyHitpoints: 5000, fighterIsLeader: true);
    }

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

    private static void Pick(BattleScreenViewModel viewModel, string id)
    {
        viewModel.SelectedPlayerAi = viewModel.PlayerAis.Single(option => option.Value == id);
    }

    [Test]
    public async Task PickerTest()
    {
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();
        BattleScreenViewModel viewModel = setup.ViewModel;

        Assert.That(viewModel.ShowPlayerAiPicker, Is.True);
        Assert.That(viewModel.SelectedPlayerAi!.Value, Is.EqualTo(AiBrainRegistry.ClassicId), "Classic by default");
        Assert.That(viewModel.PlayerAis.Select(option => option.Value), Is.EqualTo(AiBrainRegistry.BattleBrains.Select(info => info.Id)));
        Assert.That(viewModel.PlayerAis.Single(option => option.Value == NeverFleeId).Label, Is.EqualTo("Stand and fight"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task AutoPlayUsesThePickedAiTest(bool pick)
    {
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();

        if (pick)
        {
            Pick(setup.ViewModel, NeverFleeId);
        }

        Task autoTask = setup.ViewModel.ToggleAutoBattleCommand.ExecuteAsync(null);
        DriveToEnd(setup);
        await BattleHealModeTests.FinishAsync(setup, autoTask);

        Assert.That(setup.GameViewModel.TopScreen, Is.Not.InstanceOf<ErrorScreenViewModel>());
        bool fled = Events(setup).OfType<BattleUnitRetreatingEvent>().Any(ev => ev.UnitId == BattleHealModeTests.FighterId);
        Assert.That(fled, Is.EqualTo(!pick), pick ? "the picked AI never flees" : "control: Classic flees");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InstantResolveUsesThePickedAiTest(bool pick)
    {
        using BattleHealModeTests.Setup setup = await CreateBattleAsync();

        if (pick)
        {
            Pick(setup.ViewModel, NeverFleeId);
        }

        setup.ViewModel.AutoResolveCommand.Execute(null);
        await Assert.IsOfType<ConfirmPromptViewModel>(setup.GameViewModel.TopScreen).ConfirmCommand.ExecuteAsync(null);

        bool fled = Events(setup).OfType<BattleUnitRetreatingEvent>().Any(ev => ev.UnitId == BattleHealModeTests.FighterId);
        Assert.That(fled, Is.EqualTo(!pick), pick ? "the picked AI never flees" : "control: Classic flees");
    }
}
