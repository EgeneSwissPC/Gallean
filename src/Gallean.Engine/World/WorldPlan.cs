using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Gallean.Common;

namespace Gallean.Engine;

public sealed class WorldPlan
{
    private readonly record struct ObjectiveKey(
        IGameScenarioProvider Scenario,
        WorldPlayerId PlayerId);

    private readonly record struct PathMapKey(
        WorldPosition Start,
        ImmutableBitmap? FogOfWar,
        WorldMovementCalculator.MovementProfile Profile);

    private static readonly ConditionalWeakTable<WorldState, WorldPlan> Cache = [];

    // Obstacles and impassable cells only change when the map or the objects on it change, not when an army steps, so
    // they are kept across states (a new state, and so a new plan, comes with every step). Rebuilding them scanned every
    // cell of the map per step: ~10 ms on a 1024x1024 map, on the UI thread when the game works out where an army can go.
    private static readonly ConditionalWeakTable<WorldMap, StaticCells> StaticCellsCache = [];

    private readonly Lazy<Dictionary<WorldPosition, WorldBag>> bags;
    private readonly Dictionary<WorldMovementCalculator.MovementProfile, ImmutableBitmap> blockedCells;
    private readonly Lazy<Dictionary<WorldPlayerId, WorldCapital>> capitals;
    private readonly Lazy<HashSet<WorldPlayerId>> defeatedPlayers;
    private readonly Lazy<Dictionary<WorldPosition, WorldFortificationId>> fortifications;
    private readonly Lazy<Dictionary<WorldPosition, WorldLandmark>> landmarks;
    private readonly Lazy<ImmutableBitmap> obstacleCells;
    private readonly Dictionary<ObjectiveKey, ImmutableArray<WorldObjective>> objectives;
    private readonly Dictionary<PathMapKey, WorldPathMap?> pathMaps;
    private readonly Lazy<Dictionary<WorldPosition, WorldResourceNode>> resourceNodes;
    private readonly Lazy<Dictionary<WorldPosition, WorldRod>> rods;
    private readonly Lazy<Dictionary<WorldPosition, WorldSite>> sites;
    private readonly Lazy<Dictionary<WorldPosition, WorldStack>> stacks;
    private readonly WorldState state;
    private readonly Lazy<ImmutableBitmap> structureCells;
    private readonly Lazy<ImmutableBitmap> terminalCells;
    private readonly Lazy<Dictionary<WorldPosition, ImmutableArray<WorldTomb>>> tombs;
    private readonly Lazy<ImmutableBitmap> zeroCostEntrances;

    private WorldPlan(WorldState state)
    {
        this.state = state;

        bags = new Lazy<Dictionary<WorldPosition, WorldBag>>(BuildBags);
        blockedCells = [];
        capitals = new Lazy<Dictionary<WorldPlayerId, WorldCapital>>(BuildCapitals);
        defeatedPlayers = new Lazy<HashSet<WorldPlayerId>>(BuildDefeatedPlayers);
        fortifications = new Lazy<Dictionary<WorldPosition, WorldFortificationId>>(GetFortificationLayout);
        landmarks = new Lazy<Dictionary<WorldPosition, WorldLandmark>>(BuildLandmarks);
        objectives = [];
        obstacleCells = new Lazy<ImmutableBitmap>(GetObstacleCells);
        pathMaps = [];
        resourceNodes = new Lazy<Dictionary<WorldPosition, WorldResourceNode>>(BuildResourceNodes);
        rods = new Lazy<Dictionary<WorldPosition, WorldRod>>(BuildRods);
        sites = new Lazy<Dictionary<WorldPosition, WorldSite>>(BuildSites);
        stacks = new Lazy<Dictionary<WorldPosition, WorldStack>>(BuildStacks);
        structureCells = new Lazy<ImmutableBitmap>(BuildStructureCells);
        terminalCells = new Lazy<ImmutableBitmap>(BuildTerminalCells);
        tombs = new Lazy<Dictionary<WorldPosition, ImmutableArray<WorldTomb>>>(BuildTombs);
        zeroCostEntrances = new Lazy<ImmutableBitmap>(GetZeroCostEntrances);
    }

    public static WorldPlan For(WorldState state)
    {
        return Cache.GetValue(state, static s => new WorldPlan(s));
    }

    public WorldStack? GetStackAt(WorldPosition position)
    {
        stacks.Value.TryGetValue(position, out WorldStack? stack);

        return stack;
    }

    public WorldFortification? GetFortificationAt(WorldPosition position)
    {
        WorldFortification? fortification = null;

        if (fortifications.Value.TryGetValue(position, out WorldFortificationId id))
        {
            state.Fortifications.TryGetValue(id, out fortification);
        }

        return fortification;
    }

    public WorldCapital? GetCapital(WorldPlayerId playerId)
    {
        capitals.Value.TryGetValue(playerId, out WorldCapital? capital);

        return capital;
    }

    public bool IsPlayerDefeated(WorldPlayerId playerId)
    {
        return defeatedPlayers.Value.Contains(playerId);
    }

    public ImmutableArray<WorldObjective> GetObjectives(IGameScenarioProvider scenario, WorldPlayerId playerId)
    {
        ObjectiveKey key = new(scenario, playerId);

        if (!objectives.TryGetValue(key, out ImmutableArray<WorldObjective> result))
        {
            result = ObjectiveCalculator.GetObjectives(scenario, state, playerId);

            objectives.Add(key, result);
        }

        return result;
    }

    public WorldResourceNode? GetResourceNodeAt(WorldPosition position)
    {
        resourceNodes.Value.TryGetValue(position, out WorldResourceNode? resourceNode);

        return resourceNode;
    }

    public WorldPlayerId? GetLandOwner(WorldPosition position)
    {
        WorldMapCell cell = state.Map.GetCellAt(position);

        foreach (WorldPlayer player in state.Players.Values)
        {
            if (WorldHelpers.IsRacePlayable(player.Race) && WorldHelpers.GetNativeTerrain(player.Race) == cell.Terrain)
            {
                return player.Id;
            }
        }

        return null;
    }

    public WorldRod? GetRodAt(WorldPosition position)
    {
        rods.Value.TryGetValue(position, out WorldRod? rod);

        return rod;
    }

    public WorldSite? GetSiteAt(WorldPosition position)
    {
        sites.Value.TryGetValue(position, out WorldSite? site);

        return site;
    }

    public WorldSite? GetSiteEntranceAt(WorldPosition position)
    {
        return state.Sites.Values.FirstOrDefault(site => site.IsEntranceAt(position));
    }

    public WorldLandmark? GetLandmarkAt(WorldPosition position)
    {
        landmarks.Value.TryGetValue(position, out WorldLandmark? landmark);

        return landmark;
    }

    public bool HasStructureAt(WorldPosition position)
    {
        return structureCells.Value.Get(position.X, position.Y);
    }

    public bool IsObstacleAt(WorldPosition position)
    {
        return obstacleCells.Value.Get(position.X, position.Y);
    }

    private StaticCells GetStaticCells()
    {
        lock (StaticCellsCache)
        {
            if (!StaticCellsCache.TryGetValue(state.Map, out StaticCells? cells) || !cells.Matches(state))
            {
                cells = new StaticCells(state);
                StaticCellsCache.AddOrUpdate(state.Map, cells);
            }

            return cells;
        }
    }

    // Keyed by the map and the object collections the obstacles come from; a state that shares all of them shares these.
    // Gallean Upgrade (engine speed): a state with new collections but the same positions and footprints (a garrison
    // or a node owner changed, which happens on almost every step) shares them too, where the original rebuilt the
    // map-wide bitmaps for it (~15 % of AI turn time on 1024 maps). The obstacles only come from those positions.
    private sealed class StaticCells(WorldState state)
    {
        private object resourceNodes = state.ResourceNodes;
        private object rods = state.Rods;
        private object fortifications = state.Fortifications;
        private object sites = state.Sites;
        private object landmarks = state.Landmarks;
        private readonly int[] layout = GetLayout(state);
        private readonly Dictionary<WorldMovementCalculator.MovementProfile, ImmutableBitmap> impassable = [];

        public ImmutableBitmap? Obstacles { get; set; }

        public ImmutableBitmap? ZeroCostEntrances { get; set; }

        public bool Matches(WorldState other)
        {
            if (ReferenceEquals(resourceNodes, other.ResourceNodes) &&
                ReferenceEquals(rods, other.Rods) &&
                ReferenceEquals(fortifications, other.Fortifications) &&
                ReferenceEquals(sites, other.Sites) &&
                ReferenceEquals(landmarks, other.Landmarks))
            {
                return true;
            }

            if (!GetLayout(other).AsSpan().SequenceEqual(layout))
            {
                return false;
            }

            resourceNodes = other.ResourceNodes;
            rods = other.Rods;
            fortifications = other.Fortifications;
            sites = other.Sites;
            landmarks = other.Landmarks;

            return true;
        }

        // Everything the cached bitmaps read: node and rod positions, fortification footprints and kinds (cities and
        // capitals have free entrances), site and landmark footprints.
        private static int[] GetLayout(WorldState state)
        {
            List<int> layout = [];

            foreach (WorldResourceNode node in state.ResourceNodes.Values)
            {
                layout.Add(node.Position.X);
                layout.Add(node.Position.Y);
            }

            layout.Add(-1);

            foreach (WorldRod rod in state.Rods.Values)
            {
                layout.Add(rod.Position.X);
                layout.Add(rod.Position.Y);
            }

            layout.Add(-1);

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                layout.Add(fortification is WorldCapital or WorldCity ? 1 : 0);
                AddFootprint(fortification.Footprint);
            }

            layout.Add(-1);

            foreach (WorldSite site in state.Sites.Values)
            {
                AddFootprint(site.Footprint);
            }

            layout.Add(-1);

            foreach (WorldLandmark landmark in state.Landmarks.Values)
            {
                AddFootprint(landmark.Footprint);
            }

            return [.. layout];

            void AddFootprint(WorldFootprint footprint)
            {
                layout.Add(footprint.TopLeft.X);
                layout.Add(footprint.TopLeft.Y);
                layout.Add(footprint.Width);
                layout.Add(footprint.Height);
            }
        }

        public ImmutableBitmap GetObstaclesAndImpassable(WorldMovementCalculator.MovementProfile profile, Func<ImmutableBitmap> build)
        {
            lock (impassable)
            {
                if (!impassable.TryGetValue(profile, out ImmutableBitmap? cells))
                {
                    cells = build();
                    impassable.Add(profile, cells);
                }

                return cells;
            }
        }
    }

    public WorldBag? GetBagAt(WorldPosition position)
    {
        bags.Value.TryGetValue(position, out WorldBag? bag);

        return bag;
    }

    public ImmutableArray<WorldTomb> GetTombsAt(WorldPosition position)
    {
        if (tombs.Value.TryGetValue(position, out ImmutableArray<WorldTomb> result))
        {
            return result;
        }
        else
        {
            return [];
        }
    }

    internal bool CanPlaceStackAt(WorldPosition position, WorldMovementCalculator.MovementProfile profile)
    {
        return state.Map.IsValidPosition(position) &&
               GetStackAt(position) is null &&
               GetBagAt(position) is null &&
               GetRodAt(position) is null &&
               !HasStructureAt(position) &&
               WorldMovementCalculator.IsPassable(state.Map, position, profile);
    }

    internal int GetStepCost(WorldPosition from, WorldPosition to, WorldMovementCalculator.MovementProfile profile)
    {
        int cost = WorldMovementCalculator.GetMovementCost(state.Map, to, profile);

        if (cost >= int.MaxValue)
        {
            WorldStack? targetStack = GetStackAt(to);

            if (targetStack is not null)
            {
                cost = WorldMovementCalculator.GetMovementCost(state.Map, from, profile);
            }
        }

        if (cost >= int.MaxValue)
        {
            return int.MaxValue;
        }

        ImmutableBitmap entrances = zeroCostEntrances.Value;

        if (entrances.Get(from.X, from.Y) || entrances.Get(to.X, to.Y))
        {
            return 0;
        }

        return cost;
    }

    private Dictionary<WorldPosition, WorldStack> BuildStacks()
    {
        Dictionary<WorldPosition, WorldStack> result = new(state.Stacks.Count);

        foreach (WorldStack stack in state.Stacks.Values)
        {
            result.Add(stack.Position, stack);
        }

        return result;
    }

    // Gallean Upgrade (engine speed): which fortification covers which tile only depends on the fortifications' IDs
    // and footprints, which never change during a game, while the collection itself changes on almost every step
    // (garrisons, unit modifiers). The tile index is kept across states with the same layout instead of being rebuilt
    // for every state (~7 % of AI turn time on 1024 maps); the fortification itself is still taken from each state.
    private static readonly ConditionalWeakTable<WorldMap, FortificationLayout> FortificationLayoutCache = [];

    private Dictionary<WorldPosition, WorldFortificationId> GetFortificationLayout()
    {
        lock (FortificationLayoutCache)
        {
            if (!FortificationLayoutCache.TryGetValue(state.Map, out FortificationLayout? layout) || !layout.Matches(state))
            {
                layout = new FortificationLayout(state);
                FortificationLayoutCache.AddOrUpdate(state.Map, layout);
            }

            return layout.Tiles;
        }
    }

    private sealed class FortificationLayout
    {
        private readonly (WorldFortificationId Id, WorldFootprint Footprint)[] fortifications;

        public FortificationLayout(WorldState state)
        {
            fortifications = [.. state.Fortifications.Values.Select(fortification => (fortification.Id, fortification.Footprint))];
            Tiles = [];

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                foreach (WorldPosition tile in fortification.Footprint.EnumerateTiles())
                {
                    Tiles[tile] = fortification.Id;
                }
            }

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                Tiles[fortification.Entrance] = fortification.Id;
            }
        }

        public Dictionary<WorldPosition, WorldFortificationId> Tiles { get; }

        public bool Matches(WorldState state)
        {
            if (state.Fortifications.Count != fortifications.Length)
            {
                return false;
            }

            int index = 0;

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                (WorldFortificationId id, WorldFootprint footprint) = fortifications[index++];

                if (fortification.Id != id || fortification.Footprint != footprint)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private Dictionary<WorldPosition, WorldResourceNode> BuildResourceNodes()
    {
        Dictionary<WorldPosition, WorldResourceNode> result = new(state.ResourceNodes.Count);

        foreach (WorldResourceNode resourceNode in state.ResourceNodes.Values)
        {
            result.Add(resourceNode.Position, resourceNode);
        }

        return result;
    }

    private Dictionary<WorldPosition, WorldRod> BuildRods()
    {
        Dictionary<WorldPosition, WorldRod> result = new(state.Rods.Count);

        foreach (WorldRod rod in state.Rods.Values)
        {
            result.Add(rod.Position, rod);
        }

        return result;
    }

    private Dictionary<WorldPosition, WorldSite> BuildSites()
    {
        Dictionary<WorldPosition, WorldSite> result = [];

        foreach (WorldSite site in state.Sites.Values)
        {
            foreach (WorldPosition tile in site.Footprint.EnumerateTiles())
            {
                result[tile] = site;
            }
        }

        foreach (WorldSite site in state.Sites.Values)
        {
            result[site.Entrance] = site;
        }

        return result;
    }

    private Dictionary<WorldPosition, WorldLandmark> BuildLandmarks()
    {
        Dictionary<WorldPosition, WorldLandmark> result = [];

        foreach (WorldLandmark landmark in state.Landmarks.Values)
        {
            foreach (WorldPosition tile in landmark.Footprint.EnumerateTiles())
            {
                result[tile] = landmark;
            }
        }

        return result;
    }

    private ImmutableBitmap BuildStructureCells()
    {
        ImmutableBitmap.Builder structures = ImmutableBitmap.Create(state.Map.Width, state.Map.Height).ToBuilder();

        foreach (WorldResourceNode resourceNode in state.ResourceNodes.Values)
        {
            structures.Set(resourceNode.Position.X, resourceNode.Position.Y);
        }

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            SetFootprint(structures, fortification.Footprint);
        }

        foreach (WorldSite site in state.Sites.Values)
        {
            SetFootprint(structures, site.Footprint);
        }

        foreach (WorldLandmark landmark in state.Landmarks.Values)
        {
            SetFootprint(structures, landmark.Footprint);
        }

        return structures.ToImmutable();
    }

    private static void SetFootprint(ImmutableBitmap.Builder cells, WorldFootprint footprint)
    {
        foreach (WorldPosition tile in footprint.EnumerateTiles())
        {
            cells.Set(tile.X, tile.Y);
        }
    }

    private ImmutableBitmap GetZeroCostEntrances()
    {
        StaticCells cells = GetStaticCells();

        lock (cells)
        {
            cells.ZeroCostEntrances ??= BuildZeroCostEntrances();

            return cells.ZeroCostEntrances;
        }
    }

    private ImmutableBitmap BuildZeroCostEntrances()
    {
        ImmutableBitmap.Builder entrances = ImmutableBitmap.Create(state.Map.Width, state.Map.Height).ToBuilder();

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            if (fortification is WorldCapital or WorldCity)
            {
                entrances.Set(fortification.Entrance.X, fortification.Entrance.Y);
            }
        }

        foreach (WorldSite site in state.Sites.Values)
        {
            entrances.Set(site.Entrance.X, site.Entrance.Y);
        }

        return entrances.ToImmutable();
    }

    private ImmutableBitmap BuildTerminalCells()
    {
        ImmutableBitmap.Builder terminals = ImmutableBitmap.Create(state.Map.Width, state.Map.Height).ToBuilder();

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            terminals.Set(fortification.Entrance.X, fortification.Entrance.Y);
        }

        foreach (WorldSite site in state.Sites.Values)
        {
            terminals.Set(site.Entrance.X, site.Entrance.Y);
        }

        foreach (WorldStack stack in state.Stacks.Values)
        {
            terminals.Set(stack.Position.X, stack.Position.Y);
        }

        foreach (WorldBag bag in state.Bags.Values)
        {
            terminals.Set(bag.Position.X, bag.Position.Y);
        }

        return terminals.ToImmutable();
    }

    private Dictionary<WorldPosition, WorldBag> BuildBags()
    {
        Dictionary<WorldPosition, WorldBag> result = new(state.Bags.Count);

        foreach (WorldBag bag in state.Bags.Values)
        {
            result.Add(bag.Position, bag);
        }

        return result;
    }

    private Dictionary<WorldPosition, ImmutableArray<WorldTomb>> BuildTombs()
    {
        Dictionary<WorldPosition, ImmutableArray<WorldTomb>.Builder> builders = [];

        foreach (WorldTomb tomb in state.Tombs.Values)
        {
            if (!builders.TryGetValue(tomb.Position, out ImmutableArray<WorldTomb>.Builder? builder))
            {
                builder = ImmutableArray.CreateBuilder<WorldTomb>();
                builders.Add(tomb.Position, builder);
            }

            builder.Add(tomb);
        }

        Dictionary<WorldPosition, ImmutableArray<WorldTomb>> result = new(builders.Count);

        foreach ((WorldPosition position, ImmutableArray<WorldTomb>.Builder builder) in builders)
        {
            result.Add(position, builder.ToImmutable());
        }

        return result;
    }

    private Dictionary<WorldPlayerId, WorldCapital> BuildCapitals()
    {
        Dictionary<WorldPlayerId, WorldCapital> result = [];

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            if (fortification is WorldCapital { CapturedById: null } capital)
            {
                result.TryAdd(capital.OwnerId, capital);
            }
        }

        return result;
    }

    private HashSet<WorldPlayerId> BuildDefeatedPlayers()
    {
        HashSet<WorldPlayerId> result = [];

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            if (fortification is WorldCapital { CapturedById: not null } capital)
            {
                result.Add(capital.OwnerId);
            }
        }

        result.ExceptWith(capitals.Value.Keys);

        return result;
    }

    internal WorldPath? GetPath(
        WorldPosition start,
        WorldPosition end,
        ImmutableBitmap? fogOfWar,
        WorldMovementCalculator.MovementProfile profile,
        WorldMovementLimit limit)
    {
        // A bounded map from an earlier GetPathMap call answers targets within its bound without a full search.
        if (!pathMaps.TryGetValue(new PathMapKey(start, fogOfWar, profile), out WorldPathMap? pathMap) ||
            (pathMap is not null && !pathMap.IsSettled(end)))
        {
            pathMap = GetPathMap(start, fogOfWar, profile);
        }

        if (pathMap is null)
        {
            return null;
        }

        return pathMap.TryGetPath(end, limit, out WorldPath? path) ? path : null;
    }

    // maxCost limits the search to cells that cost at most maxCost to reach (see PathMapCalculator.Calculate); the
    // returned map may still cover more when an unbounded or wider map is already cached.
    internal WorldPathMap? GetPathMap(
        WorldPosition start,
        ImmutableBitmap? fogOfWar,
        WorldMovementCalculator.MovementProfile profile,
        int maxCost = int.MaxValue)
    {
        PathMapKey key = new(start, fogOfWar, profile);

        if (!pathMaps.TryGetValue(key, out WorldPathMap? pathMap) || (pathMap is not null && pathMap.MaxCost < maxCost))
        {
            WorldPathMapReuse.Inputs inputs = GetReuseInputs(start, fogOfWar, profile);
            pathMap = WorldPathMapReuse.TryReuse(inputs, cached => cached.MaxCost >= maxCost);

            if (pathMap is null)
            {
                pathMap = PathMapCalculator.Calculate(
                    state.Map,
                    start,
                    fogOfWar,
                    inputs.Blocked,
                    inputs.Terminal,
                    inputs.StepCost,
                    maxCost);

                WorldPathMapReuse.Store(inputs, pathMap);
            }
            else if (WorldPathMapReuse.Verify)
            {
                WorldPathMapReuse.Check(pathMap, PathMapCalculator.Calculate(state.Map, start, fogOfWar, inputs.Blocked, inputs.Terminal, inputs.StepCost));
            }

            pathMaps[key] = pathMap;
        }

        return pathMap;
    }

    // Gallean Upgrade (AI speed): everything a search reads, for WorldPathMapReuse.
    private WorldPathMapReuse.Inputs GetReuseInputs(WorldPosition start, ImmutableBitmap? fogOfWar, WorldMovementCalculator.MovementProfile profile)
    {
        return new WorldPathMapReuse.Inputs(
            state.Map,
            start,
            fogOfWar,
            profile,
            GetBlockedCells(profile),
            terminalCells.Value,
            zeroCostEntrances.Value,
            (from, to) => GetStepCost(from, to, profile));
    }

    // Gallean Upgrade (AI speed): like GetPathMap, but the search stops once every target is settled, which gives the
    // same paths to them as a full search. A cached map that already settles all targets is reused; the new map only
    // replaces a cached one that covers less.
    internal WorldPathMap? GetPathMapToTargets(
        WorldPosition start,
        ImmutableBitmap? fogOfWar,
        WorldMovementCalculator.MovementProfile profile,
        IReadOnlyCollection<WorldPosition> targets)
    {
        PathMapKey key = new(start, fogOfWar, profile);

        if (pathMaps.TryGetValue(key, out WorldPathMap? cached) && (cached is null || targets.All(cached.IsSettled)))
        {
            return cached;
        }

        WorldPathMapReuse.Inputs inputs = GetReuseInputs(start, fogOfWar, profile);
        WorldPathMap? pathMap = WorldPathMapReuse.TryReuse(inputs, reused => targets.All(reused.IsSettled));

        if (pathMap is not null)
        {
            if (WorldPathMapReuse.Verify)
            {
                WorldPathMapReuse.Check(pathMap, PathMapCalculator.Calculate(state.Map, start, fogOfWar, inputs.Blocked, inputs.Terminal, inputs.StepCost));
            }
        }
        else
        {
            HashSet<int> stopTargets = [];
            foreach (WorldPosition target in targets)
            {
                stopTargets.Add(target.Y * state.Map.Width + target.X);
            }

            pathMap = PathMapCalculator.Calculate(
                state.Map,
                start,
                fogOfWar,
                inputs.Blocked,
                inputs.Terminal,
                inputs.StepCost,
                int.MaxValue,
                stopTargets);

            WorldPathMapReuse.Store(inputs, pathMap);
        }

        if (cached is null || pathMap is null || pathMap.MaxCost > cached.MaxCost)
        {
            pathMaps[key] = pathMap;
        }

        return pathMap;
    }

    private ImmutableBitmap GetBlockedCells(WorldMovementCalculator.MovementProfile profile)
    {
        if (!blockedCells.TryGetValue(profile, out ImmutableBitmap? cells))
        {
            cells = BuildBlockedCells(profile);
            blockedCells.Add(profile, cells);
        }

        return cells;
    }

    private ImmutableBitmap BuildObstacleCells()
    {
        ImmutableBitmap.Builder cells = ImmutableBitmap.Create(state.Map.Width, state.Map.Height).ToBuilder();

        foreach (WorldResourceNode resourceNode in state.ResourceNodes.Values)
        {
            cells.Set(resourceNode.Position.X, resourceNode.Position.Y);
        }

        foreach (WorldRod rod in state.Rods.Values)
        {
            cells.Set(rod.Position.X, rod.Position.Y);
        }

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            BlockFootprint(cells, fortification.Footprint, fortification.Entrance);
        }

        foreach (WorldSite site in state.Sites.Values)
        {
            BlockFootprint(cells, site.Footprint, site.Entrance);
        }

        foreach (WorldLandmark landmark in state.Landmarks.Values)
        {
            foreach (WorldPosition tile in landmark.Footprint.EnumerateTiles())
            {
                cells.Set(tile.X, tile.Y);
            }
        }

        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                WorldMapCell cell = state.Map.GetCellAt(new WorldPosition(x, y));

                if (cell.Feature == WorldMapFeature.Mountain)
                {
                    cells.Set(x, y);
                }
            }
        }

        return cells.ToImmutable();
    }

    private ImmutableBitmap GetObstacleCells()
    {
        StaticCells cells = GetStaticCells();

        lock (cells)
        {
            cells.Obstacles ??= BuildObstacleCells();

            return cells.Obstacles;
        }
    }

    // Obstacles, plus impassable cells that no army stands on.
    private ImmutableBitmap BuildBlockedCells(WorldMovementCalculator.MovementProfile profile)
    {
        ImmutableBitmap obstacles = obstacleCells.Value;
        ImmutableBitmap blocked = GetStaticCells().GetObstaclesAndImpassable(profile, () => BuildObstaclesAndImpassable(obstacles, profile));
        ImmutableBitmap.Builder cells = blocked.ToBuilder();

        foreach (WorldStack stack in state.Stacks.Values)
        {
            WorldPosition position = stack.Position;

            if (!obstacles.Get(position.X, position.Y) && !WorldMovementCalculator.IsPassable(state.Map, position, profile))
            {
                cells.Clear(position.X, position.Y);
            }
        }

        return cells.ToImmutable();
    }

    private ImmutableBitmap BuildObstaclesAndImpassable(ImmutableBitmap obstacles, WorldMovementCalculator.MovementProfile profile)
    {
        ImmutableBitmap.Builder cells = obstacles.ToBuilder();

        for (int y = 0; y < state.Map.Height; y++)
        {
            for (int x = 0; x < state.Map.Width; x++)
            {
                WorldPosition position = new(x, y);

                if (!WorldMovementCalculator.IsPassable(state.Map, position, profile))
                {
                    cells.Set(x, y);
                }
            }
        }

        return cells.ToImmutable();
    }

    private static void BlockFootprint(ImmutableBitmap.Builder cells, WorldFootprint footprint, WorldPosition entrance)
    {
        foreach (WorldPosition tile in footprint.EnumerateTiles())
        {
            if (tile != entrance)
            {
                cells.Set(tile.X, tile.Y);
            }
        }
    }
}
