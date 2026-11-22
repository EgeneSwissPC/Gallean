using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (battle AI designer): plays a designed battle AI. Before every action it finds the stage for the
// acting unit's side, turns its tactics into a BattleAiProfile and lets the Classic planner decide with it.
internal sealed class DesignedBattleAiBrain(DesignedBattleAi design) : IBattleAiBrain
{
    public DesignedBattleAi Design { get; } = design;

    public AiBrainInfo Info { get; } = new(design.Id, design.Name, "Designed in the AI editor.");

    public BattleAiActionPlan DecideAction(IEngineContext context, BattleState state, bool canUseItems, bool canRetreat)
    {
        DesignedBattleStage? stage = FindStage(context, state, state.CurrentUnit.Side);
        BattleAiProfile profile = stage is null ? BattleAiProfile.Classic : stage.Tactics.ToProfile();

        return new BattleAiPlanner(context, canUseItems: canUseItems, canRetreat: canRetreat, profile).DecideAction(state);
    }

    public DesignedBattleStage? FindStage(IEngineContext context, BattleState state, BattleSide side)
    {
        foreach (DesignedBattleStage stage in Design.Stages)
        {
            if (stage.When.IsDefaultOrEmpty || stage.When.Any(group => group.All(condition => IsTrue(context, state, side, condition))))
            {
                return stage;
            }
        }

        return null;
    }

    public static bool IsTrue(IEngineContext context, BattleState state, BattleSide side, DesignedBattleCondition condition)
    {
        BattleSideInfo info = side == BattleSide.Attacker ? state.Info.Attacker : state.Info.Defender;

        return condition switch
        {
            DesignedBattleCondition.FromRound fromRound => state.RoundNumber >= fromRound.Round,
            DesignedBattleCondition.PowerShareBelow below => GetPowerSharePercent(context, state, side) < below.Percent,
            DesignedBattleCondition.PowerShareAbove above => GetPowerSharePercent(context, state, side) > above.Percent,
            DesignedBattleCondition.LeaderHitpointsBelow leader => GetLeaderHitpointsPercent(context, state, side) is { } percent && percent < leader.Percent,
            DesignedBattleCondition.CargoAbove cargo => info.CargoValue + (cargo.IncludeEquipped ? info.EquippedValue : 0) > cargo.Gold,
            DesignedBattleCondition.CarriesQuestItem => info.CarriesQuestItem,
            _ => false
        };
    }

    // The side's share of the battle's power in percent, as Classic's flee decision measures it.
    public static double GetPowerSharePercent(IEngineContext context, BattleState state, BattleSide side)
    {
        WorldAiEstimator.PowerEstimate estimate = new WorldAiEstimator(context).EstimatePower(state);
        int power = estimate.GetPower(side);
        int total = power + estimate.GetPower(side.GetOpposite());

        return total <= 0 ? 50 : 100.0 * power / total;
    }

    public static double? GetLeaderHitpointsPercent(IEngineContext context, BattleState state, BattleSide side)
    {
        foreach ((BattleUnit unit, _) in state.GetGrid(side).GetUnits())
        {
            if (unit.SummonerId is null && BattleAiPreferences.IsLeader(context, unit))
            {
                int max = context.UnitStats.GetBattleUnitStats(state, unit.Id).MaxHitpoints;

                return max <= 0 || !unit.IsAlive ? 0 : 100.0 * unit.Hitpoints / max;
            }
        }

        return null;
    }
}
