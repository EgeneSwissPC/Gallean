using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Engine;

namespace Gallean.Client;

// The placement preview: what a click with the current tool would do on the tile under the cursor. The rules come
// from CustomMap's Preview* methods, the same checks the Place* methods run, so the preview never disagrees with
// the click.
internal sealed partial class MapEditorScreenViewModel
{
    private bool previewSuspended;

    [ObservableProperty]
    public partial bool IsPreviewEnabled { get; set; } = true;

    public MapEditorPlacementPreview PlacementPreview { get; private set; } = MapEditorPlacementPreview.None;

    partial void OnIsPreviewEnabledChanged(bool value) => RefreshPreview();

    partial void OnBrushRadiusChanged(int value) => RefreshPreview();

    partial void OnSelectedLandmarkChanged(MapEditorLandmarkOption? value) => RefreshPreview();

    partial void OnSelectedCapitalRaceChanged(MapEditorOption<WorldRace> value) => RefreshPreview();

    partial void OnSelectedArmyOwnerChanged(MapEditorOption<WorldRace> value) => RefreshPreview();

    partial void OnSelectedVillageOwnerChanged(MapEditorOption<WorldRace> value) => RefreshPreview();

    partial void OnSelectedVillageLevelChanged(MapEditorOption<int> value) => RefreshPreview();

    partial void OnSelectedResourceTypeChanged(MapEditorOption<WorldResourceType> value) => RefreshPreview();

    partial void OnSelectedSiteTypeChanged(MapEditorOption<CustomMapSiteType> value) => RefreshPreview();

    partial void OnPositionInfoChanged(WorldPositionInfoViewModel? value) => RefreshPreview();

    // The map view hides the preview while the map is dragged with the middle button.
    public void SetPreviewSuspended(bool suspended)
    {
        if (previewSuspended != suspended)
        {
            previewSuspended = suspended;
            RefreshPreview();
        }
    }

    // Rebuilds the preview and the hover line for the hover tile, after the tile, a tool setting or the map changed.
    private void RefreshPreview()
    {
        if (map is null || SelectedTool is null)
        {
            return;
        }

        UpdateRoadGhost();
        UpdateEconomy();
        MapEditorPlacementPreview preview = hoverTile is { } tile && IsPreviewEnabled && !previewSuspended && PositionInfo is null
            ? IsSpreadPreview ? SpreadHoverPreview() : BuildPreview(tile)
            : MapEditorPlacementPreview.None;
        IReadOnlyList<WorldPosition> highlight = GetEconomyHighlight();

        if (highlight.Count > 0)
        {
            preview = preview with { Highlight = highlight };
        }

        PlacementPreview = preview;
        OnPropertyChanged(nameof(PlacementPreview));
        HoverText = hoverTile is { } current
            ? preview.Text.Length > 0 ? $"{preview.Text}\n{FormatTile(current)}" : FormatTile(current)
            : string.Empty;
        SetRenderRequest(CreateRenderRequest());
    }

    private MapEditorPlacementPreview BuildPreview(WorldPosition tile)
    {
        switch (SelectedTool.Value)
        {
            case MapEditorTool.Capital:
                if (map.GetCapitalAt(tile) is { } capital)
                {
                    return Opens(CapitalName(capital.Race), capital.Footprint);
                }

                WorldRace race = SelectedCapitalRace.Value;
                CustomMapPlacementPreview placed = map.PreviewCapital(race, map.CenterFootprint(tile, CustomMap.CapitalSize, CustomMap.CapitalSize));
                List<string> notes = [Tr("claims {0} tiles for {1}", CustomMap.CapitalSize * CustomMap.CapitalSize, MapEditorLabels.Race(race))];

                if (placed.MovesFrom is not null)
                {
                    notes.Add(Tr("moves it here with its garrison and army"));
                }

                return FromEngine(Sized(Tr("Capital"), CustomMap.CapitalSize, CustomMap.CapitalSize), placed, notes) with { MovesFrom = placed.Fits ? placed.MovesFrom : null };

            case MapEditorTool.Army:
                if (map.GetVillageAt(tile) is { } armyVillage)
                {
                    return Opens(VillageName(armyVillage), armyVillage.Footprint);
                }

                if (map.GetCapitalAt(tile) is { } armyCapital)
                {
                    return Opens(CapitalName(armyCapital.Race), armyCapital.Footprint);
                }

                if (map.GetStackAt(tile) is { } stack)
                {
                    return Opens(Tr("Army of {0}", MapEditorLabels.Owner(stack.Owner)), null, tile);
                }

                return FromEngine(Tr("Army"), map.PreviewStack(tile, SelectedArmyOwner.Value), []);

            case MapEditorTool.Village:
                if (map.GetVillageAt(tile) is { } village)
                {
                    return Opens(VillageName(village), village.Footprint);
                }

                WorldRace owner = SelectedVillageOwner.Value;
                CustomMapPlacementPreview villagePreview = map.PreviewVillage(
                    map.CenterFootprint(tile, CustomMapVillage.Size, CustomMapVillage.Size), SelectedVillageLevel.Value, owner);

                return FromEngine(
                    Sized(Tr("Village"), CustomMapVillage.Size, CustomMapVillage.Size),
                    villagePreview,
                    [Tr("claims {0} tiles for {1}", CustomMapVillage.Size * CustomMapVillage.Size, MapEditorLabels.Owner(owner))]);

            case MapEditorTool.Resource:
                WorldResourceType type = SelectedResourceType.Value;

                if (map.GetResourceAt(tile) is { } resource)
                {
                    return resource.Type == type
                        ? Removes(MapEditorLabels.Resource(resource.Type), null, tile)
                        : new MapEditorPlacementPreview([], null, null, tile, Tr("{0}: click changes it to {1}", MapEditorLabels.Resource(resource.Type), MapEditorLabels.Resource(type)));
                }

                return FromEngine(MapEditorLabels.Resource(type), map.PreviewResource(tile), [Tr("income goes to {0}", LandOwnerName(tile))]);

            case MapEditorTool.Ruin:
                if (map.GetRuinAt(tile) is { } ruin)
                {
                    return Opens($"{Tr("Ruin")} {ruin.Name}", ruin.Footprint);
                }

                return FromEngine(Sized(Tr("Ruin"), CustomMapRuin.Size, CustomMapRuin.Size), map.PreviewRuin(map.CenterFootprint(tile, CustomMapRuin.Size, CustomMapRuin.Size)), []);

            case MapEditorTool.Site:
                if (map.GetSiteAt(tile) is { } site)
                {
                    return Opens($"{MapEditorLabels.SiteType(site.Type)} {site.Name}", site.Footprint);
                }

                return FromEngine(
                    Sized(MapEditorLabels.SiteType(SelectedSiteType.Value), CustomMapSite.Size, CustomMapSite.Size),
                    map.PreviewSite(map.CenterFootprint(tile, CustomMapSite.Size, CustomMapSite.Size)),
                    []);

            case MapEditorTool.Bag:
                if (map.GetBagAt(tile) is not null)
                {
                    return Opens(Tr("Treasure"), null, tile);
                }

                return FromEngine(Tr("Treasure"), map.PreviewBag(tile), []);

            case MapEditorTool.Landmark:
                if (map.GetLandmarkAt(tile) is { } landmark)
                {
                    return Removes(LandmarkName(landmark.Type), landmark.Footprint, null);
                }

                if (SelectedLandmark is not { } option)
                {
                    return new MapEditorPlacementPreview([], null, null, null, Tr("Choose a landmark in the palette."));
                }

                WorldLandmarkDefinition definition = option.Definition;

                return FromEngine(
                    Sized(option.Name, definition.Width, definition.Height),
                    map.PreviewLandmark(map.CenterFootprint(tile, definition.Width, definition.Height), definition.Width, definition.Height),
                    []);

            case MapEditorTool.Road:
                return RoadPreview(tile);

            case MapEditorTool.Rod:
                return RodPreview(tile);

            default:
                return BrushPreview(tile);
        }
    }

    private MapEditorPlacementPreview BrushPreview(WorldPosition tile)
    {
        CustomMapBrush brush = ToBrush(SelectedTool.Value);
        CustomMapPlacementPreview preview = map.PreviewPaint(tile, BrushRadius, brush);
        string label = SelectedTool.Label;
        string text;

        if (brush == CustomMapBrush.Mountain)
        {
            int created = preview.NewMountains.Length;

            text = created > 0
                ? created == 1 ? Tr("{0} brush, size {1}: 1 new mountain", label, BrushRadius) : Tr("{0} brush, size {1}: {2} new mountains", label, BrushRadius, created)
                : preview.FirstBlocked is { } blocker
                    ? Tr("{0} brush, size {1}: blocked by {2} at {3}, {4}", label, BrushRadius, BlockerName(blocker), blocker.Position.X, blocker.Position.Y)
                    : Tr("{0} brush, size {1}: no room for a mountain", label, BrushRadius);
        }
        else
        {
            // Tiles of a removed mountain are amber, but inside the disk the brush paints them too.
            HashSet<WorldPosition> blocked = [.. preview.Tiles.Where(item => item.Status == CustomMapTileStatus.Blocked).Select(item => item.Position)];
            int painted = CustomMap.GetBrushArea(tile, BrushRadius).Count(position => map.IsValidPosition(position) && !blocked.Contains(position));
            int protectedTiles = blocked.Count;
            List<string> parts =
            [
                painted == 1 ? Tr("{0} brush, size {1}: paints 1 tile", label, BrushRadius) : Tr("{0} brush, size {1}: paints {2} tiles", label, BrushRadius, painted)
            ];

            if (preview.RemovedMountains.Length > 0)
            {
                parts.Add(RemovesMountains(preview.RemovedMountains.Length));
            }

            if (protectedTiles > 0)
            {
                parts.Add(protectedTiles == 1 ? Tr("leaves 1 tile alone") : Tr("leaves {0} tiles alone", protectedTiles));
            }

            text = string.Join(" · ", parts);
        }

        return new MapEditorPlacementPreview([.. preview.Tiles.Select(item => new MapEditorPreviewTile(item.Position, ToState(item.Status)))], null, null, null, text);
    }

    // A placement: green/amber/red tiles and "fits · notes · changes" or "blocked by X at x, y". When it is blocked
    // nothing changes, so the tiles that would have changed stay green and those outside the footprint go.
    private MapEditorPlacementPreview FromEngine(string title, CustomMapPlacementPreview preview, IReadOnlyList<string> notes)
    {
        IEnumerable<CustomMapPreviewTile> shown = preview.Fits
            ? preview.Tiles
            : preview.Tiles.Where(item => preview.Footprint is not { } footprint || footprint.Contains(item.Position));
        MapEditorPreviewTile[] tiles =
        [
            .. shown.Select(item => new MapEditorPreviewTile(
                item.Position,
                !preview.Fits && item.Status == CustomMapTileStatus.Changes ? MapEditorTileState.Fits : ToState(item.Status)))
        ];

        string text;

        if (preview.Fits)
        {
            List<string> changes = [];
            int forests = preview.Count(CustomMapTileReason.ClearsForest);
            int water = preview.Count(CustomMapTileReason.WaterToLand);
            int mountains = preview.RemovedMountains.Length;

            if (forests > 0)
            {
                changes.Add(forests == 1 ? Tr("clears 1 forest tile") : Tr("clears {0} forest tiles", forests));
            }

            if (mountains > 0)
            {
                changes.Add(RemovesMountains(mountains));
            }

            if (water > 0)
            {
                changes.Add(water == 1 ? Tr("turns 1 water tile into land") : Tr("turns {0} water tiles into land", water));
            }

            List<string> parts = [Tr("{0}: fits", title), .. notes];

            if (changes.Count > 0)
            {
                parts.Add(string.Join(", ", changes));
            }

            text = string.Join(" · ", parts);
        }
        else if (preview.FirstBlocked is { } blocker)
        {
            text = blocker.Reason == CustomMapTileReason.WrongOwner
                ? $"{title}: {MapEditorLabels.StackError(CustomMapStackError.VillageOwner, 0)}"
                : Tr("{0}: blocked by {1} at {2}, {3}", title, BlockerName(blocker), blocker.Position.X, blocker.Position.Y);
        }
        else
        {
            text = preview.StackError is { } stackError
                ? $"{title}: {MapEditorLabels.StackError(stackError, 0)}"
                : $"{title}: {MapEditorLabels.ObjectError(preview.Error!.Value)}";
        }

        return new MapEditorPlacementPreview(tiles, null, null, null, text)
        {
            Footprint = preview.Footprint is { Width: > 1 } or { Height: > 1 } ? preview.Footprint : null,
            Fits = preview.Fits
        };
    }

    private static MapEditorPlacementPreview Opens(string name, WorldFootprint? footprint, WorldPosition? hoverObject = null)
    {
        return new MapEditorPlacementPreview([], footprint, null, hoverObject, Tr("{0}: click opens it", name));
    }

    private static MapEditorPlacementPreview Removes(string name, WorldFootprint? footprint, WorldPosition? hoverObject)
    {
        return new MapEditorPlacementPreview([], footprint, null, hoverObject, Tr("{0}: click removes it", name));
    }

    private static string Sized(string name, int width, int height)
    {
        return Tr("{0} ({1}x{2})", name, width, height);
    }

    private static string RemovesMountains(int count)
    {
        return count == 1 ? Tr("removes 1 mountain") : Tr("removes {0} mountains", count);
    }

    private static MapEditorTileState ToState(CustomMapTileStatus status)
    {
        return status switch
        {
            CustomMapTileStatus.Ok => MapEditorTileState.Fits,
            CustomMapTileStatus.Changes => MapEditorTileState.Changes,
            _ => MapEditorTileState.Blocked
        };
    }

    private static string CapitalName(WorldRace race)
    {
        return Tr("Capital of {0}", MapEditorLabels.Race(race));
    }

    private static string VillageName(CustomMapVillage village)
    {
        return $"{Tr("Village")} {village.Name}";
    }

    private string LandmarkName(WorldLandmarkDefinitionId type)
    {
        return allLandmarks.FirstOrDefault(option => option.Definition.Id == type)?.Name ?? type.Value;
    }

    // Who earns from a resource on this tile: the player whose land it is (WorldPlan.GetLandOwner).
    private string LandOwnerName(WorldPosition tile)
    {
        return map.GetLandOwner(tile) is { } owner ? MapEditorLabels.Race(owner) : Tr("nobody");
    }

    private string BlockerName(CustomMapPreviewTile tile)
    {
        WorldPosition position = tile.Position;

        return tile.Reason switch
        {
            CustomMapTileReason.Structure when map.GetCapitalAt(position) is not null => Tr("a capital"),
            CustomMapTileReason.Structure when map.GetVillageAt(position) is not null => Tr("a village"),
            CustomMapTileReason.Structure when map.GetRuinAt(position) is not null => Tr("a ruin"),
            CustomMapTileReason.Structure => Tr("a site"),
            CustomMapTileReason.Landmark => Tr("a landmark"),
            CustomMapTileReason.Mountain => Tr("a mountain"),
            CustomMapTileReason.Water => Tr("water"),
            CustomMapTileReason.Army => Tr("an army"),
            CustomMapTileReason.Resource => Tr("a resource"),
            CustomMapTileReason.Bag => Tr("treasure"),
            CustomMapTileReason.Rod => Tr("a rod"),
            _ => Tr("the map edge")
        };
    }
}
