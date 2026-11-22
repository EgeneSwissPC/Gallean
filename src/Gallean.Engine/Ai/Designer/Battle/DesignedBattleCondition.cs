namespace Gallean.Engine;

// Gallean Upgrade (battle AI designer): a battle stage's When conditions, judged for the side whose unit acts.
public abstract record DesignedBattleCondition
{
    private DesignedBattleCondition()
    {
    }

    // From battle round N on.
    public sealed record FromRound(int Round) : DesignedBattleCondition;

    // The side holds less than N % of the battle's power (the measure Classic's flee decision uses).
    public sealed record PowerShareBelow(int Percent) : DesignedBattleCondition;

    // The side holds more than N % of the battle's power.
    public sealed record PowerShareAbove(int Percent) : DesignedBattleCondition;

    // The side's leader has less than N % of its hit points (a fallen leader counts as 0 %; no leader = false).
    public sealed record LeaderHitpointsBelow(int Percent) : DesignedBattleCondition;

    // The army carries items worth more than N gold in its bag (plus the leader's equipment with IncludeEquipped).
    public sealed record CargoAbove(int Gold, bool IncludeEquipped = false) : DesignedBattleCondition;

    // The army carries a quest item (a special item, in the bag or equipped).
    public sealed record CarriesQuestItem : DesignedBattleCondition;
}
