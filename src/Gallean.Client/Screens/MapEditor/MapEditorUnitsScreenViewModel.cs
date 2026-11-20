using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

internal enum MapEditorUnitGroupKind
{
    Army,
    Garrison,
    CapitalGarrison
}

// One unit grid of an editor screen: an army (one leader, units within its leadership) or a garrison (no leaders,
// at most Capacity places; a capital's also keeps its guardian). An optional army may stay empty: then there is none.
internal sealed partial class MapEditorUnitGroup : ObservableObject
{
    private readonly Func<int> capacity;

    public MapEditorUnitGroup(MapEditorUnitGroupKind kind, IReadOnlyList<CustomMapUnit?> slots, UnitGridViewModel grid, Func<int> capacity, Func<WorldRace> race, bool isOptional)
    {
        Kind = kind;
        Slots = [.. slots];
        UnitGrid = grid;
        this.capacity = capacity;
        Race = race;
        IsOptional = isOptional;
    }

    public MapEditorUnitGroupKind Kind { get; }
    public bool IsArmy => Kind == MapEditorUnitGroupKind.Army;
    public bool IsOptional { get; }
    public Func<WorldRace> Race { get; }
    public UnitGridViewModel UnitGrid { get; }
    public CustomMapUnit?[] Slots { get; set; }
    public int Capacity => IsArmy ? CustomMapStack.GetLeadership(Slots) : capacity();
    public bool IsEmpty => Slots.All(unit => unit is null);
    public bool HasLeaderUnit => Slots.Any(unit => unit is { IsLeader: true });

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeader))]
    public partial StackLeaderViewModel? Leader { get; set; }

    public bool HasLeader => Leader is not null;

    [ObservableProperty]
    public partial string CapacityText { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? Problem { get; set; }

    public string? Validate()
    {
        if (IsArmy)
        {
            if (IsOptional && IsEmpty)
            {
                return null;
            }

            return CustomMapStack.ValidateSlots(Slots) is { } stackError
                ? MapEditorLabels.StackError(stackError, CustomMapStack.GetLeadership(Slots))
                : null;
        }

        return CustomMapVillage.ValidateUnits(Slots, capacity(), Kind == MapEditorUnitGroupKind.CapitalGarrison) is { } garrisonError
            ? MapEditorLabels.VillageError(garrisonError)
            : null;
    }
}

// The unit grids of an editor screen (an army, a garrison, or a garrison with the army stationed with it) with the
// game's party controls: empty slots open the unit list, units are rearranged by dragging them (also from one grid to
// the other), dragging a unit onto the Remove zone takes it out, and the selected unit's level can be raised.
internal abstract partial class MapEditorUnitsScreenViewModel
    : AbstractScreenViewModel, IUnitGridSelectionProvider, IDragDropProvider, IDragDropHost
{
    private static readonly WorldStackId PreviewStackId = new("editor-army");

    private readonly List<MapEditorUnitGroup> groups = [];
    private readonly UnitGridSelectionViewModel selection;

    protected MapEditorUnitsScreenViewModel(GameViewModel game, GameModel editorGame, IReadOnlyList<UnitDefinition> units)
    {
        Game = game;
        EditorGame = editorGame;
        AllUnits = units;
        selection = new UnitGridSelectionViewModel(editorGame, this);
        DragDrop = new DragDropViewModel(editorGame, this);
        DropZones = [RemoveDropZone];
    }

    public GameViewModel Game { get; }
    public GameModel EditorGame { get; }
    public IReadOnlyList<UnitDefinition> AllUnits { get; }

    // Why a leader can't lead an army where this screen's army stands (a sea creature away from open water); shown
    // in the leader list. Army templates have no place yet, so every leader fits.
    public Func<UnitDefinition, string?> LeaderSiteProblem { get; init; } = _ => null;
    public IReadOnlyList<MapEditorUnitGroup> Groups => groups;
    public DragDropViewModel DragDrop { get; }
    public DropZoneViewModel RemoveDropZone { get; } = new();
    public IReadOnlyList<DropZoneViewModel> DropZones { get; }

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    public partial string? Problem { get; protected set; }

    [ObservableProperty]
    public partial bool IsValid { get; protected set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedUnit))]
    public partial string? SelectedUnitName { get; private set; }

    public bool HasSelectedUnit => SelectedUnitName is not null;

    [ObservableProperty]
    public partial string SelectedUnitLevel { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LevelDownCommand), nameof(LevelDownTenCommand))]
    public partial bool CanLevelDown { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LevelUpCommand), nameof(LevelUpTenCommand))]
    public partial bool CanLevelUp { get; private set; }

    protected MapEditorUnitGroup AddGroup(MapEditorUnitGroupKind kind, IReadOnlyList<CustomMapUnit?>? slots, Func<int> capacity, Func<WorldRace> race, bool isOptional = false)
    {
        MapEditorUnitGroup group = new(
            kind,
            slots ?? new CustomMapUnit?[GridPosition.Count],
            new UnitGridViewModel(EditorGame, selection, isFacingLeft: false),
            capacity,
            race,
            isOptional);
        groups.Add(group);

        return group;
    }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    // Opens the unit list for an empty slot; an army without a leader gets the leader list first.
    public void AddUnit(MapEditorUnitGroup group, GridPosition position)
    {
        if (group.IsArmy && !group.HasLeaderUnit)
        {
            ChooseLeader(group);
            return;
        }

        IReadOnlyList<UnitDefinition> candidates = group.IsArmy ? AllUnits : [.. AllUnits.Where(unit => unit.Leadership is not > 0)];

        Game.OpenScreen(new MapEditorUnitPickerViewModel(
            Game,
            EditorGame,
            candidates,
            pickLeader: false,
            group.Race(),
            definition => GetAddProblem(group, position, CustomMapUnit.From(definition)),
            definition =>
            {
                if (CustomMapStack.TryPlaceUnit(group.Slots, position, CustomMapUnit.From(definition)) is { } placed)
                {
                    group.Slots = placed;
                    Refresh();
                    Select(group, position);
                }
            }));
    }

    // Opens the leader list; the chosen leader takes the old leader's place, or the front center for a new army.
    public void ChooseLeader(MapEditorUnitGroup group)
    {
        GridPosition? current = FindLeaderSlot(group.Slots);

        Game.OpenScreen(new MapEditorUnitPickerViewModel(
            Game,
            EditorGame,
            AllUnits,
            pickLeader: true,
            group.Race(),
            definition => LeaderSiteProblem(definition) ?? (PlaceLeader(group.Slots, CustomMapUnit.From(definition), current) is null ? Tr("Needs a free row for a large leader") : null),
            definition =>
            {
                if (PlaceLeader(group.Slots, CustomMapUnit.From(definition), current) is { } placed)
                {
                    group.Slots = placed;
                    Refresh();

                    if (FindLeaderSlot(placed) is { } slot)
                    {
                        Select(group, slot);
                    }
                }
            }));
    }

    public void RemoveUnit(MapEditorUnitGroup group, GridPosition position)
    {
        GridPosition slot = CustomMapStack.GetUnitSlot(group.Slots, position);

        if (group.Slots[slot.ToIndex()] is null or { IsGuardian: true })
        {
            return;
        }

        CustomMapUnit?[] result = [.. group.Slots];
        result[slot.ToIndex()] = null;
        group.Slots = result;
        Refresh();
    }

    public void MoveUnit(MapEditorUnitGroup group, GridPosition from, GridPosition to)
    {
        group.Slots = CustomMapStack.MoveUnit(group.Slots, from, to);
        Refresh();
    }

    // Moves a unit into a free slot of another grid. Leaders only join an army without one; the guardian stays home.
    public bool MoveUnit(MapEditorUnitGroup source, GridPosition from, MapEditorUnitGroup target, GridPosition to)
    {
        GridPosition slot = CustomMapStack.GetUnitSlot(source.Slots, from);

        if (source.Slots[slot.ToIndex()] is not { } unit || unit.IsGuardian ||
            (unit.IsLeader && (!target.IsArmy || target.HasLeaderUnit)) ||
            CustomMapStack.TryPlaceUnit(target.Slots, to, unit) is not { } placed)
        {
            return false;
        }

        CustomMapUnit?[] remaining = [.. source.Slots];
        remaining[slot.ToIndex()] = null;
        source.Slots = remaining;
        target.Slots = placed;
        Refresh();

        return true;
    }

    // Updates the grids and checks every group; derived screens add their own checks in Validate.
    protected void Refresh()
    {
        string? problem = null;

        foreach (MapEditorUnitGroup group in groups)
        {
            UnitGrid<WorldUnit> grid = BuildGrid(group.Slots);
            group.UnitGrid.Update(grid, showsPurchaseHints: true);
            group.Leader = group.IsArmy && group.HasLeaderUnit ? CreateLeader(grid, group.Race(), group.Slots.First(unit => unit is { IsLeader: true })!.MovementPoints) : null;
            group.CapacityText = Tr("{0} / {1}", CustomMapStack.GetOccupiedLeadership(group.Slots), group.Capacity);
            group.Problem = group.Validate();
            problem ??= group.Problem;
        }

        problem ??= Validate();
        Problem = problem;
        IsValid = problem is null;
        OnRefreshed();
        UpdateRemoveDropZone(DragDrop.Dragged);
        UpdateSelectedUnit();
    }

    protected virtual string? Validate()
    {
        return null;
    }

    // Called after every change, once the groups are up to date.
    protected abstract void OnRefreshed();

    [RelayCommand(CanExecute = nameof(CanLevelDown))]
    private void LevelDown()
    {
        ChangeLevel(-1);
    }

    [RelayCommand(CanExecute = nameof(CanLevelUp))]
    private void LevelUp()
    {
        ChangeLevel(1);
    }

    [RelayCommand(CanExecute = nameof(CanLevelDown))]
    private void LevelDownTen()
    {
        ChangeLevel(-10);
    }

    [RelayCommand(CanExecute = nameof(CanLevelUp))]
    private void LevelUpTen()
    {
        ChangeLevel(10);
    }

    // Sets the selected unit's level (from the unit's own level up to CustomMapUnit.MaxLevel).
    public void SetSelectedLevel(int level)
    {
        if (FindSelected() is not ({ } group, { } slot) || group.Slots[slot.ToIndex()] is not { } unit ||
            EditorGame.Definitions.TryGetUnit(unit.TypeId) is not { } definition)
        {
            return;
        }

        CustomMapUnit?[] result = [.. group.Slots];
        result[slot.ToIndex()] = CustomMapUnit.From(definition, level, EditorGame.Definitions);
        group.Slots = result;
        Refresh();
    }

    private void ChangeLevel(int delta)
    {
        if (FindSelected() is ({ } group, { } slot) && group.Slots[slot.ToIndex()] is { } unit)
        {
            SetSelectedLevel(unit.Level + delta);
        }
    }

    private void Select(MapEditorUnitGroup group, GridPosition position)
    {
        if (group.UnitGrid.Slots.FirstOrDefault(slot => slot.Position == position) is { HasUnit: true } slotViewModel)
        {
            selection.SelectSlotAsync(slotViewModel).FileAndForget();
        }
    }

    private (MapEditorUnitGroup? Group, GridPosition? Slot) FindSelected()
    {
        if (selection.Selected is not { } reference || groups.FirstOrDefault(group => group.UnitGrid == reference.Grid) is not { } group)
        {
            return (null, null);
        }

        GridPosition slot = CustomMapStack.GetUnitSlot(group.Slots, reference.Position);

        return group.Slots[slot.ToIndex()] is null ? (null, null) : (group, slot);
    }

    private void UpdateSelectedUnit()
    {
        if (FindSelected() is ({ } group, { } slot) && group.Slots[slot.ToIndex()] is { } unit)
        {
            int baseLevel = EditorGame.Definitions.TryGetUnit(unit.TypeId)?.Level ?? 1;

            SelectedUnitName = GetUnitName(unit);
            SelectedUnitLevel = Tr("Level {0}", unit.Level);
            CanLevelDown = unit.Level > baseLevel;
            CanLevelUp = unit.Level < CustomMapUnit.MaxLevel;
        }
        else
        {
            SelectedUnitName = null;
            SelectedUnitLevel = string.Empty;
            CanLevelDown = false;
            CanLevelUp = false;
        }
    }

    private string? GetAddProblem(MapEditorUnitGroup group, GridPosition position, CustomMapUnit unit)
    {
        if (CustomMapStack.TryPlaceUnit(group.Slots, position, unit) is not { } placed)
        {
            return unit.IsLarge ? Tr("Requires two free slots") : Tr("This slot is taken");
        }

        if (group.IsArmy)
        {
            return CustomMapStack.GetOccupiedLeadership(placed) > CustomMapStack.GetLeadership(placed) ? Tr("Not enough leadership") : null;
        }

        return CustomMapStack.GetOccupiedLeadership(placed) > group.Capacity ? Tr("Not enough room in the garrison") : null;
    }

    private static GridPosition? FindLeaderSlot(IReadOnlyList<CustomMapUnit?> slots)
    {
        foreach (GridPosition position in GridPosition.All)
        {
            if (slots[position.ToIndex()] is { IsLeader: true })
            {
                return position;
            }
        }

        return null;
    }

    private static CustomMapUnit?[]? PlaceLeader(IReadOnlyList<CustomMapUnit?> slots, CustomMapUnit leader, GridPosition? current)
    {
        CustomMapUnit?[] cleared = [.. slots];

        if (current is { } position)
        {
            cleared[position.ToIndex()] = null;
        }

        IEnumerable<GridPosition> candidates = current is { } old
            ? [old, GridPosition.FrontCenter, .. GridPosition.All]
            : [GridPosition.FrontCenter, .. GridPosition.All];

        foreach (GridPosition candidate in candidates)
        {
            if (CustomMapStack.TryPlaceUnit(cleared, candidate, leader) is { } placed)
            {
                return placed;
            }
        }

        return null;
    }

    private static UnitGrid<WorldUnit> BuildGrid(IReadOnlyList<CustomMapUnit?> slots)
    {
        UnitGrid<WorldUnit>.Builder builder = new();

        foreach (GridPosition position in GridPosition.All)
        {
            if (slots[position.ToIndex()] is { } unit)
            {
                WorldUnit worldUnit = new(new WorldUnitId($"editor-unit-{position.ToIndex()}"), unit.TypeId, unit.Hitpoints, Experience: 0, unit.Level, [], null, false);
                builder.Place(worldUnit, position, unit.IsLarge);
            }
        }

        return builder.Build();
    }

    // The leader panel, built on a stand-in stack of the army's owner.
    private StackLeaderViewModel CreateLeader(UnitGrid<WorldUnit> grid, WorldRace race, int movementPoints)
    {
        WorldState state = EditorGame.Snapshot.WorldState;
        WorldPlayer owner = state.Players.Values.FirstOrDefault(player => player.Race == race) ?? state.Players.Values.First();
        WorldSubraceId subraceId = state.Subraces.Values.FirstOrDefault(subrace => subrace.PlayerId == owner.Id)?.Id ?? new WorldSubraceId("editor");
        WorldStack stack = new(
            PreviewStackId,
            owner.Id,
            new WorldPosition(0, 0),
            WorldStackDirection.South,
            grid,
            movementPoints,
            WorldInventory.Empty,
            LeaderEquipment.Empty,
            subraceId,
            AiPriority: 3,
            AiIgnore: false,
            Concealed: false,
            WorldStackOrder.Normal.Instance,
            ExpirationTurn: null,
            PendingLeaderUpgrades: 0,
            BattlesWon: 0,
            SourceTemplateId: null);

        return StackLeaderViewModel.Create(stack, owner, EditorGame.Definitions, EditorGame.Texts, EditorGame.Stats);
    }

    private MapEditorUnitGroup? FindGroup(UnitGridViewModel grid)
    {
        return groups.FirstOrDefault(group => group.UnitGrid == grid);
    }

    private void UpdateRemoveDropZone(IDragSourceReference? dragged)
    {
        if (dragged is UnitGridSlotReference reference && FindGroup(reference.Grid) is { } group &&
            group.Slots[CustomMapStack.GetUnitSlot(group.Slots, reference.Position).ToIndex()] is { IsGuardian: false } unit)
        {
            RemoveDropZone.Show(Tr("Remove {0}", GetUnitName(unit)));
        }
        else
        {
            RemoveDropZone.Hide();
        }
    }

    protected string GetUnitName(CustomMapUnit unit)
    {
        try
        {
            return EditorGame.Texts.GetGlobalText(EditorGame.Definitions.GetUnit(unit.TypeId).NameTextId).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return unit.TypeId.Value;
        }
    }

    Task<bool> IUnitGridSelectionProvider.MoveUnitAsync(UnitGridSlotReference source, UnitGridSlotReference target)
    {
        if (FindGroup(source.Grid) is not { } from || FindGroup(target.Grid) is not { } to)
        {
            return Task.FromResult(false);
        }

        if (from == to)
        {
            MoveUnit(from, source.Position, target.Position);
            return Task.FromResult(true);
        }

        return Task.FromResult(MoveUnit(from, source.Position, to, target.Position));
    }

    Task IUnitGridSelectionProvider.InvokeEmptySlotAsync(UnitGridSlotReference target)
    {
        if (FindGroup(target.Grid) is { } group)
        {
            AddUnit(group, target.Position);
        }

        return Task.CompletedTask;
    }

    void IUnitGridSelectionProvider.OnUnitSelectionChanged()
    {
        Refresh();
    }

    void IUnitGridSelectionProvider.OnUnitMoved()
    {
        Refresh();
    }

    async Task IDragDropProvider.DropAsync(IDragSourceReference source, IDragTargetReference target)
    {
        switch (source, target)
        {
            case (UnitGridSlotReference sourceSlot, UnitGridSlotReference targetSlot):
                await selection.DropUnitAsync(sourceSlot, targetSlot);
                break;

            case (UnitGridSlotReference sourceSlot, DropZoneReference zone) when zone.Zone == RemoveDropZone && FindGroup(sourceSlot.Grid) is { } group:
                RemoveUnit(group, sourceSlot.Position);
                break;
        }
    }

    void IDragDropProvider.OnDragChanged()
    {
        UpdateRemoveDropZone(DragDrop.Dragged);
    }
}
