using Avalonia.Headless.NUnit;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: the battle sandbox setup screen (docs/battle-ai-designer.md, phase 3).
public sealed partial class VisualTests
{
    [AvaloniaTest]
    [NonParallelizable]
    public async Task BattleSandboxTest()
    {
        using Sandbox sb = Sandbox.Create();
        string previous = BattleSandboxScreenViewModel.ArmiesDirectory;
        BattleSandboxScreenViewModel.ArmiesDirectory = sb.GetPath("Battles/armies");

        try
        {
            (GameViewModel game, GameView window, _) = InitializeMapEditor(sb);

            await game.ShowMainMenuCommand.ExecuteAsync(null);
            await CaptureAsync(window, "main-menu");

            await game.ShowBattleSandboxCommand.ExecuteAsync(null);
            BattleSandboxScreenViewModel screen = Assert.IsOfType<BattleSandboxScreenViewModel>(game.RootScreen);
            await CaptureAsync(window, "empty");

            await BattleSandboxTests.BuildArmiesAsync(game, screen);
            screen.Attacker.PresetName = "Empire raiders";
            screen.Attacker.SavePresetCommand.Execute(null);
            screen.SetPlaceCommand.Execute(screen.Places.Single(option => option.Value == BattleBenchPlace.City));
            screen.CityLevelUpCommand.Execute(null);
            screen.CityLevelUpCommand.Execute(null);
            await screen.Attacker.UnitGrid.Slots.First(slot => slot.Position == GridPosition.FrontCenter).SelectCommand.ExecuteAsync(null);
            await CaptureAsync(window, "city");

            screen.SetPlaceCommand.Execute(screen.Places.Single(option => option.Value == BattleBenchPlace.Capital));
            screen.Defender.Owners.Single(owner => owner.Race == WorldRace.Elf).SelectCommand.Execute(null);
            screen.Attacker.AllowsAnything = true;
            screen.SelectedDifficulty = screen.Difficulties.Single(option => option.Value == WorldDifficulty.VeryHard);
            await CaptureAsync(window, "capital");

            screen.Defender.Owners.Single(owner => owner.Race == WorldRace.Neutral).SelectCommand.Execute(null);
            screen.AsGarrison = true;
            await CaptureAsync(window, "problem");

            SettingsScreenViewModel settings = new(game) { Language = Language.Russian };
            await game.ShowBattleSandboxCommand.ExecuteAsync(null);
            screen = Assert.IsOfType<BattleSandboxScreenViewModel>(game.RootScreen);
            await BattleSandboxTests.BuildArmiesAsync(game, screen);
            screen.SetPlaceCommand.Execute(screen.Places.Single(option => option.Value == BattleBenchPlace.City));
            screen.Defender.SelectedPreset = "Empire raiders";
            await CaptureAsync(window, "ru");
            settings.Language = Language.English;
        }
        finally
        {
            BattleSandboxScreenViewModel.ArmiesDirectory = previous;
        }
    }
}
