using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (docs/cities-overview.md, phase 2): previous / next city (A / D, or the keycaps at the left of the tab bar, with the city name and "2 / 7")
// on a city or capital screen, in the cities list's order. The next city's screen replaces this one in the screen stack,
// so Esc still goes back to where the player came from (the list or the map).
internal sealed partial class ManagementScreenViewModel
{
    private WorldFortificationId? cycleCityId;

    public bool HasCityCycle { get; private set; }
    public string CityPositionText { get; private set; } = string.Empty;
    public string CityName { get; private set; } = string.Empty;

    private void InitializeCityCycle(WorldFortificationId fortificationId)
    {
        WorldFortification? fortification = WorldState.Fortifications.GetValueOrDefault(fortificationId);

        if (fortification is not (WorldCity or WorldCapital) || fortification.Owner != ViewPlayerId)
        {
            return;
        }

        IReadOnlyList<WorldFortificationId> cycle = CitiesTabViewModel.GetCycle(GameModel);
        int index = IndexOf(cycle, fortificationId);

        cycleCityId = fortificationId;
        HasCityCycle = index >= 0 && cycle.Count > 1;
        CityPositionText = HasCityCycle ? $"{index + 1} / {cycle.Count}" : string.Empty;
        CityName = GameModel.Texts.GetScenarioText(fortification.NameTextId);

        RegisterSnapshotGatedCommands(SelectPreviousCityCommand, SelectNextCityCommand);
        SelectPreviousCityCommand.NotifyCanExecuteChanged();
        SelectNextCityCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanSwitchCity))]
    private void SelectPreviousCity()
    {
        SwitchCity(-1);
    }

    [RelayCommand(CanExecute = nameof(CanSwitchCity))]
    private void SelectNextCity()
    {
        SwitchCity(1);
    }

    private bool CanSwitchCity()
    {
        return HasCityCycle && !IsBusy;
    }

    // True while the open tab is in the middle of something a switch would drop: typing, a drag, or picking a unit to
    // heal / resurrect / dismiss.
    private bool IsTabBusy()
    {
        return SelectedTab.IsTyping || SelectedTab switch
        {
            CityTabViewModel city => city.TargetedAction is not null || city.DragDrop.Dragged is not null,
            StackTabViewModel stack => stack.DragDrop.Dragged is not null,
            _ => false
        };
    }

    private void SwitchCity(int step)
    {
        if (cycleCityId is not { } current || !CanSwitchCity() || IsTabBusy())
        {
            return;
        }

        IReadOnlyList<WorldFortificationId> cycle = CitiesTabViewModel.GetCycle(GameModel);
        int index = IndexOf(cycle, current);

        if (index < 0 || cycle.Count < 2)
        {
            return;
        }

        WorldFortificationId next = cycle[(index + step + cycle.Count) % cycle.Count];
        bool keepsParty = SelectedTab is VisitingStackTabViewModel;
        ManagementScreenViewModel screen = CreateForFortification(GameModel, Game, next);

        if (keepsParty && screen.Tabs.OfType<VisitingStackTabViewModel>().FirstOrDefault() is { } party)
        {
            screen.ChangeSelectedTab(party);
        }

        CloseSelf();
        Game.OpenScreen(screen);
    }

    private static int IndexOf(IReadOnlyList<WorldFortificationId> cycle, WorldFortificationId id)
    {
        for (int i = 0; i < cycle.Count; i++)
        {
            if (cycle[i] == id)
            {
                return i;
            }
        }

        return -1;
    }
}
