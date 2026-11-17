using System.Collections.Immutable;
using Avalonia.Media;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed partial class WorldRenderComposer
{
    private static class MinimapBuilder
    {
        public static MinimapState Build(
            WorldMap map,
            IReadOnlyList<WorldPosition> hiddenPositions,
            ImmutableSortedDictionary<WorldFortificationId, WorldFortification> fortifications,
            ImmutableSortedDictionary<WorldStackId, WorldStack> stacks,
            PlayerRaces playerRaces,
            WorldVisibility visibility,
            WorldPlayerId viewPlayerId)
        {
            return new MinimapState(map.Width, map.Height, hiddenPositions, [.. EnumerateMarkers(fortifications, stacks, playerRaces, visibility, viewPlayerId)]);
        }

        // Row by row, left to right: DrawMinimapHiddenTiles merges consecutive tiles of a row.
        public static IReadOnlyList<WorldPosition> BuildHiddenPositions(WorldVisibility visibility, bool fogEnabled)
        {
            List<WorldPosition> hiddenPositions = [];

            if (fogEnabled)
            {
                for (int y = 0; y < visibility.Height; y++)
                {
                    for (int x = 0; x < visibility.Width; x++)
                    {
                        WorldPosition position = new(x, y);

                        if (!visibility.IsRevealed(position))
                        {
                            hiddenPositions.Add(position);
                        }
                    }
                }
            }

            return hiddenPositions;
        }

        private static IEnumerable<MinimapState.Marker> EnumerateMarkers(
            ImmutableSortedDictionary<WorldFortificationId, WorldFortification> fortifications,
            ImmutableSortedDictionary<WorldStackId, WorldStack> stacks,
            PlayerRaces playerRaces,
            WorldVisibility visibility,
            WorldPlayerId viewPlayerId)
        {
            foreach (WorldFortification fortification in fortifications.Values)
            {
                if (visibility.IsRevealed(fortification.Entrance))
                {
                    switch (fortification)
                    {
                        case WorldCapital capital:
                            yield return new MinimapState.Marker(
                                capital.Entrance, GetOwnerColor(playerRaces[capital.OwnerId]), MinimapState.MarkerType.Capital);
                            break;

                        case WorldCity city:
                            yield return new MinimapState.Marker(
                                city.Entrance, GetOwnerColor(playerRaces[city.OwnerId]), MinimapState.MarkerType.City);
                            break;
                    }
                }
            }

            foreach (WorldStack stack in stacks.Values)
            {
                if (visibility.IsStackVisible(stack, viewPlayerId))
                {
                    yield return new MinimapState.Marker(stack.Position, GetOwnerColor(playerRaces[stack.OwnerId]), MinimapState.MarkerType.Stack);
                }
            }
        }

        private static Color GetOwnerColor(WorldRace race)
        {
            return race switch
            {
                WorldRace.Human => Theme.World.Race.HumanColor,
                WorldRace.Undead => Theme.World.Race.UndeadColor,
                WorldRace.Heretic => Theme.World.Race.HereticColor,
                WorldRace.Dwarf => Theme.World.Race.DwarfColor,
                WorldRace.Neutral => Theme.World.Race.NeutralColor,
                WorldRace.Elf => Theme.World.Race.ElfColor
            };
        }
    }
}
