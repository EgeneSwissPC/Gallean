using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// The garrison grid shared by the village and ruin screens: soldiers only, at most Capacity units with large units
// counting twice. Empty slots open the unit list, units are rearranged by dragging them, and dragging a unit onto
// the Remove zone takes it out.
internal abstract partial class MapEditorGarrisonScreenViewModel
    : AbstractScreenViewModel, IUnitGridSelectionProvider, IDragDropProvider, IDragDropHost
{
    private readonly UnitGridSelectionViewModel selection;
    private CustomMapUnit?[] slots;

    protected MapEditorGarrisonScreenViewModel(GameViewModel game, GameModel editorGame, IReadOnlyList<UnitDefinition> units, IReadOnlyList<CustomMapUnit?> garrison)
    {
        Game = game;
        EditorGame = editorGame;
        Units = [.. units.Where(unit => unit.Leadership is not > 0)];
        slots = [.. garrison];

        selection = new UnitGridSelectionViewModel(editorGame, this);
        UnitGrid = new UnitGridViewModel(editorGame, selection, isFacingLeft: false);
        DragDrop = new DragDropViewModel(editorGame, this);
        DropZones = [RemoveDropZone];
    }

    public GameViewModel Game { get; }
    public GameModel EditorGame { get; }
    public IReadOnlyList<UnitDefinition> Units { get; }
    public UnitGridViewModel UnitGrid { get; }
    public DragDropViewModel DragDrop { get; }
    public DropZoneViewModel RemoveDropZone { get; } = new();
    public IReadOnlyList<DropZoneViewModel> DropZones { get; }
    public IReadOnlyList<CustomMapUnit?> Slots => slots;

    public override bool BlurBackdrop => true;

    // How many places the garrison has now.
    protected abstract int GarrisonCapacity { get; }

    // The race the unit list starts with.
    protected abstract WorldRace PickerRace { get; }

    [ObservableProperty]
    public partial string Capacity { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Problem { get; protected set; }

    [ObservableProperty]
    public partial bool IsValid { get; protected set; }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    public void AddUnit(GridPosition position)
    {
        Game.OpenScreen(new MapEditorUnitPickerViewModel(
            Game,
            EditorGame,
            Units,
            pickLeader: false,
            PickerRace,
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

    // Revalidates the screen; derived screens add their own checks in Validate.
    protected void Refresh()
    {
        UnitGrid.Update(BuildGrid(), showsPurchaseHints: true);

        CustomMapPlacementError? error = CustomMapVillage.ValidateUnits(slots, GarrisonCapacity);
        string? problem = error is { } garrisonError ? MapEditorLabels.VillageError(garrisonError) : Validate();

        Capacity = Tr("{0} / {1}", CustomMapStack.GetOccupiedLeadership(slots), GarrisonCapacity);
        Problem = problem;
        IsValid = problem is null;
        OnValidityChanged();
        UpdateRemoveDropZone(DragDrop.Dragged);
    }

    protected virtual string? Validate()
    {
        return null;
    }

    protected abstract void OnValidityChanged();

    private string? GetAddProblem(GridPosition position, CustomMapUnit unit)
    {
        if (CustomMapStack.TryPlaceUnit(slots, position, unit) is not { } placed)
        {
            return unit.IsLarge ? Tr("Requires two free slots") : Tr("This slot is taken");
        }

        return CustomMapStack.GetOccupiedLeadership(placed) > GarrisonCapacity
            ? Tr("Not enough room in the garrison")
            : null;
    }

    private void SetSlots(CustomMapUnit?[] value)
    {
        slots = value;
        Refresh();
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

    private void UpdateRemoveDropZone(IDragSourceReference? dragged)
    {
        if (dragged is UnitGridSlotReference slot && slots[CustomMapStack.GetUnitSlot(slots, slot.Position).ToIndex()] is { } unit)
        {
            RemoveDropZone.Show(Tr("Remove {0}", GetUnitName(unit)));
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
            return EditorGame.Texts.GetGlobalText(EditorGame.Definitions.GetUnit(unit.TypeId).NameTextId).Text;
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
