using Avalonia.Headless.NUnit;
using Gallean.Common;
using Gallean.Configuration;
using Gallean.Engine;
using Gallean.Tests.Common;

namespace Gallean.Client.Tests;

// Gallean Upgrade: cities list, phase 1 (docs/cities-overview.md).
public sealed partial class VisualTests
{
    // S107 with every town given to the player (one rioting),
    // so the list has several rows; the world screen's top-left row with the new Cities button, then the list,
    // in English and Russian.
    [AvaloniaTest]
    public async Task CitiesListTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel loaded = LoadScenario(null);
        WorldState state = loaded.World.State;
        WorldPlayerId viewer = loaded.Snapshot.ViewPlayerId;
        List<WorldCity> towns = state.Fortifications.Values.OfType<WorldCity>().OrderBy(city => city.Id.Value, StringComparer.Ordinal).ToList();

        Assert.That(towns, Has.Count.GreaterThanOrEqualTo(3), "S107 needs a few towns for the list");

        var fortifications = state.Fortifications;

        for (int i = 0; i < towns.Count; i++)
        {
            WorldCity town = towns[i] with { OwnerId = viewer };

            if (i == 0)
            {
                town = town with { RiotTurns = 4 };
            }

            fortifications = fortifications.SetItem(town.Id, town);
        }

        GameModel gameModel = LoadScenario(state with { Fortifications = fortifications });
        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);
        await CaptureAsync(window, "1-world");

        for (int i = 0; i < 100 && !viewModel.OpenCitiesCommand.CanExecute(null); i++)
        {
            await SkipFramesAsync(10);
        }

        viewModel.OpenCitiesCommand.Execute(null);
        await SkipFramesAsync(20);

        ManagementScreenViewModel screen = Assert.IsOfType<ManagementScreenViewModel>(gameViewModel.TopScreen);
        CitiesTabViewModel cities = Assert.IsOfType<CitiesTabViewModel>(screen.SelectedTab);

        TestContext.Out.WriteLine($"rows={cities.Rows.Count}: {string.Join(", ", cities.Rows.Select(row => $"{row.Name} [{row.LevelText}] {row.GarrisonText} {string.Join("/", row.Statuses.Select(s => s.Text))}"))}");
        Assert.That(cities.Rows, Has.Count.EqualTo(towns.Count + 1));

        await CaptureAsync(window, "2-list");

        cities.SortByCommand.Execute(CitySortOrder.Danger);
        await SkipFramesAsync(10);
        await CaptureAsync(window, "3-danger");

        gameViewModel.CloseScreen(screen);
        SettingsScreenViewModel settings = new(gameViewModel) { Language = Language.Russian };
        await SkipFramesAsync(20);
        await CaptureAsync(window, "4-world-ru");

        viewModel.OpenCitiesCommand.Execute(null);
        await SkipFramesAsync(20);
        await CaptureAsync(window, "5-list-ru");

        settings.Language = Language.English;
        CitiesTabViewModel.LastSortOrder = CitySortOrder.Name;
    }

    // The widest resource plate (gold and all five manas at 9999) next to the four buttons, in English and Russian.
    [AvaloniaTest]
    public async Task CitiesButtonWideResourcesTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel loaded = LoadScenario(null);
        WorldState state = loaded.World.State;
        WorldPlayerId viewer = loaded.Snapshot.ViewPlayerId;
        WorldPlayer player = state.Players[viewer] with { Resources = new WorldResourceSet(9999, 9999, 9999, 9999, 9999, 9999) };

        GameModel gameModel = LoadScenario(state with { Players = state.Players.SetItem(viewer, player) });
        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);

        await WaitForWorldAsync(window);
        await CaptureAsync(window, "1-en");

        SettingsScreenViewModel settings = new(gameViewModel) { Language = Language.Russian };
        await SkipFramesAsync(20);
        await CaptureAsync(window, "2-ru");

        settings.Language = Language.English;
    }

    // Phase 2: the A / D keycaps and the "2 / 6" counter beside the city name; the capital, then the next town, then
    // the same town in Russian.
    [AvaloniaTest]
    public async Task CitySwitchTest()
    {
        using Sandbox sb = Sandbox.Create();

        GameModel loaded = LoadScenario(null);
        WorldState state = loaded.World.State;
        WorldPlayerId viewer = loaded.Snapshot.ViewPlayerId;
        var fortifications = state.Fortifications;

        foreach (WorldCity owned in state.Fortifications.Values.OfType<WorldCity>())
        {
            fortifications = fortifications.SetItem(owned.Id, owned with { OwnerId = viewer });
        }

        GameModel gameModel = LoadScenario(state with { Fortifications = fortifications });
        (GameViewModel gameViewModel, GameView window) = Initialize(sb, BaseWidth, BaseHeight);

        await gameModel.World.DispatchCommandAsync(new WorldStartGameCommand());
        WorldScreenViewModel viewModel = new(gameModel, gameViewModel, new RandomProvider(0));
        gameViewModel.ShowScreen(viewModel);
        await WaitForWorldAsync(window);

        for (int i = 0; i < 100 && !viewModel.OpenCapitalCommand.CanExecute(null); i++)
        {
            await SkipFramesAsync(10);
        }

        viewModel.OpenCapitalCommand.Execute(null);
        await SkipFramesAsync(30);
        ManagementScreenViewModel capital = Assert.IsOfType<ManagementScreenViewModel>(gameViewModel.TopScreen);
        TestContext.Out.WriteLine($"capital {capital.CityPositionText}");
        Assert.That(capital.HasCityCycle, Is.True);
        await CaptureAsync(window, "1-capital");

        capital.SelectNextCityCommand.Execute(null);
        await SkipFramesAsync(30);
        ManagementScreenViewModel town = Assert.IsOfType<ManagementScreenViewModel>(gameViewModel.TopScreen);
        TestContext.Out.WriteLine($"town {town.CityPositionText}");
        await CaptureAsync(window, "2-next");

        gameViewModel.CloseScreen(town);
        SettingsScreenViewModel settings = new(gameViewModel) { Language = Language.Russian };
        viewModel.OpenCapitalCommand.Execute(null);
        await SkipFramesAsync(30);
        Assert.IsOfType<ManagementScreenViewModel>(gameViewModel.TopScreen).SelectNextCityCommand.Execute(null);
        await SkipFramesAsync(30);
        await CaptureAsync(window, "3-next-ru");

        settings.Language = Language.English;
    }
}

