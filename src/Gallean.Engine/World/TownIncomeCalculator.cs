using System.Collections.Immutable;
using System.Globalization;

namespace Gallean.Engine;

// Gallean Upgrade map rule "town income" (SRules.json TOWN_INCOME, docs/town-income.md): every town brings its owner
// gold each turn by its level, from a five-value table (levels 1-5); an uncaptured capital pays the level-5 value on
// top of the race's base income, and a rioting town pays nothing. An empty table switches the rule off, which is
// what every original scenario gets.
internal static class TownIncomeCalculator
{
    public const int LevelCount = 5;

    public static ImmutableArray<int> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length != LevelCount)
        {
            throw new InvalidDataException($"Town income needs {LevelCount} values, got '{text}'.");
        }

        return [.. parts.Select(part => Math.Max(0, int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture)))];
    }

    public static string? Format(ImmutableArray<int> table)
    {
        return table.IsDefaultOrEmpty ? null : string.Join(",", table.Select(value => value.ToString(CultureInfo.InvariantCulture)));
    }

    // Gold per turn from the player's towns and capital, before difficulty and AI scaling.
    public static int GetGold(WorldPlayer player, WorldState state, ImmutableArray<int> table, IGameDefinitionProvider definitions)
    {
        if (table.IsDefaultOrEmpty)
        {
            return 0;
        }

        int gold = 0;

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            switch (fortification)
            {
                case WorldCity city when city.OwnerId == player.Id && !FortificationCalculator.IsRioting(city):
                    gold += GetLevelValue(table, definitions.GetCity(city.DefinitionId).Level);
                    break;

                case WorldCapital { CapturedById: null } capital when capital.OwnerId == player.Id:
                    gold += GetLevelValue(table, LevelCount);
                    break;
            }
        }

        return gold;
    }

    public static int GetLevelValue(ImmutableArray<int> table, int level)
    {
        return table.IsDefaultOrEmpty ? 0 : table[Math.Clamp(level, 1, table.Length) - 1];
    }
}
