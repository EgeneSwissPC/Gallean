using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Builds one army, laid out like the game's party screen: the leader on the left, the 2 x 3 party grid on the
// right. Empty slots open the unit list (the leader list while the army has no leader), units are rearranged by
// dragging them, and dragging a unit onto the Remove zone takes it out.
internal sealed partial class MapEditorArmyScreenViewModel
    : AbstractScreenViewModel, IUnitGridSelectionProvider, IDragDropProvider, IDragDropHost
{
    private static readonly WorldStackId PreviewStackId = new("editor-army");

    private readonly GameModel editorGame;
    private readonly IReadOnlyList<UnitDefinition> units;
    private readonly Func<WorldRace, CustomMapUnit?[], string?> onConfirm;
    private readonly Action? onDelete;
    private readonly UnitGridSelectionViewModel selection;
    private CustomMapUnit?[] slots;

    public MapEditorArmyScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        WorldRace owner,
        WorldPosition position,
        IReadOnlyList<CustomMapUnit?>? existing,
        Func<WorldRace, CustomMapUnit?[], string?> onConfirm,
        Action? onDelete)
    {
        Game = game;
        this.editorGame = editorGame;
        this.units = units;
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Owner = owner;
        IsNew = existing is null;
        slots = existing is null ? new CustomMapUnit?[GridPosition.Count] : [.. existing];
        Title = IsNew
            ? Tr("New army of {0} at {1}, {2}", MapEditorLabels.Owner(owner), position.X, position.Y)
            : Tr("Army of {0} at {1}, {2}", MapEditorLabels.Owner(owner), position.X, position.Y);

        selection = new UnitGridSelectionViewModel(editorGame, this);
        UnitGrid = new UnitGridViewModel(editorGame, selection, isFacingLeft: false);
        DragDrop = new DragDropViewModel(editorGame, this);
        DropZones = [RemoveDropZone];

        Refresh();
    }

    public GameViewModel Game { get; }
    public WorldRace Owner { get; }
    public bool IsNew { get; }
    public bool CanDelete => !IsNew;
    public string Title { get; }
    public string ConfirmLabel => IsNew ? Tr("Place army") : Tr("Save army");
    public UnitGridViewModel UnitGrid { get; }
    public DragDropViewModel DragDrop { get; }
    public DropZoneViewModel RemoveDropZone { get; } = new();
    public IReadOnlyList<DropZoneViewModel> DropZones { get; }
    public IReadOnlyList<CustomMapUnit?> Slots => slots;

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasLeader))]
    public partial StackLeaderViewModel? Leader { get; private set; }

    public bool HasLeader => Leader is not null;

    [ObservableProperty]
    public partial string Leadership { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Problem { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool IsValid { get; private set; }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    // Opens the leader list; the chosen leader takes the old leader's place, or the front center for a new army.
    [RelayCommand]
    private void ChooseLeader()
    {
        GridPosition? current = FindLeaderSlot();

        Game.OpenScreen(new MapEditorUnitPickerViewModel(
            Game,
            editorGame,
            units,
            pickLeader: true,
            Owner,
            definition => PlaceLeader(slots, CustomMapUnit.From(definition), current) is null ? Tr("Needs a free row for a large leader") : null,
            definition =>
            {
                if (PlaceLeader(slots, CustomMapUnit.From(definition), current) is { } placed)
                {
                    SetSlots(placed);
                }
            }));
    }

    public void AddUnit(GridPosition position)
    {
        if (FindLeaderSlot() is null)
        {
            ChooseLeader();
            return;
        }

        Game.OpenScreen(new MapEditorUnitPickerViewModel(
            Game,
            editorGame,
            units,
            pickLeader: false,
            Owner,
            definition => GetAddProblem(position, CustomMapUnit.From(definition)),
            definition =>
            {
                if (CustomMapStack.TryPlaceUnit(slots, position, CustomMapUnit.From(definition)) is { } placed)
                {
                    SetSlots(placed);
                }
            }));
    }

    public void RemoveUnit(GridPosition position)
    {
        CustomMapUnit?[] result = [.. slots];
        result[CustomMapStack.GetUnitSlot(slots, position).ToIndex()] = null;
        SetSlots(result);
    }

    public void MoveUnit(GridPosition from, GridPosition to)
    {
        SetSlots(CustomMapStack.MoveUnit(slots, from, to));
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        if (onConfirm(Owner, [.. slots]) is { } error)
        {
            Game.ShowError(error);
            return;
        }

        CloseSelf();
    }

    [RelayCommand]
    private void Delete()
    {
        if (onDelete is null)
        {
            return;
        }

        CloseSelf();
        onDelete();
    }

    private string? GetAddProblem(GridPosition position, CustomMapUnit unit)
    {
        if (CustomMapStack.TryPlaceUnit(slots, position, unit) is not { } placed)
        {
            return unit.IsLarge ? Tr("Requires two free slots") : Tr("This slot is taken");
        }

        return CustomMapStack.GetOccupiedLeadership(placed) > CustomMapStack.GetLeadership(placed)
            ? Tr("Not enough leadership")
            : null;
    }

    private GridPosition? FindLeaderSlot()
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

    private void SetSlots(CustomMapUnit?[] value)
    {
        slots = value;
        Refresh();
    }

    private void Refresh()
    {
        UnitGrid<WorldUnit> grid = BuildGrid();
        UnitGrid.Update(grid, showsPurchaseHints: true);

        if (FindLeaderSlot() is not null)
        {
            WorldStack stack = BuildStack(grid);
            WorldPlayer owner = editorGame.Snapshot.WorldState.Players.GetValueOrDefault(stack.OwnerId)
                ?? editorGame.Snapshot.WorldState.Players.Values.First();
            Leader = StackLeaderViewModel.Create(stack, owner, editorGame.Definitions, editorGame.Texts, editorGame.Stats);
        }
        else
        {
            Leader = null;
        }

        int occupied = CustomMapStack.GetOccupiedLeadership(slots);
        int leadership = CustomMapStack.GetLeadership(slots);
        CustomMapStackError? error = CustomMapStack.ValidateSlots(slots);

        Leadership = Tr("{0} / {1}", occupied, leadership);
        Problem = error is { } problem ? MapEditorLabels.StackError(problem, leadership) : null;
        IsValid = error is null;
        UpdateRemoveDropZone(DragDrop.Dragged);
    }

    private UnitGrid<WorldUnit> BuildGrid()
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

    // A stand-in stack for the leader panel's stats.
    private WorldStack BuildStack(UnitGrid<WorldUnit> grid)
    {
        WorldState state = editorGame.Snapshot.WorldState;
        WorldPlayer? owner = state.Players.Values.FirstOrDefault(player => player.Race == Owner) ?? state.Players.Values.FirstOrDefault();
        WorldPlayerId ownerId = owner?.Id ?? new WorldPlayerId(1);
        WorldSubraceId subraceId = state.Subraces.Values.FirstOrDefault(subrace => subrace.PlayerId == ownerId)?.Id ?? new WorldSubraceId("editor");

        return new WorldStack(
            PreviewStackId,
            ownerId,
            new WorldPosition(0, 0),
            WorldStackDirection.South,
            grid,
            MovementPoints: slots.FirstOrDefault(unit => unit is { IsLeader: true })?.MovementPoints ?? 0,
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
    }

    private void UpdateRemoveDropZone(IDragSourceReference? dragged)
    {
        if (dragged is UnitGridSlotReference slot && slots[CustomMapStack.GetUnitSlot(slots, slot.Position).ToIndex()] is { } unit)
        {
            string name = GetUnitName(unit);
            RemoveDropZone.Show(Tr("Remove {0}", name));
        }
        else
        {
            RemoveDropZone.Hide();
        }
    }

    private string GetUnitName(CustomMapUnit unit)
    {
        try
        {
            return editorGame.Texts.GetGlobalText(editorGame.Definitions.GetUnit(unit.TypeId).NameTextId).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return unit.TypeId.Value;
        }
    }

    Task<bool> IUnitGridSelectionProvider.MoveUnitAsync(UnitGridSlotReference source, UnitGridSlotReference target)
    {
        MoveUnit(source.Position, target.Position);

        return Task.FromResult(true);
    }

    Task IUnitGridSelectionProvider.InvokeEmptySlotAsync(UnitGridSlotReference target)
    {
        AddUnit(target.Position);

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

            case (UnitGridSlotReference sourceSlot, DropZoneReference zone) when zone.Zone == RemoveDropZone:
                RemoveUnit(sourceSlot.Position);
                break;
        }
    }

    void IDragDropProvider.OnDragChanged()
    {
        UpdateRemoveDropZone(DragDrop.Dragged);
    }
}
