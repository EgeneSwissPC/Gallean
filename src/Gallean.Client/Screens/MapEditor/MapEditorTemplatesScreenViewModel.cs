using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Army templates (docs/map-editor-events.md, phase 5): armies that events create. Opened from the events list. A row
// per template: name (edited in the row), owner, the order a created army gets (with its zone, city or army), what is
// in it, "Units" (the army screen) and delete (asks first when events use it). Every change is one undo step.
internal sealed partial class MapEditorTemplatesScreenViewModel : AbstractScreenViewModel
{
    private readonly MapEditorScreenViewModel editor;

    public MapEditorTemplatesScreenViewModel(GameViewModel game, MapEditorScreenViewModel editor)
    {
        Game = game;
        this.editor = editor;
        Refresh();
    }

    public GameViewModel Game { get; }

    public ObservableCollection<MapEditorTemplateRowViewModel> Rows { get; } = [];

    public bool HasNoRows => Rows.Count == 0;

    public override bool BlurBackdrop => true;

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    // Rebuilds the rows from the map (after any change, undo included).
    public void Refresh()
    {
        Rows.Clear();

        foreach (CustomMapArmyTemplate template in editor.Map.Templates.OrderBy(template => template.Name, StringComparer.CurrentCultureIgnoreCase).ThenBy(template => template.Id))
        {
            Rows.Add(new MapEditorTemplateRowViewModel(template, editor, this));
        }

        OnPropertyChanged(nameof(HasNoRows));
    }

    [RelayCommand]
    private void NewTemplate()
    {
        editor.NewTemplate();
    }
}

internal sealed partial class MapEditorTemplateRowViewModel : ObservableObject
{
    private readonly MapEditorScreenViewModel editor;
    private readonly MapEditorTemplatesScreenViewModel screen;
    private readonly bool loading;

    public MapEditorTemplateRowViewModel(CustomMapArmyTemplate template, MapEditorScreenViewModel editor, MapEditorTemplatesScreenViewModel screen)
    {
        loading = true;
        Template = template;
        this.editor = editor;
        this.screen = screen;
        Name = template.Name;
        OwnerOptions = [.. editor.Map.Players.Select(player => new MapEditorOption<WorldRace>(player.Race, MapEditorLabels.RaceShort(player.Race))), new MapEditorOption<WorldRace>(WorldRace.Neutral, MapEditorLabels.RaceShort(WorldRace.Neutral))];
        SelectedOwner = OwnerOptions.FirstOrDefault(option => option.Value == template.Owner) ?? OwnerOptions[^1];
        OrderOptions = MapEditorOrders.Kinds;
        SelectedOrder = OrderOptions.First(option => option.Value == template.Order.Kind);
        TargetOptions = MapEditorOrders.Targets(editor.EventContext(), template.Order.Kind);
        SelectedTarget = TargetOptions.FirstOrDefault(option => Equals(option.Value, MapEditorOrders.Target(template.Order))) ?? TargetOptions.FirstOrDefault();
        loading = false;
    }

    public CustomMapArmyTemplate Template { get; }

    public int Id => Template.Id;

    public IReadOnlyList<MapEditorOption<WorldRace>> OwnerOptions { get; }

    public IReadOnlyList<MapEditorOption<CustomMapOrderKind>> OrderOptions { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasTarget))]
    public partial IReadOnlyList<MapEditorOption<object?>> TargetOptions { get; private set; }

    public bool HasTarget => TargetOptions.Count > 0 || MapEditorOrders.NeedsTarget(SelectedOrder.Value);

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldRace> SelectedOwner { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<CustomMapOrderKind> SelectedOrder { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<object?>? SelectedTarget { get; set; }

    // "Orc leader · 3 units" or why it can't be used yet.
    public string Details => editor.TemplateDetails(Template);

    public bool HasProblem => !editor.Map.IsTemplateSaveable(Template);

    // The name box commits when it loses focus or on Enter (the view binds it with UpdateSourceTrigger=LostFocus).
    partial void OnNameChanged(string value)
    {
        if (!loading && !string.IsNullOrWhiteSpace(value) && value.Trim() != Template.Name)
        {
            Commit(Template with { Name = value.Trim() });
        }
    }

    partial void OnSelectedOwnerChanged(MapEditorOption<WorldRace> value)
    {
        if (!loading && value.Value != Template.Owner)
        {
            Commit(Template with { Owner = value.Value });
        }
    }

    partial void OnSelectedOrderChanged(MapEditorOption<CustomMapOrderKind> value)
    {
        if (loading || value.Value == Template.Order.Kind)
        {
            return;
        }

        IReadOnlyList<MapEditorOption<object?>> targets = MapEditorOrders.Targets(editor.EventContext(), value.Value);
        Commit(Template with { Order = MapEditorOrders.Make(value.Value, targets.FirstOrDefault()?.Value) });
    }

    partial void OnSelectedTargetChanged(MapEditorOption<object?>? value)
    {
        if (!loading && value is not null && !Equals(value.Value, MapEditorOrders.Target(Template.Order)))
        {
            Commit(Template with { Order = MapEditorOrders.Make(Template.Order.Kind, value.Value) });
        }
    }

    [RelayCommand]
    private void EditUnits() => editor.EditTemplateUnits(Id);

    [RelayCommand]
    private void Delete() => editor.DeleteTemplate(Id);

    private void Commit(CustomMapArmyTemplate changed)
    {
        editor.UpdateTemplate(changed);
        screen.Refresh();
    }
}

// The orders a template or a "gets the order" block can give, and their targets.
internal static class MapEditorOrders
{
    public static IReadOnlyList<MapEditorOption<CustomMapOrderKind>> Kinds { get; } =
    [
        new(CustomMapOrderKind.Normal, Tr("Normal")),
        new(CustomMapOrderKind.Stand, Tr("Stand")),
        new(CustomMapOrderKind.Guard, Tr("Guard")),
        new(CustomMapOrderKind.Roam, Tr("Roam")),
        new(CustomMapOrderKind.Berserk, Tr("Berserk")),
        new(CustomMapOrderKind.Explore, Tr("Explore")),
        new(CustomMapOrderKind.MoveToZone, Tr("Go to a zone")),
        new(CustomMapOrderKind.DefendZone, Tr("Defend a zone")),
        new(CustomMapOrderKind.DefendCity, Tr("Defend a city")),
        new(CustomMapOrderKind.AttackArmy, Tr("Attack an army"))
    ];

    public static bool NeedsTarget(CustomMapOrderKind kind)
    {
        return kind is CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone or CustomMapOrderKind.DefendCity or CustomMapOrderKind.AttackArmy;
    }

    public static IReadOnlyList<MapEditorOption<object?>> Targets(MapEditorEventContext context, CustomMapOrderKind kind)
    {
        return kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => context.Locations,
            CustomMapOrderKind.DefendCity => context.Cities,
            CustomMapOrderKind.AttackArmy => context.Armies,
            _ => []
        };
    }

    public static object? Target(CustomMapArmyOrder order)
    {
        return order.Kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => order.LocationId,
            CustomMapOrderKind.DefendCity => order.City,
            CustomMapOrderKind.AttackArmy => order.ArmyId,
            _ => null
        };
    }

    // A missing target saves as 0 / no city, which the engine reports (the template or block is then left out).
    public static CustomMapArmyOrder Make(CustomMapOrderKind kind, object? target)
    {
        return kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => new CustomMapArmyOrder(kind, LocationId: target is int zone ? zone : 0),
            CustomMapOrderKind.DefendCity => new CustomMapArmyOrder(kind, City: target as CustomMapCity),
            CustomMapOrderKind.AttackArmy => new CustomMapArmyOrder(kind, ArmyId: target is int army ? army : 0),
            _ => new CustomMapArmyOrder(kind)
        };
    }
}
