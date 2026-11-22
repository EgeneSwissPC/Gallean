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

            // + Stage scrolls the new card into sight.
            Button addStage = view.FindControl<Button>("AddStageButton")!;
            addStage.BringIntoView();
            await SkipFramesAsync(3);
            addStage.Command!.Execute(null);
            await SkipFramesAsync(6);
            Border newCard = view.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("stage") && border.DataContext == designer.Stages[^2]);
            ScrollViewer stagesView = view.FindControl<ScrollViewer>("StagesView")!;
            Point top = Assert.NotNull(newCard.TranslatePoint(new Point(0, 0), stagesView));
            Assert.That(top.Y, Is.InRange(-1, stagesView.Bounds.Height - 40));
            await CaptureAsync(window, "4-new-stage");
        }
        finally
        {
            DesignedAiLibrary.Directory = previous;
            AiBrainRegistry.SetDesignedWorldAis([]);
        }
    }

    [AvaloniaTest]
    public async Task AiDesignerTestPanelTest()
    {
        using Sandbox sb = Sandbox.Create();
        string previous = DesignedAiLibrary.Directory;
        DesignedAiLibrary.Directory = sb.GetPath("AIs/map");

        try
        {
            (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);
            DesignedWorldAi design = new("custom:turtle", "Turtle, then strike", [
                DesignedWorldStage.Classic with { Name = "Strike", When = [[new DesignedStageCondition.FromTurn(4)]] },
                DesignedWorldStage.Classic with { Name = "Build up" }
            ]);
            DesignedAiLibrary.Store.Save(design);
            DesignedAiLibrary.Reload();

            AiDesignerScreenViewModel designer = new(gameViewModel, design);
            gameViewModel.ShowScreen(designer);
            designer.TestGamesText = "2";
            designer.TestTurnsText = "8";
            await designer.RunTestCommand.ExecuteAsync(null);
            Assert.That(designer.TestResults, Has.Count.EqualTo(3));

            AiDesignerScreenView view = window.GetVisualDescendants().OfType<AiDesignerScreenView>().Single();
            view.FindControl<TextBlock>("TestProgressText")!.BringIntoView();
            await SkipFramesAsync(4);
            await CaptureAsync(window, "results");
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
                    Rules = new DesignedWorldRules(HurtPercent: 60, HealedPercent: 85, ReinforcePercent: 50, AttackPowerPercent: 70, LeaderSavingsGold: 200, WaitForFreeCapital: true,
                        KeepArmiesHome: 1, GatherArmies: 2, RushEnemyCapital: true, GoldReserve: 300)
                },
                DesignedWorldStage.Classic with { Name = "Развитие" }
            ]);

            gameViewModel.ShowScreen(new AiDesignerScreenViewModel(gameViewModel, design));
            await SkipFramesAsync(4);
            await CaptureAsync(window, "1-top");

            AiDesignerScreenView view = window.GetVisualDescendants().OfType<AiDesignerScreenView>().Single();
            view.GetVisualDescendants().OfType<Border>().First(border => border.Tag is "rules").BringIntoView();
            view.GetVisualDescendants().OfType<Border>().Last(border => border.Classes.Contains("palette-block") && border.DataContext is AiDesignerPaletteItem).BringIntoView();
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
        string previousBattle = DesignedBattleAiLibrary.Directory;
        DesignedAiLibrary.Directory = sb.GetPath("AIs/map");
        DesignedBattleAiLibrary.Directory = sb.GetPath("AIs/battle");

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

            // Gallean Upgrade (battle AI designer phase 8): the Battle AIs tab.
            DesignedBattleAiLibrary.Store.Save(DesignedBattleAi.FromBuiltIn(AiPersonalities.RuthlessId, "berserker", "Berserker"));
            DesignedBattleAiLibrary.Store.Save(new DesignedBattleAi("custom-battle:courier", "Careful courier", [
                new DesignedBattleStage("Rich and losing", [[new DesignedBattleCondition.CargoAbove(1000), new DesignedBattleCondition.PowerShareBelow(50)]], DesignedBattleTactics.Classic with { ProtectLeader = true }),
                DesignedBattleStage.Classic
            ]));
            AiEditorMenuScreenViewModel menu = Assert.IsOfType<AiEditorMenuScreenViewModel>(gameViewModel.RootScreen);
            menu.ShowBattleAisCommand.Execute(null);
            await CaptureAsync(window, "battle-list");
            Assert.That(menu.Ais.Select(item => item.Name), Is.EqualTo(new[] { "Berserker", "Careful courier" }));
            SettingsScreenViewModel settings = new(gameViewModel) { Language = Language.Russian };
            gameViewModel.ShowScreen(new AiEditorMenuScreenViewModel(gameViewModel, battle: true));
            await CaptureAsync(window, "battle-list-ru");
            settings.Language = Language.English;
            gameViewModel.ShowAiEditorCommand.Execute(null);

            DesignedAiLibrary.Store.Delete("custom:raider");
            DesignedAiLibrary.Store.Delete("custom:turtle");
            gameViewModel.ShowAiEditorCommand.Execute(null);
            await CaptureAsync(window, "empty");
        }
        finally
        {
            DesignedAiLibrary.Directory = previous;
            DesignedBattleAiLibrary.Directory = previousBattle;
            AiBrainRegistry.SetDesignedWorldAis([]);
            AiBrainRegistry.SetDesignedBattleAis([]);
        }
    }
}
