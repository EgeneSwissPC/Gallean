using static Gallean.Client.WorldRenderState;

namespace Gallean.Client;

internal sealed partial class WorldRenderComposer
{
    private static class Fog
    {
        public readonly record struct Input(WorldVisibility Visibility);

        public static IReadOnlyList<FogTile> Build(Input input)
        {
            WorldVisibility visibility = input.Visibility;

            if (visibility.Fog is null)
            {
                return [];
            }

            // Read from the bitmap on demand instead of a list of every hidden tile (a million on a 1024 map).
            return new FogTileList(visibility.Fog);
        }
    }
}
