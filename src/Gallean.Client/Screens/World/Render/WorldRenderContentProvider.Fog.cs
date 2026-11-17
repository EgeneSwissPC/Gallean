using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;
using static Gallean.Client.WorldRenderContent;
using static Gallean.Client.WorldRenderState;

namespace Gallean.Client;

internal sealed partial class WorldRenderContentProvider
{
    // Builds the fog layer. The mask holds the fog's opacity (SamplesPerTile x SamplesPerTile samples per tile, zero
    // on explored tiles), so the layer is drawn as one quad over the map plus a ring for the map edge band, and the
    // shader's clamp mode carries the edge samples out into the band. Both meshes depend only on the map size.
    //
    // A tile's samples depend only on which tiles in its 3x3 neighbourhood are explored, so when the fog changes only
    // the tiles around the changed ones are rewritten; the rest of the mask is kept from the previous build. On a
    // large map this keeps a step that reveals a few tiles from redrawing the whole mask (100k tiles at 320x320).
    private sealed class Fog
    {
        private const int MapEdgeBand = 8;
        private const int SamplesPerTile = 4;
        private const int SearchRadius = 1;
        private const double ClearRadius = 0.7;
        private const double FadeDistance = 0.6;

        private int width;
        private int height;
        private bool[] explored = [];
        private bool[] nextExplored = [];
        private bool[] dirty = [];
        private byte[] pixels = [];
        private SKVertices? mapMesh;
        private SKVertices? mapEdgeMesh;

        public FogContent Build(IReadOnlyList<FogTile> fogTiles, WorldMap map)
        {
            if (fogTiles.Count == 0)
            {
                return new FogContent(null);
            }

            if (map.Width != width || map.Height != height)
            {
                Reset(map);
            }

            UpdateMask(fogTiles, map);

            return new FogContent(new FogLayer(mapMesh!, mapEdgeMesh, BuildShader()));
        }

        private void Reset(WorldMap map)
        {
            width = map.Width;
            height = map.Height;
            explored = new bool[width * height];
            Array.Fill(explored, true);
            nextExplored = new bool[width * height];
            dirty = new bool[width * height];
            pixels = new byte[width * SamplesPerTile * height * SamplesPerTile * 4];
            mapMesh = BuildRectanglesMesh([(0, 0, width, height)]);
            mapEdgeMesh = BuildRectanglesMesh([
                (-MapEdgeBand, -MapEdgeBand, width + MapEdgeBand, 0),
                (-MapEdgeBand, height, width + MapEdgeBand, height + MapEdgeBand),
                (-MapEdgeBand, 0, 0, height),
                (width, 0, width + MapEdgeBand, height)
            ]);
        }

        private void UpdateMask(IReadOnlyList<FogTile> fogTiles, WorldMap map)
        {
            Array.Fill(nextExplored, true);

            foreach (FogTile tile in fogTiles)
            {
                nextExplored[(tile.Position.Y * width) + tile.Position.X] = false;
            }

            List<int> dirtyTiles = [];

            for (int index = 0; index < explored.Length; index++)
            {
                if (explored[index] != nextExplored[index])
                {
                    MarkNeighborhood(index % width, index / width, dirtyTiles);
                }
            }

            foreach (int index in dirtyTiles)
            {
                WorldPosition position = new(index % width, index / width);

                if (nextExplored[index])
                {
                    ClearTile(position);
                }
                else
                {
                    WriteTile(map, nextExplored, position);
                }

                dirty[index] = false;
            }

            (explored, nextExplored) = (nextExplored, explored);
        }

        private void MarkNeighborhood(int x, int y, List<int> dirtyTiles)
        {
            for (int neighborY = Math.Max(0, y - SearchRadius); neighborY <= Math.Min(height - 1, y + SearchRadius); neighborY++)
            {
                for (int neighborX = Math.Max(0, x - SearchRadius); neighborX <= Math.Min(width - 1, x + SearchRadius); neighborX++)
                {
                    int index = (neighborY * width) + neighborX;

                    if (!dirty[index])
                    {
                        dirty[index] = true;
                        dirtyTiles.Add(index);
                    }
                }
            }
        }

        private static SKVertices BuildRectanglesMesh(IReadOnlyList<(int Left, int Top, int Right, int Bottom)> rectangles)
        {
            List<SKPoint> positions = [];
            List<ushort> indexes = [];

            foreach ((int left, int top, int right, int bottom) in rectangles)
            {
                ushort first = (ushort)positions.Count;

                positions.Add(Corner(left, top));
                positions.Add(Corner(right, top));
                positions.Add(Corner(right, bottom));
                positions.Add(Corner(left, bottom));
                indexes.AddRange([first, (ushort)(first + 1), (ushort)(first + 2), first, (ushort)(first + 2), (ushort)(first + 3)]);
            }

            return SKVertices.CreateCopy(SKVertexMode.Triangles, [.. positions], null, null, [.. indexes]);

            // Corner (x, y) is the top corner of tile (x, y) in tile space.
            static SKPoint Corner(int x, int y)
            {
                return WorldTransform.ToWorldPoint(x - 0.5, y - 0.5).ToSKPoint();
            }
        }

        private SKShader BuildShader()
        {
            SKImageInfo info = new(width * SamplesPerTile, height * SamplesPerTile, SKColorType.Bgra8888, SKAlphaType.Premul);
            SKImage mask = SKImage.FromPixelCopy(info, pixels);
            SkiaMemoryPressure.TrackImage(mask);

            float halfWidth = (float)WorldTransform.TileHalfWidth / SamplesPerTile;
            float halfHeight = (float)WorldTransform.TileHalfHeight / SamplesPerTile;

            SKMatrix matrix = new(
                halfWidth, -halfWidth, 0,
                halfHeight, halfHeight, -WorldTransform.TileHalfHeight,
                0, 0, 1);

            return mask.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, RenderSamplingOptions.Linear, matrix);
        }

        private void ClearTile(WorldPosition position)
        {
            int rowWidth = width * SamplesPerTile;

            for (int sampleY = 0; sampleY < SamplesPerTile; sampleY++)
            {
                int pixelY = (position.Y * SamplesPerTile) + sampleY;
                int offset = ((pixelY * rowWidth) + (position.X * SamplesPerTile)) * 4;

                Array.Clear(pixels, offset, SamplesPerTile * 4);
            }
        }

        private void WriteTile(WorldMap map, bool[] tiles, WorldPosition position)
        {
            Color color = Theme.World.Tile.HiddenColor;
            int rowWidth = width * SamplesPerTile;

            for (int sampleY = 0; sampleY < SamplesPerTile; sampleY++)
            {
                int pixelY = (position.Y * SamplesPerTile) + sampleY;
                double y = ((pixelY + 0.5) / SamplesPerTile) - 0.5;

                for (int sampleX = 0; sampleX < SamplesPerTile; sampleX++)
                {
                    int pixelX = (position.X * SamplesPerTile) + sampleX;
                    double x = ((pixelX + 0.5) / SamplesPerTile) - 0.5;

                    byte alpha = (byte)Math.Round(color.A * GetOpacity(map, tiles, position, x, y));
                    int offset = ((pixelY * rowWidth) + pixelX) * 4;

                    pixels[offset + 0] = (byte)(color.B * alpha / 255);
                    pixels[offset + 1] = (byte)(color.G * alpha / 255);
                    pixels[offset + 2] = (byte)(color.R * alpha / 255);
                    pixels[offset + 3] = alpha;
                }
            }
        }

        private static double GetOpacity(WorldMap map, bool[] explored, WorldPosition position, double x, double y)
        {
            double distance = double.MaxValue;

            for (int offsetY = -SearchRadius; offsetY <= SearchRadius; offsetY++)
            {
                for (int offsetX = -SearchRadius; offsetX <= SearchRadius; offsetX++)
                {
                    WorldPosition neighbor = new(position.X + offsetX, position.Y + offsetY);

                    if (map.IsValidPosition(neighbor) && explored[(neighbor.Y * map.Width) + neighbor.X])
                    {
                        double deltaX = x - neighbor.X;
                        double deltaY = y - neighbor.Y;

                        distance = Math.Min(distance, Math.Sqrt((deltaX * deltaX) + (deltaY * deltaY)));
                    }
                }
            }

            double fade = Math.Clamp((distance - ClearRadius) / FadeDistance, 0, 1);

            return fade * fade * (3 - (2 * fade));
        }
    }
}
