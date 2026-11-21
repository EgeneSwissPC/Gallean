using System.Collections.Immutable;
using Avalonia.Media;
using Gallean.Common;
using SkiaSharp;
using static Gallean.Client.WorldRenderState;

namespace Gallean.Client.Tests;

public sealed class WorldRenderContentProviderTests
{
    private static TestAssetProvider CreateAssets()
    {
        SKColor color = new(110, 120, 130, 255);
        TestAssetProviderBuilder builder = new();
        Sprite smallSprite = builder.CreateSprite(color, 64, 32);
        Sprite bigSprite = builder.CreateSprite(color, 96, 96);
        Animation animation = builder.CreateAnimation(
            new AssetAnimationMetadata(FrameCount: 4, Width: 64, Height: 32, OffsetX: 0, OffsetY: 0, TriggerFrame: null, SecondTriggerFrame: null),
            [builder.CreateSprite(color, 64, 32), builder.CreateSprite(color, 64, 32), builder.CreateSprite(color, 64, 32), builder.CreateSprite(color, 64, 32)]);

        return builder
            .ResolveSprite(key => key is AssetSpriteKey.Ground ? smallSprite : bigSprite)
            .ResolveAnimation(_ => animation)
            .ResolveSound(_ => null)
            .Build();
    }

    private static WorldRenderState BuildState()
    {
        const int side = 6;
        List<MapTile> tiles = new(side * side);
        List<FogTile> fogTiles = new(side * side);
        List<MapObject> objects = [];
        ImmutableArray<WorldMapCell>.Builder cells = ImmutableArray.CreateBuilder<WorldMapCell>(side * side);

        for (int y = 0; y < side; y++)
        {
            for (int x = 0; x < side; x++)
            {
                WorldPosition position = new(x, y);
                bool isWater = (x + y * 3) % 4 == 0;

                if (isWater)
                {
                    tiles.Add(new MapTile(position, new MapTileSurface.Water(), RoadKey: null));
                    cells.Add(new WorldMapCell(WorldMapTerrain.Water, WorldMapFeature.None, new WorldObjectVariant(0)));
                }
                else
                {
                    tiles.Add(new MapTile(position, new MapTileSurface.Ground(WorldMapTerrain.Human), RoadKey: null));
                    cells.Add(new WorldMapCell(WorldMapTerrain.Human, WorldMapFeature.None, new WorldObjectVariant(0)));
                    objects.Add(new MapTileFeature(position, position.X + position.Y, new AssetSpriteKey.Tree(WorldMapTerrain.Neutral, 0)));
                }

                fogTiles.Add(new FogTile(position));
            }
        }

        WorldMap map = new(side, side, cells.MoveToImmutable());

        return new WorldRenderState(
            Terrain: new MapTerrain(map, tiles, [WorldMapTerrain.Human], []),
            ObjectiveMarkers: [], AmbientSounds: [],
            MapObjects: [.. objects.OrderBy(item => item.Depth)],
            Stacks: [],
            Animation: null,
            TerrainExpansion: null,
            Clouds: [],
            FogTiles: fogTiles,
            SelectedPath: null,
            Cursors: [],
            Reach: null,
            Highlights: new HighlightOverlay([]),
            Minimap: new MinimapState(side, side, [], []));
    }

    private static void CheckSameState(WorldRenderContent first, WorldRenderContent second)
    {
        Assert.That(second.GroundLayers, Is.SameAs(first.GroundLayers));
        Assert.That(second.Water, Is.SameAs(first.Water));
        Assert.That(second.BorderAtlas, Is.SameAs(first.BorderAtlas));
        Assert.That(second.RoadAtlas, Is.SameAs(first.RoadAtlas));
        Assert.That(second.ObjectAtlas, Is.SameAs(first.ObjectAtlas));
        Assert.That(second.ObjectSpriteDepths, Is.SameAs(first.ObjectSpriteDepths));
        Assert.That(second.DirectSprites, Is.SameAs(first.DirectSprites));
        Assert.That(second.Fog, Is.SameAs(first.Fog));
        Assert.That(second.MapClip, Is.SameAs(first.MapClip));
        Assert.That(second.MinimapImage, Is.SameAs(first.MinimapImage));
        Assert.That(second.HitTargets, Is.SameAs(first.HitTargets));
        Assert.That(second.TerrainExpansion, Is.SameAs(first.TerrainExpansion));
    }

    [Test]
    public void SameStateTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderState state = BuildState();

        WorldRenderContent first = provider.Get(state, assets);
        WorldRenderContent second = provider.Get(state, assets);

        CheckSameState(first, second);
    }

    [Test]
    public void HoverChangeTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderState state = BuildState();

        WorldRenderContent first = provider.Get(state, assets);
        WorldRenderState hovered = state with { Cursors = [new Cursor(new WorldPosition(1, 1), 1344, Theme.World.HoverCursorInnerColor, Theme.World.HoverCursorOuterColor, 1.0)] };
        WorldRenderContent second = provider.Get(hovered, assets);

        CheckSameState(first, second);
    }

    [Test]
    public void SelectionChangeTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderState state = BuildState();

        WorldRenderContent first = provider.Get(state, assets);
        WorldRenderState selected = state with
        {
            Reach = new ReachOverlay([new WorldPosition(2, 2), new WorldPosition(3, 2)]),
            Highlights = new HighlightOverlay(
            [
                new Highlight(
                    new Stack(
                        Position: new WorldPosition(2, 2),
                        Depth: 2439,
                        AssetKey: new AssetAnimationKey.StackUnit(new UnitDefinitionId("leader"), AssetAnimationKey.StackUnitPose.Stop, 7),
                        ShadowKey: null,
                        AuraKey: null,
                        BannerKey: new AssetSpriteKey.StackBanner(0),
                        Opacity: 1.0),
                    Theme.World.Highlight.SelectedColor)
            ])
        };
        WorldRenderContent second = provider.Get(selected, assets);

        CheckSameState(first, second);
    }

    [Test]
    public void RebuiltStateTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();

        WorldRenderContent first = provider.Get(BuildState(), assets);
        WorldRenderContent second = provider.Get(BuildState(), assets);

        Assert.That(second.Water, Is.SameAs(first.Water));
        Assert.That(second.ObjectAtlas, Is.Not.SameAs(first.ObjectAtlas));
    }

    [Test]
    public void TerrainLayoutChangeTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderState state = BuildState();

        WorldRenderContent first = provider.Get(state, assets);
        assets.RequiredAssets.Clear();

        List<MapTile> tiles = [.. state.Terrain.MapTiles];
        tiles[0] = new MapTile(new WorldPosition(0, 0), new MapTileSurface.Ground(WorldMapTerrain.Human), RoadKey: null);
        WorldRenderState expanded = state with
        {
            Terrain = state.Terrain with
            {
                MapTiles = tiles,
                Borders =
                [
                    new MapTileBorder.Blend(
                        new WorldPosition(0, 0),
                        new AssetSpriteKey.GroundBorder(
                            AssetSpriteKey.GroundBorderSet.Mask,
                            new AssetSpriteKey.GroundBorderShape.Edges(AssetSpriteKey.GroundBorderEdges.North),
                            Variant: 0),
                        WorldMapTerrain.Human)
                ]
            }
        };
        WorldRenderContent second = provider.Get(expanded, assets);

        Assert.That(second.GroundLayers, Is.Not.SameAs(first.GroundLayers));
        Assert.That(second.BorderAtlas, Is.Not.SameAs(first.BorderAtlas));
        Assert.That(assets.RequiredAssets, Is.EqualTo<IReadOnlyList<AssetKey>>([
            new AssetAnimationKey.Water(),
            new AssetSpriteKey.GroundBorder(
                AssetSpriteKey.GroundBorderSet.Mask,
                new AssetSpriteKey.GroundBorderShape.Edges(AssetSpriteKey.GroundBorderEdges.North),
                Variant: 0)
        ]));
    }

    [Test]
    public void TerrainExpansionHoverChangeTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderState plain = BuildState();
        WorldRenderState state = plain with
        {
            TerrainExpansion = new TerrainExpansionAnimation(
                new AssetSoundKey.Occupy(),
                new MapTerrain(
                    plain.Terrain.Map,
                    [new MapTile(new WorldPosition(2, 1), new MapTileSurface.Ground(WorldMapTerrain.Neutral), RoadKey: null)],
                    [WorldMapTerrain.Neutral],
                    []),
                [
                    new TerrainExpansionTile(
                        new WorldPosition(2, 1),
                        TimeSpan.FromMilliseconds(0.0),
                        TimeSpan.FromMilliseconds(0.0),
                        TimeSpan.FromMilliseconds(0.0),
                        TimeSpan.FromMilliseconds(0.0))
                ],
                [],
                TimeSpan.FromMilliseconds(500.0),
                TimeSpan.FromMilliseconds(500.0))
        };

        WorldRenderContent first = provider.Get(state, assets);
        WorldRenderState hovered = state with { Cursors = [new Cursor(new WorldPosition(1, 1), 1344, Theme.World.HoverCursorInnerColor, Theme.World.HoverCursorOuterColor, 1.0)] };
        WorldRenderContent second = provider.Get(hovered, assets);

        Assert.That(first.TerrainExpansion, Is.Not.Null);
        CheckSameState(first, second);
    }

    [TestCase(144, 144, 870)]
    [TestCase(168, 168, 1014)]
    [TestCase(171, 171, 1024)]
    [TestCase(320, 320, 1024)]
    [TestCase(1024, 1024, 1024)]
    public void MinimapNativeWidthTest(int mapWidth, int mapHeight, double expected)
    {
        // Original quest sizes keep 3 px per half tile; larger maps are capped so the image stays small.
        Assert.That(WorldRenderContentProvider.GetMinimapNativeWidth(mapWidth, mapHeight), Is.EqualTo(expected));
    }

    [Test]
    public void MinimapMarkerMoveTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderState state = BuildState();
        IReadOnlyList<WorldPosition> hidden = [new WorldPosition(0, 0), new WorldPosition(1, 0), new WorldPosition(5, 5)];
        MinimapState.Marker marker = new(new WorldPosition(2, 2), Colors.Red, MinimapState.MarkerType.Stack);

        WorldRenderContent first = provider.Get(state with { Minimap = new MinimapState(6, 6, hidden, [marker]) }, assets);
        WorldRenderContent moved = provider.Get(state with { Minimap = new MinimapState(6, 6, hidden, [marker with { Position = new WorldPosition(3, 2) }]) }, assets);
        WorldRenderContent revealed = provider.Get(state with { Minimap = new MinimapState(6, 6, [new WorldPosition(5, 5)], [marker]) }, assets);

        // The markers are drawn over the image by MinimapRenderer: a moving stack must not rebuild it, a fog change must.
        Assert.That(moved.MinimapImage, Is.SameAs(first.MinimapImage));
        Assert.That(revealed.MinimapImage, Is.Not.SameAs(first.MinimapImage));
    }

    [Test]
    public void FogUpdateMatchesFreshBuildTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderContentProvider provider = new();
        WorldRenderer renderer = new();
        WorldRenderState state = BuildState();
        WorldPosition[] all = [.. state.FogTiles.Select(tile => tile.Position)];

        // Reveal a growing area, conceal part of it again, then clear the fog completely and bring it back.
        HashSet<WorldPosition>[] explored =
        [
            [],
            [new WorldPosition(2, 2)],
            [.. all.Where(position => Math.Abs(position.X - 2) <= 1 && Math.Abs(position.Y - 2) <= 1)],
            [.. all.Where(position => position.X <= 3 && position.Y >= 1)],
            [.. all.Where(position => position.X == 0 || position.Y == 5)],
            [.. all],
            [new WorldPosition(5, 0)]
        ];

        foreach (HashSet<WorldPosition> step in explored)
        {
            WorldRenderState fogged = state with { FogTiles = [.. all.Where(position => !step.Contains(position)).Select(position => new FogTile(position))] };

            SKColor[] updated = Render(renderer, provider.Get(fogged, assets), fogged, assets);
            SKColor[] fresh = Render(renderer, new WorldRenderContentProvider().Get(fogged, assets), fogged, assets);

            Assert.That(updated, Is.EqualTo(fresh), $"{step.Count} explored tiles");
        }
    }

    [Test]
    public void FogChunkSeamsTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderer renderer = new();
        WorldRenderState state = BuildState();
        WorldRenderContentProvider chunked = new(fogChunkTiles: 2);
        WorldPosition[] all = [.. state.FogTiles.Select(tile => tile.Position)];

        // 2x2-tile chunks on a 6x6 map: every fog edge below crosses a chunk seam, and chunks on the map edge draw the
        // band outside it. Each chunk image carries its neighbours' edge samples, so the result must match one chunk.
        HashSet<WorldPosition>[] explored =
        [
            [new WorldPosition(1, 1)],
            [.. all.Where(position => position.X >= 1 && position.X <= 3 && position.Y >= 2 && position.Y <= 4)],
            [.. all.Where(position => (position.X + position.Y) % 3 == 0)],
            [.. all.Where(position => position.X == 5 || position.Y == 0)]
        ];

        foreach (HashSet<WorldPosition> step in explored)
        {
            WorldRenderState fogged = state with { FogTiles = [.. all.Where(position => !step.Contains(position)).Select(position => new FogTile(position))] };

            SKColor[] updated = Render(renderer, chunked.Get(fogged, assets), fogged, assets);
            SKColor[] fresh = Render(renderer, new WorldRenderContentProvider(fogChunkTiles: 2).Get(fogged, assets), fogged, assets);
            SKColor[] single = Render(renderer, new WorldRenderContentProvider().Get(fogged, assets), fogged, assets);

            Assert.That(updated, Is.EqualTo(fresh), $"{step.Count} explored tiles, updated");
            Assert.That(fresh, Is.EqualTo(single), $"{step.Count} explored tiles, chunks");
        }
    }

    [Test]
    public void FogTileListMatchesListTest()
    {
        using TestAssetProvider assets = CreateAssets();
        WorldRenderer renderer = new();
        WorldRenderState state = BuildState();
        ImmutableBitmap.Builder builder = ImmutableBitmap.Create(6, 6).ToBuilder();

        builder.Set(2, 2);
        builder.Set(3, 2);
        builder.Set(0, 5);
        FogTileList fogTiles = new(builder.ToImmutable());
        WorldRenderState fromBitmap = state with { FogTiles = fogTiles };
        WorldRenderState fromList = state with { FogTiles = [.. fogTiles] };

        // The fog layer reads a FogTileList's words directly; it must draw exactly what the same tiles as a list draw.
        Assert.That(fogTiles, Has.Count.EqualTo(33));
        Assert.That(
            Render(renderer, new WorldRenderContentProvider().Get(fromBitmap, assets), fromBitmap, assets),
            Is.EqualTo(Render(renderer, new WorldRenderContentProvider().Get(fromList, assets), fromList, assets)));
    }

    private static SKColor[] Render(WorldRenderer renderer, WorldRenderContent content, WorldRenderState state, TestAssetProvider assets)
    {
        SKImageInfo info = new(512, 320, SKColorType.Rgba8888, SKAlphaType.Premul);
        using SKSurface surface = SKSurface.Create(info);
        surface.Canvas.Clear(SKColors.White);
        surface.Canvas.Translate(256, 32);
        renderer.Render(surface.Canvas, content, state, assets, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, renderResolution: 1.0f, hitboxOverlay: false);

        using SKBitmap bitmap = new(info);
        surface.ReadPixels(info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0);

        return bitmap.Pixels;
    }
}
