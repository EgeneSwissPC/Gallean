using CommunityToolkit.Mvvm.ComponentModel;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed record MapEditorUnitOption(CustomMapUnit? Unit, WorldRace Race, string Name, string Label) : MapEditorLabeled(Label)
{
    public static IReadOnlyList<MapEditorUnitOption> Create(IEnumerable<UnitDefinition> definitions, IGameTextProvider texts)
    {
        List<MapEditorUnitOption> result = [];

        foreach (UnitDefinition definition in definitions)
        {
            string name;

            try
            {
                name = texts.GetGlobalText(definition.NameTextId).Text;
            }
            catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
            {
                name = definition.Id.Value;
            }

            CustomMapUnit unit = CustomMapUnit.From(definition);
            string size = unit.IsLarge ? Tr(", large") : string.Empty;
            string label = unit.IsLeader
                ? Tr("{0} (leader {1}, level {2}{3})", name, unit.Leadership, unit.Level, size)
                : Tr("{0} (level {1}{2})", name, unit.Level, size);

            result.Add(new MapEditorUnitOption(unit, definition.Race, name, label));
        }

        return
        [
            .. result
                .OrderBy(option => option.Unit!.IsLeader ? 0 : 1)
                .ThenBy(option => option.Race)
                .ThenBy(option => option.Unit!.Level)
                .ThenBy(option => option.Name, StringComparer.CurrentCulture)
        ];
    }
}

internal sealed partial class MapEditorSlotViewModel(GridPosition position, string label) : ObservableObject
{
    public GridPosition Position { get; } = position;
    public string Label { get; } = label;

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorUnitOption> Options { get; set; } = [];

    [ObservableProperty]
    public partial MapEditorUnitOption? SelectedOption { get; set; }

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = true;
}

// The army being placed or edited: owner and a 2 x 3 unit grid, checked with the game's stack rules.
internal sealed partial class MapEditorArmyViewModel : ObservableObject
{
    private readonly IReadOnlyList<MapEditorUnitOption> units;
    private readonly MapEditorUnitOption empty;
    private bool updating;

    public MapEditorArmyViewModel(IReadOnlyList<MapEditorUnitOption> units, IEnumerable<WorldRace> playerRaces)
    {
        this.units = units;
        empty = new MapEditorUnitOption(null, WorldRace.Neutral, string.Empty, Tr("(empty)"));
        Owners =
        [
            new MapEditorOption<WorldRace>(WorldRace.Neutral, Tr("Neutral")),
            .. playerRaces.Select(race => new MapEditorOption<WorldRace>(race, MapEditorLabels.Race(race)))
        ];

        // Display order: rows top to bottom, front line on the left.
        Slots =
        [
            new MapEditorSlotViewModel(GridPosition.FrontTop, Tr("Front, top")),
            new MapEditorSlotViewModel(GridPosition.BackTop, Tr("Back, top")),
            new MapEditorSlotViewModel(GridPosition.FrontCenter, Tr("Front, center")),
            new MapEditorSlotViewModel(GridPosition.BackCenter, Tr("Back, center")),
            new MapEditorSlotViewModel(GridPosition.FrontBottom, Tr("Front, bottom")),
            new MapEditorSlotViewModel(GridPosition.BackBottom, Tr("Back, bottom"))
        ];

        foreach (MapEditorSlotViewModel slot in Slots)
        {
            slot.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MapEditorSlotViewModel.SelectedOption))
                {
                    OnSlotsChanged();
                }
            };
        }

        SelectedOwner = Owners[0];
    }

    public IReadOnlyList<MapEditorOption<WorldRace>> Owners { get; }
    public IReadOnlyList<MapEditorSlotViewModel> Slots { get; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedOwner { get; set; }

    [ObservableProperty]
    public partial string Status { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial bool IsValid { get; private set; }

    public event Action? Changed;

    public WorldRace Owner => SelectedOwner.Value;

    public CustomMapUnit?[] GetSlots()
    {
        CustomMapUnit?[] result = new CustomMapUnit?[GridPosition.Count];

        foreach (MapEditorSlotViewModel slot in Slots)
        {
            result[slot.Position.ToIndex()] = slot.IsEnabled ? slot.SelectedOption?.Unit : null;
        }

        return result;
    }

    public void Load(CustomMapStack stack)
    {
        updating = true;

        try
        {
            SelectedOwner = Owners.FirstOrDefault(owner => owner.Value == stack.Owner) ?? Owners[0];

            foreach (MapEditorSlotViewModel slot in Slots)
            {
                CustomMapUnit? unit = stack.Slots[slot.Position.ToIndex()];
                slot.SelectedOption = unit is null ? empty : FindOption(slot, unit);
            }
        }
        finally
        {
            updating = false;
        }

        OnSlotsChanged();
    }

    public string? GetUnitName(UnitDefinitionId typeId)
    {
        return units.FirstOrDefault(option => option.Unit!.TypeId == typeId)?.Name;
    }

    public static string FormatError(CustomMapStackError error, int leadership)
    {
        return error switch
        {
            CustomMapStackError.OutsideMap => Tr("Armies must stand inside the map."),
            CustomMapStackError.Blocked => Tr("Armies cannot stand on water, mountains or capitals."),
            CustomMapStackError.Occupied => Tr("Another army already stands there."),
            CustomMapStackError.UnknownOwner => Tr("That player is not on this map."),
            CustomMapStackError.NoLeader => Tr("An army needs a leader."),
            CustomMapStackError.MultipleLeaders => Tr("An army can have only one leader."),
            CustomMapStackError.LeadershipExceeded => Tr("Too many units for the leader's leadership ({0}).", leadership),
            _ => Tr("A large unit fills both lines of its row.")
        };
    }

    partial void OnSelectedOwnerChanged(MapEditorOption<WorldRace> value)
    {
        bool wasUpdating = updating;
        updating = true;

        try
        {
            IReadOnlyList<MapEditorUnitOption> allowed =
            [
                empty,
                // Player armies are led by their own race; any soldier of their race or a neutral one may join.
                .. units.Where(option => value.Value == WorldRace.Neutral || option.Race == value.Value || (option.Race == WorldRace.Neutral && !option.Unit!.IsLeader))
            ];
            IReadOnlyList<MapEditorUnitOption> small = [.. allowed.Where(option => option.Unit is not { IsLarge: true })];

            foreach (MapEditorSlotViewModel slot in Slots)
            {
                MapEditorUnitOption? selected = slot.SelectedOption;
                slot.Options = slot.Position.IsBackLine ? small : allowed;
                slot.SelectedOption = selected is not null && slot.Options.Contains(selected) ? selected : empty;
            }

            // Keep the army valid: if the owner change removed the leader, put the race's first leader in.
            if (Slots.All(slot => slot.SelectedOption?.Unit is not { IsLeader: true }) &&
                allowed.FirstOrDefault(option => option.Unit is { IsLeader: true, IsLarge: false } && option.Race == value.Value) is { } leader)
            {
                MapEditorSlotViewModel target = Slots.FirstOrDefault(slot => slot.Position == GridPosition.FrontCenter && slot.SelectedOption == empty)
                    ?? Slots.FirstOrDefault(slot => slot.Position.IsFrontLine && slot.SelectedOption == empty)
                    ?? Slots.Single(slot => slot.Position == GridPosition.FrontCenter);
                target.SelectedOption = leader;
            }
        }
        finally
        {
            updating = wasUpdating;
        }

        if (!updating)
        {
            OnSlotsChanged();
        }
    }

    private MapEditorUnitOption FindOption(MapEditorSlotViewModel slot, CustomMapUnit unit)
    {
        MapEditorUnitOption? option = slot.Options.FirstOrDefault(candidate => candidate.Unit?.TypeId == unit.TypeId);

        if (option is null)
        {
            // A unit outside the owner's list (e.g. from an edited file) stays selectable for this army.
            option = units.FirstOrDefault(candidate => candidate.Unit!.TypeId == unit.TypeId) ?? new MapEditorUnitOption(unit, WorldRace.Neutral, unit.TypeId.Value, unit.TypeId.Value);
            slot.Options = [.. slot.Options, option];
        }

        return option;
    }

    private void OnSlotsChanged()
    {
        if (updating)
        {
            return;
        }

        updating = true;

        try
        {
            // A large unit in the front line also takes the back slot of its row.
            foreach (MapEditorSlotViewModel back in Slots.Where(slot => slot.Position.IsBackLine))
            {
                MapEditorSlotViewModel front = Slots.Single(slot => slot.Position == GridPosition.AtFrontLine(back.Position.Flank));
                back.IsEnabled = front.SelectedOption?.Unit is not { IsLarge: true };

                if (!back.IsEnabled)
                {
                    back.SelectedOption = empty;
                }
            }
        }
        finally
        {
            updating = false;
        }

        CustomMapUnit?[] slots = GetSlots();
        CustomMapStackError? error = CustomMapStack.ValidateSlots(slots);
        CustomMapUnit? leader = slots.FirstOrDefault(unit => unit is { IsLeader: true });
        int leadership = leader is null ? 0 : leader.IsLarge ? Math.Max(leader.Leadership, GridPosition.LineCount) : leader.Leadership;
        int occupied = slots.Sum(unit => unit is null ? 0 : unit.IsLarge ? GridPosition.LineCount : 1);

        IsValid = error is null;
        Status = error is { } problem
            ? FormatError(problem, leadership)
            : Tr("{0} of {1} places used.", occupied, leadership);
        Changed?.Invoke();
    }
}
