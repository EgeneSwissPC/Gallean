namespace Gallean.Engine.Contracts;

public sealed record BattleSideInfo(bool IsNeutral)
{
    // Gallean Upgrade (battle AI designer, "we carry valuables"): what the side's army carries, worth in gold:
    // its bag (CargoValue) and its leader's equipment (EquippedValue); CarriesQuestItem = a special item in either.
    public int CargoValue { get; init; }
    public int EquippedValue { get; init; }
    public bool CarriesQuestItem { get; init; }
}
