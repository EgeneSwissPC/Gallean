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

        WorldMapTerrain nativeTerrain = WorldHelpers.GetNativeTerrain(player.Race);
        ReadOnlySpan<WorldMapCell> cells = map.Cells.AsSpan();
        int width = map.Width;

        for (int index = 0; index < cells.Length; index++)
        {
            if (cells[index].Terrain == nativeTerrain)
            {
                builder.Set(index % width, index / width);
            }
        }
    }
}
