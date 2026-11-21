using System.Numerics;
using Avalonia.Media;
using Avalonia.Skia;
using SkiaSharp;
using static Gallean.Client.WorldRenderContent;
using static Gallean.Client.WorldRenderState;

namespace Gallean.Client;

internal sealed partial class WorldRenderContentProvider
{
    // Builds the fog layer. The mask holds the fog's opacity (SamplesPerTile x SamplesPerTile alpha samples per tile,
    // zero on explored tiles); the renderer tints it with the hidden colour. The mask is cut into square chunks of
    // ChunkTiles x ChunkTiles tiles, each its own image and quad, and each chunk image carries a one-sample margin of its
    // neighbours' samples (or the repeated edge sample at the map edge), so linear sampling across a chunk seam reads
    // the same samples as one big image would. Chunks on the map edge also draw the edge band outside the map, where
    // the shader's clamp mode carries the edge samples outwards.
    //
    // A tile's samples depend only on which tiles in its 3x3 neighbourhood are explored, so when the fog changes only
    // the tiles around the changed ones are rewritten, and only the chunks holding those samples get a new image. On a
    // 1024x1024 map one big mask was 64 MB, copied and uploaded again on every step that revealed a tile.
    private sealed class Fog(int chunkTiles)
    {
        private const int MapEdgeBand = 8;
        private const int SamplesPerTile = 4;
        private const int SearchRadius = 1;
        private const double ClearRadius = 0.7;
        private const double FadeDistance = 0.6;

        private int width;
        private int height;
        private int chunksX;
        private int chunksY;
        private ulong[] explored = [];
        private ulong[] nextExplored = [];
        private bool[] dirty = [];
        private byte[] mask = [];
        private byte[] chunkPixels = [];
        private FogChunk?[] chunks = [];
        private bool[] dirtyChunks = [];

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

            LoadExplored(fogTiles);
            UpdateMask(map);

            return new FogContent(new FogLayer(BuildChunks()));
        }

        private void Reset(WorldMap map)
        {
            width = map.Width;
            height = map.Height;
            int words = ((width * height) + 63) / 64;
            explored = new ulong[words];
            FillExplored(explored);
            nextExplored = new ulong[words];
            dirty = new bool[width * height];
            mask = new byte[width * SamplesPerTile * height * SamplesPerTile];
            chunksX = (width + chunkTiles - 1) / chunkTiles;
            chunksY = (height + chunkTiles - 1) / chunkTiles;
            chunks = new FogChunk?[chunksX * chunksY];
            dirtyChunks = new bool[chunksX * chunksY];
        }

        private void FillExplored(ulong[] words)
        {
            Array.Fill(words, ulong.MaxValue);

            int used = (width * height) & 63;

            if (used != 0)
            {
                words[^1] = (1UL << used) - 1;
            }
        }

        private void LoadExplored(IReadOnlyList<FogTile> fogTiles)
        {
            if (fogTiles is FogTileList list && list.Explored.Width == width && list.Explored.Height == height)
            {
                list.Explored.GetWords().CopyTo(nextExplored);
                return;
            }

            FillExplored(nextExplored);

            foreach (FogTile tile in fogTiles)
            {
                int index = (tile.Position.Y * width) + tile.Position.X;
                nextExplored[index >> 6] &= ~(1UL << index);
            }
        }

        private void UpdateMask(WorldMap map)
        {
            List<int> dirtyTiles = [];

            for (int wordIndex = 0; wordIndex < explored.Length; wordIndex++)
            {
                ulong changed = explored[wordIndex] ^ nextExplored[wordIndex];

                while (changed != 0)
                {
                    int index = (wordIndex * 64) + BitOperations.TrailingZeroCount(changed);
                    MarkNeighborhood(index % width, index / width, dirtyTiles);
                    changed &= changed - 1;
                }
            }

            foreach (int index in dirtyTiles)
            {
                WorldPosition position = new(index % width, index / width);

                if (IsExplored(nextExplored, index))
                {
                    ClearTile(position);
                }
                else
                {
                    WriteTile(map, nextExplored, position);
                }

                MarkChunks(position);
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

        // A tile's samples sit in its own chunk, and a tile on a chunk's border also in the neighbour's margin.
        private void MarkChunks(WorldPosition position)
        {
            int firstX = Math.Max(0, ((position.X + chunkTiles - 1) / chunkTiles) - 1);
            int lastX = Math.Min(chunksX - 1, (position.X + 1) / chunkTiles);
            int firstY = Math.Max(0, ((position.Y + chunkTiles - 1) / chunkTiles) - 1);
            int lastY = Math.Min(chunksY - 1, (position.Y + 1) / chunkTiles);

            for (int chunkY = firstY; chunkY <= lastY; chunkY++)
            {
                for (int chunkX = firstX; chunkX <= lastX; chunkX++)
                {
                    dirtyChunks[(chunkY * chunksX) + chunkX] = true;
                }
            }
        }

        private FogChunk[] BuildChunks()
        {
            FogChunk[] result = new FogChunk[chunks.Length];

            for (int chunkY = 0; chunkY < chunksY; chunkY++)
            {
                for (int chunkX = 0; chunkX < chunksX; chunkX++)
                {
                    int index = (chunkY * chunksX) + chunkX;
                    FogChunk? chunk = chunks[index];

                    if (chunk is null || dirtyChunks[index])
                    {
                        chunk = BuildChunk(chunkX, chunkY, chunk);
                        chunks[index] = chunk;
                        dirtyChunks[index] = false;
                    }

                    result[index] = chunk;
                }
            }

            return result;
        }

        private FogChunk BuildChunk(int chunkX, int chunkY, FogChunk? previous)
        {
            int left = chunkX * chunkTiles;
            int top = chunkY * chunkTiles;
            int right = Math.Min(width, left + chunkTiles);
            int bottom = Math.Min(height, top + chunkTiles);

            return new FogChunk(
                previous?.Mesh ?? BuildRectanglesMesh([(left, top, right, bottom)]),
                previous is not null ? previous.MapEdgeMesh : BuildEdgeMesh(left, top, right, bottom),
                BuildShader(left, top, right, bottom));
        }

        // The part of the edge band outside the map that this chunk's samples cover (null inside the map).
        private SKVertices? BuildEdgeMesh(int left, int top, int right, int bottom)
        {
            int outerLeft = left == 0 ? -MapEdgeBand : left;
            int outerTop = top == 0 ? -MapEdgeBand : top;
            int outerRight = right == width ? width + MapEdgeBand : right;
            int outerBottom = bottom == height ? height + MapEdgeBand : bottom;
            List<(int Left, int Top, int Right, int Bottom)> rectangles = [];

            if (outerTop < top)
            {
                rectangles.Add((outerLeft, outerTop, outerRight, top));
            }

            if (outerBottom > bottom)
            {
                rectangles.Add((outerLeft, bottom, outerRight, outerBottom));
            }

            if (outerLeft < left)
            {
                rectangles.Add((outerLeft, top, left, bottom));
            }

            if (outerRight > right)
            {
                rectangles.Add((right, top, outerRight, bottom));
            }

            return rectangles.Count > 0 ? BuildRectanglesMesh(rectangles) : null;
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

        private SKShader BuildShader(int left, int top, int right, int bottom)
        {
            // The chunk's samples plus a one-sample margin, clamped to the mask at the map edge.
            int originX = (left * SamplesPerTile) - 1;
            int originY = (top * SamplesPerTile) - 1;
            int imageWidth = ((right - left) * SamplesPerTile) + 2;
            int imageHeight = ((bottom - top) * SamplesPerTile) + 2;
            int maskWidth = width * SamplesPerTile;
            int maskHeight = height * SamplesPerTile;

            if (chunkPixels.Length < imageWidth * imageHeight)
            {
                chunkPixels = new byte[imageWidth * imageHeight];
            }

            for (int row = 0; row < imageHeight; row++)
            {
                int maskY = Math.Clamp(originY + row, 0, maskHeight - 1);
                int source = maskY * maskWidth;
                int target = row * imageWidth;
                int firstX = Math.Max(0, originX);
                int lastX = Math.Min(maskWidth - 1, originX + imageWidth - 1);

                Array.Copy(mask, source + firstX, chunkPixels, target + (firstX - originX), lastX - firstX + 1);

                if (originX < 0)
                {
                    chunkPixels[target] = mask[source];
                }

                if (originX + imageWidth - 1 > maskWidth - 1)
                {
                    chunkPixels[target + imageWidth - 1] = mask[source + maskWidth - 1];
                }
            }

            SKImageInfo info = new(imageWidth, imageHeight, SKColorType.Alpha8, SKAlphaType.Premul);
            SKImage image = SKImage.FromPixelCopy(info, chunkPixels.AsSpan(0, imageWidth * imageHeight), imageWidth);
            SkiaMemoryPressure.TrackImage(image);

            float halfWidth = (float)WorldTransform.TileHalfWidth / SamplesPerTile;
            float halfHeight = (float)WorldTransform.TileHalfHeight / SamplesPerTile;

            SKMatrix matrix = new(
                halfWidth, -halfWidth, 0,
                halfHeight, halfHeight, -WorldTransform.TileHalfHeight,
                0, 0, 1);

            matrix = matrix.PreConcat(SKMatrix.CreateTranslation(originX, originY));

            return image.ToShader(SKShaderTileMode.Clamp, SKShaderTileMode.Clamp, RenderSamplingOptions.Linear, matrix);
        }

        private void ClearTile(WorldPosition position)
        {
            int rowWidth = width * SamplesPerTile;

            for (int sampleY = 0; sampleY < SamplesPerTile; sampleY++)
            {
                int pixelY = (position.Y * SamplesPerTile) + sampleY;
                int offset = (pixelY * rowWidth) + (position.X * SamplesPerTile);

                Array.Clear(mask, offset, SamplesPerTile);
            }
        }

        private void WriteTile(WorldMap map, ulong[] tiles, WorldPosition position)
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

                    mask[(pixelY * rowWidth) + pixelX] = (byte)Math.Round(color.A * GetOpacity(map, tiles, position, x, y));
                }
            }
        }

        private static bool IsExplored(ulong[] tiles, int index)
        {
            return (tiles[index >> 6] & (1UL << index)) != 0;
        }

        private static double GetOpacity(WorldMap map, ulong[] explored, WorldPosition position, double x, double y)
        {
            double distance = double.MaxValue;

            for (int offsetY = -SearchRadius; offsetY <= SearchRadius; offsetY++)
            {
                for (int offsetX = -SearchRadius; offsetX <= SearchRadius; offsetX++)
                {
                    WorldPosition neighbor = new(position.X + offsetX, position.Y + offsetY);

                    if (map.IsValidPosition(neighbor) && IsExplored(explored, (neighbor.Y * map.Width) + neighbor.X))
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
