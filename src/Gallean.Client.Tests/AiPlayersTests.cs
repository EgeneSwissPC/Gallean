using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI players panel on the race screen and in the game menu, and the
// question when the settings change while players have their own AI (docs/ai-switching.md).
public sealed class AiPlayersTests
{
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (int i = 0; i < 100 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.That(condition(), Is.True);
    }

    private static async Task<WorldScreenViewModel> PassBriefingAsync(GameViewModel gameViewModel)
    {
        if (gameViewModel.TopScreen is BriefingScreenViewModel briefing)
        {
            await briefing.ContinueCommand.ExecuteAsync(null);
        }

        return Assert.IsOfType<WorldScreenViewModel>(gameViewModel.RootScreen);
    }

    [Test]
    public async Task RaceScreenOwnAiReachesGameTest()
    {
        using Sandbox sb = Sandbox.Create();
        using GameViewModel gameViewModel = new TestGameViewModelBuilder().WithSaveStore(new GameSaveStore(sb.GetPath("saves"), 10)).Build();

        await gameViewModel.ShowScenarioSelectCommand.ExecuteAsync(null);
        ScenarioSelectScreenViewModel scenarioSelect = Assert.IsOfType<ScenarioSelectScreenViewModel>(gameViewModel.RootScreen);
        scenarioSelect.StartCommand.Execute(null);
        RaceSelectScreenViewModel raceSelect = Assert.IsOfType<RaceSelectScreenViewModel>(gameViewModel.TopScreen);

        // Both races human: only the neutrals are listed.
        raceSelect.Races[0].PlayerType = PlayerType.Human;
        raceSelect.Races[1].PlayerType = PlayerType.Human;
        raceSelect.OpenAiPlayersCommand.Execute(null);
        AiPlayersScreenViewModel neutralsOnly = Assert.IsOfType<AiPlayersScreenViewModel>(gameViewModel.TopScreen);
        Assert.That(neutralsOnly.Players.Select(row => row.Name), Is.EqualTo(new[] { "Neutrals" }));
        neutralsOnly.CloseCommand.Execute(null);

        raceSelect.Races[0].PlayerType = PlayerType.Computer;
        raceSelect.Races[1].PlayerType = PlayerType.Human;
        raceSelect.OpenAiPlayersCommand.Execute(null);

        AiPlayersScreenViewModel panel = Assert.IsOfType<AiPlayersScreenViewModel>(gameViewModel.TopScreen);
        Assert.That(panel.Players.Select(row => row.Name), Is.EqualTo(new[] { raceSelect.Races[0].Name, "Neutrals" }));
        Assert.That(panel.Players[0].WorldAi, Is.EqualTo(AiPlayerRowViewModel.SettingsChoice));
        Assert.That(panel.WorldAis, Is.EqualTo(new[] { AiPlayerRowViewModel.SettingsChoice, AiBrainRegistry.ClassicId }));

        panel.Players[0].BattleAi = AiBrainRegistry.ClassicId;
        panel.Players[1].WorldAi = AiBrainRegistry.ClassicId;
        Assert.That(panel.Players[0].HasOwnAi, Is.True);
        panel.CloseCommand.Execute(null);
        Assert.That(gameViewModel.TopScreen, Is.SameAs(raceSelect));

        raceSelect.NextCommand.Execute(null);
        ScenarioConfigurationScreenViewModel configuration = Assert.IsOfType<ScenarioConfigurationScreenViewModel>(gameViewModel.TopScreen);
        WorldRace computerRace = raceSelect.Races[0].Race;

        Assert.That(
            configuration.GameConfiguration.Configuration!.AiOverrides,
            Is.EquivalentTo(new Dictionary<WorldRace, WorldAiOverride>
            {
                [computerRace] = new(null, AiBrainRegistry.ClassicId),
                [WorldRace.Neutral] = new(AiBrainRegistry.ClassicId, null)
            }));

        await gameViewModel.StartGameCommand.ExecuteAsync(configuration.GameConfiguration);
        WorldScreenViewModel world = await PassBriefingAsync(gameViewModel);
        WorldPlayer computer = world.GameModel.Snapshot.WorldState.Players.Values.Single(player => player.Race == computerRace);

        Assert.That(computer.AiOverride, Is.EqualTo(new WorldAiOverride(null, AiBrainRegistry.ClassicId)));

        List<WorldPlayer> neutrals = [.. world.GameModel.Snapshot.WorldState.Players.Values.Where(player => player.Race == WorldRace.Neutral)];
        Assert.That(neutrals, Is.Not.Empty);
        Assert.That(neutrals.Select(player => player.AiOverride), Is.All.EqualTo(new WorldAiOverride(AiBrainRegistry.ClassicId, null)));
    }

    [Test]
    public async Task GameMenuOwnAiAndSettingsQuestionTest()
    {
        using Sandbox sb = Sandbox.Create();
        using GameViewModel gameViewModel = new TestGameViewModelBuilder().WithSaveStore(new GameSaveStore(sb.GetPath("saves"), 10)).Build();
        await gameViewModel.ShowMainMenuCommand.ExecuteAsync(null);
        await gameViewModel.StartGameCommand.ExecuteAsync(new GameConfiguration("S108SC0000", Configuration: null));
        WorldScreenViewModel world = await PassBriefingAsync(gameViewModel);
        GameModel gameModel = world.GameModel;

        GameMenuViewModel menu = new(gameModel, gameViewModel);
        gameViewModel.OpenScreen(menu);
        Assert.That(menu.OpenAiPlayersCommand.CanExecute(null), Is.True);
        menu.OpenAiPlayersCommand.Execute(null);

        AiPlayersScreenViewModel panel = Assert.IsOfType<AiPlayersScreenViewModel>(gameViewModel.TopScreen);
        List<WorldPlayer> computers = [.. gameModel.Snapshot.WorldState.Players.Values.Where(player => player.IsAi)];
        Assert.That(panel.Players, Has.Count.EqualTo(computers.Count));
        Assert.That(computers.Any(player => player.Race == WorldRace.Neutral), Is.True);
        Assert.That(panel.Players.Select(row => row.Name), Is.All.Not.Empty);
        Assert.That(panel.Players.Select(row => row.Name), Does.Contain("Neutrals"));

        panel.Players[0].WorldAi = AiBrainRegistry.ClassicId;
        panel.CloseCommand.Execute(null);
        WorldPlayerId ownId = (WorldPlayerId)panel.Players[0].Key;
        await WaitForAsync(() => gameModel.Snapshot.WorldState.Players[ownId].AiOverride is not null);
        Assert.That(gameModel.Snapshot.WorldState.Players[ownId].AiOverride, Is.EqualTo(new WorldAiOverride(AiBrainRegistry.ClassicId, null)));

        // Reopening shows the saved choice.
        menu.OpenAiPlayersCommand.Execute(null);
        AiPlayersScreenViewModel reopened = Assert.IsOfType<AiPlayersScreenViewModel>(gameViewModel.TopScreen);
        Assert.That(reopened.Players[0].WorldAi, Is.EqualTo(AiBrainRegistry.ClassicId));
        reopened.CloseCommand.Execute(null);

        // Changing the Battle AI asks nothing: nobody has an own Battle AI.
        gameViewModel.OpenSettingsCommand.Execute(null);
        SettingsScreenViewModel settings = Assert.IsOfType<SettingsScreenViewModel>(gameViewModel.TopScreen);
        settings.BattleAi = "some-battle-ai";
        Assert.That(gameViewModel.TopScreen, Is.SameAs(settings));

        // Changing the Map AI asks; closing the question keeps the player's own AI.
        settings.WorldAi = "some-map-ai";
        ConfirmPromptViewModel question = Assert.IsOfType<ConfirmPromptViewModel>(gameViewModel.TopScreen);
        question.CloseCommand.Execute(null);
        Assert.That(gameModel.Snapshot.WorldState.Players[ownId].AiOverride, Is.Not.Null);
        Assert.That(gameViewModel.Settings.WorldAi, Is.EqualTo("some-map-ai"));

        // Confirming switches everyone: the own Map AI is removed.
        settings.WorldAi = AiBrainRegistry.ClassicId;
        question = Assert.IsOfType<ConfirmPromptViewModel>(gameViewModel.TopScreen);
        await question.ConfirmCommand.ExecuteAsync(null);
        await WaitForAsync(() => gameModel.Snapshot.WorldState.Players[ownId].AiOverride is null);
        Assert.That(gameViewModel.Settings.WorldAi, Is.Null);
    }
}
