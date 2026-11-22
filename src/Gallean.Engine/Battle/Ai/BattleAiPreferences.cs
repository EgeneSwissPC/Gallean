namespace Gallean.Engine;

// Gallean Upgrade (battle AI designer, docs/battle-ai-designer.md): the target preferences and "protect the
// leader" of a designed battle AI, applied on top of Classic's score for one target. The facts about the
// enemy side (hardest hitter, most wounded...) are worked out once per decision.
internal sealed class BattleAiPreferences
{
    // Heals, cures and protections on the own leader count five times (a leader missing a fifth as many hit points
    // as another unit still gets the heal).
    public const int ProtectLeaderBonus = 400;

    private readonly IEngineContext context;
    private readonly BattleAiProfile profile;
    private readonly BattleSide side;
    private readonly BattleUnitId? hardestHitter;
    private readonly BattleUnitId? mostWounded;
    private readonly BattleUnitId? mostHurt;

    public BattleAiPreferences(IEngineContext context, BattleAiProfile profile, BattleState state, BattleUnit unit)
    {
        this.context = context;
        this.profile = profile;
        side = unit.Side;

        BattleAiEstimator estimator = new(context);
        List<BattleUnit> enemies = [.. state.GetGrid(unit.Side.GetOpposite()).GetUnits().Select(pair => pair.Item1).Where(enemy => enemy.IsActive())];

        if (enemies.Count > 0)
        {
            hardestHitter = enemies.MaxBy(enemy => estimator.EstimateUnitPower(state, enemy))!.Id;
            mostWounded = enemies.MinBy(enemy => enemy.Hitpoints)!.Id;

            BattleUnit hurt = enemies.MaxBy(enemy => LostShare(state, enemy))!;
            mostHurt = LostShare(state, hurt) > 0 ? hurt.Id : null;
        }
    }

    public static bool IsLeader(IEngineContext context, BattleUnit unit)
    {
        UnitDefinition definition = context.Definitions.GetUnit(unit.DefinitionId);

        return definition.Leadership is > 0 && definition.Category != UnitCategory.Guardian;
    }

    public static bool IsHealer(UnitDefinition definition)
    {
        return definition.HasAnyAttackType(UnitAttackType.Heal, UnitAttackType.Cure, UnitAttackType.Revive);
    }

    public static bool IsCaster(UnitDefinition definition)
    {
        return definition.Attack.Reach == UnitAttackReach.All && !IsHealer(definition);
    }

    public int Apply(int score, BattleUnit target, UnitAttackDefinition attack)
    {
        int bonus = 0;

        if (target.Side != side)
        {
            UnitDefinition definition = context.Definitions.GetUnit(target.DefinitionId);

            bonus += IsHealer(definition) ? profile.HealersFirst : 0;
            bonus += IsCaster(definition) ? profile.CastersFirst : 0;
            bonus += IsLeader(context, target) ? profile.LeaderFirst : 0;
            bonus += target.Id == hardestHitter ? profile.HardestHitterFirst : 0;
            bonus += target.Id == mostWounded ? profile.WoundedFirst : 0;
            bonus += target.Id == mostHurt ? profile.FocusFire : 0;
        }
        else if (profile.ProtectLeader && IsSupport(attack.Type) && IsLeader(context, target))
        {
            bonus += ProtectLeaderBonus;
        }

        return (int)Math.Min(int.MaxValue / 4, (long)score * (100 + bonus) / 100);
    }

    private static bool IsSupport(UnitAttackType type)
    {
        return type is UnitAttackType.Heal or UnitAttackType.Cure or UnitAttackType.Revive or UnitAttackType.GiveProtection or UnitAttackType.IncreaseDamage;
    }

    private double LostShare(BattleState state, BattleUnit unit)
    {
        int max = context.UnitStats.GetBattleUnitStats(state, unit.Id).MaxHitpoints;

        return max <= 0 ? 0 : (double)(max - unit.Hitpoints) / max;
    }
}
