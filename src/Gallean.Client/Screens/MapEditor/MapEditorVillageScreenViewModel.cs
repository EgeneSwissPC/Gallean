using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Sets up one village: name, owner, level and garrison. Laid out like the army screen, with the village picture in
// place of the leader. Empty slots open the unit list, units are rearranged by dragging them, and dragging a unit
// onto the Remove zone takes it out. A village of level n holds n units; a large unit counts twice.
internal sealed partial class MapEditorVillageScreenViewModel
    : AbstractScreenViewModel, IUnitGridSelectionProvider, IDragDropProvider, IDragDropHost
{
    public const int MaxNameLength = 40;

    private readonly GameModel editorGame;
    private readonly IReadOnlyList<UnitDefinition> units;
    private readonly Func<int, WorldRace, string, CustomMapUnit?[], string?> onConfirm;
    private readonly Action onDelete;
    private readonly UnitGridSelectionViewModel selection;
    private CustomMapUnit?[] slots;

    public MapEditorVillageScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        IReadOnlyList<MapEditorOption<WorldRace>> owners,
        CustomMapVillage village,
        Func<int, WorldRace, string, CustomMapUnit?[], string?> onConfirm,
        Action onDelete)
    {
        Game = game;
        this.editorGame = editorGame;
        this.units = [.. units.Where(unit => unit.Leadership is not > 0)];
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        slots = [.. village.Garrison];
        Title = Tr("Village at {0}, {1}", village.TopLeft.X, village.TopLeft.Y);
        Owners = owners;
        Levels = MapEditorLabels.VillageLevels();
        Name = village.Name;
        SelectedOwner = owners.FirstOrDefault(option => option.Value == village.Owner) ?? owners[0];
        SelectedLevel = Levels.FirstOrDefault(option => option.Value == village.Level) ?? Levels[0];

        selection = new UnitGridSelectionViewModel(editorGame, this);
        UnitGrid = new UnitGridViewModel(editorGame, selection, isFacingLeft: false);
        DragDrop = new DragDropViewModel(editorGame, this);
        DropZones = [RemoveDropZone];

        Refresh();
    }

    public GameViewModel Game { get; }
    public string Title { get; }
    public IReadOnlyList<MapEditorOption<WorldRace>> Owners { get; }
    public IReadOnlyList<MapEditorOption<int>> Levels { get; }
    public int NameLength => MaxNameLength;
    public UnitGridViewModel UnitGrid { get; }
    public DragDropViewModel DragDrop { get; }
    public DropZoneViewModel RemoveDropZone { get; } = new();
    public IReadOnlyList<DropZoneViewModel> DropZones { get; }
    public IReadOnlyList<CustomMapUnit?> Slots => slots;
    public AssetSpriteKey ImageKey => new AssetSpriteKey.CityImage(SelectedLevel.Value);

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedOwner { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ImageKey))]
    public partial MapEditorOption<int> SelectedLevel { get; set; }

    [ObservableProperty]
    public partial string Capacity { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string? Problem { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
    public partial bool IsValid { get; private set; }

    partial void OnSelectedLevelChanged(MapEditorOption<int> value)
    {
        if (UnitGrid is not null)
        {
            Refresh();
        }
    }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    public void AddUnit(GridPosition position)
    {
        Game.OpenScreen(new MapEditorUnitPickerViewModel(
            Game,
            editorGame,
            units,
            pickLeader: false,
            SelectedOwner.Value,
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
        if (onConfirm(SelectedLevel.Value, SelectedOwner.Value, Name.Trim(), [.. slots]) is { } error)
        {
            Game.ShowError(error);
            return;
        }

        CloseSelf();
    }

    [RelayCommand]
    private void Delete()
    {
        CloseSelf();
        onDelete();
    }

    private string? GetAddProblem(GridPosition position, CustomMapUnit unit)
    {
        if (CustomMapStack.TryPlaceUnit(slots, position, unit) is not { } placed)
        {
            return unit.IsLarge ? Tr("Requires two free slots") : Tr("This slot is taken");
        }

        return CustomMapStack.GetOccupiedLeadership(placed) > SelectedLevel.Value
            ? Tr("Not enough room in the garrison")
            : null;
    }

    private void SetSlots(CustomMapUnit?[] value)
    {
        slots = value;
        Refresh();
    }

    private void Refresh()
    {
        UnitGrid.Update(BuildGrid(), showsPurchaseHints: true);

        CustomMapPlacementError? error = CustomMapVillage.ValidateGarrison(slots, SelectedLevel.Value);

        Capacity = Tr("{0} / {1}", CustomMapStack.GetOccupiedLeadership(slots), SelectedLevel.Value);
        Problem = error is { } problem ? MapEditorLabels.VillageError(problem) : null;
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
