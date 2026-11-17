using System.Text.Json.Nodes;
using Gallean.Engine;

namespace MapFromPlan;

internal enum LabelKind
{
    Region,
    Capital,
    Village,
    Ruin,
    Site
}

internal sealed record Label(string Text, double X, double Y, LabelKind Kind);

// An area of a plan: a polygon, or a circle ("around" + "radius" in miles).
internal sealed class Area
{
    private readonly IReadOnlyList<Miles>? polygon;
    private readonly Miles center;
    private readonly double radius;

    private Area(IReadOnlyList<Miles>? polygon, Miles center, double radius)
    {
        this.polygon = polygon;
        this.center = center;
        this.radius = radius;
    }

    public static Area? From(JsonNode? node)
    {
        if (node?["polygon"] is JsonArray points)
        {
            return new Area([.. points.Select(Plan.Point)], default, 0);
        }

        return node?["around"] is { } around ? new Area(null, Plan.Point(around), Plan.Number(node["radius"], 50)) : null;
    }

    public bool Contains(Miles point)
    {
        if (polygon is not null)
        {
            return Shapes.Contains(polygon, point);
        }

        double de = point.E - center.E;
        double dn = point.N - center.N;

        return (de * de) + (dn * dn) <= radius * radius;
    }

    public Miles RandomPoint(Random random, Func<Miles, bool>? accept = null)
    {
        (Miles min, Miles max) = polygon is null
            ? (new Miles(center.E - radius, center.N - radius), new Miles(center.E + radius, center.N + radius))
            : (new Miles(polygon.Min(p => p.E), polygon.Min(p => p.N)), new Miles(polygon.Max(p => p.E), polygon.Max(p => p.N)));

        for (int attempt = 0; attempt < 1000; attempt++)
        {
            Miles point = new(min.E + (random.NextDouble() * (max.E - min.E)), min.N + (random.NextDouble() * (max.N - min.N)));

            if (Contains(point) && (accept is null || accept(point)))
            {
                return point;
            }
        }

        return new Miles((min.E + max.E) / 2, (min.N + max.N) / 2);
    }
}

internal static class Plan
{
    public static Miles Point(JsonNode? node)
    {
        return new Miles(node![0]!.GetValue<double>(), node[1]!.GetValue<double>());
    }

    public static double Number(JsonNode? node, double fallback)
    {
        return node is null ? fallback : node.GetValue<double>();
    }

    public static int Integer(JsonNode? node, int fallback)
    {
        return node is null ? fallback : (int)Math.Round(node.GetValue<double>());
    }

    public static string Text(JsonNode? node, string fallback = "")
    {
        return node is null ? fallback : node.GetValue<string>();
    }

    public static IEnumerable<JsonNode> List(JsonNode? node)
    {
        return node is JsonArray array ? array.Where(item => item is not null).Select(item => item!) : [];
    }

    public static IEnumerable<string> Strings(JsonNode? node)
    {
        return List(node).Select(item => item.GetValue<string>());
    }
}

// Builds a CustomMap from a plan, layer by layer, placing everything through the editor's own rules (Preview* checks,
// then Place*). Objects move to the nearest spot where they fit; anything that can't be placed is reported.
internal sealed class Generator(JsonObject plan, IGameDefinitionProvider definitions, GameNames names)
{
    private static readonly GridPosition[] SlotOrder =
        [GridPosition.FrontCenter, GridPosition.FrontTop, GridPosition.FrontBottom, GridPosition.BackCenter, GridPosition.BackTop, GridPosition.BackBottom];

    private readonly List<string> warnings = [];
    private readonly List<Label> labels = [];
    private readonly List<WorldFootprint> structures = [];
    private readonly Dictionary<string, WorldPosition> approaches = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<WorldPosition> stackPositions = [];
    private readonly List<WorldPosition> capitalPositions = [];
    private Geo geo = null!;
    private Noise noise = null!;
    private Random random = null!;
    private CustomMap map = null!;
    private Miles[] warped = [];
    private bool[] keepClear = [];
    private int size;

    public IReadOnlyList<string> Warnings => warnings;

    public IReadOnlyList<Label> Labels => labels;

    public IReadOnlyDictionary<string, WorldPosition> Approaches => approaches;

    public Geo Geo => geo;

    public CustomMap Generate(string id)
    {
        size = Plan.Integer(plan["size"], 144);
        geo = new Geo(size, Plan.Number(plan["milesPerTile"], 10), Plan.Point(plan["center"]));
        int seed = Plan.Integer(plan["seed"], 1);
        noise = new Noise(seed);
        random = new Random(seed);
        keepClear = new bool[size * size];

        WorldRace[] races = [.. Plan.Strings(plan["races"]).Select(Enum.Parse<WorldRace>)];
        map = CustomMap.Create(id, Plan.Text(plan["name"], "Generated map"), size, Enum.Parse<WorldMapTerrain>(Plan.Text(plan["baseTerrain"], "Neutral")), races);
        map.Description = Plan.Text(plan["description"]);

        CheckInside();
        ComputeWarp(Plan.Number(plan["warpMiles"], 25));
        PaintLand();
        PaintWater();
        PaintRivers();
        PaintMarshes();
        PlaceCapitals();
        PlaceVillages();
        PlaceRuins();
        PlaceSites();
        PaintMountains();
        PaintForests();
        LayRoads();
        PlaceLandmarks();
        PlaceResources();
        PlaceArmies();
        PlaceBags();
        AddRegionLabels();

        return map;
    }

    private int Index(int x, int y)
    {
        return (y * size) + x;
    }

    private void Warn(string message)
    {
        warnings.Add(message);
    }

    // Every named point of the plan should be on the map; points outside the diamond are reported.
    private void CheckInside()
    {
        foreach (string section in new[] { "capitals", "villages", "ruins", "sites", "armies" })
        {
            foreach (JsonNode item in Plan.List(plan[section]))
            {
                Miles at = Plan.Point(item["at"]);

                if (!geo.IsInside(at, 3))
                {
                    Warn($"{section}: '{Plan.Text(item["name"], Plan.Text(item["template"]))}' at ({at.E}, {at.N}) is outside the map.");
                }
            }
        }
    }

    // Plan coordinates of each tile, pushed around by noise so region borders, coasts and lakes look natural.
    private void ComputeWarp(double warpMiles)
    {
        warped = new Miles[size * size];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Miles point = geo.ToMiles(x, y);
                double de = (noise.Fractal(point.E / 70.0, point.N / 70.0) - 0.5) * 2 * warpMiles;
                double dn = (noise.Fractal((point.E / 70.0) + 31.7, (point.N / 70.0) - 12.3) - 0.5) * 2 * warpMiles;
                warped[Index(x, y)] = new Miles(point.E + de, point.N + dn);
            }
        }
    }

    private void PaintLand()
    {
        List<(WorldMapTerrain Terrain, Area Area)> regions =
        [
            .. Plan.List(plan["land"]).Select(region => (Enum.Parse<WorldMapTerrain>(Plan.Text(region["terrain"])), Area.From(region)!))
        ];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Miles point = warped[Index(x, y)];
                WorldMapTerrain? terrain = null;

                foreach ((WorldMapTerrain regionTerrain, Area area) in regions)
                {
                    if (area.Contains(point))
                    {
                        terrain = regionTerrain;
                    }
                }

                if (terrain is { } land && map.GetCell(new WorldPosition(x, y)).Terrain != land)
                {
                    map.Paint(new WorldPosition(x, y), 0, CustomMapBrush.Land, land, random);
                }
            }
        }
    }

    private void PaintWater()
    {
        Area[] waters = [.. Plan.List(plan["water"]).Select(water => Area.From(water)!)];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                Miles point = warped[Index(x, y)];

                if (waters.Any(area => area.Contains(point)))
                {
                    map.Paint(new WorldPosition(x, y), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
                }
            }
        }
    }

    // Rivers are painted in tile space so they stay at least two tiles wide (armies can't slip across diagonally);
    // fords stay dry land.
    private void PaintRivers()
    {
        foreach (JsonNode river in Plan.List(plan["rivers"]))
        {
            (double X, double Y)[] path = [.. Plan.List(river["path"]).Select(point => geo.ToTile(Plan.Point(point)))];
            (double X, double Y)[] fords = [.. Plan.List(river["fords"]).Select(point => geo.ToTile(Plan.Point(point)))];
            double half = Math.Max(2.2, Plan.Number(river["width"], 2.5)) / 2;
            double fordRadius = Plan.Number(river["fordRadius"], 1.6);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double wobble = 0.95 + (0.3 * noise.Fractal(x / 6.0, y / 6.0));

                    if (Shapes.Distance(path, x, y) > half * wobble ||
                        fords.Any(ford => Math.Sqrt(((ford.X - x) * (ford.X - x)) + ((ford.Y - y) * (ford.Y - y))) <= fordRadius))
                    {
                        continue;
                    }

                    map.Paint(new WorldPosition(x, y), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
                }
            }

            if (river["label"] is not null && river["label"]!.GetValue<bool>())
            {
                WorldPosition middle = geo.ToPosition(Plan.Point(Plan.List(river["path"]).ElementAt(Plan.List(river["path"]).Count() / 2)));
                labels.Add(new Label(Plan.Text(river["name"]), middle.X, middle.Y, LabelKind.Region));
            }
        }
    }

    // Marshes: pools scattered over an area.
    private void PaintMarshes()
    {
        foreach (JsonNode marsh in Plan.List(plan["marshes"]))
        {
            Area area = Area.From(marsh)!;
            double density = Plan.Number(marsh["density"], 0.35);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (area.Contains(warped[Index(x, y)]) && noise.Fractal(x / 2.5, y / 2.5) < density)
                    {
                        map.Paint(new WorldPosition(x, y), 0, CustomMapBrush.Water, WorldMapTerrain.Neutral, random);
                    }
                }
            }
        }
    }

    // The tile nearest to the target (ring by ring) that passes the check, or null within maxRadius tiles.
    private static WorldPosition? Search(WorldPosition target, int maxRadius, Func<WorldPosition, bool> fits)
    {
        for (int radius = 0; radius <= maxRadius; radius++)
        {
            WorldPosition? best = null;
            double bestDistance = double.MaxValue;

            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (Math.Max(Math.Abs(dx), Math.Abs(dy)) != radius)
                    {
                        continue;
                    }

                    WorldPosition position = new(target.X + dx, target.Y + dy);
                    double distance = (dx * dx) + (dy * dy);

                    if (distance < bestDistance && fits(position))
                    {
                        best = position;
                        bestDistance = distance;
                    }
                }
            }

            if (best is not null)
            {
                return best;
            }
        }

        return null;
    }

    private bool IsLand(WorldPosition position)
    {
        return map.IsValidPosition(position) && map.GetCell(position).Terrain != WorldMapTerrain.Water;
    }

    // A random spot worth trying: inside the map, on dry land.
    private bool IsUsable(Miles point)
    {
        return geo.IsInside(point, 2) && IsOpenLand(geo.ToPosition(point));
    }

    private bool IsOpenLand(WorldPosition position)
    {
        return IsLand(position) && map.GetCell(position).Feature != WorldMapFeature.Mountain;
    }

    private bool IsStructure(WorldPosition position)
    {
        return structures.Any(footprint => footprint.Contains(position));
    }

    // A structure needs dry land under it and one free tile around it (so armies can reach it).
    private bool FitsStructure(WorldPosition topLeft, int structureSize)
    {
        for (int y = topLeft.Y - 1; y <= topLeft.Y + structureSize; y++)
        {
            for (int x = topLeft.X - 1; x <= topLeft.X + structureSize; x++)
            {
                WorldPosition position = new(x, y);

                if (!IsOpenLand(position) || IsStructure(position))
                {
                    return false;
                }
            }
        }

        // The tile in front of the entrance (bottom corner) too.
        return IsOpenLand(new WorldPosition(topLeft.X + structureSize, topLeft.Y + structureSize));
    }

    private void AddStructure(string name, WorldFootprint footprint, LabelKind kind)
    {
        structures.Add(footprint);
        WorldPosition approach = new(footprint.TopLeft.X + footprint.Width, footprint.TopLeft.Y + footprint.Height);
        approaches[name] = approach;

        for (int y = footprint.TopLeft.Y - 1; y <= footprint.TopLeft.Y + footprint.Height + 1; y++)
        {
            for (int x = footprint.TopLeft.X - 1; x <= footprint.TopLeft.X + footprint.Width + 1; x++)
            {
                if (x >= 0 && y >= 0 && x < size && y < size)
                {
                    keepClear[Index(x, y)] = true;
                }
            }
        }

        labels.Add(new Label(name, footprint.TopLeft.X + (footprint.Width / 2.0), footprint.TopLeft.Y + (footprint.Height / 2.0), kind));
    }

    private WorldPosition? FindStructureSite(JsonNode item, int structureSize, Func<WorldPosition, bool> fits)
    {
        WorldPosition center = geo.ToPosition(Plan.Point(item["at"]));
        WorldPosition target = new(center.X - (structureSize / 2), center.Y - (structureSize / 2));
        WorldPosition? found = Search(target, Plan.Integer(item["search"], 10), topLeft => FitsStructure(topLeft, structureSize) && fits(topLeft));

        if (found is null)
        {
            Warn($"Could not place '{Plan.Text(item["name"])}' near ({target.X}, {target.Y}).");
        }

        return found;
    }

    private void PlaceCapitals()
    {
        foreach (JsonNode capital in Plan.List(plan["capitals"]))
        {
            WorldRace race = Enum.Parse<WorldRace>(Plan.Text(capital["race"]));
            WorldPosition? topLeft = FindStructureSite(capital, CustomMap.CapitalSize, position => map.PreviewCapital(race, position).Fits);

            if (topLeft is { } position && map.PlaceCapital(race, position) is null)
            {
                AddStructure(Plan.Text(capital["name"]), new WorldFootprint(position, CustomMap.CapitalSize, CustomMap.CapitalSize), LabelKind.Capital);
                capitalPositions.Add(position);
            }
        }
    }

    private void PlaceVillages()
    {
        foreach (JsonNode village in Plan.List(plan["villages"]))
        {
            string name = Plan.Text(village["name"]);
            WorldRace owner = Enum.Parse<WorldRace>(Plan.Text(village["owner"], "Neutral"));
            int level = Math.Clamp(Plan.Integer(village["level"], 1), 1, 5);
            WorldPosition? topLeft = FindStructureSite(village, CustomMapVillage.Size, position => map.PreviewVillage(position, level, owner).Fits);

            if (topLeft is null)
            {
                continue;
            }

            (CustomMapVillage? placed, CustomMapPlacementError? error) = map.PlaceVillage(topLeft.Value, level, owner, name);

            if (placed is null)
            {
                Warn($"Village '{name}': {error}.");
                continue;
            }

            AddStructure(name, placed.Footprint, LabelKind.Village);

            if (village["garrison"] is not null)
            {
                CustomMapUnit?[] garrison = BuildGarrison(Template(Plan.Text(village["garrison"])), Plan.Integer(village["tier"], 1), level);
                CustomMapPlacementError? garrisonError = map.UpdateVillage(placed.Id, level, owner, name, garrison);

                if (garrisonError is not null)
                {
                    Warn($"Village '{name}' garrison: {garrisonError}.");
                }
            }
        }
    }

    private void PlaceRuins()
    {
        foreach (JsonNode ruin in Plan.List(plan["ruins"]))
        {
            string name = Plan.Text(ruin["name"]);
            WorldPosition? topLeft = FindStructureSite(ruin, CustomMapRuin.Size, position => map.PreviewRuin(position).Fits);

            if (topLeft is null)
            {
                continue;
            }

            int variant = random.Next(11);
            (CustomMapRuin? placed, CustomMapPlacementError? error) = map.PlaceRuin(topLeft.Value, variant, name);

            if (placed is null)
            {
                Warn($"Ruin '{name}': {error}.");
                continue;
            }

            AddStructure(name, placed.Footprint, LabelKind.Ruin);
            CustomMapUnit?[] guards = ruin["guards"] is null
                ? new CustomMapUnit?[GridPosition.Count]
                : BuildGarrison(Template(Plan.Text(ruin["guards"])), Plan.Integer(ruin["tier"], 1), 6);
            ItemDefinitionId? item = ruin["item"] is null ? null : names.Item(Plan.Text(ruin["item"]));
            CustomMapPlacementError? updateError = map.UpdateRuin(placed.Id, variant, name, Plan.Integer(ruin["gold"], 200), item, guards);

            if (updateError is not null)
            {
                Warn($"Ruin '{name}' guards: {updateError}.");
            }
        }
    }

    private void PlaceSites()
    {
        foreach (JsonNode site in Plan.List(plan["sites"]))
        {
            string name = Plan.Text(site["name"]);
            string greeting = Plan.Text(site["greeting"]);
            CustomMapSiteType type = Enum.Parse<CustomMapSiteType>(Plan.Text(site["type"]));
            WorldPosition? topLeft = FindStructureSite(site, CustomMapSite.Size, position => map.PreviewSite(position).Fits);

            if (topLeft is null)
            {
                continue;
            }

            int variant = random.Next(type switch
            {
                CustomMapSiteType.Merchant => 8,
                CustomMapSiteType.Mercenaries => 5,
                _ => 4
            });
            (CustomMapSite? placed, CustomMapPlacementError? error) = map.PlaceSite(topLeft.Value, type, variant, name, greeting);

            if (placed is null)
            {
                Warn($"Site '{name}': {error}.");
                continue;
            }

            AddStructure(name, placed.Footprint, LabelKind.Site);
            CustomMapMerchantItem[] items = [.. Plan.List(site["items"]).Select(entry => new CustomMapMerchantItem(names.Item(Plan.Text(entry[0])), Plan.Integer(entry[1], 1)))];
            WorldSpellDefinitionId[] spells = [.. Plan.Strings(site["spells"]).Select(names.Spell)];
            CustomMapMercenary[] mercenaries =
            [
                .. Plan.List(site["mercenaries"]).Select(entry =>
                {
                    UnitDefinition unit = names.Unit(Plan.Text(entry[0]), leader: false);

                    return new CustomMapMercenary(unit.Id, Math.Max(unit.Level, Plan.Integer(entry[1], 1)));
                })
            ];
            CustomMapPlacementError? updateError = map.UpdateSite(placed.Id, variant, name, greeting, items, spells, mercenaries);

            if (updateError is not null)
            {
                Warn($"Site '{name}' stock: {updateError}.");
            }
        }
    }

    // Mountain ranges (walls along a path, with gaps for passes) and hills (scattered small peaks), filled with
    // 5x5, 3x3 and 1x1 mountains like the editor's brush. Tiles around structures stay free.
    private void PaintMountains()
    {
        bool[] range = new bool[size * size];
        bool[] hills = new bool[size * size];

        foreach (JsonNode ridge in Plan.List(plan["ranges"]))
        {
            (double X, double Y)[] path = [.. Plan.List(ridge["path"]).Select(point => geo.ToTile(Plan.Point(point)))];
            double half = geo.ToGridUnits(Plan.Number(ridge["width"], 30)) / 2;
            (double X, double Y, double Radius)[] gaps =
            [
                .. Plan.List(ridge["gaps"]).Select(gap =>
                {
                    (double x, double y) = geo.ToTile(Plan.Point(gap["at"]));

                    return (x, y, geo.ToGridUnits(Plan.Number(gap["radius"], 10)));
                })
            ];

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    double wobble = 0.75 + (0.5 * noise.Fractal((x / 7.0) + 50, (y / 7.0) + 50));
                    (double nx, double ny, double distance) = Shapes.Nearest(path, x, y);

                    // A gap cuts the whole width of the range: tiles whose nearest ridge point is in the gap stay open.
                    if (distance <= half * wobble &&
                        !gaps.Any(gap => Math.Sqrt(((gap.X - nx) * (gap.X - nx)) + ((gap.Y - ny) * (gap.Y - ny))) <= gap.Radius))
                    {
                        range[Index(x, y)] = true;
                    }
                }
            }

            if (ridge["label"] is null || ridge["label"]!.GetValue<bool>())
            {
                WorldPosition middle = geo.ToPosition(Plan.Point(Plan.List(ridge["path"]).ElementAt(Plan.List(ridge["path"]).Count() / 2)));
                labels.Add(new Label(Plan.Text(ridge["name"]), middle.X, middle.Y, LabelKind.Region));
            }
        }

        foreach (JsonNode hill in Plan.List(plan["hills"]))
        {
            Area area = Area.From(hill)!;
            double density = Plan.Number(hill["density"], 0.25);

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    if (area.Contains(warped[Index(x, y)]) && noise.Tile(x, y) < density)
                    {
                        hills[Index(x, y)] = true;
                    }
                }
            }
        }

        for (int i = 0; i < range.Length; i++)
        {
            bool blocked = keepClear[i] || map.Cells[i].Terrain == WorldMapTerrain.Water;
            range[i] &= !blocked;
            hills[i] &= !blocked && !range[i];
        }

        FillMountains(range, [5, 3, 1]);
        FillMountains(hills, [3, 1]);
    }

    private void FillMountains(bool[] mask, int[] sizes)
    {
        bool[] covered = new bool[size * size];
        int[] order = [.. Enumerable.Range(0, size * size).OrderBy(_ => random.Next())];

        foreach (int blockSize in sizes)
        {
            foreach (int index in order)
            {
                int left = index % size;
                int top = index / size;

                if (left + blockSize > size || top + blockSize > size)
                {
                    continue;
                }

                bool fits = true;

                for (int y = top; y < top + blockSize && fits; y++)
                {
                    for (int x = left; x < left + blockSize && fits; x++)
                    {
                        fits = mask[Index(x, y)] && !covered[Index(x, y)];
                    }
                }

                // Big blocks only now and then on hills, so they stay broken up.
                if (!fits || (blockSize > 1 && sizes[0] == 3 && random.NextDouble() < 0.5))
                {
                    continue;
                }

                WorldPosition center = new(left + (blockSize / 2), top + (blockSize / 2));

                if (map.Paint(center, blockSize / 2, CustomMapBrush.Mountain, WorldMapTerrain.Neutral, random))
                {
                    for (int y = top; y < top + blockSize; y++)
                    {
                        for (int x = left; x < left + blockSize; x++)
                        {
                            covered[Index(x, y)] = true;
                        }
                    }
                }
            }
        }
    }

    private void PaintForests()
    {
        List<(Area Area, double Density)> forests = [.. Plan.List(plan["forests"]).Select(forest => (Area.From(forest)!, Plan.Number(forest["density"], 0.8)))];
        JsonNode? wild = plan["wildForest"];
        double wildThreshold = Plan.Number(wild?["threshold"], 1.1);
        double wildScale = Plan.Number(wild?["scaleMiles"], 60);
        HashSet<WorldMapTerrain> noWild = [.. Plan.Strings(wild?["exceptTerrain"]).Select(Enum.Parse<WorldMapTerrain>)];

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                int index = Index(x, y);
                WorldPosition position = new(x, y);
                WorldMapCell cell = map.Cells[index];

                if (keepClear[index] || cell.Terrain == WorldMapTerrain.Water || cell.Feature == WorldMapFeature.Mountain)
                {
                    continue;
                }

                Miles point = warped[index];
                double clump = noise.Fractal((x / 5.0) + 90, (y / 5.0) + 90);
                bool forest = forests.Any(entry => entry.Area.Contains(point) && noise.Tile(x, y) < entry.Density * (0.55 + (0.9 * clump)));

                if (!forest && !noWild.Contains(cell.Terrain))
                {
                    Miles plain = geo.ToMiles(x, y);
                    forest = noise.Fractal((plain.E / wildScale) + 300, (plain.N / wildScale) + 300) > wildThreshold && noise.Tile(x, y) < 0.85;
                }

                if (forest)
                {
                    map.Paint(position, 0, CustomMapBrush.Forest, WorldMapTerrain.Neutral, random);
                }
            }
        }

        foreach (JsonNode forest in Plan.List(plan["forests"]).Where(forest => forest["label"] is null || forest["label"]!.GetValue<bool>()))
        {
            if (forest["name"] is not null && forest["polygon"] is JsonArray points)
            {
                Miles center = new(points.Average(point => Plan.Point(point).E), points.Average(point => Plan.Point(point).N));
                WorldPosition position = geo.ToPosition(center);
                labels.Add(new Label(Plan.Text(forest["name"]), position.X, position.Y, LabelKind.Region));
            }
        }
    }

    private bool IsRoadPassable(WorldPosition position)
    {
        return IsOpenLand(position) && !IsStructure(position) && map.GetLandmarkAt(position) is null;
    }

    private WorldPosition? ResolveWaypoint(JsonNode point)
    {
        if (point is JsonValue value && value.GetValue<string>() is { } name)
        {
            string key = name.TrimStart('@');

            if (approaches.TryGetValue(key, out WorldPosition approach))
            {
                return approach;
            }

            Warn($"Road waypoint '{name}' is not a placed structure.");
            return null;
        }

        return Search(geo.ToPosition(Plan.Point(point)), 6, IsRoadPassable);
    }

    // Roads follow the cheapest 4-connected path between waypoints (road sprites only join north/east/south/west),
    // around water, mountains, structures and landmarks; existing roads are cheap so roads merge.
    private void LayRoads()
    {
        foreach (JsonNode road in Plan.List(plan["roads"]))
        {
            WorldPosition?[] waypoints = [.. Plan.List(road["path"]).Select(ResolveWaypoint)];

            for (int i = 0; i + 1 < waypoints.Length; i++)
            {
                if (waypoints[i] is not { } from || waypoints[i + 1] is not { } to)
                {
                    continue;
                }

                List<WorldPosition>? path = FindRoadPath(from, to);

                if (path is null)
                {
                    Warn($"Road '{Plan.Text(road["name"])}': no way from ({from.X}, {from.Y}) to ({to.X}, {to.Y}).");
                    continue;
                }

                foreach (WorldPosition position in path)
                {
                    map.SetRoad(position, true);
                }
            }
        }
    }

    private List<WorldPosition>? FindRoadPath(WorldPosition from, WorldPosition to)
    {
        double[] cost = new double[size * size];
        int[] previous = new int[size * size];
        Array.Fill(cost, double.MaxValue);
        Array.Fill(previous, -1);
        PriorityQueue<int, double> queue = new();
        int start = Index(from.X, from.Y);
        int goal = Index(to.X, to.Y);
        cost[start] = 0;
        queue.Enqueue(start, 0);
        (int Dx, int Dy)[] steps = [(0, -1), (1, 0), (0, 1), (-1, 0)];

        while (queue.TryDequeue(out int current, out _))
        {
            if (current == goal)
            {
                List<WorldPosition> path = [];

                for (int at = goal; at != -1; at = previous[at])
                {
                    path.Add(new WorldPosition(at % size, at / size));
                }

                path.Reverse();
                return path;
            }

            int cx = current % size;
            int cy = current / size;

            foreach ((int dx, int dy) in steps)
            {
                WorldPosition next = new(cx + dx, cy + dy);

                if (!map.IsValidPosition(next) || (!IsRoadPassable(next) && next != to))
                {
                    continue;
                }

                int nextIndex = Index(next.X, next.Y);
                WorldMapCell cell = map.Cells[nextIndex];
                double step = cell.Feature switch
                {
                    WorldMapFeature.Road => 0.35,
                    WorldMapFeature.Forest => 2.2,
                    _ => 1.0
                } + (keepClear[nextIndex] ? 0.5 : 0);
                double total = cost[current] + step;

                if (total < cost[nextIndex])
                {
                    cost[nextIndex] = total;
                    previous[nextIndex] = current;
                    double heuristic = 0.35 * (Math.Abs(next.X - to.X) + Math.Abs(next.Y - to.Y));
                    queue.Enqueue(nextIndex, total + heuristic);
                }
            }
        }

        return null;
    }

    private IEnumerable<(Miles At, int Count, double RadiusTiles)> Spots(JsonNode item)
    {
        if (item["count"] is null)
        {
            yield return (Plan.Point(item["at"]), 1, Plan.Number(item["search"], 6));
            yield break;
        }

        Area area = Area.From(item) ?? Area.From(new JsonObject { ["around"] = item["at"]!.DeepClone(), ["radius"] = Plan.Number(item["radius"], 40) })!;
        int count = Plan.Integer(item["count"], 1);

        for (int i = 0; i < count; i++)
        {
            yield return (area.RandomPoint(random, IsUsable), 1, Plan.Number(item["search"], 3));
        }
    }

    private bool IsFreeFor(WorldPosition topLeft, int width, int height, bool water)
    {
        for (int y = topLeft.Y; y < topLeft.Y + height; y++)
        {
            for (int x = topLeft.X; x < topLeft.X + width; x++)
            {
                WorldPosition position = new(x, y);

                if (!map.IsValidPosition(position) || keepClear[Index(x, y)] || map.GetCell(position).Feature == WorldMapFeature.Road ||
                    (!water && map.GetCell(position).Terrain == WorldMapTerrain.Water))
                {
                    return false;
                }
            }
        }

        return true;
    }

    private void PlaceLandmarks()
    {
        foreach (JsonNode item in Plan.List(plan["landmarks"]))
        {
            string name = Plan.Text(item["name"]);
            IReadOnlyList<WorldLandmarkDefinition> options = names.Landmarks(name, item["size"] is null ? null : Plan.Integer(item["size"], 1));
            bool water = item["water"] is not null && item["water"]!.GetValue<bool>();
            int placed = 0;
            int wanted = 0;

            foreach ((Miles at, _, double radius) in Spots(item))
            {
                wanted++;
                WorldLandmarkDefinition definition = options[random.Next(options.Count)];
                WorldPosition center = geo.ToPosition(at);
                WorldPosition target = new(center.X - (definition.Width / 2), center.Y - (definition.Height / 2));
                WorldPosition? topLeft = Search(target, (int)radius, position =>
                    IsFreeFor(position, definition.Width, definition.Height, water) && map.PreviewLandmark(position, definition.Width, definition.Height).Fits);

                if (topLeft is { } position && map.PlaceLandmark(position, definition).Landmark is not null)
                {
                    placed++;
                }
            }

            if (placed < wanted)
            {
                Warn($"Landmark '{name}': placed {placed} of {wanted}.");
            }
        }
    }

    private static double Distance(WorldPosition a, WorldPosition b)
    {
        return Math.Sqrt(((a.X - b.X) * (a.X - b.X)) + ((a.Y - b.Y) * (a.Y - b.Y)));
    }

    private bool IsFreeTile(WorldPosition position)
    {
        return IsOpenLand(position) && !keepClear[Index(position.X, position.Y)] && !IsStructure(position);
    }

    private void PlaceResources()
    {
        List<WorldPosition> placed = [];

        void Place(WorldResourceType type, Miles at, int radius, double spacing, string what)
        {
            WorldPosition? position = Search(geo.ToPosition(at), radius, p =>
                IsFreeTile(p) && map.GetCell(p).Feature != WorldMapFeature.Road && placed.All(other => Distance(other, p) >= spacing) &&
                map.PreviewResource(p).Fits);

            if (position is { } tile && map.PlaceResource(tile, type) is null)
            {
                placed.Add(tile);
            }
            else
            {
                Warn($"Resource {type} ({what}) not placed.");
            }
        }

        foreach (JsonNode item in Plan.List(plan["resources"]))
        {
            Place(Enum.Parse<WorldResourceType>(Plan.Text(item["type"])), Plan.Point(item["at"]), 6, 2, "fixed");
        }

        foreach (JsonNode scatter in Plan.List(plan["resourceScatter"]))
        {
            string[] types = [.. Plan.Strings(scatter["types"])];
            Area area = Area.From(scatter)!;
            int count = Plan.Integer(scatter["count"], 1);

            for (int i = 0; i < count; i++)
            {
                Place(Enum.Parse<WorldResourceType>(types[random.Next(types.Length)]), area.RandomPoint(random, IsUsable), 4, 6, "scatter");
            }
        }
    }

    private JsonObject Template(string name)
    {
        return plan["armyTemplates"]?[name]?.AsObject() ?? throw new InvalidOperationException($"No army template '{name}'.");
    }

    private CustomMapUnit MakeUnit(UnitDefinition definition, int level)
    {
        return CustomMapUnit.From(definition, Math.Max(definition.Level, level), definitions);
    }

    private string Pick(JsonNode? names)
    {
        string[] options = [.. Plan.Strings(names)];

        return options[random.Next(options.Length)];
    }

    // Soldiers of a template for a tier (1-5): more units, higher levels and big units from tier 3.
    private void AddSoldiers(CustomMapUnit?[] slots, JsonObject template, int tier, int count, int capacity)
    {
        List<UnitDefinition> wanted = [];
        string[] large = [.. Plan.Strings(template["large"])];
        int largeCount = large.Length == 0 ? 0 : tier >= 5 ? 2 : tier >= 3 ? 1 : 0;

        for (int i = 0; i < largeCount; i++)
        {
            wanted.Add(names.Unit(large[random.Next(large.Length)], leader: false));
        }

        while (wanted.Count < count && template["units"] is JsonArray { Count: > 0 })
        {
            wanted.Add(names.Unit(Pick(template["units"]), leader: false));
        }

        foreach (UnitDefinition definition in wanted)
        {
            CustomMapUnit unit = MakeUnit(definition, 1 + ((tier - 1) * 2));

            if (CustomMapStack.GetOccupiedLeadership(slots) + (unit.IsLarge ? 2 : 1) > capacity)
            {
                continue;
            }

            foreach (GridPosition position in SlotOrder)
            {
                if (CustomMapStack.TryPlaceUnit(slots, position, unit) is { } result)
                {
                    Array.Copy(result.ToArray(), slots, slots.Length);
                    break;
                }
            }
        }
    }

    private CustomMapUnit?[] BuildArmy(JsonObject template, int tier)
    {
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];
        CustomMapUnit leader = MakeUnit(names.Unit(Pick(template["leaders"]), leader: true), 1 + ((tier - 1) * 3));
        slots[GridPosition.FrontCenter.ToIndex()] = leader;
        AddSoldiers(slots, template, tier, tier, CustomMapStack.GetLeadership(slots));

        return slots;
    }

    private CustomMapUnit?[] BuildGarrison(JsonObject template, int tier, int capacity)
    {
        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];
        AddSoldiers(slots, template, tier, Math.Min(capacity, tier + 1), capacity);

        return slots;
    }

    private bool PlaceArmy(JsonObject template, int tier, Miles at, int radius, double spacing, string what)
    {
        CustomMapUnit?[] slots = BuildArmy(template, tier);

        if (CustomMapStack.ValidateSlots(slots) is { } invalid)
        {
            Warn($"Army {what}: {invalid}.");
            return false;
        }

        WorldPosition? position = Search(geo.ToPosition(at), radius, p =>
            IsOpenLand(p) && !IsStructure(p) && (spacing == 0 || !keepClear[Index(p.X, p.Y)]) &&
            stackPositions.All(other => Distance(other, p) >= Math.Max(spacing, 1.5)) &&
            capitalPositions.All(capital => Distance(capital, p) >= (spacing == 0 ? 0 : 10)) &&
            map.PreviewStack(p, WorldRace.Neutral).Fits);

        if (position is { } tile && map.PlaceStack(tile, WorldRace.Neutral, slots).Error is null)
        {
            stackPositions.Add(tile);
            return true;
        }

        Warn($"Army {what} not placed.");
        return false;
    }

    private void PlaceArmies()
    {
        foreach (JsonNode army in Plan.List(plan["armies"]))
        {
            string templateName = Plan.Text(army["template"]);
            PlaceArmy(Template(templateName), Plan.Integer(army["tier"], 1), Plan.Point(army["at"]), Plan.Integer(army["search"], 4), 0, $"'{Plan.Text(army["name"], templateName)}'");
        }

        foreach (JsonNode scatter in Plan.List(plan["armyScatter"]))
        {
            Area area = Area.From(scatter)!;
            string[] templates = [.. Plan.Strings(scatter["templates"])];
            int[] tiers = [.. Plan.List(scatter["tier"]).Select(tier => Plan.Integer(tier, 1))];
            int count = Plan.Integer(scatter["count"], 1);

            for (int i = 0; i < count; i++)
            {
                int tier = random.Next(tiers[0], tiers[^1] + 1);
                PlaceArmy(Template(templates[random.Next(templates.Length)]), tier, area.RandomPoint(random, IsUsable), 4, 5, $"scatter '{Plan.Text(scatter["name"])}'");
            }
        }
    }

    private void PlaceBags()
    {
        foreach (JsonNode scatter in Plan.List(plan["bagScatter"]))
        {
            Area area = Area.From(scatter)!;
            ItemDefinitionId[] items = [.. Plan.Strings(scatter["items"]).Select(names.Item)];
            int count = Plan.Integer(scatter["count"], 1);
            int placed = 0;

            for (int i = 0; i < count; i++)
            {
                WorldPosition? position = Search(geo.ToPosition(area.RandomPoint(random, IsUsable)), 3, p => IsFreeTile(p) && map.PreviewBag(p).Fits);

                if (position is not { } tile)
                {
                    continue;
                }

                int variant = random.Next(8);
                (CustomMapBag? bag, _) = map.PlaceBag(tile, variant);

                if (bag is null)
                {
                    continue;
                }

                ItemDefinitionId[] contents = [.. Enumerable.Range(0, 1 + random.Next(2)).Select(_ => items[random.Next(items.Length)])];

                if (map.UpdateBag(bag.Id, variant, contents) is null)
                {
                    placed++;
                }
            }

            if (placed < count)
            {
                Warn($"Bags '{Plan.Text(scatter["name"])}': placed {placed} of {count}.");
            }
        }
    }

    private void AddRegionLabels()
    {
        foreach (JsonNode label in Plan.List(plan["labels"]))
        {
            WorldPosition position = geo.ToPosition(Plan.Point(label["at"]));
            labels.Add(new Label(Plan.Text(label["text"]), position.X, position.Y, LabelKind.Region));
        }
    }
}
