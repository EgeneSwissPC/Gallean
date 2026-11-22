using Gallean.Engine;

namespace Gallean.Client.Tests;

// The world screen's "Income per turn" table: the towns column (map rule "town income") appears only when towns pay.
public sealed class WorldIncomeViewModelTests
{
    [Test]
    public void TownsColumnTest()
    {
        WorldGetPlayerInfoCommand.Result withTowns = new(new WorldResourceSet(310, 0, 0, 25, 0, 0), new WorldResourceSet(100, 0, 0, 25, 0, 0), new WorldResourceSet(50, 0, 0, 0, 0, 0))
        {
            TownsIncome = new WorldResourceSet(160, 0, 0, 0, 0, 0)
        };
        WorldIncomeViewModel income = new(withTowns);

        Assert.That(income.ShowTowns, Is.True);
        Assert.That(income.Rows.Select(row => (row.Type, row.CapitalIncomeText, row.ResourceNodesIncomeText, row.TownsIncomeText, row.IncomeText, row.ShowTowns)), Is.EqualTo(new[]
        {
            (WorldResourceType.Gold, "100", "50", "160", "+310", true),
            (WorldResourceType.LifeMana, "25", "0", "0", "+25", true)
        }));

        WorldIncomeViewModel plain = new(withTowns with { TotalIncome = new WorldResourceSet(150, 0, 0, 25, 0, 0), TownsIncome = WorldResourceSet.Empty });
        Assert.That(plain.ShowTowns, Is.False);
        Assert.That(plain.Rows.Select(row => row.ShowTowns), Is.All.False);
    }
}
