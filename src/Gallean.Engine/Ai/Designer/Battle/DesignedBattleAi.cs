using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (battle AI designer, docs/battle-ai-designer.md): a battle AI built from stage cards. Before every
// action of one of its units the first stage whose When is true for that unit's side plays; a stage with an
// empty When always matches. No matching stage plays Classic.
public sealed record DesignedBattleAi(string Id, string Name, ImmutableArray<DesignedBattleStage> Stages)
{
    public const string IdPrefix = "custom-battle:";

    // A designed battle AI that plays exactly like Classic: one default stage with Classic's tactics.
    public static DesignedBattleAi FromClassic(string fileName, string name)
    {
        return new DesignedBattleAi(IdPrefix + fileName, name, [DesignedBattleStage.Classic]);
    }

    // A copy of a built-in battle AI (Classic or a personality) or of another designed battle AI.
    public static DesignedBattleAi FromBuiltIn(string sourceId, string fileName, string name)
    {
        IBattleAiBrain brain = AiBrainRegistry.ResolveBattle(sourceId);

        ImmutableArray<DesignedBattleStage> stages = brain switch
        {
            DesignedBattleAiBrain designed => designed.Design.Stages,
            ProfileBattleAiBrain personality => [DesignedBattleStage.Classic with { Tactics = DesignedBattleTactics.FromProfile(personality.Profile) }],
            _ => [DesignedBattleStage.Classic]
        };

        return new DesignedBattleAi(IdPrefix + fileName, name, stages);
    }

    public bool Equals(DesignedBattleAi? other)
    {
        return other is not null && Id == other.Id && Name == other.Name && Stages.SequenceEqual(other.Stages);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Id, Name, Stages.Length);
    }
}

// When: OR of AND groups (empty = always).
public sealed record DesignedBattleStage(string Name, ImmutableArray<ImmutableArray<DesignedBattleCondition>> When, DesignedBattleTactics Tactics)
{
    public static DesignedBattleStage Classic { get; } = new("Default", [], DesignedBattleTactics.Classic);

    public bool Equals(DesignedBattleStage? other)
    {
        return other is not null && Name == other.Name && Tactics == other.Tactics &&
            When.Length == other.When.Length && When.Zip(other.When).All(pair => pair.First.SequenceEqual(pair.Second));
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Name, When.Length, Tactics);
    }
}

// What a stage does. Target preferences are bonuses in percent on Classic's score (0 = off; the designer turns
// one on at 100). FleeBelowPercent = the leader thinks about fleeing below this share of the battle's power
// (Classic 25); NeverFlee drops fleeing altogether.
public sealed record DesignedBattleTactics(
    int HealersFirst = 0,
    int CastersFirst = 0,
    int LeaderFirst = 0,
    int HardestHitterFirst = 0,
    int WoundedFirst = 0,
    int FocusFire = 0,
    bool ProtectLeader = false,
    bool NeverFlee = false,
    int FleeBelowPercent = 25,
    bool CanDefend = true)
{
    public static DesignedBattleTactics Classic { get; } = new();

    internal BattleAiProfile ToProfile()
    {
        return BattleAiProfile.Classic with
        {
            HealersFirst = HealersFirst,
            CastersFirst = CastersFirst,
            LeaderFirst = LeaderFirst,
            HardestHitterFirst = HardestHitterFirst,
            WoundedFirst = WoundedFirst,
            FocusFire = FocusFire,
            ProtectLeader = ProtectLeader,
            CanRetreat = !NeverFlee,
            RetreatPowerShare = Math.Clamp(FleeBelowPercent, 0, 100) * 10,
            CanDefend = CanDefend
        };
    }

    internal static DesignedBattleTactics FromProfile(BattleAiProfile profile)
    {
        return new DesignedBattleTactics(
            profile.HealersFirst,
            profile.CastersFirst,
            profile.LeaderFirst,
            profile.HardestHitterFirst,
            profile.WoundedFirst,
            profile.FocusFire,
            profile.ProtectLeader,
            !profile.CanRetreat,
            profile.RetreatPowerShare / 10,
            profile.CanDefend);
    }
}
