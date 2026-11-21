using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (AI designer, docs/ai-designer.md): a map AI built from stage cards. The first stage
// whose When is true plays; a stage with an empty When always matches (the default stage, last).
public sealed record DesignedWorldAi(string Id, string Name, ImmutableArray<DesignedWorldStage> Stages)
{
    public const string IdPrefix = "custom:";

    // A designed AI that plays exactly like Classic: one default stage with Classic's order.
    public static DesignedWorldAi FromClassic(string fileName, string name)
    {
        return new DesignedWorldAi(IdPrefix + fileName, name, [DesignedWorldStage.Classic]);
    }

    public bool Equals(DesignedWorldAi? other)
    {
        return other is not null && Id == other.Id && Name == other.Name && Stages.SequenceEqual(other.Stages);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name, Stages.Length);
    }
}

// When: OR of AND groups (empty = always). Economy and Army: priority tiers, highest first; actions in one
// tier have the same priority and compare scores, as in Classic. An action in no tier is never done.
public sealed record DesignedWorldStage(
    string Name,
    ImmutableArray<ImmutableArray<DesignedStageCondition>> When,
    ImmutableArray<ImmutableArray<WorldAiActionKind>> Economy,
    ImmutableArray<ImmutableArray<WorldAiActionKind>> Army,
    DesignedWorldRules Rules)
{
    public static DesignedWorldStage Classic { get; } = new(
        "Default",
        [],
        [
            [WorldAiActionKind.CastSpell],
            [WorldAiActionKind.ResearchSpell],
            [WorldAiActionKind.HireLeader],
            [WorldAiActionKind.ConstructBuilding],
            [WorldAiActionKind.PurchaseUnit]
        ],
        [
            [WorldAiActionKind.UseItem],
            [WorldAiActionKind.Resurrect],
            [WorldAiActionKind.Heal],
            [WorldAiActionKind.AutoFit],
            [WorldAiActionKind.Attack, WorldAiActionKind.Thief, WorldAiActionKind.Rod, WorldAiActionKind.EnterCity, WorldAiActionKind.TakeBag],
            [WorldAiActionKind.Move]
        ],
        DesignedWorldRules.Classic);

    public bool Equals(DesignedWorldStage? other)
    {
        return other is not null &&
               Name == other.Name &&
               When.Length == other.When.Length && When.Zip(other.When).All(pair => pair.First.SequenceEqual(pair.Second)) &&
               Economy.Length == other.Economy.Length && Economy.Zip(other.Economy).All(pair => pair.First.SequenceEqual(pair.Second)) &&
               Army.Length == other.Army.Length && Army.Zip(other.Army).All(pair => pair.First.SequenceEqual(pair.Second)) &&
               Rules == other.Rules;
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, When.Length, Economy.Length, Army.Length, Rules);
    }
}

// Rule and tuning blocks; null = Classic's value.
public sealed record DesignedWorldRules(
    int? HurtPercent = null,
    int? HealedPercent = null,
    int? ReinforcePercent = null,
    int? AttackPowerPercent = null,
    int? LeaderSavingsGold = null,
    bool? WaitForFreeCapital = null)
{
    public static DesignedWorldRules Classic { get; } = new();
}
