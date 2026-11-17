using System.Collections.Immutable;
using Gallean.Engine;

namespace Gallean.Client;

// The road tool. A click starts a road; moving the mouse shows the road the tool would lay from there to the tile
// under the cursor (the cheapest way around water, mountains, structures and landmarks, CustomMap.FindRoadPath),
// drawn with the game's road pieces so it joins the roads already there. The next click lays it and the road
// carries on from that tile; clicking the last tile again or Esc stops. Shift+click removes one road tile.
internal sealed partial class MapEditorScreenViewModel
{
    private WorldPosition? roadStart;
    private ImmutableArray<WorldPosition>? roadPath;
    private ImmutableArray<WorldPosition>? roadGhostTiles;

    // Where the road being drawn starts (the last tile clicked), or null.
    public WorldPosition? RoadStart => roadStart;

    // Stops drawing a road; false when none was being drawn.
    public bool CancelRoad()
    {
        if (roadStart is null)
        {
            return false;
        }

        roadStart = null;
        RefreshPreview();

        return true;
    }

    private void ClickRoad(WorldPosition tile, bool remove)
    {
        if (remove)
        {
            roadStart = null;
            CommitTerrain(current => current.GetCell(tile).Feature == WorldMapFeature.Road && current.SetRoad(tile, false));
            return;
        }

        CustomMapTileReason block = map.GetRoadBlock(tile);

        if (block != CustomMapTileReason.None)
        {
            Game.ShowError(Tr("A road cannot go here: {0} at {1}, {2}.", BlockerName(new CustomMapPreviewTile(tile, CustomMapTileStatus.Blocked, block)), tile.X, tile.Y));
            return;
        }

        if (roadStart is not { } start)
        {
            roadStart = tile;
            RefreshPreview();
            return;
        }

        if (start == tile)
        {
            CancelRoad();
            return;
        }

        if (map.FindRoadPath(start, tile) is not { } path)
        {
            Game.ShowError(Tr("No way for a road from {0}, {1} to {2}, {3}.", start.X, start.Y, tile.X, tile.Y));
            return;
        }

        roadStart = tile;
        CommitTerrain(current => current.LayRoad(path).Laid > 0);
    }

    // A terrain-only change (roads): one undo step, the shown map updated at once, no preview rebuild.
    private void CommitTerrain(Func<CustomMap, bool> change)
    {
        CustomMap before = map.Clone();

        if (change(map))
        {
            undoSteps.Push(before);
            TrimUndo();
            CanUndo = true;
            IsDirty = true;
            state = CustomMapStore.ApplyTerrain(state, map);
            UpdateStatus();
        }

        RefreshPreview();
    }

    // The road shown before it is laid: from the start to the hover tile, or just the start tile.
    private void UpdateRoadGhost()
    {
        roadPath = null;
        roadGhostTiles = null;

        if (SelectedTool?.Value != MapEditorTool.Road || roadStart is not { } start)
        {
            return;
        }

        if (hoverTile is { } tile && tile != start)
        {
            roadPath = map.FindRoadPath(start, tile);
        }

        roadGhostTiles = roadPath ?? [start];
    }

    private MapEditorPlacementPreview RoadPreview(WorldPosition tile)
    {
        CustomMapTileReason block = map.GetRoadBlock(tile);
        MapEditorPreviewTile[] blockedTile = [new MapEditorPreviewTile(tile, MapEditorTileState.Blocked)];

        if (block != CustomMapTileReason.None)
        {
            return new MapEditorPlacementPreview(
                blockedTile, null, null, null,
                Tr("Road: blocked by {0} at {1}, {2}", BlockerName(new CustomMapPreviewTile(tile, CustomMapTileStatus.Blocked, block)), tile.X, tile.Y)) { Fits = false };
        }

        bool onRoad = map.GetCell(tile).Feature == WorldMapFeature.Road;

        if (roadStart is not { } start)
        {
            string text = onRoad ? Tr("Road: click starts a road here · Shift+click removes this road tile") : Tr("Road: click starts a road here");

            return new MapEditorPlacementPreview([new MapEditorPreviewTile(tile, MapEditorTileState.Fits)], null, null, null, text);
        }

        if (tile == start)
        {
            return new MapEditorPlacementPreview([], null, null, null, Tr("Road from {0}, {1}: move the mouse to where it goes · click here again or press Esc to stop", start.X, start.Y));
        }

        if (roadPath is not { } path)
        {
            return new MapEditorPlacementPreview(blockedTile, null, null, null, Tr("Road from {0}, {1}: no way to here", start.X, start.Y)) { Fits = false };
        }

        CustomMapPlacementPreview preview = map.PreviewRoad(path);
        int laid = preview.Tiles.Length;
        int forests = preview.Count(CustomMapTileReason.ClearsForest);
        List<string> parts =
        [
            Tr("Road from {0}, {1}: {2} tiles long", start.X, start.Y, path.Length),
            laid == 1 ? Tr("1 new road tile") : Tr("{0} new road tiles", laid)
        ];

        if (forests > 0)
        {
            parts.Add(forests == 1 ? Tr("clears 1 forest tile") : Tr("clears {0} forest tiles", forests));
        }

        parts.Add(Tr("click lays it"));

        return new MapEditorPlacementPreview(
            [.. preview.Tiles.Select(item => new MapEditorPreviewTile(item.Position, ToState(item.Status)))],
            null, null, null,
            string.Join(" · ", parts)) { OutlineOnly = true };
    }
}
