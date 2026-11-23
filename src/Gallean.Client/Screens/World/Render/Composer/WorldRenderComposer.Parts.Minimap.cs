using System.Collections.Immutable;

namespace Gallean.Client;

internal sealed partial class WorldRenderComposer
{
    private static class Minimap
    {
        // The hidden tiles only change with the fog, so they get their own part: the list keeps its identity while
        // stacks move, and the minimap image (which draws them) is only rebuilt when the fog changes.
        public readonly record struct HiddenInput(WorldVisibility Visibility, bool FogEnabled);

        public readonly record struct Input(
            WorldMap Map,
            IReadOnlyList<WorldPosition> HiddenPositions,
            ImmutableSortedDictionary<WorldFortificationId, WorldFortification> Fortifications,
            ImmutableSortedDictionary<WorldSiteId, WorldSite> Sites,
            ImmutableSortedDictionary<WorldResourceNodeId, WorldResourceNode> ResourceNodes,
            ImmutableSortedDictionary<WorldStackId, WorldStack> Stacks,
            PlayerRaces PlayerRaces,
            WorldVisibility Visibility,
            WorldPlayerId ViewPlayerId);

        public static IReadOnlyList<WorldPosition> BuildHidden(HiddenInput input)
        {
            return MinimapBuilder.BuildHiddenPositions(input.Visibility, input.FogEnabled);
        }

        public static MinimapState Build(Input input)
        {
            return MinimapBuilder.Build(
                input.Map,
                input.HiddenPositions,
                input.Fortifications,
                input.Sites,
                input.ResourceNodes,
                input.Stacks,
                input.PlayerRaces,
                input.Visibility,
                input.ViewPlayerId);
        }
    }
}
