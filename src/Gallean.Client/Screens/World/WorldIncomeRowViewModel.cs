namespace Gallean.Client;

internal sealed record WorldIncomeRowViewModel(WorldResourceType Type, int CapitalIncome, int ResourceNodesIncome, int Income)
{
    public string Name => Type switch
    {
        WorldResourceType.Gold => Tr("Gold"),
        WorldResourceType.LifeMana => Tr("Life mana"),
        WorldResourceType.DeathMana => Tr("Death mana"),
        WorldResourceType.RuneMana => Tr("Rune mana"),
        WorldResourceType.InfernalMana => Tr("Infernal mana"),
        WorldResourceType.GroveMana => Tr("Grove mana")
    };

    public IconMoniker Icon => Type switch
    {
        WorldResourceType.Gold => IconMoniker.Gold,
        WorldResourceType.LifeMana => IconMoniker.ManaLife,
        WorldResourceType.DeathMana => IconMoniker.ManaDeath,
        WorldResourceType.RuneMana => IconMoniker.ManaRune,
        WorldResourceType.InfernalMana => IconMoniker.ManaInfernal,
        WorldResourceType.GroveMana => IconMoniker.ManaGrove
    };

    public string CapitalIncomeText => CapitalIncome.ToString();

    public string ResourceNodesIncomeText => ResourceNodesIncome.ToString();

    // Gallean Upgrade, map rule "town income": the towns column (gold only), shown when the map has the rule.
    public int TownsIncome { get; init; }

    public bool ShowTowns { get; init; }

    public string TownsIncomeText => TownsIncome.ToString();

    public string IncomeText => $"+{Income}";
}
