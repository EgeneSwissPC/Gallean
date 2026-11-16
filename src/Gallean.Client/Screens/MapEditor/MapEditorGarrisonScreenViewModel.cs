using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// A screen with a garrison grid, shared by the village and ruin screens: soldiers only, at most GarrisonCapacity
// units with large units counting twice.
internal abstract partial class MapEditorGarrisonScreenViewModel : MapEditorUnitsScreenViewModel
{
    protected MapEditorGarrisonScreenViewModel(GameViewModel game, GameModel editorGame, IReadOnlyList<UnitDefinition> units, IReadOnlyList<CustomMapUnit?> garrison)
        : base(game, editorGame, units)
    {
        Units = [.. units.Where(unit => unit.Leadership is not > 0)];
        Garrison = AddGroup(MapEditorUnitGroupKind.Garrison, garrison, () => GarrisonCapacity, () => PickerRace);
    }

    public IReadOnlyList<UnitDefinition> Units { get; }
    public MapEditorUnitGroup Garrison { get; }
    public UnitGridViewModel UnitGrid => Garrison.UnitGrid;
    public IReadOnlyList<CustomMapUnit?> Slots => Garrison.Slots;

    // How many places the garrison has now.
    protected abstract int GarrisonCapacity { get; }

    // The race the unit list starts with.
    protected abstract WorldRace PickerRace { get; }

    [ObservableProperty]
    public partial string Capacity { get; private set; } = string.Empty;

    public void AddUnit(GridPosition position)
    {
        AddUnit(Garrison, position);
    }

    public void RemoveUnit(GridPosition position)
    {
        RemoveUnit(Garrison, position);
    }

    public void MoveUnit(GridPosition from, GridPosition to)
    {
        MoveUnit(Garrison, from, to);
    }

    protected override void OnRefreshed()
    {
        Capacity = Garrison.CapacityText;
        OnValidityChanged();
    }

    protected abstract void OnValidityChanged();
}
