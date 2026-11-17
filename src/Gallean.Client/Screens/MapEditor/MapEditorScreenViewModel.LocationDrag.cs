using Gallean.Engine;

namespace Gallean.Client;

// Dragging locations (Locations tool): press inside a zone and move to another tile to carry it; the grabbed tile
// stays under the cursor. Releasing moves it (snapped onto the map, one undo step, ID and name kept); Esc or the right
// button puts it back. Press and release on the same tile is a click: select a zone, or place one on empty ground.
// A drag that starts on empty ground does nothing. Exact positions go through the X/Y boxes.
internal sealed partial class MapEditorScreenViewModel
{
    private LocationPress? locationPress;

    public bool IsDraggingLocation => locationPress is { Dragging: true };

    public bool HasSelectedLocation => SelectedLocationId is { } id && GetLocation(id) is not null;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial string LocationXText { get; set; } = string.Empty;

    [CommunityToolkit.Mvvm.ComponentModel.ObservableProperty]
    public partial string LocationYText { get; set; } = string.Empty;

    // Left button pressed with the Locations tool (Shift+click removes at once, in ClickLocation).
    private void PressLocation(WorldPosition tile, bool remove)
    {
        locationPress = null;

        if (remove)
        {
            ClickLocation(tile, remove: true);
            return;
        }

        CustomMapLocation? picked = map.PickLocation(tile, SelectedLocationId);
        locationPress = new LocationPress(tile, picked, tile, Dragging: false);
        RefreshPreview();
    }

    // Returns true while a location press is in progress (the move is handled here).
    private bool MoveLocationPress(WorldPosition tile)
    {
        if (locationPress is not { } press)
        {
            return false;
        }

        if (press.Location is null)
        {
            // From empty ground: moving away turns the click into nothing (no stray locations).
            locationPress = press with { Current = tile };
            return true;
        }

        if (tile == press.Start && !press.Dragging)
        {
            return true;
        }

        bool startedDrag = !press.Dragging;
        locationPress = press with { Current = tile, Dragging = true };

        if (startedDrag)
        {
            SelectLocationFromMap(press.Location.Id);
            OnPropertyChanged(nameof(IsDraggingLocation));
        }

        RefreshPreview();

        return true;
    }

    // Returns true when the release ended a location press (a click or a drop).
    private bool ReleaseLocationPress()
    {
        if (locationPress is not { } press)
        {
            return false;
        }

        locationPress = null;
        OnPropertyChanged(nameof(IsDraggingLocation));

        if (!press.Dragging)
        {
            if (press.Location is not null || press.Current == press.Start)
            {
                ClickLocation(press.Start, remove: false);
            }

            return true;
        }

        WorldPosition center = DraggedCenter(press);

        if (center != press.Location!.Center)
        {
            CommitLocations(current => current.MoveLocation(press.Location.Id, center) is not null, press.Location.Id);
        }
        else
        {
            RefreshPreview();
        }

        return true;
    }

    // Esc or the right button during a drag: the location stays where it was.
    public bool CancelLocationDrag()
    {
        if (locationPress is not { Dragging: true })
        {
            return false;
        }

        locationPress = null;
        OnPropertyChanged(nameof(IsDraggingLocation));
        RefreshPreview();

        return true;
    }

    // The X/Y boxes (Enter or leaving a box): moves the selected location there, snapped onto the map.
    public void ApplyLocationPosition()
    {
        if (SelectedLocationId is not { } id || GetLocation(id) is not { } location)
        {
            return;
        }

        if (int.TryParse(LocationXText.Trim(), out int x) && int.TryParse(LocationYText.Trim(), out int y))
        {
            WorldPosition center = map.SnapLocationCenter(new WorldPosition(Math.Clamp(x, 0, map.Size - 1), Math.Clamp(y, 0, map.Size - 1)), location.Radius);

            if (center != location.Center)
            {
                CommitLocations(current => current.MoveLocation(id, center) is not null, id);
                return;
            }
        }

        UpdateLocationPositionFields();
    }

    private void UpdateLocationPositionFields()
    {
        CustomMapLocation? location = SelectedLocationId is { } id ? GetLocation(id) : null;
        LocationXText = location?.Center.X.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        LocationYText = location?.Center.Y.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        OnPropertyChanged(nameof(HasSelectedLocation));
    }

    private WorldPosition DraggedCenter(LocationPress press)
    {
        CustomMapLocation location = press.Location!;
        WorldPosition wanted = new(location.Center.X + press.Current.X - press.Start.X, location.Center.Y + press.Current.Y - press.Start.Y);

        return map.SnapLocationCenter(wanted, location.Radius);
    }

    // While dragging: the square at the drop place (amber where it was snapped onto the map), the old place dashed.
    private MapEditorPlacementPreview? LocationDragPreview()
    {
        if (locationPress is not { Dragging: true } press)
        {
            return null;
        }

        CustomMapLocation location = press.Location!;
        WorldPosition wanted = new(location.Center.X + press.Current.X - press.Start.X, location.Center.Y + press.Current.Y - press.Start.Y);
        WorldPosition center = DraggedCenter(press);
        bool snapped = center != wanted;
        WorldFootprint footprint = (location with { Center = center }).Footprint;
        MapEditorTileState state = snapped ? MapEditorTileState.Changes : MapEditorTileState.Fits;
        string text = snapped
            ? Tr("{0}: {1}, {2} -> {3}, {4} (snapped to the map edge) · Esc puts it back", location.Name, location.Center.X, location.Center.Y, center.X, center.Y)
            : Tr("{0}: {1}, {2} -> {3}, {4} · Esc puts it back", location.Name, location.Center.X, location.Center.Y, center.X, center.Y);

        return new MapEditorPlacementPreview([.. footprint.EnumerateTiles().Select(position => new MapEditorPreviewTile(position, state))], null, location.Footprint, null, text)
        {
            Footprint = footprint
        };
    }

    private sealed record LocationPress(WorldPosition Start, CustomMapLocation? Location, WorldPosition Current, bool Dragging);
}
