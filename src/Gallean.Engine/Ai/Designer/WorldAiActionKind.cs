namespace Gallean.Engine;

// Gallean Upgrade (AI designer): the Classic world AI's actions a designed AI orders and switches on or off.
// Change order is not listed: it always runs last (priority 0).
public enum WorldAiActionKind
{
    // Economy
    CastSpell,
    ResearchSpell,
    HireLeader,
    ConstructBuilding,
    PurchaseUnit,

    // Armies
    UseItem,
    Resurrect,
    Heal,
    AutoFit,
    Attack,
    Thief,
    Rod,
    EnterCity,
    TakeBag,
    Move
}
