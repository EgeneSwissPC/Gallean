using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Gallean.Engine;

namespace MapFromPlan;

// A north-up overview of the generated map as a PNG: terrain, forests, mountains, roads, structures, armies and
// labels. Each tile is drawn at its screen position ((x - y), (x + y)), like the game's own world view.
internal static class Overview
{

    private static readonly Dictionary<char, string[]> Font = BuildFont();

    // The whole map at 3 pixels per screen unit, or a window of it (screen units: left = x - y, top = x + y) at a
    // larger scale for close-ups.
    public static void Write(CustomMap map, IReadOnlyList<Label> labels, string path, int scale = 3, (double Left, double Top, double Right, double Bottom)? window = null)
    {
        int size = map.Size;
        (double left, double top, double right, double bottom) = window ?? (-size, 0, size, 2 * size);
        View view = new(scale, left, top);
        int width = (int)((right - left) * scale);
        int height = (int)((bottom - top) * scale);
        byte[] pixels = new byte[width * height * 3];

        for (int py = 0; py < height; py++)
        {
            for (int px = 0; px < width; px++)
            {
                double sx = (px / (double)scale) + left;
                double sy = (py / (double)scale) + top;
                int x = (int)Math.Floor((sy + sx) / 2.0 + 0.5);
                int y = (int)Math.Floor((sy - sx) / 2.0 + 0.5);
                (byte r, byte g, byte b) = map.IsValidPosition(new WorldPosition(x, y)) ? TileColor(map, new WorldPosition(x, y)) : ((byte)18, (byte)18, (byte)24);
                Set(pixels, width, height, px, py, r, g, b);
            }
        }

        foreach (CustomMapResource resource in map.Resources)
        {
            Dot(pixels, width, height, view, resource.Position.X + 0.5, resource.Position.Y + 0.5, scale * 2 / 3, (250, 220, 40));
        }

        foreach (CustomMapBag bag in map.Bags)
        {
            Dot(pixels, width, height, view, bag.Position.X + 0.5, bag.Position.Y + 0.5, scale / 3, (255, 160, 60));
        }

        foreach (CustomMapStack stack in map.Stacks)
        {
            Dot(pixels, width, height, view, stack.Position.X + 0.5, stack.Position.Y + 0.5, scale * 2 / 3, (230, 30, 30));
        }

        foreach (Label label in labels.OrderBy(label => label.Kind == LabelKind.Region ? 0 : 1))
        {
            (int cx, int cy) = view.ToPixel(label.X, label.Y);
            (byte, byte, byte) color = label.Kind switch
            {
                LabelKind.Region => ((byte)235, (byte)225, (byte)190),
                LabelKind.Capital => ((byte)255, (byte)230, (byte)90),
                LabelKind.Ruin => ((byte)240, (byte)150, (byte)240),
                LabelKind.Site => ((byte)140, (byte)230, (byte)255),
                _ => ((byte)255, (byte)255, (byte)255)
            };
            int textScale = (label.Kind is LabelKind.Region or LabelKind.Capital ? 2 : 1) * Math.Max(1, scale / 6);
            string text = label.Kind == LabelKind.Region ? Simplify(label.Text).ToUpperInvariant() : Simplify(label.Text);
            int offset = label.Kind == LabelKind.Region ? 0 : 8 + (textScale * 4);
            Text(pixels, width, height, text, cx, cy + offset, textScale, color);
        }

        WritePng(path, width, height, pixels);
    }

    private sealed record View(int Scale, double Left, double Top)
    {
        public (int X, int Y) ToPixel(double x, double y)
        {
            return ((int)((x - y - Left) * Scale), (int)((x + y - Top) * Scale));
        }
    }

    private static (byte R, byte G, byte B) TileColor(CustomMap map, WorldPosition position)
    {
        WorldMapCell cell = map.GetCell(position);

        if (map.GetCapitalAt(position) is { } capital)
        {
            return RaceColor(capital.Race, bright: true);
        }

        if (map.GetVillageAt(position) is { } village)
        {
            return village.Owner == WorldRace.Neutral ? ((byte)245, (byte)245, (byte)245) : RaceColor(village.Owner, bright: true);
        }

        if (map.GetRuinAt(position) is not null)
        {
            return (200, 60, 200);
        }

        if (map.GetSiteAt(position) is not null)
        {
            return (60, 210, 240);
        }

        if (map.GetLandmarkAt(position) is not null)
        {
            return (40, 40, 40);
        }

        if (cell.Terrain == WorldMapTerrain.Water)
        {
            return (38, 74, 140);
        }

        (byte r, byte g, byte b) = cell.Terrain switch
        {
            WorldMapTerrain.Human => ((byte)150, (byte)172, (byte)92),
            WorldMapTerrain.Elf => ((byte)96, (byte)170, (byte)112),
            WorldMapTerrain.Dwarf => ((byte)168, (byte)148, (byte)112),
            WorldMapTerrain.Heretic => ((byte)118, (byte)58, (byte)46),
            WorldMapTerrain.Undead => ((byte)96, (byte)96, (byte)110),
            _ => ((byte)128, (byte)150, (byte)88)
        };

        return cell.Feature switch
        {
            WorldMapFeature.Forest => ((byte)(r * 0.55), (byte)(g * 0.7), (byte)(b * 0.55)),
            WorldMapFeature.Mountain => ((byte)((r / 5) + 70), (byte)((g / 5) + 62), (byte)((b / 5) + 56)),
            WorldMapFeature.Road => (214, 190, 130),
            _ => (r, g, b)
        };
    }

    private static (byte R, byte G, byte B) RaceColor(WorldRace race, bool bright)
    {
        return race switch
        {
            WorldRace.Human => (70, 120, 255),
            WorldRace.Elf => (60, 230, 120),
            WorldRace.Dwarf => (255, 150, 40),
            WorldRace.Heretic => (255, 40, 30),
            WorldRace.Undead => (170, 90, 255),
            _ => bright ? ((byte)245, (byte)245, (byte)245) : ((byte)200, (byte)200, (byte)200)
        };
    }

    private static void Set(byte[] pixels, int width, int height, int x, int y, byte r, byte g, byte b)
    {
        if (x < 0 || y < 0 || x >= width || y >= height)
        {
            return;
        }

        int i = ((y * width) + x) * 3;
        pixels[i] = r;
        pixels[i + 1] = g;
        pixels[i + 2] = b;
    }

    private static void Dot(byte[] pixels, int width, int height, View view, double x, double y, int radius, (byte R, byte G, byte B) color)
    {
        (int cx, int cy) = view.ToPixel(x, y);

        for (int dy = -radius; dy <= radius; dy++)
        {
            for (int dx = -radius; dx <= radius; dx++)
            {
                Set(pixels, width, height, cx + dx, cy + dy, color.R, color.G, color.B);
            }
        }
    }

    private static string Simplify(string text)
    {
        StringBuilder builder = new();

        foreach (char c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // Centred text with a dark outline.
    private static void Text(byte[] pixels, int width, int height, string text, int cx, int cy, int scale, (byte R, byte G, byte B) color)
    {
        int textWidth = text.Length * 6 * scale;
        int left = cx - (textWidth / 2);
        int top = cy - (7 * scale / 2);

        for (int pass = 0; pass < 2; pass++)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (!Font.TryGetValue(char.ToUpperInvariant(text[i]), out string[]? glyph))
                {
                    continue;
                }

                for (int row = 0; row < 7; row++)
                {
                    for (int column = 0; column < 5; column++)
                    {
                        if (glyph[row][column] != '1')
                        {
                            continue;
                        }

                        for (int sy = 0; sy < scale; sy++)
                        {
                            for (int sx = 0; sx < scale; sx++)
                            {
                                int x = left + (i * 6 * scale) + (column * scale) + sx;
                                int y = top + (row * scale) + sy;

                                if (pass == 0)
                                {
                                    for (int oy = -1; oy <= 1; oy++)
                                    {
                                        for (int ox = -1; ox <= 1; ox++)
                                        {
                                            Set(pixels, width, height, x + ox, y + oy, 10, 10, 10);
                                        }
                                    }
                                }
                                else
                                {
                                    Set(pixels, width, height, x, y, color.R, color.G, color.B);
                                }
                            }
                        }
                    }
                }
            }
        }
    }

    private static void WritePng(string path, int width, int height, byte[] pixels)
    {
        using MemoryStream raw = new();

        using (ZLibStream zlib = new(raw, CompressionLevel.Optimal, leaveOpen: true))
        {
            for (int y = 0; y < height; y++)
            {
                zlib.WriteByte(0);
                zlib.Write(pixels, y * width * 3, width * 3);
            }
        }

        using FileStream file = File.Create(path);
        file.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        byte[] header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2;
        WriteChunk(file, "IHDR", header);
        WriteChunk(file, "IDAT", raw.ToArray());
        WriteChunk(file, "IEND", []);
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(typeBytes);
        stream.Write(data);
        uint crc = Crc32(typeBytes, data);
        byte[] crcBytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] type, byte[] data)
    {
        uint crc = 0xFFFFFFFF;

        foreach (byte value in type.Concat(data))
        {
            crc ^= value;

            for (int bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
            }
        }

        return ~crc;
    }

    private static Dictionary<char, string[]> BuildFont()
    {
        string glyphs = """
            A 01110 10001 10001 11111 10001 10001 10001
            B 11110 10001 10001 11110 10001 10001 11110
            C 01110 10001 10000 10000 10000 10001 01110
            D 11110 10001 10001 10001 10001 10001 11110
            E 11111 10000 10000 11110 10000 10000 11111
            F 11111 10000 10000 11110 10000 10000 10000
            G 01110 10001 10000 10111 10001 10001 01111
            H 10001 10001 10001 11111 10001 10001 10001
            I 01110 00100 00100 00100 00100 00100 01110
            J 00111 00010 00010 00010 00010 10010 01100
            K 10001 10010 10100 11000 10100 10010 10001
            L 10000 10000 10000 10000 10000 10000 11111
            M 10001 11011 10101 10101 10001 10001 10001
            N 10001 10001 11001 10101 10011 10001 10001
            O 01110 10001 10001 10001 10001 10001 01110
            P 11110 10001 10001 11110 10000 10000 10000
            Q 01110 10001 10001 10001 10101 10010 01101
            R 11110 10001 10001 11110 10100 10010 10001
            S 01111 10000 10000 01110 00001 00001 11110
            T 11111 00100 00100 00100 00100 00100 00100
            U 10001 10001 10001 10001 10001 10001 01110
            V 10001 10001 10001 10001 10001 01010 00100
            W 10001 10001 10001 10101 10101 10101 01010
            X 10001 10001 01010 00100 01010 10001 10001
            Y 10001 10001 01010 00100 00100 00100 00100
            Z 11111 00001 00010 00100 01000 10000 11111
            0 01110 10001 10011 10101 11001 10001 01110
            1 00100 01100 00100 00100 00100 00100 01110
            2 01110 10001 00001 00010 00100 01000 11111
            3 11111 00010 00100 00010 00001 10001 01110
            4 00010 00110 01010 10010 11111 00010 00010
            5 11111 10000 11110 00001 00001 10001 01110
            6 00110 01000 10000 11110 10001 10001 01110
            7 11111 00001 00010 00100 01000 01000 01000
            8 01110 10001 10001 01110 10001 10001 01110
            9 01110 10001 10001 01111 00001 00010 01100
            ' 00100 00100 01000 00000 00000 00000 00000
            - 00000 00000 00000 11111 00000 00000 00000
            . 00000 00000 00000 00000 00000 01100 01100
            """;
        Dictionary<char, string[]> font = new() { [' '] = ["00000", "00000", "00000", "00000", "00000", "00000", "00000"] };

        foreach (string line in glyphs.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] parts = line.Split(' ');
            font[parts[0][0]] = parts[1..];
        }

        return font;
    }
}
