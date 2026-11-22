using Gallean.Engine;

namespace Gallean.Client;

internal sealed record WorldIncomeViewModel(WorldGetPlayerInfoCommand.Result Info)
{
    public IReadOnlyList<WorldIncomeRowViewModel> Rows => GetRows();

    // The towns column appears only on maps with town income (an uncaptured capital always pays then).
    public bool ShowTowns => Info.TownsIncome.Gold > 0;

    private IReadOnlyList<WorldIncomeRowViewModel> GetRows()
    {
        List<WorldIncomeRowViewModel> rows = [];

        foreach (WorldResourceType type in ResourceSetProjection.All.Types)
        {
            int income = GetValue(Info.TotalIncome, type);

            if (income > 0)
            {
                rows.Add(new WorldIncomeRowViewModel(type, GetValue(Info.CapitalIncome, type), GetValue(Info.ResourceNodesIncome, type), income)
                {
                    TownsIncome = GetValue(Info.TownsIncome, type),
                    ShowTowns = ShowTowns
                });
            }
        }

        return rows;
    }

    private static int GetValue(WorldResourceSet resources, WorldResourceType type)
    {
        return type switch
        {
            WorldResourceType.Gold => resources.Gold,
            WorldResourceType.LifeMana => resources.LifeMana,
            WorldResourceType.DeathMana => resources.DeathMana,
            WorldResourceType.RuneMana => resources.RuneMana,
            WorldResourceType.InfernalMana => resources.InfernalMana,
            WorldResourceType.GroveMana => resources.GroveMana
        };
    }
}
