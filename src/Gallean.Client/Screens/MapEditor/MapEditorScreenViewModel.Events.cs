using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The events list (V): over the left of the map like the locations list, sorted by name, with a search box. A row
// shows the event's races and size, an "on at start" box and a red mark when the event can't be saved yet; rename in
// the row, double-click or Open shows the block editor. Events change nothing in the world, so edits only rebuild
// the list (one undo step each).
internal sealed partial class MapEditorScreenViewModel
{
    private bool updatingEvents;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLandmarkPalette), nameof(ShowEventsList))]
    public partial bool IsEventsOpen { get; set; }

    public ObservableCollection<MapEditorEventRowViewModel> EventRows { get; } = [];

    // The list steps aside while a block's target is picked on the map, so the whole map is free to click.
    public bool ShowEventsList => IsEventsOpen && !IsPickingEventTarget;

    [ObservableProperty]
    public partial string EventSearch { get; set; } = string.Empty;

    public string EventsTitle => Tr("Events ({0})", map.Events.Count);

    public bool HasEvents => map.Events.Count > 0;

    public bool HasNoEventRows => EventRows.Count == 0;

    [RelayCommand]
    private void ToggleEvents()
    {
        IsEventsOpen = !IsEventsOpen;
    }

    partial void OnIsEventsOpenChanged(bool value)
    {
        if (value)
        {
            IsEconomyOpen = false;
            IsLocationsOpen = false;
            RebuildEventRows();
        }

        RefreshPreview();
    }

    partial void OnEventSearchChanged(string value)
    {
        RebuildEventRows();
    }

    [RelayCommand]
    private void NewEvent()
    {
        WorldRace race = map.Players.FirstOrDefault()?.Race ?? WorldRace.Human;
        CustomMapEvent? added = null;
        CommitEvents(current =>
        {
            added = current.AddEvent(CustomMapEvent.Create(string.Empty, race) with { Name = Tr("Event {0}", NextEventNumber(current)) });
            return true;
        });

        if (added is not null)
        {
            OpenEvent(added.Id);
        }
    }

    public void OpenEvent(int id)
    {
        if (map.GetEvent(id) is not { } ev)
        {
            return;
        }

        MapEditorEventContext context = EventContext();
        Game.OpenScreen(new MapEditorEventScreenViewModel(
            Game,
            context,
            ev,
            changed =>
            {
                if (!changed.Equals(map.GetEvent(id)))
                {
                    CommitEvents(current => current.UpdateEvent(changed));
                }

                return null;
            },
            () => DeleteEvent(id)));
    }

    public void DuplicateEvent(int id)
    {
        if (map.GetEvent(id) is { } ev)
        {
            CommitEvents(current =>
            {
                current.AddEvent(ev with { Name = Tr("{0} (copy)", ev.Name) });
                return true;
            });
        }
    }

    // Asks first when other events switch this one on or off.
    public void DeleteEvent(int id)
    {
        ConfirmRemoval(new CustomMapEventTarget.Event(id), map.GetEvent(id)?.Name ?? string.Empty, () => CommitEvents(current => current.RemoveEvent(id)));
    }

    public void RenameEvent(int id, string name)
    {
        if (!string.IsNullOrWhiteSpace(name) && map.GetEvent(id) is { } ev && ev.Name != name.Trim())
        {
            CommitEvents(current => current.UpdateEvent(ev with { Name = name.Trim() }));
        }
    }

    public void SetEventEnabled(int id, bool enabled)
    {
        if (!updatingEvents && map.GetEvent(id) is { } ev && ev.Enabled != enabled)
        {
            CommitEvents(current => current.UpdateEvent(ev with { Enabled = enabled }));
        }
    }

    private void CommitEvents(Func<CustomMap, bool> change)
    {
        CustomMap before = map.Clone();

        if (change(map))
        {
            undoSteps.Push(before);
            TrimUndo();
            CanUndo = true;
            IsDirty = true;
            UpdateStatus();
        }

        RebuildEventRows();
    }

    private void RebuildEventRows()
    {
        string search = EventSearch.Trim();
        CompareInfo compare = CultureInfo.CurrentCulture.CompareInfo;
        updatingEvents = true;
        HashSet<int> saveable = [.. map.GetSaveableEvents().Select(ev => ev.Id)];

        try
        {
            EventRows.Clear();

            foreach (CustomMapEvent ev in map.Events
                .Where(ev => search.Length == 0 || compare.IndexOf(ev.Name, search, CompareOptions.IgnoreCase) >= 0)
                .OrderBy(ev => ev.Name, StringComparer.Create(CultureInfo.CurrentCulture, ignoreCase: true))
                .ThenBy(ev => ev.Id))
            {
                int conditions = ev.Groups.Sum(group => group.Length);
                string races = string.Join(", ", map.GetEventRaces(ev.Races).Select(MapEditorLabels.RaceShort));
                string conditionText = conditions == 1 ? Tr("1 condition") : Tr("{0} conditions", conditions);
                string effectText = ev.Effects.Length == 1 ? Tr("1 effect") : Tr("{0} effects", ev.Effects.Length);
                string details = Tr("{0} · {1} · {2}", races.Length == 0 ? Tr("no race") : races, conditionText, effectText);
                EventRows.Add(new MapEditorEventRowViewModel(ev.Id, ev.Name, details, ev.Enabled, !saveable.Contains(ev.Id), this));
            }
        }
        finally
        {
            updatingEvents = false;
        }

        OnPropertyChanged(nameof(EventsTitle));
        OnPropertyChanged(nameof(HasEvents));
        OnPropertyChanged(nameof(HasNoEventRows));
    }

    private static int NextEventNumber(CustomMap current)
    {
        return current.Events.Count == 0 ? 1 : current.Events.Max(ev => ev.Id) + 1;
    }

    private void PickEventItem(Action<ItemDefinitionId> picked)
    {
        Game.OpenScreen(new MapEditorListPickerViewModel(Game, Tr("Choose item"), library.CreateItemOptions(editorGame), value => picked((ItemDefinitionId)value)));
    }

    private void PickEventPortrait(Action<UnitDefinitionId?> picked)
    {
        WorldRace race = map.Players.FirstOrDefault()?.Race ?? WorldRace.Human;
        Game.OpenScreen(new MapEditorUnitPickerViewModel(Game, editorGame, units, pickLeader: true, race, _ => null, unit => picked(unit.Id)));
    }
}

internal sealed partial class MapEditorEventRowViewModel(int id, string name, string details, bool enabled, bool hasProblem, MapEditorScreenViewModel editor)
    : ObservableObject, IMapEditorRenameRow
{
    public int Id { get; } = id;

    public string Name { get; } = name;

    public string Details { get; } = details;

    // The event can't be saved yet (red blocks, no race or no condition).
    public bool HasProblem { get; } = hasProblem;

    [ObservableProperty]
    public partial bool IsEnabled { get; set; } = enabled;

    [ObservableProperty]
    public partial bool IsEditing { get; set; }

    [ObservableProperty]
    public partial string EditText { get; set; } = string.Empty;

    partial void OnIsEnabledChanged(bool value) => editor.SetEventEnabled(Id, value);

    [RelayCommand]
    private void BeginEdit()
    {
        EditText = Name;
        IsEditing = true;
    }

    [RelayCommand]
    private void CommitEdit()
    {
        if (!IsEditing)
        {
            return;
        }

        IsEditing = false;
        editor.RenameEvent(Id, EditText);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        IsEditing = false;
    }

    [RelayCommand]
    private void Open() => editor.OpenEvent(Id);

    [RelayCommand]
    private void Duplicate() => editor.DuplicateEvent(Id);

    [RelayCommand]
    private void Delete() => editor.DeleteEvent(Id);
}

// A list row whose name is edited in place (locations, events): the view's key and focus handlers drive it.
internal interface IMapEditorRenameRow
{
    IRelayCommand BeginEditCommand { get; }

    IRelayCommand CommitEditCommand { get; }

    IRelayCommand CancelEditCommand { get; }
}
