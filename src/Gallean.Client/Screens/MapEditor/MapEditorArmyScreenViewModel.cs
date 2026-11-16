using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Builds one army, laid out like the game's party screen: the leader on the left, the 2 x 3 party grid on the
// right. Empty slots open the unit list (the leader list while the army has no leader), units are rearranged by
// dragging them, dragging a unit onto the Remove zone takes it out, and a selected unit's level can be raised.
internal sealed partial class MapEditorArmyScreenViewModel : MapEditorUnitsScreenViewModel
{
    private readonly Func<WorldRace, CustomMapUnit?[], string?> onConfirm;
    private readonly Action? onDelete;

    public MapEditorArmyScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        IReadOnlyList<UnitDefinition> units,
        WorldRace owner,
        WorldPosition position,
        IReadOnlyList<CustomMapUnit?>? existing,
        Func<WorldRace, CustomMapUnit?[], string?> onConfirm,
        Action? onDelete)
        : base(game, editorGame, units)
    {
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Owner = owner;
        IsNew = existing is null;
        Title = IsNew
            ? Tr("New army of {0} at {1}, {2}", MapEditorLabels.Owner(owner), position.X, position.Y)
            : Tr("Army of {0} at {1}, {2}", MapEditorLabels.Owner(owner), position.X, position.Y);
        Army = AddGroup(MapEditorUnitGroupKind.Army, existing, () => 0, () => Owner);

        Refresh();
    }

    public WorldRace Owner { get; }
    public bool IsNew { get; }
    public bool CanDelete => !IsNew;
    public string Title { get; }
    public string ConfirmLabel => IsNew ? Tr("Place army") : Tr("Save army");
    public MapEditorUnitGroup Army { get; }
    public UnitGridViewModel UnitGrid => Army.UnitGrid;
    public IReadOnlyList<CustomMapUnit?> Slots => Army.Slots;
    public StackLeaderViewModel? Leader => Army.Leader;
    public bool HasLeader => Army.HasLeader;
    public string Leadership => Army.CapacityText;

    [RelayCommand]
    private void ChooseLeader()
    {
        ChooseLeader(Army);
    }

    public void AddUnit(GridPosition position)
    {
        AddUnit(Army, position);
    }

    public void RemoveUnit(GridPosition position)
    {
        RemoveUnit(Army, position);
    }

    public void MoveUnit(GridPosition from, GridPosition to)
    {
        MoveUnit(Army, from, to);
    }

    protected override void OnRefreshed()
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        if (onConfirm(Owner, [.. Army.Slots]) is { } error)
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
}
