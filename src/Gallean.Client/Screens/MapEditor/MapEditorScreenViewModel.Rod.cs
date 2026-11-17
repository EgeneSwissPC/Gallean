using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Engine;

namespace Gallean.Client;

// The rod tool. A click places a rod of the chosen race; like the game on the turn a rod is planted, it turns the
// nearest tiles into the race's land (CustomMap.PlaceRod), and a mine those tiles touch changes owner. Clicking a
// rod, or Shift+click, removes it; its land stays, as in the game.
internal sealed partial class MapEditorScreenViewModel
{
    // Only playable races on the map own rods: neutral land doesn't spread.
    public IReadOnlyList<MapEditorOption<WorldRace>> RodOwners => CapitalRaces;

    public bool IsRodTool => SelectedTool.Value == MapEditorTool.Rod;

    [ObservableProperty]
    public partial MapEditorOption<WorldRace>? SelectedRodOwner { get; set; }

    partial void OnSelectedRodOwnerChanged(MapEditorOption<WorldRace>? value) => RefreshPreview();

    private WorldRace RodOwner => (SelectedRodOwner ?? RodOwners[0]).Value;

    private void ClickRod(WorldPosition tile, bool remove)
    {
        if (map.GetRodAt(tile) is not null)
        {
            CommitTerrain(current => current.RemoveRod(tile));
            return;
        }

        if (remove)
        {
            return;
        }

        CustomMapPlacementPreview preview = map.PreviewRod(tile, RodOwner);

        if (!preview.Fits)
        {
            Game.ShowError(RodBlockedText(preview));
            return;
        }

        CommitTerrain(current => current.PlaceRod(tile, RodOwner).Error is null);
    }

    private MapEditorPlacementPreview RodPreview(WorldPosition tile)
    {
        if (map.GetRodAt(tile) is { } rod)
        {
            return new MapEditorPlacementPreview([], null, null, tile, Tr("Rod of {0}: click removes it (its land stays)", MapEditorLabels.Race(rod.Owner)));
        }

        WorldRace owner = RodOwner;
        CustomMapPlacementPreview preview = map.PreviewRod(tile, owner);
        string title = Tr("Rod ({0})", MapEditorLabels.Race(owner));

        if (!preview.Fits)
        {
            // Unlike other objects, a blocked rod also shows the rod that is too close, outside its own tile.
            return new MapEditorPlacementPreview([.. preview.Tiles.Select(item => new MapEditorPreviewTile(item.Position, MapEditorTileState.Blocked))], null, null, null, RodBlockedText(preview))
            {
                Fits = false
            };
        }

        CustomMapPreviewTile[] claimed = [.. preview.Tiles.Where(item => item.Reason == CustomMapTileReason.ClaimsLand)];
        List<string> parts =
        [
            Tr("{0}: fits", title),
            claimed.Length == 1 ? Tr("turns 1 tile into {0} land", MapEditorLabels.Race(owner)) : Tr("turns {0} tiles into {1} land", claimed.Length, MapEditorLabels.Race(owner))
        ];

        foreach (CustomMapPreviewTile item in claimed)
        {
            if (map.GetResourceAt(item.Position) is { } resource)
            {
                parts.Add(Tr("takes {0} from {1}", MapEditorLabels.Resource(resource.Type), LandOwnerName(item.Position)));
            }
        }

        return new MapEditorPlacementPreview([.. preview.Tiles.Select(item => new MapEditorPreviewTile(item.Position, ToState(item.Status)))], null, null, null, string.Join(" · ", parts))
        {
            Fits = true
        };
    }

    // "Rod (Empire): another rod at 12, 8 is too close" / "blocked by water at ..." / "an army is in the way at ...".
    private string RodBlockedText(CustomMapPlacementPreview preview)
    {
        string title = Tr("Rod ({0})", MapEditorLabels.Race(RodOwner));

        if (preview.FirstBlocked is { } blocker)
        {
            return blocker.Reason == CustomMapTileReason.Rod && map.GetRodTooClose(preview.Footprint!.Value.TopLeft) is { } other
                ? Tr("{0}: another rod at {1}, {2} is too close (rods need {3} free tiles between them)", title, other.Position.X, other.Position.Y, CustomMap.RodSpacing)
                : Tr("{0}: blocked by {1} at {2}, {3}", title, BlockerName(blocker), blocker.Position.X, blocker.Position.Y);
        }

        return $"{title}: {MapEditorLabels.ObjectError(preview.Error ?? CustomMapPlacementError.Blocked)}";
    }
}
