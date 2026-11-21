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

            // Phase 4: items and upgrades. The banner first (marked: no Banner Bearer yet), then the upgrade list.
            screen.Attacker.EquipmentSlots[0].ClickCommand.Execute(null);
            await CaptureAsync(window, "items");
            MapEditorListPickerViewModel items = Assert.IsOfType<MapEditorListPickerViewModel>(game.TopScreen);
            items.Options.Single(option => Equals(option.Value, new ItemDefinitionId("G000IG1003"))).PickCommand.Execute(null);
            screen.Attacker.EquipmentSlots[4].ClickCommand.Execute(null);
            Assert.IsOfType<MapEditorListPickerViewModel>(game.TopScreen).Options.Single(option => Equals(option.Value, new ItemDefinitionId("G000IG2001"))).PickCommand.Execute(null);
            screen.LevelUpCommand.Execute(null);
            screen.LevelUpCommand.Execute(null);
            await CaptureAsync(window, "item-problem");
            screen.Attacker.ChooseUpgradesCommand.Execute(null);
            BattleSandboxUpgradesViewModel upgrades = Assert.IsOfType<BattleSandboxUpgradesViewModel>(game.TopScreen);
            upgrades.Options.Single(option => option.ModifierId == new UnitModifierDefinitionId("g000um4003")).SelectCommand.Execute(null);
            await CaptureAsync(window, "upgrades");
            upgrades.Options.Single(option => option.ModifierId == new UnitModifierDefinitionId("g000um4001")).SelectCommand.Execute(null);
            upgrades.ConfirmCommand.Execute(null);
            await CaptureAsync(window, "equipped");

            // Phase 5: Run N with the results popup.
            screen.RunCount = 300;
            screen.AlternateSides = true;
            screen.NextSeed = () => 1;
            await screen.RunCommand.ExecuteAsync(null);
            await CaptureAsync(window, "results");
            Assert.IsOfType<BattleSandboxRunViewModel>(game.TopScreen).CloseCommand.Execute(null);

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
            screen.NextSeed = () => 1;
            screen.RunCount = 200;
            await screen.RunCommand.ExecuteAsync(null);
            await CaptureAsync(window, "ru-results");
            Assert.IsOfType<BattleSandboxRunViewModel>(game.TopScreen).CloseCommand.Execute(null);
            settings.Language = Language.English;
        }
        finally
        {
            BattleSandboxScreenViewModel.ArmiesDirectory = previous;
        }
    }
}
