using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The locations tool and list. A click on empty ground places a location (snapped onto the map) and opens its list
// row for naming; a click inside a location selects it (the selected one first, then the smallest); Shift+click
// removes it. The list sits over the left of the map, sorted by name, with a search box; hovering a row lights its
// square up, clicking it moves the camera there. Locations change nothing in the world, so edits only redraw the
// overlay (no preview rebuild).
internal sealed partial class MapEditorScreenViewModel
{
    private bool updatingLocations;
    private bool selectingFromMap;
    private int? hoveredLocationId;
    // Kept apart from the list selection, so a search that hides the row doesn't lose it.
    private int? selectedLocationId;
    private WorldPosition? cameraTarget;

    public bool IsLocationTool => SelectedTool.Value == MapEditorTool.Location;

    // The squares are drawn while the tool is on or the list is open.
    public bool ShowLocations => IsLocationTool || IsLocationsOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLandmarkPalette), nameof(ShowLocations))]
    public partial bool IsLocationsOpen { get; set; }

    // The radius new locations get (0-3); with a location selected, changing it resizes that location.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LocationSizeText))]
    [NotifyCanExecuteChangedFor(nameof(LocationRadiusDownCommand), nameof(LocationRadiusUpCommand))]
    public partial int LocationRadius { get; set; } = 1;

    public string LocationSizeText => SizeText(LocationRadius);

    public ObservableCollection<MapEditorLocationRowViewModel> LocationRows { get; } = [];

    [ObservableProperty]
    public partial MapEditorLocationRowViewModel? SelectedLocationRow { get; set; }

    [ObservableProperty]
    public partial string LocationSearch { get; set; } = string.Empty;

    public string LocationsTitle => Tr("Locations ({0})", map.Locations.Count);

    public bool HasLocations => map.Locations.Count > 0;

    public bool HasNoLocationRows => LocationRows.Count == 0;

    private int? SelectedLocationId => selectedLocationId;

    private string LocationNamePrefix => Tr("Location ");

    // The tile the camera should move to (a list row was clicked); the map view takes it once.
    public WorldPosition? TakeCameraTarget()
    {
        WorldPosition? target = cameraTarget;
        cameraTarget = null;

        return target;
    }

    [RelayCommand]
    private void ToggleLocations()
    {
        IsLocationsOpen = !IsLocationsOpen;
    }

    partial void OnIsLocationsOpenChanged(bool value)
    {
        if (value)
        {
            IsEconomyOpen = false;
            RebuildLocationRows();
        }

        RefreshPreview();
    }

    partial void OnLocationSearchChanged(string value)
    {
        RebuildLocationRows();
    }

    partial void OnSelectedLocationRowChanged(MapEditorLocationRowViewModel? value)
    {
        if (updatingLocations)
        {
            return;
        }

        selectedLocationId = value?.Id;

        if (value is not null && GetLocation(value.Id) is { } location)
        {
            SetLocationRadius(location.Radius);

            if (!selectingFromMap)
            {
                cameraTarget = location.Center;
            }
        }

        RefreshPreview();
    }

    private bool CanLocationRadiusDown() => LocationRadius > 0;

    private bool CanLocationRadiusUp() => LocationRadius < CustomMap.MaxLocationRadius;

    [RelayCommand(CanExecute = nameof(CanLocationRadiusDown))]
    private void LocationRadiusDown() => ChangeLocationRadius(-1);

    [RelayCommand(CanExecute = nameof(CanLocationRadiusUp))]
    private void LocationRadiusUp() => ChangeLocationRadius(1);

    // + / - keys and the size buttons: the radius for new locations, and the selected location's radius.
    public void ChangeLocationRadius(int delta)
    {
        int radius = Math.Clamp(LocationRadius + delta, 0, CustomMap.MaxLocationRadius);

        if (radius == LocationRadius)
        {
            return;
        }

        LocationRadius = radius;

        if (SelectedLocationId is { } id && GetLocation(id) is { } location && location.Radius != radius)
        {
            CommitLocations(current => current.ResizeLocation(id, radius) is not null, id);
        }
        else
        {
            RefreshPreview();
        }
    }

    // Lights a location's square up while the mouse is over its list row (null: none).
    public void SetHoveredLocation(int? id)
    {
        if (hoveredLocationId != id)
        {
            hoveredLocationId = id;
            RefreshPreview();
        }
    }

    public void RenameLocation(int id, string name)
    {
        if (GetLocation(id) is { } location && !string.IsNullOrWhiteSpace(name) && location.Name != name.Trim())
        {
            CommitLocations(current => current.RenameLocation(id, name) is not null, id);
        }
        else
        {
            RebuildLocationRows(id);
        }
    }

    public void DeleteLocation(int id)
    {
        CommitLocations(current => current.RemoveLocation(id), SelectedLocationId == id ? null : SelectedLocationId);
    }

    private void OnToolChangedForLocations(MapEditorTool tool)
    {
        if (tool == MapEditorTool.Location)
        {
            IsLocationsOpen = true;
        }

        OnPropertyChanged(nameof(IsLocationTool));
        OnPropertyChanged(nameof(ShowLocations));
    }

    private void ClickLocation(WorldPosition tile, bool remove)
    {
        CustomMapLocation? picked = map.PickLocation(tile, SelectedLocationId);

        if (remove)
        {
            if (picked is not null)
            {
                DeleteLocation(picked.Id);
            }

            return;
        }

        if (picked is not null)
        {
            SelectLocationFromMap(picked.Id);
            return;
        }

        PlaceLocationAt(tile);
    }

    // Places a location centred on tile (Ctrl+click: also inside another location, e.g. a small trap zone inside a
    // larger warning zone), selects it and opens its row for the name.
    public void PlaceLocationAt(WorldPosition tile)
    {
        if (!map.IsValidPosition(tile) || LeaveSpreadPreview())
        {
            return;
        }

        int placedId = 0;
        LocationSearch = string.Empty;
        CommitLocations(current =>
        {
            placedId = current.PlaceLocation(tile, LocationRadius, current.GetNextLocationName(LocationNamePrefix)).Id;
            return true;
        }, null);
        SelectLocationFromMap(placedId);
        SelectedLocationRow?.BeginEditCommand.Execute(null);
    }

    private void SelectLocationFromMap(int id)
    {
        if (!LocationRows.Any(row => row.Id == id))
        {
            LocationSearch = string.Empty;
        }

        selectingFromMap = true;

        try
        {
            SelectedLocationRow = LocationRows.FirstOrDefault(row => row.Id == id);
        }
        finally
        {
            selectingFromMap = false;
        }
    }

    // A location change: one undo step, the list rebuilt (keeping selectId selected) and the overlay redrawn.
    private void CommitLocations(Func<CustomMap, bool> change, int? selectId)
    {
        CustomMap before = map.Clone();

        if (change(map))
        {
            undoSteps.Push(before);
            TrimUndo();
            CanUndo = true;
            IsDirty = true;
            UpdateStatus();
        }

        RebuildLocationRows(selectId);
        RefreshPreview();
    }

    private void RebuildLocationRows()
    {
        RebuildLocationRows(SelectedLocationId);
    }

    // Rows sorted by name (culture-aware, so Cyrillic names sort too), filtered by the search box.
    private void RebuildLocationRows(int? selectId)
    {
        string search = LocationSearch.Trim();
        CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
        updatingLocations = true;

        try
        {
            LocationRows.Clear();

            foreach (CustomMapLocation location in map.Locations
                .Where(location => search.Length == 0 || compare.IndexOf(location.Name, search, CompareOptions.IgnoreCase) >= 0)
                .OrderBy(location => location.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
                .ThenBy(location => location.Id))
            {
                LocationRows.Add(new MapEditorLocationRowViewModel(
                    location.Id,
                    location.Name,
                    Tr("{0} · {1}, {2}", SizeText(location.Radius), location.Center.X, location.Center.Y),
                    this));
            }

            selectedLocationId = selectId is { } id && map.GetLocation(id) is not null ? id : null;
            SelectedLocationRow = selectedLocationId is { } shown ? LocationRows.FirstOrDefault(row => row.Id == shown) : null;
        }
        finally
        {
            updatingLocations = false;
        }

        if (selectedLocationId is { } selected && GetLocation(selected) is { } current)
        {
            SetLocationRadius(current.Radius);
        }

        OnPropertyChanged(nameof(LocationsTitle));
        OnPropertyChanged(nameof(HasLocations));
        OnPropertyChanged(nameof(HasNoLocationRows));
    }

    private void SetLocationRadius(int radius)
    {
        LocationRadius = radius;
    }

    private CustomMapLocation? GetLocation(int id)
    {
        return map.GetLocation(id);
    }

    private MapEditorPlacementPreview LocationPreview(WorldPosition tile)
    {
        if (map.PickLocation(tile, SelectedLocationId) is { } picked)
        {
            string action = picked.Id == SelectedLocationId
                ? Tr("{0} ({1}): selected · Ctrl+click places a zone inside · Shift+click removes it", picked.Name, SizeText(picked.Radius))
                : Tr("{0} ({1}): click selects it · Ctrl+click places a zone inside · Shift+click removes it", picked.Name, SizeText(picked.Radius));

            return new MapEditorPlacementPreview([], picked.Footprint, null, null, action);
        }

        WorldPosition center = map.SnapLocationCenter(tile, LocationRadius);
        WorldFootprint footprint = new CustomMapLocation(0, string.Empty, center, LocationRadius).Footprint;
        bool snapped = center != tile;
        MapEditorTileState state = snapped ? MapEditorTileState.Changes : MapEditorTileState.Fits;
        string name = map.GetNextLocationName(LocationNamePrefix);
        string text = snapped
            ? Tr("Location ({0}): click places \"{1}\" at {2}, {3} (moved inside the map)", SizeText(LocationRadius), name, center.X, center.Y)
            : Tr("Location ({0}): click places \"{1}\"", SizeText(LocationRadius), name);

        return new MapEditorPlacementPreview([.. footprint.EnumerateTiles().Select(position => new MapEditorPreviewTile(position, state))], null, null, null, text)
        {
            Footprint = footprint
        };
    }

    // The squares and names drawn over the map: the selected one and the hovered row outlined in gold.
    private IReadOnlyList<MapEditorLocationMark> BuildLocationMarks()
    {
        if (!ShowLocations || map.Locations.Count == 0)
        {
            return [];
        }

        return [.. map.Locations
            .OrderByDescending(location => location.Radius)
            .Select(location => new MapEditorLocationMark(location.Footprint, location.Center, location.Name, location.Id == SelectedLocationId || location.Id == hoveredLocationId))];
    }

    // "Locations here: Northern Pass, Pass Warning" for the hover line, with any tool (empty when there is none).
    private string LocationHoverText(WorldPosition tile)
    {
        string[] names = [.. map.GetLocationsAt(tile).Select(location => location.Name)];

        return names.Length == 0 ? string.Empty : Tr("Locations here: {0}", string.Join(", ", names));
    }

    private static string SizeText(int radius)
    {
        int side = (2 * radius) + 1;

        return $"{side}x{side}";
    }
}

// One row of the locations list: name (edited in place), size and position, and a delete button.
internal sealed partial class MapEditorLocationRowViewModel(int id, string name, string details, MapEditorScreenViewModel editor) : ObservableObject
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public string Details { get; } = details;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Name;
        IsEditing = true;
    }

    // Enter or leaving the box: a blank name keeps the old one.
    [RelayCommand]
    private void CommitEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        editor.RenameLocation(Id, EditText);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void Delete()
    {
        editor.DeleteLocation(Id);
    }
}
