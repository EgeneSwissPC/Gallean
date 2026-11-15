using System.Buffers.Binary;

namespace Gallean.Engine;

internal static class ScenarioMapDecoder
{
    public readonly record struct TileInfo(int TerrainId, int GroundId, int ImageVariant);

    private const int BlockWidth = 8;
    private const int BlockHeight = 4;
    private const int TilesPerBlock = BlockWidth * BlockHeight;
    private const int BlockByteCount = TilesPerBlock * sizeof(int);

    public static TileInfo[] DecodeMap(int size, IEnumerable<Db.ScenarioMapBlock> blocksList)
    {
        TileInfo[] map = new TileInfo[size * size];

        foreach ((Db.ScenarioId _, Db.Id blockId, string blockData) in blocksList)
        {
            if (TryGetBlockPosition(blockId.Value, out int xPos, out int yPos))
            {
                int[] tiles = DecodeBlock(blockData);

                for (int by = 0; by < BlockHeight; by++)
                {
                    for (int bx = 0; bx < BlockWidth; bx++)
                    {
                        int mapX = xPos + bx;
                        int mapY = yPos + by;

                        if (mapX < size && mapY < size)
                        {
                            map[mapY * size + mapX] = ParseTileInfo(tiles[by * BlockWidth + bx]);
                        }
                    }
                }
            }
        }

        return map;
    }

    // Original block IDs hold the block position as two hex digits per axis ("S216MB" + yy + xx), which limits maps to
    // 256 tiles. Larger maps use three hex digits per axis ("S900MB" + yyy + xxx, up to 4096 tiles).
    private static bool TryGetBlockPosition(string blockId, out int x, out int y)
    {
        int digits = blockId.Length switch
        {
            >= 12 => 3,
            >= 10 => 2,
            _ => 0
        };

        if (digits == 0)
        {
            x = 0;
            y = 0;

            return false;
        }

        y = Convert.ToInt32(blockId.Substring(6, digits), 16);
        x = Convert.ToInt32(blockId.Substring(6 + digits, digits), 16);

        return true;
    }

    private static int[] DecodeBlock(string data)
    {
        byte[] bytes = SixbitTextDecoder.Decode(data);

        if (bytes.Length < BlockByteCount)
        {
            throw new InvalidDataException($"Map block data holds {bytes.Length} bytes instead of {BlockByteCount}.");
        }

        int[] tiles = new int[TilesPerBlock];

        for (int i = 0; i < TilesPerBlock; i++)
        {
            tiles[i] = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(i * sizeof(int)));
        }

        return tiles;
    }

    private static TileInfo ParseTileInfo(int value)
    {
        int terrainId = (value >> 0) & 0b111;
        int groundId = (value >> 3) & 0b11111;
        int imageVariant = (value >> 26) & 0b111111;

        return new TileInfo(terrainId, groundId, imageVariant);
    }
}
