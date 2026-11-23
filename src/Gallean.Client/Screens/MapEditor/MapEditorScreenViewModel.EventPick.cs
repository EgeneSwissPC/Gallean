using System.Collections.Immutable;
using Gallean.Engine;

namespace Gallean.Client;

// Events on the map: picking a block's target on the map, "used by" in the hover line, and a warning before deleting
// something events point at (docs/map-editor-events.md, phase 3). While picking, the event screen steps aside, the
// valid targets glow gold, a click on one picks it and brings the screen back; Esc or the right button cancels.
internal sealed partial class MapEditorScreenViewModel
{
    private EventPick? eventPick;
    private bool swallowStroke;

    public bool IsPickingEventTarget => eventPick is not null;

    public MapEditorPickKind? EventPickKind => eventPick?.Kind;

    public void BeginEventPick(MapEditorPickKind kind, Action<object> picked, Action cancelled)
    {
        eventPick = new EventPick(kind, picked, cancelled);
        OnPropertyChanged(nameof(IsPickingEventTarget));
        OnPropertyChanged(nameof(ShowEventsList));
        OnPropertyChanged(nameof(ShowLocations));
        RefreshPreview();
    }

    // Esc, the right button or leaving the editor: the screen comes back unchanged.
    public bool CancelEventPick()
    {
        if (eventPick is not { } pick)
        {
            return false;
        }

        EndEventPick();
        pick.Cancelled();

        return true;
    }

    // A click while picking: the target on the tile, or a beep and keep picking.
    private void ClickEventPick(WorldPosition tile)
    {
        if (eventPick is not { } pick)
        {
            return;
        }

        object? value = pick.Kind switch
        {
            MapEditorPickKind.Zone => map.PickLocation(tile)?.Id,
            MapEditorPickKind.City => map.GetCapitalAt(tile) is { } capital
                ? new CustomMapCity.Capital(capital.Race)
                : map.GetVillageAt(tile) is { } village ? new CustomMapCity.Village(village.Id) : null,
            MapEditorPickKind.Ruin => map.GetRuinAt(tile)?.Id,
            MapEditorPickKind.Army => map.GetStackAt(tile)?.Id,
            MapEditorPickKind.ArmyRef => map.GetStackAt(tile) is { } stack ? new CustomMapArmyRef.Army(stack.Id) : null,
            MapEditorPickKind.Site => map.Sites.FirstOrDefault(site => site.Footprint.Contains(tile))?.Id,
            MapEditorPickKind.Landmark => map.GetLandmarkAt(tile)?.Id,
            _ => null
        };

        if (value is null)
        {
            Game.ShowError(PickHint(pick.Kind));
            return;
        }

        EndEventPick();
        pick.Picked(value);
    }

    private void EndEventPick()
    {
        eventPick = null;
        OnPropertyChanged(nameof(IsPickingEventTarget));
        OnPropertyChanged(nameof(ShowEventsList));
        OnPropertyChanged(nameof(ShowLocations));
        RefreshPreview();
    }

    // The tiles of every valid target, drawn gold like the economy panel's highlight.
    private IReadOnlyList<WorldPosition> GetEventPickHighlight()
    {
        if (eventPick is not { } pick)
        {
            return [];
        }

        IEnumerable<WorldFootprint> footprints = pick.Kind switch
        {
            MapEditorPickKind.Zone => map.Locations.Select(location => location.Footprint),
            MapEditorPickKind.City => [.. map.Players.Select(player => player.Footprint).OfType<WorldFootprint>(), .. map.Villages.Select(village => village.Footprint)],
            MapEditorPickKind.Ruin => map.Ruins.Select(ruin => ruin.Footprint),
            MapEditorPickKind.Army or MapEditorPickKind.ArmyRef => map.Stacks.Select(stack => new WorldFootprint(stack.Position, 1, 1)),
            MapEditorPickKind.Site => map.Sites.Select(site => site.Footprint),
            MapEditorPickKind.Landmark => map.Landmarks.Select(landmark => landmark.Footprint),
            _ => []
        };

        return [.. footprints.SelectMany(footprint => footprint.EnumerateTiles()).Where(map.IsValidPosition).Distinct()];
    }

    private MapEditorPlacementPreview EventPickPreview()
    {
        return MapEditorPlacementPreview.None with { Text = PickHint(eventPick!.Kind) };
    }

    private static string PickHint(MapEditorPickKind kind)
    {
        return kind switch
        {
            MapEditorPickKind.Zone => Tr("Pick a zone: click one of the gold squares · Esc cancels"),
            MapEditorPickKind.City => Tr("Pick a city: click a gold capital or village · Esc cancels"),
            MapEditorPickKind.Ruin => Tr("Pick a ruin: click a gold ruin · Esc cancels"),
            MapEditorPickKind.Site => Tr("Pick a site: click a gold site · Esc cancels"),
            MapEditorPickKind.Landmark => Tr("Pick a landmark: click a gold landmark · Esc cancels"),
            _ => Tr("Pick an army: click a gold army · Esc cancels")
        };
    }

    // "Used by events: Gates, Northgate" for the objects and zones on the hover tile (empty when none).
    private string EventUsageHoverText(WorldPosition tile)
    {
        string[] names = [.. map.GetEventTargetsAt(tile).SelectMany(map.GetEventsUsing).Distinct().Select(ev => ev.Name)];

        return names.Length == 0 ? string.Empty : Tr("Used by events: {0}", string.Join(", ", names));
    }

    // Deletes at once when no event points at target; otherwise asks once. Either way the list's red marks follow.
    private void ConfirmRemoval(CustomMapEventTarget target, string name, Action remove)
    {
        IReadOnlyList<CustomMapEvent> users = map.GetEventsUsing(target);

        void Remove()
        {
            remove();
            RebuildEventRows();
        }

        if (users.Count == 0)
        {
            Remove();
            return;
        }

        string names = string.Join(", ", users.Select(ev => ev.Name));
        Game.OpenScreen(new ConfirmPromptViewModel(
            TrSpans("Delete {0}", name),
            users.Count == 1
                ? TrSpans("\"{0}\" is used by the event {1}. Delete it anyway? That block turns red, and the event is left out of the saved map until you fix it.", name, names)
                : TrSpans("\"{0}\" is used by {1} events: {2}. Delete it anyway? Their blocks turn red, and the events are left out of the saved map until you fix them.", name, users.Count, names),
            Tr("Delete anyway"),
            ConfirmPromptType.Danger,
            () =>
            {
                Remove();
                return Task.CompletedTask;
            }));
    }

    // After saving: which events the scenario left out (empty when none).
    private string SkippedEventsText()
    {
        HashSet<int> saved = [.. map.GetSaveableEvents().Select(ev => ev.Id)];
        string[] skipped = [.. map.Events.Where(ev => !saved.Contains(ev.Id)).Select(ev => ev.Name)];

        return skipped.Length switch
        {
            0 => string.Empty,
            1 => Tr("The event {0} was left out of the game until it is fixed.", skipped[0]),
            _ => Tr("{0} events were left out of the game until they are fixed: {1}.", skipped.Length, string.Join(", ", skipped))
        };
    }

    // The block editor asks for a target on the map: it steps aside and comes back with the pick.
    private void PickEventTargetOnMap(MapEditorPickKind kind, Action<object> picked)
    {
        if (Game.TopScreen is not MapEditorEventScreenViewModel screen)
        {
            return;
        }

        Game.CloseScreen(screen);
        BeginEventPick(
            kind,
            value =>
            {
                Game.OpenScreen(screen);
                picked(value);
            },
            () => Game.OpenScreen(screen));
    }

    private sealed record EventPick(MapEditorPickKind Kind, Action<object> Picked, Action Cancelled);
}

internal enum MapEditorPickKind
{
    Zone,
    City,
    Ruin,
    Army,
    // An army effect's subject: the picked army as a CustomMapArmyRef.
    ArmyRef,
    Site,
    Landmark
}
