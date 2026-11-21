using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: "Create spell group". In this mode a click on a known spell adds it to the group (or takes it
// out again); the number on the tile is its place in the casting order. Saving stores the group and opens the
// Spell groups tab.
internal sealed partial class SpellbookTabViewModel
{
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGroupCommand))]
    public partial bool IsPickingGroup { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartGroupCommand))]
    public partial bool HasKnownSpells { get; private set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveGroupCommand))]
    public partial int PickedCount { get; private set; }

    [ObservableProperty]
    public partial string GroupName { get; set; } = string.Empty;

    public int GroupNameLength => SpellGroupStore.MaxNameLength;

    public string PickedCountText => PickedCount == 1 ? Tr("1 spell in the group") : Tr("{0} spells in the group", PickedCount);

    partial void OnPickedCountChanged(int value)
    {
        OnPropertyChanged(nameof(PickedCountText));
    }

    [RelayCommand(CanExecute = nameof(CanStartGroup))]
    private void StartGroup()
    {
        selectedSpellId = null;
        ApplySelection();
        pickedSpells.Clear();
        GroupName = Tr("Group {0}", SpellGroupStore.Load(ViewRace).Count + 1);
        IsPickingGroup = true;
        ApplyPicks();
    }

    private bool CanStartGroup()
    {
        return !IsPickingGroup && HasKnownSpells;
    }

    [RelayCommand(CanExecute = nameof(CanSaveGroup))]
    private void SaveGroup()
    {
        string name = string.IsNullOrWhiteSpace(GroupName) ? Tr("Group {0}", SpellGroupStore.Load(ViewRace).Count + 1) : GroupName.Trim();

        SpellGroupStore.Add(ViewRace, new SpellGroup(name, [.. pickedSpells]));
        EndPicking();

        if (Screen.Tabs.OfType<SpellGroupsTabViewModel>().FirstOrDefault() is { } groups)
        {
            groups.Refresh();
            Screen.SelectTabCommand.Execute(groups);
        }
    }

    private bool CanSaveGroup()
    {
        return PickedCount > 0;
    }

    [RelayCommand]
    private void CancelGroup()
    {
        EndPicking();
    }

    public override bool TryCancel()
    {
        if (IsPickingGroup)
        {
            EndPicking();
            return true;
        }

        return false;
    }

    private WorldRace ViewRace => WorldState.Players[ViewPlayerId].Race;

    private void EndPicking()
    {
        IsPickingGroup = false;
        IsTyping = false;
        pickedSpells.Clear();
        ApplyPicks();
    }

    private void TogglePick(SpellbookItemViewModel spell)
    {
        if (spell.Type != SpellbookItemType.Known)
        {
            return;
        }

        if (!pickedSpells.Remove(spell.SpellId))
        {
            pickedSpells.Add(spell.SpellId);
        }

        ApplyPicks();
    }

    private void ApplyPicks()
    {
        pickedSpells.RemoveAll(id => FindItem(id) is not { Type: SpellbookItemType.Known });

        foreach (SpellbookLevelViewModel level in Levels)
        {
            foreach (SpellbookItemViewModel item in level.Spells)
            {
                int index = pickedSpells.IndexOf(item.SpellId);
                item.PickOrder = IsPickingGroup && index >= 0 ? index + 1 : null;
            }
        }

        PickedCount = pickedSpells.Count;
    }
}
