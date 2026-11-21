using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Gallean.Engine;
using Gallean.Engine.Contracts;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the AI editor mode (docs/ai-designer.md).
public sealed partial class VisualTests
{
    // Drags with the real mouse path (press, several moves, release) so the view's drop code is exercised.
    private static async Task DragAsync(Window window, Control from, Control to, Point? toOffset = null)
    {
        from.BringIntoView();
        await SkipFramesAsync(3);
        Point start = Assert.NotNull(from.TranslatePoint(new Point(from.Bounds.Width / 2, from.Bounds.Height / 2), window));
        Point end = Assert.NotNull(to.TranslatePoint(toOffset ?? new Point(to.Bounds.Width - 12, to.Bounds.Height / 2), window));

        window.MouseDown(start, MouseButton.Left);
        for (int step = 1; step <= 6; step++)
        {
            window.MouseMove(start + ((end - start) * (step / 6.0)));
            await SkipFramesAsync(1);
        }

        window.MouseUp(end, MouseButton.Left);
        await SkipFramesAsync(2);
    }

    [AvaloniaTest]
    public async Task AiDesignerTest()
    {
        using Sandbox sb = Sandbox.Create();
        string previous = DesignedAiLibrary.Directory;
        DesignedAiLibrary.Directory = sb.GetPath("AIs/map");

        try
        {
            (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);
            DesignedWorldAi design = new("custom:turtle", "Turtle, then strike", [
                DesignedWorldStage.Classic with
                {
                    Name = "Take it back",
                    When = [[new DesignedStageCondition.LostCity()], [new DesignedStageCondition.EnemyNearCapital(6)]],
                    Economy = [[WorldAiActionKind.PurchaseUnit], [WorldAiActionKind.HireLeader], [WorldAiActionKind.CastSpell]],
                    Army = [[WorldAiActionKind.UseItem], [WorldAiActionKind.AutoFit], [WorldAiActionKind.Attack, WorldAiActionKind.EnterCity, WorldAiActionKind.TakeBag], [WorldAiActionKind.Heal], [WorldAiActionKind.Move]],
                    Rules = new DesignedWorldRules(ReinforcePercent: 50, AttackPowerPercent: 70, WaitForFreeCapital: true)
                },
                DesignedWorldStage.Classic with { Name = "Build up", Rules = new DesignedWorldRules(AttackPowerPercent: 120) }
            ]);
            DesignedAiLibrary.Store.Save(design);
            DesignedAiLibrary.Reload();

            AiDesignerScreenViewModel designer = new(gameViewModel, design);
            gameViewModel.ShowScreen(designer);
            await SkipFramesAsync(4);
            await CaptureAsync(window, "1-loaded");

            AiDesignerScreenView view = window.GetVisualDescendants().OfType<AiDesignerScreenView>().Single();
            Control PaletteBlock(string label) => view.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("palette-block") && border.DataContext is AiDesignerPaletteItem item && item.Label == label);

            // Drag "Gold above N" from the palette into the first condition set of the first stage.
            Border firstGroup = view.GetVisualDescendants().OfType<Border>().First(border => border.Tag is "group");
            await DragAsync(window, PaletteBlock(Tr("Gold above N")), firstGroup);
            TestContext.Out.WriteLine("WHEN: " + string.Join(" | ", designer.ToDesign().Stages[0].When.Select(group => string.Join(" & ", group))));
            Assert.That(designer.ToDesign().Stages[0].When[0], Has.Length.EqualTo(2));

            // Drag the "Move and explore" block of the first stage onto the palette: removed.
            Border move = view.GetVisualDescendants().OfType<Border>()
                .First(border => border.Classes.Contains("block") && border.DataContext is AiDesignerBlockViewModel { Item.Action: WorldAiActionKind.Move });
            await DragAsync(window, move, view.FindControl<Border>("Palette")!, new Point(110, 300));
            Assert.That(designer.ToDesign().Stages[0].Army.SelectMany(tier => tier), Does.Not.Contain(WorldAiActionKind.Move));

            await CaptureAsync(window, "2-edited");

            // The help column follows the mouse over the palette.
            Control attackAdvantage = PaletteBlock(Tr("Attack advantage"));
            attackAdvantage.BringIntoView();
            await SkipFramesAsync(3);
            window.MouseMove(Assert.NotNull(attackAdvantage.TranslatePoint(new Point(20, 8), window)));
            await SkipFramesAsync(2);
            Assert.That(designer.HelpTitle, Is.EqualTo(Tr("Attack advantage")));
            await CaptureAsync(window, "3-help");
        }
        finally
        {
            DesignedAiLibrary.Directory = previous;
            AiBrainRegistry.SetDesignedWorldAis([]);
        }
    }

    [AvaloniaTest]
    public async Task AiDesignerRussianTest()
    {
        using Sandbox sb = Sandbox.Create();
        string previous = DesignedAiLibrary.Directory;
        DesignedAiLibrary.Directory = sb.GetPath("AIs/map");

        try
        {
            (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);
            gameViewModel.ShowScreen(new MainMenuScreenViewModel(gameViewModel, saves: []));
            SettingsScreenViewModel settings = new(gameViewModel);
            settings.Language = Language.Russian;

            DesignedWorldAi design = new("custom:ru", "Черепаха", [
                DesignedWorldStage.Classic with
                {
                    Name = "Вернуть город",
                    When = [[new DesignedStageCondition.LostCity(), new DesignedStageCondition.EnemyNearCapital(6)]],
                    Rules = new DesignedWorldRules(HurtPercent: 60, HealedPercent: 85, ReinforcePercent: 50, AttackPowerPercent: 70, LeaderSavingsGold: 200, WaitForFreeCapital: true)
                },
                DesignedWorldStage.Classic with { Name = "Развитие" }
            ]);

            gameViewModel.ShowScreen(new AiDesignerScreenViewModel(gameViewModel, design));
            await SkipFramesAsync(4);
            await CaptureAsync(window, "1-top");

            AiDesignerScreenView view = window.GetVisualDescendants().OfType<AiDesignerScreenView>().Single();
            view.GetVisualDescendants().OfType<Border>().First(border => border.Tag is "rules").BringIntoView();
            view.GetVisualDescendants().OfType<Border>().Last(border => border.Classes.Contains("palette-block")).BringIntoView();
            await SkipFramesAsync(4);
            await CaptureAsync(window, "2-rules");

            settings.Language = Language.English;
        }
        finally
        {
            DesignedAiLibrary.Directory = previous;
            AiBrainRegistry.SetDesignedWorldAis([]);
        }
    }

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
