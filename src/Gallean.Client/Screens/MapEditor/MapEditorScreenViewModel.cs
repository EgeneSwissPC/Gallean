using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal enum MapEditorTool
{
    Land,
    Water,
    Forest,
    Mountain,
    Clear,
    Capital,
    Army,
    Village,
    Resource,
    Ruin,
    Bag,
    Site,
    Landmark,
    Road,
    Rod
}

// Paints a CustomMap and shows it through the game's world renderer. Terrain strokes update the preview state
// directly; placing a capital or any object rebuilds the preview through the scenario loader.
internal sealed partial class MapEditorScreenViewModel : AbstractScreenViewModel
{
    public const int MaxBrushRadius = 8;
    public const double MinZoom = 0.3;
    public const double InitialZoom = 0.5;
    private const int MaxUndoSteps = 30;

    private readonly CustomMapStore store;
    private readonly IGameDefinitionProvider definitions;
    private readonly Random random = new();
    private readonly Stack<CustomMap> undoSteps = new();
    private CustomMap map;
    private WorldState state;
    private WorldRenderRequest renderRequest;
    private WorldPosition? hoverTile;
    private WorldPosition? lastStrokeTile;
    private bool strokeChanged;
    private readonly IReadOnlyList<UnitDefinition> units;
    private int previewVersion;
    private GameModel editorGame;
    private readonly MapEditorLibrary library;
    private readonly IReadOnlyList<MapEditorLandmarkOption> allLandmarks;

    public MapEditorScreenViewModel(
        GameViewModel game,
        CustomMapStore store,
        GameModel editorGame,
        CustomMap map,
        MapEditorLibrary library)
    {
        Game = game;
        this.store = store;
        this.editorGame = editorGame;
        this.library = library;
        units = library.Units;
        definitions = editorGame.Definitions;
        this.map = map;
        Scenario = editorGame.Scenario;
        state = editorGame.Snapshot.WorldState;
        ViewPlayerId = state.Players.Keys.First();

        Tools =
        [
            new MapEditorOption<MapEditorTool>(MapEditorTool.Land, Tr("Land")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Water, Tr("Water")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Forest, Tr("Forest")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Mountain, Tr("Mountains")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Clear, Tr("Clear")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Capital, Tr("Capital")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Army, Tr("Army")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Village, Tr("Village")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Resource, Tr("Resources")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Ruin, Tr("Ruin")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Bag, Tr("Treasure")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Site, Tr("Site")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Landmark, Tr("Landmark")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Road, Tr("Road")),
            new MapEditorOption<MapEditorTool>(MapEditorTool.Rod, Tr("Rods"))
        ];
        SelectedTool = Tools[0];
        LandTerrains =
        [
            .. new[] { WorldMapTerrain.Neutral, WorldMapTerrain.Human, WorldMapTerrain.Undead, WorldMapTerrain.Heretic, WorldMapTerrain.Dwarf, WorldMapTerrain.Elf }
                .Select(terrain => new MapEditorOption<WorldMapTerrain>(terrain, MapEditorLabels.Terrain(terrain)))
        ];
        SelectedLandTerrain = LandTerrains[0];
        CapitalRaces = [.. map.Players.Select(player => new MapEditorOption<WorldRace>(player.Race, MapEditorLabels.Race(player.Race)))];
        SelectedCapitalRace = CapitalRaces[0];
        SelectedRodOwner = CapitalRaces[0];
        ArmyOwners =
        [
            new MapEditorOption<WorldRace>(WorldRace.Neutral, Tr("Neutral")),
            .. map.Players.Select(player => new MapEditorOption<WorldRace>(player.Race, MapEditorLabels.Race(player.Race)))
        ];
        SelectedArmyOwner = ArmyOwners[0];
        VillageOwners = ArmyOwners;
        SelectedVillageOwner = VillageOwners[0];
        VillageLevels = MapEditorLabels.VillageLevels();
        SelectedVillageLevel = VillageLevels[0];
        ResourceTypes =
        [
            .. new[] { WorldResourceType.Gold, WorldResourceType.LifeMana, WorldResourceType.InfernalMana, WorldResourceType.DeathMana, WorldResourceType.RuneMana, WorldResourceType.GroveMana }
                .Select(type => new MapEditorOption<WorldResourceType>(type, MapEditorLabels.Resource(type)))
        ];
        SelectedResourceType = ResourceTypes[0];
        SiteTypes =
        [
            .. new[] { CustomMapSiteType.Merchant, CustomMapSiteType.MageTower, CustomMapSiteType.Mercenaries, CustomMapSiteType.Trainer }
                .Select(type => new MapEditorOption<CustomMapSiteType>(type, MapEditorLabels.SiteType(type)))
        ];
        SelectedSiteType = SiteTypes[0];
        allLandmarks = MapEditorLandmarkOption.Create(library.Landmarks ?? [], editorGame);
        LandmarkCategories =
        [
            .. new[] { WorldLandmarkType.Misc, WorldLandmarkType.Building, WorldLandmarkType.Structure, WorldLandmarkType.Terrain }
                .Select(type => new MapEditorOption<WorldLandmarkType>(type, MapEditorLabels.LandmarkCategory(type)))
        ];
        LandmarkSizes =
        [
            new MapEditorOption<int>(0, Tr("Any size")),
            .. allLandmarks.Select(option => option.Definition.Width).Distinct().Order().Select(size => new MapEditorOption<int>(size, $"{size}x{size}"))
        ];
        SelectedLandmarkCategory = LandmarkCategories[0];
        SelectedLandmarkSize = LandmarkSizes[0];
        Name = map.Name;
        BrushRadius = 1;
        renderRequest = CreateRenderRequest();
        UpdateStatus();
    }

    public GameViewModel Game { get; }
    public IGameScenarioProvider Scenario { get; }
    public IGameDefinitionProvider Definitions => definitions;
    public WorldPlayerId ViewPlayerId { get; }
    public IReadOnlyList<MapEditorOption<MapEditorTool>> Tools { get; }
    public IReadOnlyList<MapEditorOption<WorldMapTerrain>> LandTerrains { get; }
    public IReadOnlyList<MapEditorOption<WorldRace>> CapitalRaces { get; }
    public IReadOnlyList<MapEditorOption<WorldRace>> ArmyOwners { get; }
    public IReadOnlyList<MapEditorOption<WorldRace>> VillageOwners { get; }
    public IReadOnlyList<MapEditorOption<int>> VillageLevels { get; }
    public IReadOnlyList<MapEditorOption<WorldResourceType>> ResourceTypes { get; }
    public IReadOnlyList<MapEditorOption<CustomMapSiteType>> SiteTypes { get; }
    public IReadOnlyList<MapEditorOption<WorldLandmarkType>> LandmarkCategories { get; }
    public IReadOnlyList<MapEditorOption<int>> LandmarkSizes { get; }
    public MapEditorLibrary Library => library;
    public GameModel EditorGame => editorGame;
    public int MapWidth => map.Size;
    public int MapHeight => map.Size;
    public int MaxBrush => MaxBrushRadius;
    public int NameLength => MapEditorMenuScreenViewModel.MaxNameLength;
    public CustomMap Map => map;
    public WorldState State => state;
    public bool IsActive => IsOpen && Host?.Top == this;
    public WorldPosition? CameraFocus => map.Players.Select(player => player.Capital).FirstOrDefault(capital => capital is not null) is { } capital
        ? new WorldPosition(capital.X + 2, capital.Y + 2)
        : new WorldPosition(map.Size / 2, map.Size / 2);

    public WorldRenderRequest RenderRequest => renderRequest;

    private readonly MusicQueue musicQueue = MusicQueue.Create([new AssetSoundKey.MenuMusic()], isLooping: true);

    public override bool PaintsOwnBackground => true;
    public override ScreenMusic Music => ScreenMusic.Tracks(musicQueue);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLandTool), nameof(IsCapitalTool), nameof(IsArmyTool), nameof(IsVillageTool), nameof(IsResourceTool))]
    [NotifyPropertyChangedFor(nameof(IsRuinTool), nameof(IsBagTool), nameof(IsSiteTool), nameof(IsLandmarkTool), nameof(IsRoadTool), nameof(IsRodTool), nameof(HasBrush), nameof(ShowLandmarkPalette))]
    public partial MapEditorOption<MapEditorTool> SelectedTool { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldLandmarkType> SelectedLandmarkCategory { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<int> SelectedLandmarkSize { get; set; }

    // The palette: landmarks of the chosen category and size.
    [ObservableProperty]
    public partial IReadOnlyList<MapEditorLandmarkOption> Landmarks { get; private set; } = [];

    [ObservableProperty]
    public partial MapEditorLandmarkOption? SelectedLandmark { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<CustomMapSiteType> SelectedSiteType { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedVillageOwner { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<int> SelectedVillageLevel { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldResourceType> SelectedResourceType { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldMapTerrain> SelectedLandTerrain { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedCapitalRace { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedArmyOwner { get; set; }

    [ObservableProperty]
    public partial WorldPositionInfoViewModel? PositionInfo { get; private set; }

    [ObservableProperty]
    public partial int BrushRadius { get; set; }

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool HasProblems { get; private set; }

    [ObservableProperty]
    public partial string HoverText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    public partial bool CanUndo { get; private set; }

    public bool IsDirty { get; private set; }
    public bool IsLandTool => SelectedTool.Value == MapEditorTool.Land;
    public bool IsCapitalTool => SelectedTool.Value == MapEditorTool.Capital;
    public bool IsArmyTool => SelectedTool.Value == MapEditorTool.Army;
    public bool IsVillageTool => SelectedTool.Value == MapEditorTool.Village;
    public bool IsResourceTool => SelectedTool.Value == MapEditorTool.Resource;
    public bool IsRuinTool => SelectedTool.Value == MapEditorTool.Ruin;
    public bool IsBagTool => SelectedTool.Value == MapEditorTool.Bag;
    public bool IsSiteTool => SelectedTool.Value == MapEditorTool.Site;
    public bool IsLandmarkTool => SelectedTool.Value == MapEditorTool.Landmark;
    public bool IsRoadTool => SelectedTool.Value == MapEditorTool.Road;
    public bool HasBrush => SelectedTool.Value is MapEditorTool.Land or MapEditorTool.Water or MapEditorTool.Forest or MapEditorTool.Mountain or MapEditorTool.Clear;

    partial void OnSelectedToolChanged(MapEditorOption<MapEditorTool> value)
    {
        roadStart = null;
        renderRequest = CreateRenderRequest();
        RefreshPreview();
    }

    partial void OnSelectedLandmarkCategoryChanged(MapEditorOption<WorldLandmarkType> value)
    {
        FilterLandmarks();
    }

    partial void OnSelectedLandmarkSizeChanged(MapEditorOption<int> value)
    {
        FilterLandmarks();
    }

    // Keeps the chosen landmark when it is still in the palette, otherwise picks the first one shown.
    private void FilterLandmarks()
    {
        if (SelectedLandmarkCategory is null || SelectedLandmarkSize is null)
        {
            return;
        }

        Landmarks =
        [
            .. allLandmarks.Where(option => option.Definition.Type == SelectedLandmarkCategory.Value &&
                                            (SelectedLandmarkSize.Value == 0 || option.Definition.Width == SelectedLandmarkSize.Value))
        ];

        if (SelectedLandmark is null || !Landmarks.Contains(SelectedLandmark))
        {
            SelectedLandmark = Landmarks.Count > 0 ? Landmarks[0] : null;
        }
    }

    partial void OnNameChanged(string value)
    {
        if (value != map.Name)
        {
            IsDirty = true;
        }
    }

    public void SelectTool(MapEditorTool tool)
    {
        SelectedTool = Tools.Single(option => option.Value == tool);
    }

    public void UpdateHover(WorldPosition? tile)
    {
        if (tile is { } position && !map.IsValidPosition(position))
        {
            tile = null;
        }

        if (hoverTile == tile)
        {
            return;
        }

        hoverTile = tile;
        RefreshPreview();
    }

    // alternate: Shift is held (the road tool then removes a road tile, the rod tool a rod).
    public void BeginStroke(WorldPosition tile, bool alternate = false)
    {
        if (!map.IsValidPosition(tile) || LeaveSpreadPreview())
        {
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Road)
        {
            ClickRoad(tile, alternate);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Rod)
        {
            ClickRod(tile, alternate);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Capital)
        {
            if (map.GetCapitalAt(tile) is { } capital)
            {
                OpenCapital(capital);
                return;
            }

            PlaceCapitalAsync(tile).FileAndForget();
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Army)
        {
            ClickArmy(tile);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Village)
        {
            ClickVillage(tile);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Resource)
        {
            ClickResource(tile);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Ruin)
        {
            ClickRuin(tile);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Bag)
        {
            ClickBag(tile);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Site)
        {
            ClickSite(tile);
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Landmark)
        {
            ClickLandmark(tile);
            return;
        }

        undoSteps.Push(map.Clone());
        TrimUndo();
        strokeChanged = false;
        lastStrokeTile = null;
        ContinueStroke(tile);
    }

    public void ContinueStroke(WorldPosition tile)
    {
        if (!HasBrush || lastStrokeTile == tile || !map.IsValidPosition(tile) || IsSpreadPreview)
        {
            return;
        }

        lastStrokeTile = tile;

        if (map.Paint(tile, BrushRadius, ToBrush(SelectedTool.Value), SelectedLandTerrain.Value, random))
        {
            strokeChanged = true;
            IsDirty = true;
            state = CustomMapStore.ApplyTerrain(state, map);
            RefreshPreview();
        }
    }

    public void EndStroke()
    {
        if (lastStrokeTile is null)
        {
            return;
        }

        lastStrokeTile = null;

        if (!strokeChanged && undoSteps.Count > 0)
        {
            undoSteps.Pop();
        }

        CanUndo = undoSteps.Count > 0;
        UpdateStatus();
    }

    private async Task PlaceCapitalAsync(WorldPosition tile)
    {
        WorldPosition topLeft = map.CenterFootprint(tile, CustomMap.CapitalSize, CustomMap.CapitalSize);
        CustomMap before = map.Clone();

        if (map.PlaceCapital(SelectedCapitalRace.Value, topLeft) is { } error)
        {
            Game.ShowError(MapEditorLabels.CapitalError(error));
            return;
        }

        undoSteps.Push(before);
        TrimUndo();
        CanUndo = true;
        IsDirty = true;
        await RebuildPreviewAsync();
    }

    private async Task RebuildPreviewAsync()
    {
        int version = ++previewVersion;
        state = CustomMapStore.ApplyTerrain(state, map);
        RefreshPreview();
        UpdateStatus();

        try
        {
            GameModel preview = await Game.BuildMapPreviewAsync(store, map.Clone(), definitions);

            if (version == previewVersion)
            {
                editorGame.Dispose();
                editorGame = preview;
                state = CustomMapStore.ApplyTerrain(preview.Snapshot.WorldState, map);
                SetRenderRequest(CreateRenderRequest());
                OnPropertyChanged(nameof(EditorGame));
            }
            else
            {
                preview.Dispose();
            }
        }
        catch (Exception ex)
        {
            Game.ShowError(Tr("Failed to update the map: {0}", ex.Message));
        }
    }

    // Army tool: clicking free land opens the army screen for a new army, clicking an army opens it for editing.
    // Clicking a village or capital opens its screen, with the garrison and the army stationed in it.
    private void ClickArmy(WorldPosition tile)
    {
        WorldRace owner = SelectedArmyOwner.Value;

        if (map.GetVillageAt(tile) is { } village)
        {
            OpenVillage(village);
            return;
        }

        if (map.GetCapitalAt(tile) is { } capital)
        {
            OpenCapital(capital);
            return;
        }

        CustomMapStack? existing = map.GetStackAt(tile);

        if (existing is null && map.PlaceStack(tile, owner, ProbeArmy).Error is { } blocked and not CustomMapStackError.NoLeader)
        {
            Game.ShowError(MapEditorLabels.StackError(blocked, 0));
            return;
        }

        Game.OpenScreen(new MapEditorArmyScreenViewModel(
            Game,
            editorGame,
            units,
            existing?.Owner ?? owner,
            tile,
            existing?.Slots,
            (armyOwner, slots) => SaveArmy(tile, existing?.Id, armyOwner, slots),
            existing is null ? null : () => DeleteArmy(existing.Id)));
    }

    // Ruin tool: clicking free land places a ruin centred on the tile, clicking a ruin opens it.
    private void ClickRuin(WorldPosition tile)
    {
        if (map.GetRuinAt(tile) is { } existing)
        {
            Game.OpenScreen(new MapEditorRuinScreenViewModel(
                Game,
                editorGame,
                library,
                existing,
                (variant, name, gold, item, guards) => Commit(current => current.UpdateRuin(existing.Id, variant, name, gold, item, guards)),
                () => CommitRemoval(current => current.RemoveRuin(existing.Id))));
            return;
        }

        WorldPosition topLeft = map.CenterFootprint(tile, CustomMapRuin.Size, CustomMapRuin.Size);
        CustomMap before = map.Clone();

        if (map.PlaceRuin(topLeft, random.Next(CustomMapRuin.VariantCount), PickName(library.Names.Ruins, map.Ruins.Select(ruin => ruin.Name)).Name).Error is { } error)
        {
            Game.ShowError(MapEditorLabels.ObjectError(error));
            return;
        }

        CommitArmyChange(before);
    }

    // Treasure tool: clicking land or water places a bag, clicking a bag opens it.
    private void ClickBag(WorldPosition tile)
    {
        if (map.GetBagAt(tile) is { } existing)
        {
            Game.OpenScreen(new MapEditorBagScreenViewModel(
                Game,
                editorGame,
                library,
                existing,
                map.GetCell(tile).Terrain == WorldMapTerrain.Water,
                (variant, items) => Commit(current => current.UpdateBag(existing.Id, variant, items)),
                () => CommitRemoval(current => current.RemoveBag(existing.Id))));
            return;
        }

        CustomMap before = map.Clone();
        int variant = random.Next(map.GetCell(tile).Terrain == WorldMapTerrain.Water ? 4 : CustomMapBag.VariantCount);

        if (map.PlaceBag(tile, variant).Error is { } error)
        {
            Game.ShowError(MapEditorLabels.ObjectError(error));
            return;
        }

        CommitArmyChange(before);
    }

    // Site tool: clicking free land places a site of the chosen type centred on the tile, clicking a site opens it.
    private void ClickSite(WorldPosition tile)
    {
        if (map.GetSiteAt(tile) is { } existing)
        {
            Game.OpenScreen(new MapEditorSiteScreenViewModel(
                Game,
                editorGame,
                library,
                existing,
                (variant, name, greeting, items, spells, siteUnits) => Commit(current => current.UpdateSite(existing.Id, variant, name, greeting, items, spells, siteUnits)),
                () => CommitRemoval(current => current.RemoveSite(existing.Id))));
            return;
        }

        CustomMapSiteType type = SelectedSiteType.Value;
        IReadOnlyList<MapEditorName> names = type switch
        {
            CustomMapSiteType.Merchant => library.Names.Merchants,
            CustomMapSiteType.MageTower => library.Names.MageTowers,
            CustomMapSiteType.Mercenaries => library.Names.MercenaryCamps,
            _ => library.Names.Trainers
        };
        MapEditorName name = PickName(names, map.Sites.Select(site => site.Name));
        CustomMap before = map.Clone();

        if (map.PlaceSite(map.CenterFootprint(tile, CustomMapSite.Size, CustomMapSite.Size), type, random.Next(CustomMapSite.GetVariantCount(type)), name.Name, name.Greeting).Error is { } error)
        {
            Game.ShowError(MapEditorLabels.ObjectError(error));
            return;
        }

        CommitArmyChange(before);
    }

    // Landmark tool: clicking places the landmark chosen in the palette centred on the tile; clicking a landmark
    // removes it.
    private void ClickLandmark(WorldPosition tile)
    {
        if (map.GetLandmarkAt(tile) is { } existing)
        {
            CommitRemoval(current => current.RemoveLandmark(existing.Id));
            return;
        }

        if (SelectedLandmark is not { } option)
        {
            return;
        }

        CustomMap before = map.Clone();
        WorldLandmarkDefinition definition = option.Definition;
        WorldPosition topLeft = map.CenterFootprint(tile, definition.Width, definition.Height);

        if (map.PlaceLandmark(topLeft, definition).Error is { } error)
        {
            Game.ShowError(MapEditorLabels.ObjectError(error));
            return;
        }

        CommitArmyChange(before);
    }

    // A name from the game's list that the map does not use yet.
    private MapEditorName PickName(IReadOnlyList<MapEditorName> names, IEnumerable<string> used)
    {
        HashSet<string> taken = new(used, StringComparer.OrdinalIgnoreCase);
        MapEditorName[] free = [.. names.Where(name => !taken.Contains(name.Name))];

        return free.Length > 0 ? free[random.Next(free.Length)] : names.Count > 0 ? names[random.Next(names.Count)] : new MapEditorName(string.Empty, string.Empty);
    }

    // Applies a change made in an object screen; returns the problem to show there, if any.
    private string? Commit(Func<CustomMap, CustomMapPlacementError?> change)
    {
        CustomMap before = map.Clone();

        if (change(map) is { } error)
        {
            return MapEditorLabels.ObjectError(error);
        }

        CommitArmyChange(before);
        return null;
    }

    private void CommitRemoval(Func<CustomMap, bool> removal)
    {
        CustomMap before = map.Clone();

        if (removal(map))
        {
            CommitArmyChange(before);
        }
    }

    // An empty army: placing it only checks the tile (it fails with NoLeader when the tile itself is fine).
    private static CustomMapUnit?[] ProbeArmy => new CustomMapUnit?[GridPosition.Count];

    private string? SaveArmy(WorldPosition tile, int? armyId, WorldRace owner, CustomMapUnit?[] slots)
    {
        CustomMap before = map.Clone();
        CustomMapStackError? error = armyId is { } id
            ? map.UpdateStack(id, owner, slots)
            : map.PlaceStack(tile, owner, slots).Error;

        if (error is { } problem)
        {
            return MapEditorLabels.StackError(problem, CustomMapStack.GetLeadership(slots));
        }

        CommitArmyChange(before);
        return null;
    }

    private void DeleteArmy(int armyId)
    {
        CustomMap before = map.Clone();

        if (map.RemoveStack(armyId))
        {
            CommitArmyChange(before);
        }
    }

    // Village tool: clicking free land places a village centred on the tile, clicking a village opens it.
    private void ClickVillage(WorldPosition tile)
    {
        if (map.GetVillageAt(tile) is { } existing)
        {
            OpenVillage(existing);
            return;
        }

        WorldPosition topLeft = map.CenterFootprint(tile, CustomMapVillage.Size, CustomMapVillage.Size);
        CustomMap before = map.Clone();

        if (map.PlaceVillage(topLeft, SelectedVillageLevel.Value, SelectedVillageOwner.Value, NextVillageName()).Error is { } error)
        {
            Game.ShowError(MapEditorLabels.VillageError(error));
            return;
        }

        CommitArmyChange(before);
    }

    // A name the map does not use yet, from the game's list of city names.
    private string NextVillageName()
    {
        return PickName(library.Names.Villages, map.Villages.Select(village => village.Name)).Name;
    }

    // The village screen: its settings, garrison and the army stationed in it.
    private void OpenVillage(CustomMapVillage village)
    {
        Game.OpenScreen(new MapEditorVillageScreenViewModel(
            Game,
            editorGame,
            units,
            VillageOwners,
            village,
            map.GetStationedStack(village)?.Slots,
            (level, owner, name, garrison, army) => SaveVillage(village.Id, level, owner, name, garrison, army),
            () => DeleteVillage(village.Id)));
    }

    private string? SaveVillage(int id, int level, WorldRace owner, string name, CustomMapUnit?[] garrison, CustomMapUnit?[]? army)
    {
        CustomMap before = map.Clone();

        if (map.UpdateVillage(id, level, owner, name, garrison) is { } error)
        {
            map = before;
            return MapEditorLabels.VillageError(error);
        }

        CustomMapVillage village = map.Villages.Single(candidate => candidate.Id == id);

        if (SaveStationedArmy(village.Entrance, owner, army) is { } armyError)
        {
            map = before;
            return armyError;
        }

        CommitArmyChange(before);
        return null;
    }

    // Puts the army on a village's or capital's entrance, changes the one there, or removes it (army null).
    private string? SaveStationedArmy(WorldPosition entrance, WorldRace owner, CustomMapUnit?[]? army)
    {
        CustomMapStack? stationed = map.GetStackAt(entrance);

        if (army is null)
        {
            if (stationed is not null)
            {
                map.RemoveStack(stationed.Id);
            }

            return null;
        }

        CustomMapStackError? error = stationed is not null
            ? map.UpdateStack(stationed.Id, owner, army)
            : map.PlaceStack(entrance, owner, army).Error;

        return error is { } problem ? MapEditorLabels.StackError(problem, CustomMapStack.GetLeadership(army)) : null;
    }

    // The capital screen: its garrison with the guardian and the army stationed in it (the race's first leader
    // until set here).
    private void OpenCapital(CustomMapPlayer capital)
    {
        (CustomMapUnit?[] defaultGarrison, CustomMapUnit?[] defaultArmy) = library.GetCapitalDefaults(capital.Race);
        IReadOnlyList<CustomMapUnit?> garrison = capital.Garrison.Any(unit => unit is not null) ? capital.Garrison : defaultGarrison;
        IReadOnlyList<CustomMapUnit?> army = map.GetStationedStack(capital)?.Slots ?? (IReadOnlyList<CustomMapUnit?>)defaultArmy;

        Game.OpenScreen(new MapEditorCapitalScreenViewModel(
            Game,
            editorGame,
            units,
            capital.Race,
            capital.Capital!.Value,
            garrison,
            army,
            (newGarrison, newArmy) => SaveCapital(capital.Race, newGarrison, newArmy)));
    }

    private string? SaveCapital(WorldRace race, CustomMapUnit?[] garrison, CustomMapUnit?[] army)
    {
        CustomMap before = map.Clone();

        if (map.UpdateCapital(race, garrison) is { } error)
        {
            map = before;
            return MapEditorLabels.VillageError(error);
        }

        CustomMapPlayer capital = map.Players.Single(player => player.Race == race);

        if (SaveStationedArmy(capital.Entrance!.Value, race, army) is { } armyError)
        {
            map = before;
            return armyError;
        }

        CommitArmyChange(before);
        return null;
    }

    private void DeleteVillage(int id)
    {
        CustomMap before = map.Clone();

        if (map.RemoveVillage(id))
        {
            CommitArmyChange(before);
        }
    }

    // Resource tool: clicking land places the chosen resource; clicking a resource of that type removes it, one of
    // another type changes to it.
    private void ClickResource(WorldPosition tile)
    {
        CustomMap before = map.Clone();

        if (map.GetResourceAt(tile) is { } existing && existing.Type == SelectedResourceType.Value)
        {
            map.RemoveResource(tile);
        }
        else if (map.PlaceResource(tile, SelectedResourceType.Value) is { } error)
        {
            Game.ShowError(MapEditorLabels.ResourceError(error));
            return;
        }

        CommitArmyChange(before);
    }

    private void CommitArmyChange(CustomMap before)
    {
        undoSteps.Push(before);
        TrimUndo();
        CanUndo = true;
        IsDirty = true;
        RebuildPreviewAsync().FileAndForget();
    }

    // Right-click info, as in the game: armies, capitals, villages, resources and tiles.
    public void ShowPositionInfo(WorldPosition tile)
    {
        WorldState shown = state;

        if (!shown.Map.IsValidPosition(tile))
        {
            PositionInfo = null;
            return;
        }

        WorldPlan plan = WorldPlan.For(shown);
        WorldVisibility visibility = WorldVisibility.Create(shown, ViewPlayerId, fogEnabled: false);

        try
        {
            // The editor shows capitals and villages as their owner sees them: garrison and stationed army,
            // with no spying needed.
            if (plan.GetFortificationAt(tile) is WorldCapital capital)
            {
                WorldVisibility ownerView = WorldVisibility.Create(shown, capital.OwnerId, fogEnabled: false);
                PositionInfo = WorldFortificationInfoViewModel.CreateCapital(
                    shown, capital.OwnerId, capital, ownerView.GetStackAt(plan, capital.Entrance, capital.OwnerId), editorGame.Definitions, editorGame.Texts, editorGame.Stats);
            }
            else if (plan.GetFortificationAt(tile) is WorldCity city)
            {
                WorldVisibility ownerView = WorldVisibility.Create(shown, city.OwnerId, fogEnabled: false);
                PositionInfo = WorldFortificationInfoViewModel.CreateCity(
                    shown, city.OwnerId, city, ownerView.GetStackAt(plan, city.Entrance, city.OwnerId), editorGame.Definitions, editorGame.Texts, editorGame.Stats);
            }
            else if (visibility.GetStackAt(plan, tile, ViewPlayerId) is { } stack)
            {
                PositionInfo = WorldStackInfoViewModel.Create(shown, ViewPlayerId, stack, editorGame.Definitions, editorGame.Texts, editorGame.Stats);
            }
            else
            {
                PositionInfo = WorldTileInfoViewModel.TryCreate(shown, ViewPlayerId, tile, visibility, GetResourceNodeInfo(tile, plan), editorGame.Definitions, editorGame.Texts, editorGame.Stats);
            }
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            // The preview is still being rebuilt for an object placed a moment ago.
            PositionInfo = null;
        }
    }

    // A resource pays whoever owns the land under it; the info shows that income.
    private WorldGetResourceNodeInfoCommand.Result? GetResourceNodeInfo(WorldPosition tile, WorldPlan plan)
    {
        if (plan.GetResourceNodeAt(tile) is null)
        {
            return null;
        }

        WorldPlayerId viewer = plan.GetLandOwner(tile) ?? ViewPlayerId;
        ICommandResult<WorldCommandError, WorldEvent, WorldGetResourceNodeInfoCommand.Result> result =
            editorGame.World.DryRunCommand(new WorldGetResourceNodeInfoCommand(viewer, tile));

        return result.Failed ? null : result.Data;
    }

    public void HidePositionInfo()
    {
        PositionInfo = null;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        LeaveSpreadPreview();

        if (undoSteps.Count == 0)
        {
            return;
        }

        CustomMap previous = undoSteps.Peek();
        bool capitalsChanged = !previous.Players.SequenceEqual(map.Players) ||
                               !previous.Stacks.SequenceEqual(map.Stacks) ||
                               !previous.Villages.SequenceEqual(map.Villages) ||
                               !previous.Resources.SequenceEqual(map.Resources) ||
                               !previous.Ruins.SequenceEqual(map.Ruins) ||
                               !previous.Bags.SequenceEqual(map.Bags) ||
                               !previous.Sites.SequenceEqual(map.Sites) ||
                               !previous.Landmarks.SequenceEqual(map.Landmarks);
        map = undoSteps.Pop();
        roadStart = null;

        CanUndo = undoSteps.Count > 0;
        IsDirty = true;

        if (capitalsChanged)
        {
            RebuildPreviewAsync().FileAndForget();
        }
        else
        {
            state = CustomMapStore.ApplyTerrain(state, map);
            RefreshPreview();
            UpdateStatus();
        }
    }

    [RelayCommand]
    private void Save()
    {
        TrySave();
    }

    private bool TrySave()
    {
        string name = Name.Trim();

        if (name.Length == 0)
        {
            Game.ShowError(Tr("Give the map a name."));
            return false;
        }

        IReadOnlyList<string> problems = map.Validate();

        if (problems.Count > 0)
        {
            Game.ShowError(string.Join(" ", problems.Select(TranslateProblem)));
            return false;
        }

        try
        {
            map.Name = name;
            store.Save(map);
        }
        catch (Exception ex)
        {
            Game.ShowError(Tr("Failed to save map: {0}", ex.Message));
            return false;
        }

        IsDirty = false;
        Status = Tr("Saved. Play it from New quest.");
        HasProblems = false;

        return true;
    }

    [RelayCommand]
    private void Play()
    {
        if (!TrySave())
        {
            return;
        }

        WorldRace race = map.Players[0].Race;
        ScenarioConfiguration configuration = new(
            [new ScenarioPlayerConfiguration(race, LordCategory.Warrior, new WorldObjectVariant(0), null, LeaderName: null)],
            WorldDifficulty.Average,
            Leader: null);
        GameConfiguration game = new(map.Id, configuration);

        if (Game.StartGameCommand.CanExecute(game))
        {
            Game.StartGameCommand.Execute(game);
        }
    }

    [RelayCommand]
    private void Back()
    {
        TryClose();
    }

    protected override bool TryClose()
    {
        if (!IsDirty)
        {
            Game.ShowMapEditorCommand.Execute(null);
            return true;
        }

        Game.OpenScreen(new ConfirmPromptViewModel(
            Tr("Leave the editor"),
            TrSpans("Discard unsaved changes to \"{0}\"?", Name),
            Tr("Discard"),
            ConfirmPromptType.Danger,
            () =>
            {
                Game.ShowMapEditorCommand.Execute(null);
                return Task.CompletedTask;
            }));

        return true;
    }

    private void UpdateStatus()
    {
        IReadOnlyList<string> problems = map.Validate();
        HasProblems = problems.Count > 0;
        Status = problems.Count > 0
            ? string.Join("\n", problems.Select(TranslateProblem))
            : Tr("Ready to save.");
    }

    private string TranslateProblem(string problem)
    {
        foreach (CustomMapPlayer player in map.Players)
        {
            if (player.Capital is null && problem.Contains(player.Race.ToString(), StringComparison.Ordinal))
            {
                return Tr("Place a capital for {0}.", MapEditorLabels.Race(player.Race));
            }
        }

        return problem;
    }

    private string FormatTile(WorldPosition tile)
    {
        WorldMapCell cell = map.GetCell(tile);
        string feature = cell.Feature switch
        {
            WorldMapFeature.Forest => Tr(", forest"),
            WorldMapFeature.Mountain => Tr(", mountain"),
            _ => string.Empty
        };

        string army = map.GetStackAt(tile) is { } stack
            ? Tr(" · army of {0}: {1}", stack.Owner == WorldRace.Neutral ? Tr("Neutral") : MapEditorLabels.Race(stack.Owner), GetUnitName(stack.Leader.TypeId))
            : string.Empty;
        string village = map.GetVillageAt(tile) is { } found
            ? Tr(" · village {0} ({1}, level {2})", found.Name, MapEditorLabels.Owner(found.Owner), found.Level)
            : string.Empty;
        string resource = map.GetResourceAt(tile) is { } source
            ? $" · {MapEditorLabels.Resource(source.Type)}"
            : string.Empty;
        string ruin = map.GetRuinAt(tile) is { } foundRuin
            ? Tr(" · ruin {0}", foundRuin.Name)
            : string.Empty;
        string site = map.GetSiteAt(tile) is { } foundSite
            ? $" · {MapEditorLabels.SiteType(foundSite.Type)} {foundSite.Name}"
            : string.Empty;
        string bag = map.GetBagAt(tile) is { } foundBag
            ? Tr(" · treasure ({0} items)", foundBag.Items.Length)
            : string.Empty;
        string landmark = map.GetLandmarkAt(tile) is { } foundLandmark
            ? $" · {allLandmarks.FirstOrDefault(option => option.Definition.Id == foundLandmark.Type)?.Name ?? foundLandmark.Type.Value}"
            : string.Empty;

        return $"{tile.X}, {tile.Y}: {MapEditorLabels.Terrain(cell.Terrain)}{feature}{army}{village}{resource}{ruin}{site}{bag}{landmark}";
    }

    private string GetUnitName(UnitDefinitionId typeId)
    {
        try
        {
            return editorGame.Texts.GetGlobalText(definitions.GetUnit(typeId).NameTextId).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return typeId.Value;
        }
    }

    public override void Dispose()
    {
        editorGame.Dispose();
        base.Dispose();
    }

    private void TrimUndo()
    {
        if (undoSteps.Count <= MaxUndoSteps)
        {
            return;
        }

        CustomMap[] kept = [.. undoSteps.Take(MaxUndoSteps)];
        undoSteps.Clear();

        foreach (CustomMap step in kept.Reverse())
        {
            undoSteps.Push(step);
        }
    }

    private WorldRenderRequest CreateRenderRequest()
    {
        WorldCursor cursor = WorldCursor.Empty with { HoverTile = hoverTile, HoverObject = PlacementPreview.HoverObject };

        // The road tool shows the road it would lay as real road pieces, joined with the roads already there.
        // The spread preview shows a later turn's land instead.
        WorldState shown = spreadState ?? (roadGhostTiles is { } ghost ? state with { Map = map.ToWorldMap(ghost) } : state);

        return new WorldRenderRequest(shown, ViewPlayerId, cursor, FogEnabled: false, Animation: null, IsPlayingEvent: false);
    }

    private void SetRenderRequest(WorldRenderRequest request)
    {
        renderRequest = request;
    }

    private static CustomMapBrush ToBrush(MapEditorTool tool)
    {
        return tool switch
        {
            MapEditorTool.Land => CustomMapBrush.Land,
            MapEditorTool.Water => CustomMapBrush.Water,
            MapEditorTool.Forest => CustomMapBrush.Forest,
            MapEditorTool.Mountain => CustomMapBrush.Mountain,
            _ => CustomMapBrush.Clear
        };
    }
}
