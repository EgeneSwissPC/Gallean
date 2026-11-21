using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

internal sealed partial class BattleSandboxUpgradeItemViewModel : ObservableObject
{
    private readonly BattleSandboxUpgradesViewModel owner;

    public BattleSandboxUpgradeItemViewModel(BattleSandboxUpgradesViewModel owner, UnitModifierDefinitionId modifierId, IconMoniker icon, string name, string description, int minLevel)
    {
        this.owner = owner;
        ModifierId = modifierId;
        Icon = icon;
        Name = name;
        Description = description;
        MinLevel = minLevel;
        SelectCommand = new RelayCommand(() => owner.Toggle(this), () => Status.IsAvailable || IsSelected);
    }

    public UnitModifierDefinitionId ModifierId { get; }
    public IconMoniker Icon { get; }
    public string Name { get; }
    public string Description { get; }
    public int MinLevel { get; }
    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial OfferStatusViewModel Status { get; set; } = new(null);

    partial void OnStatusChanged(OfferStatusViewModel value) => SelectCommand.NotifyCanExecuteChanged();

    partial void OnIsSelectedChanged(bool value) => SelectCommand.NotifyCanExecuteChanged();
}

// Gallean Upgrade (battle sandbox, phase 4): the leader's level-up upgrades, laid out like the game's level-up
// popup (portrait, "level N", the list of abilities), but several can be ticked at once, up to the number the
// leader's level allows in the game. Upgrades above the leader's level are greyed with the reason. "Anything goes"
// lifts both limits.
internal sealed partial class BattleSandboxUpgradesViewModel : AbstractScreenViewModel
{
    private readonly Action<IReadOnlyList<UnitModifierDefinitionId>> onConfirm;

    public BattleSandboxUpgradesViewModel(
        GameModel editorGame,
        IReadOnlyList<LeaderUpgradeDefinition> choices,
        UnitDefinitionId leaderType,
        string leaderName,
        int leaderLevel,
        int maxUpgrades,
        bool allowsAnything,
        IReadOnlyList<UnitModifierDefinitionId> current,
        Action<IReadOnlyList<UnitModifierDefinitionId>> onConfirm)
    {
        this.onConfirm = onConfirm;
        LeaderLevel = leaderLevel;
        MaxUpgrades = allowsAnything ? choices.Count : maxUpgrades;
        AllowsAnything = allowsAnything;
        FaceKey = new AssetSpriteKey.UnitFace(leaderType);
        Title = Tr("{0}, level {1}", leaderName, leaderLevel);
        Options = [.. choices.Select(choice => new BattleSandboxUpgradeItemViewModel(
            this,
            choice.ModifierId,
            UnitModifierIconResolver.Resolve(editorGame.Definitions.GetUnitModifier(choice.ModifierId)),
            editorGame.Texts.GetGlobalText(choice.NameTextId).Text,
            editorGame.Texts.GetGlobalText(choice.DescriptionTextId).Text,
            choice.MinLevel)
        {
            IsSelected = current.Contains(choice.ModifierId)
        })];

        Refresh();
    }

    public AssetSpriteKey.UnitFace FaceKey { get; }
    public string Title { get; }
    public int LeaderLevel { get; }
    public int MaxUpgrades { get; }
    public bool AllowsAnything { get; }
    public IReadOnlyList<BattleSandboxUpgradeItemViewModel> Options { get; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    public IReadOnlyList<UnitModifierDefinitionId> Selected => [.. Options.Where(option => option.IsSelected).Select(option => option.ModifierId)];

    public void Toggle(BattleSandboxUpgradeItemViewModel item)
    {
        item.IsSelected = !item.IsSelected;
        Refresh();
    }

    private void Refresh()
    {
        int count = Options.Count(option => option.IsSelected);
        bool full = count >= MaxUpgrades;

        foreach (BattleSandboxUpgradeItemViewModel option in Options)
        {
            string? reason = AllowsAnything || option.IsSelected ? null
                : option.MinLevel > LeaderLevel ? Tr("Needs leader level {0}", option.MinLevel)
                : full ? Tr("No more upgrades at this level")
                : null;

            if (option.Status.UnavailableReason != reason)
            {
                option.Status = new OfferStatusViewModel(reason);
            }
        }

        CountText = AllowsAnything
            ? Tr("{0} upgrades (anything goes)", count)
            : Tr("Upgrades: {0} of {1} (one per level after the first)", count, MaxUpgrades);
    }

    [RelayCommand]
    private void Confirm()
    {
        CloseSelf();
        onConfirm(Selected);
    }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }
}
