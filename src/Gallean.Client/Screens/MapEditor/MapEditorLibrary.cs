using Gallean.Engine;

namespace Gallean.Client;

// What the editor can put on a map besides terrain: units for armies and garrisons, items for bags, ruins and
// merchants, spells for mage towers, landmarks, and the game's names for new objects.
internal sealed record MapEditorLibrary(
    IReadOnlyList<UnitDefinition> Units,
    IReadOnlyList<ItemDefinition> Items,
    IReadOnlyList<WorldSpellDefinition> Spells,
    MapEditorNames Names,
    IReadOnlyDictionary<WorldRace, (CustomMapUnit?[] Garrison, CustomMapUnit?[] Army)>? CapitalDefaults = null,
    IReadOnlyList<WorldLandmarkDefinition>? Landmarks = null)
{
    public static MapEditorLibrary Empty { get; } = new([], [], [], MapEditorNames.Empty);

    public static MapEditorLibrary Load(CustomMapStore store, IGameDefinitionProvider definitions)
    {
        Dictionary<WorldRace, (CustomMapUnit?[] Garrison, CustomMapUnit?[] Army)> capitals = [];

        foreach (WorldRace race in Enum.GetValues<WorldRace>().Where(race => race != WorldRace.Neutral))
        {
            capitals[race] = store.GetCapitalDefaults(race, definitions);
        }

        return new MapEditorLibrary(store.ListUnits(definitions), store.ListItems(definitions), store.ListSpells(definitions), MapEditorNames.Load(), capitals, store.ListLandmarks(definitions));
    }

    // What a capital holds until changed in the editor: the race's guardian and its first leader (copies).
    public (CustomMapUnit?[] Garrison, CustomMapUnit?[] Army) GetCapitalDefaults(WorldRace race)
    {
        return CapitalDefaults?.GetValueOrDefault(race) is var (garrison, army) && garrison is not null
            ? ([.. garrison], [.. army])
            : (new CustomMapUnit?[GridPosition.Count], new CustomMapUnit?[GridPosition.Count]);
    }

    public static string GetItemName(GameModel game, ItemDefinitionId id)
    {
        try
        {
            return ItemFormatter.FormatName(game.Definitions, game.Texts, id, charges: null);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return id.Value;
        }
    }

    public static AssetSpriteKey? GetItemIconKey(GameModel game, ItemDefinitionId id)
    {
        try
        {
            return ItemIconResolver.Resolve(game.Definitions, id);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    public static IconViewModel? GetItemIcon(GameModel game, ItemDefinitionId id)
    {
        return GetItemIconKey(game, id) is AssetSpriteKey.ItemIcon key ? new IconViewModel.Item(key) : null;
    }

    public static IconViewModel? GetSpellIcon(GameModel game, WorldSpellDefinitionId id)
    {
        try
        {
            return SpellIconResolver.Resolve(game.Definitions, id);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    public static string GetSpellName(GameModel game, WorldSpellDefinitionId id)
    {
        try
        {
            return game.Texts.GetGlobalText(game.Definitions.GetSpell(id).NameTextId).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return id.Value;
        }
    }

    public static string GetUnitName(GameModel game, UnitDefinitionId id)
    {
        try
        {
            return game.Texts.GetGlobalText(game.Definitions.GetUnit(id).NameTextId).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return id.Value;
        }
    }

    // Items sorted by category, then value; the group is the category.
    public IReadOnlyList<MapEditorListPickerItemViewModel> CreateItemOptions(GameModel game)
    {
        return
        [
            .. Items
                .OrderBy(item => item.Category)
                .ThenBy(item => item.Value.Gold)
                .Select(item => new MapEditorListPickerItemViewModel(
                    item.Id,
                    GetItemIcon(game, item.Id),
                    GetItemName(game, item.Id),
                    GetText(game, item.DescriptionTextId),
                    Tr("{0} · worth {1} gold", ItemFormatter.FormatCategory(item.Category), item.Value.Gold),
                    ItemFormatter.FormatCategory(item.Category),
                    () => CreateItemDetails(game, item.Id)))
        ];
    }

    // Spells sorted by race and level; the group is the race.
    public IReadOnlyList<MapEditorListPickerItemViewModel> CreateSpellOptions(GameModel game)
    {
        return
        [
            .. Spells
                .OrderBy(spell => spell.Race)
                .ThenBy(spell => spell.Level)
                .Select(spell => new MapEditorListPickerItemViewModel(
                    spell.Id,
                    GetSpellIcon(game, spell.Id),
                    GetSpellName(game, spell.Id),
                    GetText(game, spell.DescriptionTextId),
                    Tr("Level {0}", spell.Level),
                    MapEditorLabels.RaceShort(spell.Race),
                    () => CreateSpellDetails(game, spell.Id)))
        ];
    }

    private static object? CreateItemDetails(GameModel game, ItemDefinitionId id)
    {
        try
        {
            return ItemDetailsViewModel.Create(id, null, WorldResourceSet.Empty, game.Definitions, game.Texts);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    private static object? CreateSpellDetails(GameModel game, WorldSpellDefinitionId id)
    {
        try
        {
            return SpellDetailsViewModel.Create(id, game.Definitions, game.Texts);
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return null;
        }
    }

    private static string GetText(GameModel game, GlobalTextId id)
    {
        try
        {
            return game.Texts.GetGlobalText(id).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return string.Empty;
        }
    }
}
