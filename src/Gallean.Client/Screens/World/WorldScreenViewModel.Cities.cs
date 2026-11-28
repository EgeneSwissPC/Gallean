using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (docs/cities-overview.md): the Cities button (I) opens the list of the player's cities; a row's
// "Show on map" centres the camera on that city.
internal sealed partial class WorldScreenViewModel
{
    [RelayCommand(CanExecute = nameof(CanOpenCities))]
    private void OpenCities()
    {
        ClearSelectedAction();
        Game.OpenScreen(ManagementScreenViewModel.CreateForCities(GameModel, Game));
    }

    private bool CanOpenCities()
    {
        if (IsBusy)
        {
            return false;
        }

        foreach (WorldFortification fortification in WorldState.Fortifications.Values)
        {
            if (fortification is WorldCity or WorldCapital && fortification.Owner == ViewPlayerId)
            {
                return true;
            }
        }

        return false;
    }

    internal void ShowPosition(WorldPosition position)
    {
        ClearSelectedAction();
        FocusCamera(position, TimeSpan.Zero);
    }
}
