using System.Text.Json;
using Gallean.Engine;

namespace MapFromPlan;

// Finds units, items, spells and landmarks by their English names (Globals/Tglobal.json), the way a plan names them.
internal sealed class GameNames
{
    private readonly Dictionary<string, string> texts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IReadOnlyList<UnitDefinition> units;
    private readonly IReadOnlyList<ItemDefinition> items;
    private readonly IReadOnlyList<WorldSpellDefinition> spells;
    private readonly IReadOnlyList<WorldLandmarkDefinition> landmarks;
    private readonly CustomMapStore store;
    private readonly IGameDefinitionProvider definitions;

    public GameNames(string globalsDirectory, CustomMapStore store, IGameDefinitionProvider definitions)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(Path.Combine(globalsDirectory, "Tglobal.json")));
        JsonElement rows = document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.GetProperty("rows") : document.RootElement;

        foreach (JsonElement row in rows.EnumerateArray())
        {
            texts[row.GetProperty("TXT_ID").GetString()!] = row.GetProperty("TEXT").GetString() ?? string.Empty;
        }

        units = [.. store.ListUnits(definitions).OrderBy(unit => unit.Id.Value, StringComparer.OrdinalIgnoreCase)];
        items = [.. store.ListItems(definitions).OrderBy(item => item.Id.Value, StringComparer.OrdinalIgnoreCase)];
        spells = [.. store.ListSpells(definitions).OrderBy(spell => spell.Id.Value, StringComparer.OrdinalIgnoreCase)];
        landmarks = [.. store.ListLandmarks(definitions).OrderBy(landmark => landmark.Id.Value, StringComparer.OrdinalIgnoreCase)];
        this.store = store;
        this.definitions = definitions;
    }

    public string Text(GlobalTextId id)
    {
        return texts.GetValueOrDefault(id.Value, id.Value);
    }

    // A leader (Category Leader) or a non-leader unit with this name; units of the given race first (neutral units
    // when none is given), then the others.
    public UnitDefinition Unit(string name, bool leader, WorldRace race = WorldRace.Neutral)
    {
        UnitDefinition? found = units
            .Where(unit => string.Equals(Text(unit.NameTextId), name, StringComparison.OrdinalIgnoreCase))
            .Where(unit => leader ? unit.Category == UnitCategory.Leader : unit.Category != UnitCategory.Leader)
            .Where(unit => unit.Category != UnitCategory.Guardian)
            .OrderBy(unit => unit.Race == race ? 0 : 1)
            .FirstOrDefault();

        return found ?? throw new InvalidOperationException($"No {(leader ? "leader" : "unit")} named '{name}'.");
    }

    public ItemDefinitionId Item(string name)
    {
        ItemDefinition? found = items.FirstOrDefault(item => string.Equals(Text(item.NameTextId), name, StringComparison.OrdinalIgnoreCase));

        return found?.Id ?? throw new InvalidOperationException($"No item named '{name}'.");
    }

    // A new capital's garrison: the race's guardian alone, in the editor's slot (a capital keeps exactly one).
    public CustomMapUnit?[] CapitalGarrison(WorldRace race)
    {
        return store.GetCapitalDefaults(race, definitions).Garrison;
    }

    // A unit's type ID for message portraits (any category).
    public string Portrait(string name)
    {
        UnitDefinition? found = units.FirstOrDefault(unit => string.Equals(Text(unit.NameTextId), name, StringComparison.OrdinalIgnoreCase));

        return found?.Id.Value ?? throw new InvalidOperationException($"No unit named '{name}' for a portrait.");
    }

    public WorldSpellDefinition SpellDefinition(string name)
    {
        return spells.FirstOrDefault(spell => string.Equals(Text(spell.NameTextId), name, StringComparison.OrdinalIgnoreCase))
               ?? throw new InvalidOperationException($"No spell named '{name}'.");
    }

    public WorldSpellDefinitionId Spell(string name)
    {
        WorldSpellDefinition? found = spells.FirstOrDefault(spell => string.Equals(Text(spell.NameTextId), name, StringComparison.OrdinalIgnoreCase));

        return found?.Id ?? throw new InvalidOperationException($"No spell named '{name}'.");
    }

    // Every landmark with this name (and size, when given): a plan picks one of them at random for variety.
    public IReadOnlyList<WorldLandmarkDefinition> Landmarks(string name, int? size)
    {
        WorldLandmarkDefinition[] found =
        [
            .. landmarks.Where(landmark => string.Equals(Text(landmark.NameTextId), name, StringComparison.OrdinalIgnoreCase) &&
                                           (size is null || landmark.Width == size))
        ];

        return found.Length > 0 ? found : throw new InvalidOperationException($"No landmark named '{name}'{(size is null ? string.Empty : $" of size {size}")}.");
    }
}
