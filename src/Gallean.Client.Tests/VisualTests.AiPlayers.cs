using Avalonia.Headless.NUnit;
using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Engine.Tests;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI players panel (docs/ai-switching.md).
public sealed partial class VisualTests
{
    [AvaloniaTest]
    public async Task AiPlayersTest()
    {
        using Sandbox sb = Sandbox.Create();

        (IReadOnlyList<GameCampaign> campaigns, IGameTextProvider texts, IGameDefinitionProvider definitions) = new GameLoader(new RandomProvider(0)).LoadCampaigns(Language.English);
        GameScenario scenario = campaigns[0].Scenarios[1];

        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        RaceSelectScreenViewModel raceSelect = new(gameViewModel, NullAssetPreloader.Instance, texts, definitions, scenario);
        gameViewModel.ShowScreen(raceSelect);

        await CaptureAsync(window, "race-select");

        raceSelect.OpenAiPlayersCommand.Execute(null);
        AiPlayersScreenViewModel panel = (AiPlayersScreenViewModel)gameViewModel.TopScreen!;
        panel.Players[0].BattleAi = AiBrainRegistry.ClassicId;

        await CaptureAsync(window, "panel");
    }
}
