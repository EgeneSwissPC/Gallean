using Avalonia.Headless.NUnit;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI editor mode (docs/ai-designer.md).
public sealed partial class VisualTests
{
    [AvaloniaTest]
    public async Task AiEditorMenuTest()
    {
        using Sandbox sb = Sandbox.Create();
        string previous = DesignedAiLibrary.Directory;
        DesignedAiLibrary.Directory = sb.GetPath("AIs/map");

        try
        {
            (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);
            DesignedAiLibrary.Store.Save(DesignedWorldAi.FromBuiltIn(AiPersonalities.AggressiveId, "raider", "Raider"));
            DesignedAiLibrary.Store.Save(new DesignedWorldAi("custom:turtle", "Turtle, then strike", [
                DesignedWorldStage.Classic with { Name = "Strike", When = [[new DesignedStageCondition.FromTurn(20)]] },
                DesignedWorldStage.Classic
            ]));

            gameViewModel.ShowAiEditorCommand.Execute(null);
            await CaptureAsync(window, "list");

            DesignedAiLibrary.Store.Delete("custom:raider");
            DesignedAiLibrary.Store.Delete("custom:turtle");
            gameViewModel.ShowAiEditorCommand.Execute(null);
            await CaptureAsync(window, "empty");
        }
        finally
        {
            DesignedAiLibrary.Directory = previous;
            AiBrainRegistry.SetDesignedWorldAis([]);
        }
    }
}
