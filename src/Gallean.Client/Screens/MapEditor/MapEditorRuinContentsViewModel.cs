using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (map editor): the ruin's item and gold in the right-click info. The game hides them since
// upstream 7e120ae6 (players no longer see them after spying), but the map designer placed them and needs them.
internal sealed record MapEditorRuinContentsViewModel(string? ItemName, AssetSpriteKey.ItemIcon? ItemSpriteKey, ResourceSetViewModel? Reward)
{
    public static MapEditorRuinContentsViewModel? TryCreate(WorldRuin ruin, IGameDefinitionProvider definitions, IGameTextProvider texts)
    {
        string? itemName = null;
        AssetSpriteKey.ItemIcon? itemSpriteKey = null;

        if (ruin.Item is { } itemId)
        {
            itemName = ItemFormatter.FormatName(definitions, texts, itemId, charges: null);
            itemSpriteKey = ItemIconResolver.Resolve(definitions, itemId);
        }

        ResourceSetViewModel? reward = ruin.Resources != WorldResourceSet.Empty ? new ResourceSetViewModel(ruin.Resources, ResourceSetProjection.All) : null;

        return itemName is null && reward is null ? null : new MapEditorRuinContentsViewModel(itemName, itemSpriteKey, reward);
    }
}
