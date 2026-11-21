using System.Runtime.CompilerServices;
using Gallean.Common;

namespace Gallean.Engine;

/// <summary>
/// Gallean Upgrade rule for large maps (256x256 and bigger): a playable race always sees its own land
/// (tiles of its native terrain). Those tiles are revealed whenever the player's vision is updated,
/// which includes the start of every turn right after the land has expanded, and the Fog spell
/// can't conceal them again.
/// </summary>
internal static class OwnLandVisionCalculator
{
    public const int MinMapSize = 256;

    // The cells of each terrain, per map (the map object changes when land spreads): scanning a million cells on every
    // army step was the biggest part of a fog update on a 1024x1024 map.
    private static readonly ConditionalWeakTable<WorldMap, Dictionary<WorldMapTerrain, ImmutableBitmap>> TerrainCells = [];

    public static bool AppliesTo(WorldMap map)
    {
        return map.Width >= MinMapSize && map.Height >= MinMapSize;
    }

    public static bool IsOwnLand(WorldState state, WorldPlayer player, WorldPosition position)
    {
        if (!AppliesTo(state.Map) || !WorldHelpers.IsRacePlayable(player.Race))
        {
            return false;
        }

        return state.Map.GetCellAt(position).Terrain == WorldHelpers.GetNativeTerrain(player.Race);
    }

    public static void Reveal(ImmutableBitmap.Builder builder, WorldState state, WorldPlayerId playerId)
    {
        WorldMap map = state.Map;

        if (!AppliesTo(map))
        {
            return;
        }

        WorldPlayer player = state.Players[playerId];

        if (!WorldHelpers.IsRacePlayable(player.Race))
        {
            return;
        }

        builder.UnionWith(GetTerrainCells(map, WorldHelpers.GetNativeTerrain(player.Race)));
    }

    private static ImmutableBitmap GetTerrainCells(WorldMap map, WorldMapTerrain terrain)
    {
        Dictionary<WorldMapTerrain, ImmutableBitmap> byTerrain = TerrainCells.GetValue(map, static _ => []);

        lock (byTerrain)
        {
            if (!byTerrain.TryGetValue(terrain, out ImmutableBitmap? cells))
            {
                ImmutableBitmap.Builder builder = ImmutableBitmap.Create(map.Width, map.Height).ToBuilder();
                ReadOnlySpan<WorldMapCell> mapCells = map.Cells.AsSpan();

                for (int index = 0; index < mapCells.Length; index++)
                {
                    if (mapCells[index].Terrain == terrain)
                    {
                        builder.Set(index % map.Width, index / map.Width);
                    }
                }

                cells = builder.ToImmutable();
                byTerrain.Add(terrain, cells);
            }

            return cells;
        }
    }
}
