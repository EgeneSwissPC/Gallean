namespace Gallean.Engine;

// Gallean Upgrade: the "heal and defend" battle mode. Healers heal the most wounded ally,
// every other unit defends, until the army is fully healed.
internal static class BattleHealModePlanner
{
    public static BattleAttackTarget? FindHealTarget(BattleState state, IEngineContext context, BattleUnitId healerId)
    {
        BattleUnit healer = state.GetUnit(healerId);

        // Attack info resolves a hit roll for single-target attacks; use a fixed random
        // provider so planning never changes the battle's random sequence.
        IEngineContext planningContext =
            new EngineContext(new FixedRandomProvider(), context.Definitions, context.UnitStats, context.Scenario, context.Globals);

        BattleAttackTarget? bestTarget = null;
        int bestMissing = 0;

        foreach (BattleUnitId id in state.GetAllUnitIds())
        {
            BattleUnit target = state.GetUnit(id);

            if (target.Side != healer.Side || !target.IsActive())
            {
                continue;
            }

            BattleAttackUnitTarget attackTarget = new(id);
            BattleAttackCalculator.AttackInfo info = BattleAttackCalculator.GetUnitAttackInfo(state, planningContext, healerId, attackTarget);

            if (info.Failed || info.Attacks[0].Type != UnitAttackType.Heal)
            {
                continue;
            }

            int missing = 0;

            foreach (BattleUnitId healedId in info.Targets.GetUnitIds())
            {
                BattleUnit healed = state.GetUnit(healedId);

                if (healed.Side == healer.Side && healed.IsActive())
                {
                    missing += GetMissingHitpoints(state, context, healed);
                }
            }

            if (missing > bestMissing)
            {
                bestMissing = missing;
                bestTarget = attackTarget;
            }
        }

        return bestTarget;
    }

    public static bool IsWounded(BattleState state, IEngineContext context, BattleSide side)
    {
        foreach (BattleUnitId id in state.GetAllUnitIds())
        {
            BattleUnit unit = state.GetUnit(id);

            if (unit.Side == side && unit.IsActive() && GetMissingHitpoints(state, context, unit) > 0)
            {
                return true;
            }
        }

        return false;
    }

    public static bool HasHealer(BattleState state, IEngineContext context, BattleSide side)
    {
        foreach (BattleUnitId id in state.GetAllUnitIds())
        {
            BattleUnit unit = state.GetUnit(id);

            if (unit.Side == side && unit.IsActive() && IsHealer(context, unit))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsHealer(IEngineContext context, BattleUnit unit)
    {
        UnitAttackDefinition attack = context.Definitions.GetUnit(unit.DefinitionId).Attack;

        if (attack.Type == UnitAttackType.Heal)
        {
            return true;
        }

        return attack.AlternativeAttackId is { } alternativeId &&
               context.Definitions.GetAttack(alternativeId).Type == UnitAttackType.Heal;
    }

    private static int GetMissingHitpoints(BattleState state, IEngineContext context, BattleUnit unit)
    {
        int maxHitpoints = context.UnitStats.GetBattleUnitStats(state, unit.Id).MaxHitpoints;

        return Math.Max(0, maxHitpoints - unit.Hitpoints);
    }
}
