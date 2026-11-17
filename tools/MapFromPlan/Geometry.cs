using Gallean.Engine;

namespace MapFromPlan;

// A point in plan space: miles east (E) and north (N) of the plan's origin.
internal readonly record struct Miles(double E, double N);

// Plan miles <-> map tiles for a north-up map. On screen a tile (x, y) sits at ((x - y) * w/2, (x + y) * h/2), so
// east is +x -y and north is -x -y. One step east or north moves one tile, the map diamond holds every point with
// |east| + |north| <= Size / 2 tiles from the centre.
internal sealed class Geo(int size, double milesPerTile, Miles center)
{
    private readonly double c = (size - 1) / 2.0;

    public int Size => size;

    public double MilesPerTile => milesPerTile;

    // Grid distances: neighbouring tiles east/north are a diagonal apart in (x, y), so one grid unit is
    // milesPerTile / sqrt(2) miles.
    public double ToGridUnits(double miles)
    {
        return miles * Math.Sqrt(2) / milesPerTile;
    }

    public Miles ToMiles(double x, double y)
    {
        return new Miles(center.E + (milesPerTile * (x - y) / 2.0), center.N + (milesPerTile * ((2 * c) - x - y) / 2.0));
    }

    public (double X, double Y) ToTile(Miles point)
    {
        double east = (point.E - center.E) / milesPerTile;
        double north = (point.N - center.N) / milesPerTile;

        return (c + east - north, c - east - north);
    }

    public WorldPosition ToPosition(Miles point)
    {
        (double x, double y) = ToTile(point);

        return new WorldPosition((int)Math.Round(x), (int)Math.Round(y));
    }

    public bool IsInside(Miles point, int margin = 0)
    {
        (double x, double y) = ToTile(point);

        return x >= margin && y >= margin && x <= size - 1 - margin && y <= size - 1 - margin;
    }
}

// Seeded value noise and fractal sums of it, in [0, 1).
internal sealed class Noise(int seed)
{
    private double Hash(int x, int y)
    {
        unchecked
        {
            uint h = (uint)((x * 374761393) + (y * 668265263) + (seed * 1442695041));
            h = (h ^ (h >> 13)) * 1274126177;
            h ^= h >> 16;

            return (h & 0xFFFFFF) / (double)0x1000000;
        }
    }

    public double Value(double x, double y)
    {
        int x0 = (int)Math.Floor(x);
        int y0 = (int)Math.Floor(y);
        double fx = x - x0;
        double fy = y - y0;
        double sx = fx * fx * (3 - (2 * fx));
        double sy = fy * fy * (3 - (2 * fy));
        double top = Lerp(Hash(x0, y0), Hash(x0 + 1, y0), sx);
        double bottom = Lerp(Hash(x0, y0 + 1), Hash(x0 + 1, y0 + 1), sx);

        return Lerp(top, bottom, sy);
    }

    public double Fractal(double x, double y, int octaves = 3)
    {
        double sum = 0;
        double amplitude = 0.5;
        double total = 0;

        for (int octave = 0; octave < octaves; octave++)
        {
            sum += Value(x, y) * amplitude;
            total += amplitude;
            x = (x * 2.03) + 17.1;
            y = (y * 2.03) + 5.7;
            amplitude *= 0.5;
        }

        return sum / total;
    }

    public double Tile(int x, int y)
    {
        return Hash(x + 7919, y - 104729);
    }

    private static double Lerp(double a, double b, double t)
    {
        return a + ((b - a) * t);
    }
}

internal static class Shapes
{
    public static bool Contains(IReadOnlyList<Miles> polygon, Miles point)
    {
        bool inside = false;

        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            Miles a = polygon[i];
            Miles b = polygon[j];

            if ((a.N > point.N) != (b.N > point.N) && point.E < ((b.E - a.E) * (point.N - a.N) / (b.N - a.N)) + a.E)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    public static double Distance(IReadOnlyList<(double X, double Y)> path, double x, double y)
    {
        return Nearest(path, x, y).Distance;
    }

    // The point of the path nearest to (x, y) and how far away it is.
    public static (double X, double Y, double Distance) Nearest(IReadOnlyList<(double X, double Y)> path, double x, double y)
    {
        double best = double.MaxValue;
        (double X, double Y) nearest = path[0];

        for (int i = 0; i + 1 < path.Count; i++)
        {
            (double ax, double ay) = path[i];
            (double bx, double by) = path[i + 1];
            double dx = bx - ax;
            double dy = by - ay;
            double lengthSquared = (dx * dx) + (dy * dy);
            double t = lengthSquared == 0 ? 0 : Math.Clamp((((x - ax) * dx) + ((y - ay) * dy)) / lengthSquared, 0, 1);
            double px = ax + (t * dx);
            double py = ay + (t * dy);
            double distance = Math.Sqrt(((px - x) * (px - x)) + ((py - y) * (py - y)));

            if (distance < best)
            {
                best = distance;
                nearest = (px, py);
            }
        }

        return (nearest.X, nearest.Y, best);
    }
}
