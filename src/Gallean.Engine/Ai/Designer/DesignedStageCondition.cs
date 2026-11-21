namespace Gallean.Engine;

// Gallean Upgrade (AI designer): a stage's When conditions (docs/ai-designer.md).
public abstract record DesignedStageCondition
{
    private DesignedStageCondition()
    {
    }

    // From turn N on.
    public sealed record FromTurn(int Turn) : DesignedStageCondition;

    // The player holds more than N gold.
    public sealed record GoldAbove(int Gold) : DesignedStageCondition;

    // The player owns fewer than N cities (capital included).
    public sealed record CitiesBelow(int Count) : DesignedStageCondition;

    // A hostile army the player has explored stands within N tiles of its capital.
    public sealed record EnemyNearCapital(int Tiles) : DesignedStageCondition;

    // The player lost a city (or its capital) since its previous turn.
    public sealed record LostCity : DesignedStageCondition;
}
