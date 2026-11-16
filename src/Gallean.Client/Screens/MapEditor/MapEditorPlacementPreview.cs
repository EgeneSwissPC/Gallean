using Avalonia.Media;
using Gallean.Engine;

namespace Gallean.Client;

// Placement preview colours: green/amber/red tiles, the gold of the game's hover cursor for an object a click opens,
// and a faint outline for a capital's current place. Kept with the editor instead of in Theme, so the game's theme
// files (and their brush resources) stay as upstream has them.
internal static class MapEditorPreviewColors
{
    public static Color Fits { get; } = Color.FromRgb(96, 206, 104);
    public static Color Changes { get; } = Color.FromRgb(240, 172, 52);
    public static Color Blocked { get; } = Color.FromRgb(226, 62, 50);
    public static Color Glow { get; } = Theme.World.HoverCursorOuterColor;
    public static Color GlowInner { get; } = Theme.World.HoverCursorInnerColor;
    public static Color Halo { get; } = Theme.World.CursorHaloColor;
    public static Color MovesFrom { get; } = Color.FromArgb(150, 236, 232, 222);
    public const double FillOpacity = 0.36;
    public const double OutlineOpacity = 0.85;
}

internal enum MapEditorTileState
{
    Fits,
    Changes,
    Blocked
}

internal readonly record struct MapEditorPreviewTile(WorldPosition Position, MapEditorTileState State);

// What the map editor shows under the cursor before a click: the tiles the click covers (green), changes in a way
// you can see (amber) or that block it (red), the object a click opens or removes (outlined, or the game's hover
// cursor for one-tile objects), a capital's current place when placing it again moves it, and the hover line text.
// Footprint is the area a placed object would stand on (outlined, so it stays visible among amber tiles).
internal sealed record MapEditorPlacementPreview(
    IReadOnlyList<MapEditorPreviewTile> Tiles,
    WorldFootprint? Glow,
    WorldFootprint? MovesFrom,
    WorldPosition? HoverObject,
    string Text)
{
    public static readonly MapEditorPlacementPreview None = new([], null, null, null, string.Empty);

    public WorldFootprint? Footprint { get; init; }

    public bool Fits { get; init; } = true;

    public bool IsEmpty => Tiles.Count == 0 && Glow is null && MovesFrom is null && HoverObject is null && Text.Length == 0;

    public int Count(MapEditorTileState state)
    {
        return Tiles.Count(tile => tile.State == state);
    }

    public MapEditorTileState? GetState(WorldPosition position)
    {
        foreach (MapEditorPreviewTile tile in Tiles)
        {
            if (tile.Position == position)
            {
                return tile.State;
            }
        }

        return null;
    }
}
