using Gallean.Common;

namespace Gallean.Engine.Tests;

public sealed class ScenarioMapDecoderTests
{
    private static IReadOnlyList<string> FormatMap(ScenarioMapDecoder.TileInfo[] map, int size)
    {
        List<string> rows = [];

        for (int y = 0; y < size; y++)
        {
            JoinStringBuilder tiles = new(" ");

            for (int x = 0; x < size; x++)
            {
                ScenarioMapDecoder.TileInfo tile = map[y * size + x];
                tiles.Add($"{tile.TerrainId}/{tile.GroundId}/{tile.ImageVariant}");
            }

            rows.Add(tiles.ToString());
        }

        return rows;
    }

    [Test]
    public void EmptyTest()
    {
        ScenarioMapDecoder.TileInfo[] map = ScenarioMapDecoder.DecodeMap(10, []);

        Assert.That(map, Has.Length.EqualTo(100));
        Assert.That(map, Has.All.EqualTo(new ScenarioMapDecoder.TileInfo(0, 0, 0)));
    }

    [Test]
    public void SimpleTest()
    {
        string data = "B\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!B";
        Db.ScenarioMapBlock block = new(new Db.ScenarioId("0001"), "000000000000", data);

        List<Db.ScenarioMapBlock> blocks = [block];

        ScenarioMapDecoder.TileInfo[] map = ScenarioMapDecoder.DecodeMap(8, blocks);

        Assert.That(map, Has.Length.EqualTo(64));
        Assert.That(FormatMap(map, 8), Is.EqualTo([
            "4/16/0 4/30/24 0/0/48 2/0/16 0/12/4 0/24/0 3/8/0 0/2/61",
            "0/0/0 0/0/0 4/30/24 0/0/48 2/0/16 0/12/4 0/24/0 3/8/0",
            "0/2/61 0/0/0 0/0/0 4/30/24 0/0/48 2/0/16 0/12/4 0/24/0",
            "3/8/0 0/2/61 0/0/0 0/0/0 4/30/24 0/0/48 2/0/16 0/12/4",
            "0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0",
            "0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0",
            "0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0",
            "0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0 0/0/0"
        ]));
    }

    [Test]
    public void BlockPositionTest()
    {
        string data = "B\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!\"$!!P1!1W!!!B";
        ScenarioMapDecoder.TileInfo first = new(4, 16, 0);

        // Original IDs: two hex digits per axis (yy, xx).
        ScenarioMapDecoder.TileInfo[] small = ScenarioMapDecoder.DecodeMap(16, [new Db.ScenarioMapBlock(new Db.ScenarioId("S216SC0000"), "S216MB0408", data)]);
        // Maps larger than 256 tiles: three hex digits per axis (yyy, xxx).
        ScenarioMapDecoder.TileInfo[] large = ScenarioMapDecoder.DecodeMap(512, [new Db.ScenarioMapBlock(new Db.ScenarioId("S900SC0000"), "S900MB1fc1f8", data)]);

        Assert.That(small[4 * 16 + 8], Is.EqualTo(first));
        Assert.That(large[508 * 512 + 504], Is.EqualTo(first));
        Assert.That(large[511 * 512 + 511], Is.Not.EqualTo(new ScenarioMapDecoder.TileInfo(0, 0, 0)));
    }
}
