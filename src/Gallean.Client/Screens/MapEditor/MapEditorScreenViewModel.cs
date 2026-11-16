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
    Army
}

// Paints a CustomMap and shows it through the game's world renderer. Terrain strokes update the preview state
// directly; placing a capital rebuilds the preview through the scenario loader.
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

    public MapEditorScreenViewModel(
        GameViewModel game,
        CustomMapStore store,
        GameModel editorGame,
        CustomMap map,
        IReadOnlyList<UnitDefinition> units)
    {
        Game = game;
        this.store = store;
        this.editorGame = editorGame;
        this.units = units;
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
            new MapEditorOption<MapEditorTool>(MapEditorTool.Army, Tr("Army"))
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
        ArmyOwners =
        [
            new MapEditorOption<WorldRace>(WorldRace.Neutral, Tr("Neutral")),
            .. map.Players.Select(player => new MapEditorOption<WorldRace>(player.Race, MapEditorLabels.Race(player.Race)))
        ];
        SelectedArmyOwner = ArmyOwners[0];
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
    [NotifyPropertyChangedFor(nameof(IsLandTool), nameof(IsCapitalTool), nameof(IsArmyTool), nameof(HasBrush))]
    public partial MapEditorOption<MapEditorTool> SelectedTool { get; set; }

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
    public bool HasBrush => SelectedTool.Value is not (MapEditorTool.Capital or MapEditorTool.Army);

    partial void OnSelectedToolChanged(MapEditorOption<MapEditorTool> value)
    {
        renderRequest = CreateRenderRequest();
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
        HoverText = tile is { } current ? FormatTile(current) : string.Empty;
        SetRenderRequest(CreateRenderRequest());
    }

    public void BeginStroke(WorldPosition tile)
    {
        if (!map.IsValidPosition(tile))
        {
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Capital)
        {
            PlaceCapitalAsync(tile).FileAndForget();
            return;
        }

        if (SelectedTool.Value == MapEditorTool.Army)
        {
            ClickArmy(tile);
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
        if (!HasBrush || lastStrokeTile == tile || !map.IsValidPosition(tile))
        {
            return;
        }

        lastStrokeTile = tile;

        if (map.Paint(tile, BrushRadius, ToBrush(SelectedTool.Value), SelectedLandTerrain.Value, random))
        {
            strokeChanged = true;
            IsDirty = true;
            state = CustomMapStore.ApplyTerrain(state, map);
            SetRenderRequest(CreateRenderRequest());
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
        int offset = CustomMap.CapitalSize / 2;
        WorldPosition topLeft = new(
            Math.Clamp(tile.X - offset, 0, map.Size - CustomMap.CapitalSize),
            Math.Clamp(tile.Y - offset, 0, map.Size - CustomMap.CapitalSize));
        CustomMap before = map.Clone();

        if (!map.PlaceCapital(SelectedCapitalRace.Value, topLeft))
        {
            Game.ShowError(Tr("A capital cannot overlap another capital or an army."));
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
        SetRenderRequest(CreateRenderRequest());
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
    private void ClickArmy(WorldPosition tile)
    {
        CustomMapStack? existing = map.GetStackAt(tile);

        if (existing is null && map.PlaceStack(tile, SelectedArmyOwner.Value, ProbeArmy).Error is { } blocked and not CustomMapStackError.NoLeader)
        {
            Game.ShowError(MapEditorLabels.StackError(blocked, 0));
            return;
        }

        Game.OpenScreen(new MapEditorArmyScreenViewModel(
            Game,
            editorGame,
            units,
            existing?.Owner ?? SelectedArmyOwner.Value,
            tile,
            existing?.Slots,
            (owner, slots) => SaveArmy(tile, existing?.Id, owner, slots),
            existing is null ? null : () => DeleteArmy(existing.Id)));
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

    private void CommitArmyChange(CustomMap before)
    {
        undoSteps.Push(before);
        TrimUndo();
        CanUndo = true;
        IsDirty = true;
        HoverText = hoverTile is { } tile ? FormatTile(tile) : string.Empty;
        RebuildPreviewAsync().FileAndForget();
    }

    // Right-click info, as in the game: armies, capitals and tiles.
    public void ShowPositionInfo(WorldPosition tile)
    {
        if (!state.Map.IsValidPosition(tile))
        {
            PositionInfo = null;
            return;
        }

        WorldPlan plan = WorldPlan.For(state);
        WorldVisibility visibility = WorldVisibility.Create(state, ViewPlayerId, fogEnabled: false);

        if (plan.GetFortificationAt(tile) is WorldCapital capital)
        {
            PositionInfo = WorldFortificationInfoViewModel.CreateCapital(
                state, ViewPlayerId, capital, visibility.GetStackAt(plan, capital.Entrance, ViewPlayerId), editorGame.Definitions, editorGame.Texts, editorGame.Stats);
        }
        else if (visibility.GetStackAt(plan, tile, ViewPlayerId) is { } stack)
        {
            PositionInfo = WorldStackInfoViewModel.Create(state, ViewPlayerId, stack, editorGame.Definitions, editorGame.Texts, editorGame.Stats);
        }
        else
        {
            PositionInfo = WorldTileInfoViewModel.TryCreate(state, ViewPlayerId, tile, visibility, null, editorGame.Definitions, editorGame.Texts, editorGame.Stats);
        }
    }

    public void HidePositionInfo()
    {
        PositionInfo = null;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        if (undoSteps.Count == 0)
        {
            return;
        }

        CustomMap previous = undoSteps.Peek();
        bool capitalsChanged = !previous.Players.SequenceEqual(map.Players) || !previous.Stacks.SequenceEqual(map.Stacks);
        map = undoSteps.Pop();


        CanUndo = undoSteps.Count > 0;
        IsDirty = true;

        if (capitalsChanged)
        {
            RebuildPreviewAsync().FileAndForget();
        }
        else
        {
            state = CustomMapStore.ApplyTerrain(state, map);
            SetRenderRequest(CreateRenderRequest());
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

        return $"{tile.X}, {tile.Y}: {MapEditorLabels.Terrain(cell.Terrain)}{feature}{army}";
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
        WorldCursor cursor = WorldCursor.Empty with { HoverTile = hoverTile };

        return new WorldRenderRequest(state, ViewPlayerId, cursor, FogEnabled: false, Animation: null, IsPlayingEvent: false);
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
