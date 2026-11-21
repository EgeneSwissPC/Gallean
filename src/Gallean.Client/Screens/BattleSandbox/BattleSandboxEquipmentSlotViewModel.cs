using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (battle sandbox, phase 4): one of the leader's 7 equipment slots, drawn like the army tab's
// sockets (item icon, or the slot's own icon while empty). Clicking it opens the item list for that slot; a red
// frame and Problem say the leader can't use the item yet (allowed with "Anything goes").
internal sealed class BattleSandboxEquipmentSlotViewModel : IDetailsSource
{
    public BattleSandboxEquipmentSlotViewModel(LeaderEquipmentSlot slot, ItemDefinitionId? itemId, AssetSpriteKey? spriteKey, string? itemName, string? problem, object? details, Action<LeaderEquipmentSlot> onClick)
    {
        Slot = slot;
        ItemId = itemId;
        SpriteKey = spriteKey;
        Problem = problem;
        Details = details;
        Label = LeaderEquipmentSlotFormatter.FormatName(slot);
        ToolTip = itemName is null ? Label : problem is null ? $"{Label}: {itemName}" : $"{Label}: {itemName}\n{problem}";
        ClickCommand = new RelayCommand(() => onClick(slot));
    }

    public LeaderEquipmentSlot Slot { get; }
    public ItemDefinitionId? ItemId { get; }
    public AssetSpriteKey? SpriteKey { get; }
    public string Label { get; }
    public string ToolTip { get; }
    public string? Problem { get; }
    public bool HasItem => ItemId is not null;
    public bool HasProblem => Problem is not null;
    public object? Details { get; }
    public IRelayCommand ClickCommand { get; }

    public IconMoniker Icon => Slot switch
    {
        LeaderEquipmentSlot.Banner => IconMoniker.Banner,
        LeaderEquipmentSlot.Tome => IconMoniker.Tome,
        LeaderEquipmentSlot.Battle1 => IconMoniker.BattleItem,
        LeaderEquipmentSlot.Battle2 => IconMoniker.BattleItem,
        LeaderEquipmentSlot.Artifact1 => IconMoniker.Artifact,
        LeaderEquipmentSlot.Artifact2 => IconMoniker.Artifact,
        _ => IconMoniker.Boot
    };

    object? IDetailsSource.DetailsContent => Details;
}
