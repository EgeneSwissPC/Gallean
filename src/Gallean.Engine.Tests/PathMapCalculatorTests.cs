using System.Collections.Immutable;
using Gallean.Common;
using Gallean.Tests.Common;

namespace Gallean.Engine.Tests;

public sealed class PathMapCalculatorTests
{
    private static readonly WorldMovementCalculator.MovementProfile DefaultProfile =
        new(MoveAbility: UnitMoveAbility.None, IsWaterOnly: false, IsLeaderDead: false, IsThief: false, IsIllusion: false);

    private static WorldState CreateEmptyState(WorldMap map)
    {
        return new WorldState(
            map,
            new WorldTurnInfo(1, new WorldPlayerId(0)),
            WorldDifficulty.Average,
            ImmutableSortedDictionary<WorldPlayerId, WorldPlayer>.Empty,
            ImmutableSortedDictionary<WorldRelationId, WorldRelation>.Empty,
            ImmutableSortedDictionary<WorldStackId, WorldStack>.Empty,
            ImmutableSortedDictionary<WorldFortificationId, WorldFortification>.Empty,
            ImmutableSortedDictionary<WorldResourceNodeId, WorldResourceNode>.Empty,
            ImmutableSortedDictionary<WorldRodId, WorldRod>.Empty,
            ImmutableSortedDictionary<WorldBagId, WorldBag>.Empty,
            ImmutableSortedDictionary<WorldTombId, WorldTomb>.Empty,
            ImmutableSortedDictionary<WorldLandmarkId, WorldLandmark>.Empty,
            ImmutableSortedDictionary<WorldMountainId, WorldMountain>.Empty,
            ImmutableSortedDictionary<WorldSiteId, WorldSite>.Empty,
            ImmutableSortedDictionary<WorldSubraceId, WorldSubrace>.Empty,
            WorldScenarioState.Empty,
            ImmutableSortedDictionary<WorldPlayerId, WorldAiMemory>.Empty);
    }

    private static ImmutableBitmap CreateFullFog(WorldMap map)
    {
        return ImmutableBitmap.Create(map.Width, map.Height, true);
    }

    private static string[] SerializePathMap(WorldMap map, WorldPath path)
    {
        WorldPosition start = path.Segments[0].Start;
        HashSet<WorldPosition> pathPositions = [start, .. path.Positions];
        WorldPosition end = path.Positions[^1];

        return SerializeMap(map, position =>
        {
            if (position == start)
            {
                return 'S';
            }
            else if (position == end)
            {
                return 'E';
            }
            else if (pathPositions.Contains(position))
            {
                return '*';
            }
            else
            {
                return null;
            }
        });
    }

    private static string[] SerializeMap(WorldMap map, Func<WorldPosition, char?> getOverlay)
    {
        List<string> rows = [];

        for (int y = 0; y < map.Height; y++)
        {
            List<char> cells = [];

            for (int x = 0; x < map.Width; x++)
            {
                WorldPosition position = new(x, y);
                char? overlay = getOverlay(position);
                cells.Add(overlay ?? GetTerrainChar(map.GetCellAt(position)));
            }

            rows.Add(string.Join("", cells));
        }

        return [.. rows];
    }

    private static char GetTerrainChar(WorldMapCell cell)
    {
        if (cell.Terrain == WorldMapTerrain.Water)
        {
            return '~';
        }

        return cell.Feature switch
        {
            WorldMapFeature.None => '.',
            WorldMapFeature.Forest => 'T',
            WorldMapFeature.Mountain => '^',
            WorldMapFeature.Road => '='
        };
    }

    [Test]
    public void StraightLinePathTest()
    {
        WorldMap map = TestHelpers.SimpleMap(5, 1);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(4, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S***E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(8));
    }

    [Test]
    public void VerticalLinePathTest()
    {
        WorldMap map = TestHelpers.SimpleMap(1, 5);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(0, 4), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S",
            "*",
            "*",
            "*",
            "E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(8));
    }

    [Test]
    public void DiagonalPathTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 3);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 2), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S..",
            ".*.",
            "..E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void PathAroundMountainTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^.",
            "...",
            "..."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S^E",
            ".*.",
            "..."
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void PathAroundWaterTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".~.",
            "...",
            "..."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S~E",
            ".*.",
            "..."
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void MountainBlockedNoPathTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^^."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(3, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void SameStartAndEndTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 1);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(1, 0), new WorldPosition(1, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.EqualTo(new WorldPath([])));
    }

    [Test]
    public void BlockedStartPositionTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            "^."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(1, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void BlockedEndPositionTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^"
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(1, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void HiddenEndPositionTest()
    {
        WorldMap map = TestHelpers.SimpleMap(2, 1);
        ImmutableBitmap fogOfWar = TestHelpers.BuildFogOfWar([
            ".?"
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(1, 0), fogOfWar, DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void ExploredCellsConstrainPathTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 2);
        ImmutableBitmap fogOfWar = TestHelpers.BuildFogOfWar([
            "?.?",
            ".?."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 1), new WorldPosition(2, 1), fogOfWar, DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            ".*.",
            "S.E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void NoFogTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 1);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), fogOfWar: null, DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S*E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void PathCostWithForestTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".+."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S*E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(6));
    }

    [Test]
    public void PrefersRoadTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".===.",
            ".+++.",
            ".^^^."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 1), new WorldPosition(4, 1), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            ".***.",
            "STTTE",
            ".^^^."
        ]));
        Assert.That(path.Cost, Is.EqualTo(5));
    }

    [Test]
    public void AvoidsImpassableTerrainTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^.",
            ".^.",
            "..."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 2), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S^.",
            "*^.",
            ".*E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(6));
    }

    [Test]
    public void AvoidsResourceNodesTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 3);

        WorldResourceNode node1 = new(new WorldResourceNodeId("0"), new WorldPosition(1, 0), default, AiPriority: 3);
        WorldResourceNode node2 = new(new WorldResourceNodeId("1"), new WorldPosition(1, 1), default, AiPriority: 3);
        WorldState state = CreateEmptyState(map) with
        {
            ResourceNodes = ImmutableSortedDictionary<WorldResourceNodeId, WorldResourceNode>.Empty
                .Add(node1.Id, node1)
                .Add(node2.Id, node2)
        };

        WorldPlan plan = WorldPlan.For(state);
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S.E",
            "*.*",
            ".*."
        ]));
        Assert.That(path.Cost, Is.EqualTo(8));
    }

    [Test]
    public void LandmarkImpassableTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 3);

        WorldLandmark landmark = new(new WorldLandmarkId("0"), new WorldLandmarkDefinitionId("def"), new WorldFootprint(new WorldPosition(1, 0), 1, 1));
        WorldState state = CreateEmptyState(map) with
        {
            Landmarks = ImmutableSortedDictionary<WorldLandmarkId, WorldLandmark>.Empty
                .Add(landmark.Id, landmark)
        };

        WorldPlan plan = WorldPlan.For(state);
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S.E",
            ".*.",
            "..."
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void LandmarkBlocksEndPositionTest()
    {
        WorldMap map = TestHelpers.SimpleMap(2, 1);

        WorldLandmark landmark = new(new WorldLandmarkId("0"), new WorldLandmarkDefinitionId("def"), new WorldFootprint(new WorldPosition(1, 0), 1, 1));
        WorldState state = CreateEmptyState(map) with
        {
            Landmarks = ImmutableSortedDictionary<WorldLandmarkId, WorldLandmark>.Empty
                .Add(landmark.Id, landmark)
        };

        WorldPlan plan = WorldPlan.For(state);
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(1, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void LandmarkBlocksPathTest()
    {
        WorldMap map = TestHelpers.SimpleMap(4, 1);

        WorldLandmark landmark = new(new WorldLandmarkId("0"), new WorldLandmarkDefinitionId("def"), new WorldFootprint(new WorldPosition(1, 0), 2, 1));
        WorldState state = CreateEmptyState(map) with
        {
            Landmarks = ImmutableSortedDictionary<WorldLandmarkId, WorldLandmark>.Empty
                .Add(landmark.Id, landmark)
        };

        WorldPlan plan = WorldPlan.For(state);
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void PrefersStraightHorizontalTest()
    {
        WorldMap map = TestHelpers.SimpleMap(4, 2);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 1), new WorldPosition(3, 1), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "....",
            "S**E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(6));
    }

    [Test]
    public void PrefersStraightVerticalTest()
    {
        WorldMap map = TestHelpers.SimpleMap(2, 4);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(0, 3), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S.",
            "*.",
            "*.",
            "E."
        ]));
        Assert.That(path.Cost, Is.EqualTo(6));
    }

    [Test]
    public void DiagonalBetweenBlocksTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".^.",
            "^.^",
            ".^."
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(1, 1), new WorldPosition(0, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "E^.",
            "^S^",
            ".^."
        ]));
        Assert.That(path.Cost, Is.EqualTo(2));
    }

    [Test]
    public void DiagonalForestCostTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            "..",
            ".+"
        ]);

        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(1, 1), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S.",
            ".E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void WaterBonusCostTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".~."
        ]);

        WorldMovementCalculator.MovementProfile profile = new(MoveAbility: UnitMoveAbility.Water, IsWaterOnly: false, IsLeaderDead: false, IsThief: false, IsIllusion: false);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), profile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S*E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void ForestBonusCostTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".+."
        ]);

        WorldMovementCalculator.MovementProfile profile = new(MoveAbility: UnitMoveAbility.Forest, IsWaterOnly: false, IsLeaderDead: false, IsThief: false, IsIllusion: false);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), profile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S*E"
        ]));
        Assert.That(path.Cost, Is.EqualTo(4));
    }

    [Test]
    public void WaterOnlyDeepCostTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            "~~~~",
            "~~~~",
            "~~~."
        ]);

        WorldMovementCalculator.MovementProfile profile = new(MoveAbility: UnitMoveAbility.None, IsWaterOnly: true, IsLeaderDead: false, IsThief: false, IsIllusion: false);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(3, 0), CreateFullFog(map), profile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "S**E",
            "~~~~",
            "~~~."
        ]));
        Assert.That(path.Cost, Is.EqualTo(6));
    }

    [Test]
    public void WaterOnlyCoastBlockedTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            "~~."
        ]);

        WorldMovementCalculator.MovementProfile profile = new(MoveAbility: UnitMoveAbility.None, IsWaterOnly: true, IsLeaderDead: false, IsThief: false, IsIllusion: false);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(1, 0), CreateFullFog(map), profile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void WaterOnlyLandBlockedTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            "~~."
        ]);

        WorldMovementCalculator.MovementProfile profile = new(MoveAbility: UnitMoveAbility.None, IsWaterOnly: true, IsLeaderDead: false, IsThief: false, IsIllusion: false);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPath? path = plan.GetPath(new WorldPosition(0, 0), new WorldPosition(2, 0), CreateFullFog(map), profile, WorldMovementLimit.Unlimited);

        Assert.That(path, Is.Null);
    }

    [Test]
    public void CityEntranceNoDetourTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            "....",
            "....",
            "..+."
        ]);

        WorldCity city = new(
            new WorldFortificationId("city"),
            new ScenarioTextId("Name"),
            new WorldCityDefinitionId(WorldRace.Human, 1),
            new WorldPlayerId(0),
            TestHelpers.NeutralSubrace.Id,
            FortificationCalculator.CreateFootprint(new WorldPosition(2, 1), 1),
            UnitGrid<WorldUnit>.Empty,
            WorldInventory.Empty,
            AiPriority: 3,
            RiotTurns: 0,
            GrowthTurn: 0);
        WorldState state = CreateEmptyState(map) with
        {
            Fortifications = ImmutableSortedDictionary<WorldFortificationId, WorldFortification>.Empty.Add(city.Id, city)
        };

        WorldPlan plan = WorldPlan.For(state);
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(0, 2), new WorldPosition(2, 2), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            "....",
            "....",
            "S*E."
        ]));
        Assert.That(path.Cost, Is.EqualTo(6));
    }

    [Test]
    public void BoundedSearchMatchesUnboundedTest()
    {
        // Forests, water, roads and mountains give unequal step costs, so a truncated search could pick a worse
        // predecessor if it stopped settling cells too early.
        WorldMap map = TestHelpers.BuildMap([
            "..++~~..==..",
            ".^^+~~.==...",
            "..++..==.^^.",
            "~~..==...++.",
            "~~==....~~+.",
            "..=..^^.~~..",
            ".==.++..^...",
            "=....++....."
        ]);
        WorldPosition start = new(0, 0);
        WorldMovementCalculator.MovementProfile deadLeader = DefaultProfile with { IsLeaderDead = true };

        foreach (WorldMovementCalculator.MovementProfile profile in (WorldMovementCalculator.MovementProfile[])[DefaultProfile, deadLeader])
        {
            WorldPathMap full = Assert.NotNull(WorldPlan.For(CreateEmptyState(map)).GetPathMap(start, CreateFullFog(map), profile));

            foreach (int maxCost in (int[])[0, 5, 9, 14, 20])
            {
                WorldPathMap bounded = Assert.NotNull(WorldPlan.For(CreateEmptyState(map)).GetPathMap(start, CreateFullFog(map), profile, maxCost));

                for (int y = 0; y < map.Height; y++)
                {
                    for (int x = 0; x < map.Width; x++)
                    {
                        WorldPosition target = new(x, y);
                        bool inFull = full.TryGetPath(target, WorldMovementLimit.Unlimited, out WorldPath? fullPath);
                        bool inBounded = bounded.TryGetPath(target, WorldMovementLimit.Unlimited, out WorldPath? boundedPath);

                        if (inFull && fullPath!.Cost <= maxCost)
                        {
                            Assert.That(inBounded, Is.True, $"{target} within {maxCost}");
                            Assert.That(boundedPath!.Positions, Is.EqualTo(fullPath.Positions), $"{target} within {maxCost}");
                            Assert.That(bounded.IsSettled(target), Is.True);
                        }
                        else if (target != start)
                        {
                            Assert.That(inBounded, Is.False, $"{target} beyond {maxCost}");
                            Assert.That(bounded.IsSettled(target), Is.False);
                        }
                    }
                }

                Assert.That(bounded.GetReachablePositions(maxCost - WorldMovementCalculator.MaxMovementCost), Is.EqualTo(full.GetReachablePositions(maxCost - WorldMovementCalculator.MaxMovementCost)));
            }
        }
    }

    [Test]
    public void TargetSearchMatchesFullSearchTest()
    {
        // Gallean Upgrade (AI speed): a search that stops once its targets are settled must give the paths of a full
        // search to every target, on a map with unequal step costs and an unreachable island.
        WorldMap map = TestHelpers.BuildMap([
            "..++~~..==..",
            ".^^+~~.==...",
            "..++..==.^^.",
            "~~..==...++.",
            "~~==....~~+.",
            "..=..^^.~~..",
            ".==.++..^.~~",
            "=....++...~."
        ]);
        WorldPosition start = new(0, 0);
        WorldPosition island = new(11, 7);
        WorldPathMap full = Assert.NotNull(WorldPlan.For(CreateEmptyState(map)).GetPathMap(start, CreateFullFog(map), DefaultProfile));
        WorldPosition[][] targetSets =
        [
            [new(1, 0)],
            [new(3, 2), new(7, 1)],
            [new(11, 0), new(0, 7), new(6, 6)],
            [new(2, 2), island]
        ];

        foreach (WorldPosition[] targets in targetSets)
        {
            WorldPathMap partial = Assert.NotNull(WorldPlan.For(CreateEmptyState(map)).GetPathMapToTargets(start, CreateFullFog(map), DefaultProfile, targets));

            foreach (WorldPosition target in targets)
            {
                bool inFull = full.TryGetPath(target, WorldMovementLimit.Unlimited, out WorldPath? fullPath);
                bool inPartial = partial.TryGetPath(target, WorldMovementLimit.Unlimited, out WorldPath? partialPath);

                Assert.That(inPartial, Is.EqualTo(inFull), $"{target}");
                Assert.That(partial.IsSettled(target), Is.True, $"{target}");

                if (inFull)
                {
                    Assert.That(partialPath!.Positions, Is.EqualTo(fullPath!.Positions), $"{target}");
                    Assert.That(partialPath.Cost, Is.EqualTo(fullPath.Cost), $"{target}");
                }
            }

            // Stopping early is the point: a set of near targets must not settle the far corner.
            if (targets.Length == 1)
            {
                Assert.That(partial.IsSettled(new WorldPosition(11, 6)), Is.False);
            }
        }
    }

    [Test]
    public void TargetSearchReusesWiderCachedMapTest()
    {
        // Gallean Upgrade (AI speed): a full map cached on the plan answers later target searches, and a narrow target
        // map never replaces it, so callers that need the whole map still get it.
        WorldMap map = TestHelpers.SimpleMap(20, 20);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        WorldPosition start = new(0, 0);
        ImmutableBitmap fog = CreateFullFog(map);

        WorldPathMap full = Assert.NotNull(plan.GetPathMap(start, fog, DefaultProfile));
        Assert.That(plan.GetPathMapToTargets(start, fog, DefaultProfile, [new WorldPosition(1, 1)]), Is.SameAs(full));

        WorldPlan other = WorldPlan.For(CreateEmptyState(map));
        WorldPathMap near = Assert.NotNull(other.GetPathMapToTargets(start, fog, DefaultProfile, [new WorldPosition(1, 1)]));
        Assert.That(near.IsSettled(new WorldPosition(19, 19)), Is.False);
        WorldPathMap far = Assert.NotNull(other.GetPathMapToTargets(start, fog, DefaultProfile, [new WorldPosition(19, 19)]));
        Assert.That(far.IsSettled(new WorldPosition(19, 19)), Is.True);
        Assert.That(other.GetPathMapToTargets(start, fog, DefaultProfile, [new WorldPosition(1, 1)]), Is.SameAs(far));
        Assert.That(other.GetPathMap(start, fog, DefaultProfile)!.IsSettled(new WorldPosition(19, 19)), Is.True);
    }

    [Test]
    public void BlockedCellsFollowEachStateTest()
    {
        // Blocked cells are kept across states that share the map and the obstacle collections; the armies standing on
        // impassable cells and a changed collection must still be taken from each state.
        WorldMap map = TestHelpers.BuildMap([
            "~~~~."
        ]);
        ImmutableBitmap fog = CreateFullFog(map);
        WorldMovementCalculator.MovementProfile seaCreature = DefaultProfile with { IsWaterOnly = true };
        WorldStack stack = TestHelpers.SimpleStack(new WorldStackId("Stack"), new WorldPlayerId(1), new WorldPosition(3, 0), UnitGrid<WorldUnit>.Empty, movementPoints: 0);
        WorldRod rod = new(new WorldRodId("Rod"), new WorldPlayerId(1), new WorldPosition(1, 0));
        WorldState empty = CreateEmptyState(map);
        WorldState withStack = empty with { Stacks = empty.Stacks.Add(stack.Id, stack) };
        WorldState withRod = empty with { Rods = empty.Rods.Add(rod.Id, rod) };

        WorldPath? PathTo(WorldState state, int x) =>
            WorldPlan.For(state with { }).GetPath(new WorldPosition(0, 0), new WorldPosition(x, 0), fog, seaCreature, WorldMovementLimit.Unlimited);

        // (3, 0) is next to land, so not deep water: impassable for a sea creature unless an army stands there.
        Assert.That(PathTo(empty, 3), Is.Null, "shallow water");
        Assert.That(PathTo(withStack, 3), Is.Not.Null, "an army on shallow water can be reached");
        Assert.That(PathTo(empty, 3), Is.Null, "shallow water again once the army left");
        Assert.That(PathTo(withRod, 1), Is.Null, "rod");
        Assert.That(PathTo(empty, 1), Is.Not.Null, "rod gone");
    }

    [Test]
    public void BoundedMapFallbackTest()
    {
        WorldMap map = TestHelpers.SimpleMap(20, 1);
        WorldPlan plan = WorldPlan.For(CreateEmptyState(map));
        ImmutableBitmap fog = CreateFullFog(map);

        WorldPathMap bounded = Assert.NotNull(plan.GetPathMap(new WorldPosition(0, 0), fog, DefaultProfile, maxCost: 6));
        Assert.That(bounded.IsSettled(new WorldPosition(3, 0)), Is.True);
        Assert.That(bounded.IsSettled(new WorldPosition(4, 0)), Is.False);

        // A near target is answered from the bounded map, a far one runs the full search and replaces it.
        Assert.That(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(3, 0), fog, DefaultProfile, WorldMovementLimit.Unlimited)?.Cost, Is.EqualTo(6));
        Assert.That(plan.GetPathMap(new WorldPosition(0, 0), fog, DefaultProfile), Is.Not.SameAs(bounded));
        Assert.That(plan.GetPath(new WorldPosition(0, 0), new WorldPosition(19, 0), fog, DefaultProfile, WorldMovementLimit.Unlimited)?.Cost, Is.EqualTo(38));
        Assert.That(plan.GetPathMap(new WorldPosition(0, 0), fog, DefaultProfile, maxCost: 6)!.MaxCost, Is.EqualTo(int.MaxValue));
    }

    [Test]
    public void MaxMovementCostTest()
    {
        WorldMap map = TestHelpers.BuildMap([
            ".+=^~"
        ]);
        int max = 0;

        foreach (UnitMoveAbility ability in (UnitMoveAbility[])[UnitMoveAbility.None, UnitMoveAbility.Water, UnitMoveAbility.Forest, UnitMoveAbility.Plain])
        {
            foreach (bool waterOnly in (bool[])[false, true])
            {
                foreach (bool leaderDead in (bool[])[false, true])
                {
                    WorldMovementCalculator.MovementProfile profile = DefaultProfile with { MoveAbility = ability, IsWaterOnly = waterOnly, IsLeaderDead = leaderDead };

                    for (int x = 0; x < map.Width; x++)
                    {
                        int cost = WorldMovementCalculator.GetMovementCost(map, new WorldPosition(x, 0), profile);

                        if (cost != int.MaxValue)
                        {
                            max = Math.Max(max, cost);
                        }
                    }
                }
            }
        }

        Assert.That(WorldMovementCalculator.MaxMovementCost, Is.EqualTo(max));
    }

    [Test]
    public void CityEntranceFreeTest()
    {
        WorldMap map = TestHelpers.SimpleMap(3, 1);

        WorldCity city = new(
            new WorldFortificationId("city"),
            new ScenarioTextId("Name"),
            new WorldCityDefinitionId(WorldRace.Human, 1),
            new WorldPlayerId(0),
            TestHelpers.NeutralSubrace.Id,
            FortificationCalculator.CreateFootprint(new WorldPosition(2, 0), 1),
            UnitGrid<WorldUnit>.Empty,
            WorldInventory.Empty,
            AiPriority: 3,
            RiotTurns: 0,
            GrowthTurn: 0);
        WorldState state = CreateEmptyState(map) with
        {
            Fortifications = ImmutableSortedDictionary<WorldFortificationId, WorldFortification>.Empty.Add(city.Id, city)
        };

        WorldPlan plan = WorldPlan.For(state);
        WorldPath path = Assert.NotNull(plan.GetPath(new WorldPosition(1, 0), new WorldPosition(2, 0), CreateFullFog(map), DefaultProfile, WorldMovementLimit.Unlimited));

        Assert.That(SerializePathMap(map, path), Is.EqualTo([
            ".SE"
        ]));
        Assert.That(path.Cost, Is.Zero);
    }
}
