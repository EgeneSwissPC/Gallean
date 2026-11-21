using System.Collections.Immutable;

namespace Gallean.Engine;

// Gallean Upgrade (AI designer): stage conditions and the stage -> profile mapping.
internal static class DesignedStageEvaluator
{
    public static DesignedWorldStage? FindActiveStage(DesignedWorldAi design, WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
    {
        foreach (DesignedWorldStage stage in design.Stages)
        {
            if (IsActive(stage, state, playerId, memory))
            {
                return stage;
            }
        }

        return null;
    }

    public static bool IsActive(DesignedWorldStage stage, WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
    {
        // OR of AND groups; no groups = always.
        return stage.When.IsDefaultOrEmpty || stage.When.Any(group => group.All(condition => IsTrue(condition, state, playerId, memory)));
    }

    public static bool IsTrue(DesignedStageCondition condition, WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
    {
        WorldPlayer player = state.Players[playerId];

        return condition switch
        {
            DesignedStageCondition.FromTurn fromTurn => state.TurnInfo.TurnNumber >= fromTurn.Turn,
            DesignedStageCondition.GoldAbove goldAbove => player.Resources.Gold > goldAbove.Gold,
            DesignedStageCondition.CitiesBelow citiesBelow => GetOwnedCities(state, playerId).Count < citiesBelow.Count,
            DesignedStageCondition.EnemyNearCapital near => IsEnemyNearCapital(state, player, near.Tiles),
            DesignedStageCondition.LostCity => memory.LostCityTurn == state.TurnInfo.TurnNumber,
            _ => false
        };
    }

    public static ImmutableSortedSet<WorldFortificationId> GetOwnedCities(WorldState state, WorldPlayerId playerId)
    {
        ImmutableSortedSet<WorldFortificationId>.Builder owned = ImmutableSortedSet.CreateBuilder<WorldFortificationId>();

        foreach (WorldFortification fortification in state.Fortifications.Values)
        {
            WorldPlayerId? owner = fortification switch
            {
                WorldCity city => city.OwnerId,
                WorldCapital capital => capital.OwnerId,
                _ => null
            };

            if (owner == playerId)
            {
                owned.Add(fortification.Id);
            }
        }

        return owned.ToImmutable();
    }

    // Only armies on tiles the player has explored count (the same knowledge Classic uses).
    private static bool IsEnemyNearCapital(WorldState state, WorldPlayer player, int tiles)
    {
        WorldCapital? capital = WorldPlan.For(state).GetCapital(player.Id);

        if (capital is null)
        {
            return false;
        }

        WorldPosition entrance = capital.Entrance;

        foreach (WorldStack stack in state.Stacks.Values)
        {
            if (stack.OwnerId != player.Id &&
                stack.Position.GetDistance(entrance) <= tiles &&
                player.FogOfWar.Get(stack.Position.X, stack.Position.Y) &&
                DiplomacyCalculator.IsHostile(state, player.Id, stack.OwnerId))
            {
                return true;
            }
        }

        return false;
    }

    public static WorldAiProfile ToProfile(DesignedWorldStage stage)
    {
        ImmutableDictionary<WorldAiActionKind, int>.Builder priorities = ImmutableDictionary.CreateBuilder<WorldAiActionKind, int>();

        // Economy tiers: the last tier gets 0, like Classic's units. Army tiers: the last gets 1, change order is 0.
        AddTiers(priorities, stage.Economy, lowest: 0);
        AddTiers(priorities, stage.Army, lowest: 1);

        WorldAiProfile classic = WorldAiProfile.Classic;
        DesignedWorldRules rules = stage.Rules;

        return classic with
        {
            Priorities = priorities.ToImmutable(),
            HurtThreshold = rules.HurtPercent is { } hurt ? hurt * 10 : classic.HurtThreshold,
            HealedThreshold = rules.HealedPercent is { } healed ? healed * 10 : classic.HealedThreshold,
            ReinforceThreshold = rules.ReinforcePercent is { } reinforce ? reinforce * 10 : classic.ReinforceThreshold,
            AttackPowerPercent = rules.AttackPowerPercent ?? classic.AttackPowerPercent,
            LeaderOvercommitGold = rules.LeaderSavingsGold ?? classic.LeaderOvercommitGold,
            WaitForFreeCapital = rules.WaitForFreeCapital ?? classic.WaitForFreeCapital
        };
    }

    private static void AddTiers(ImmutableDictionary<WorldAiActionKind, int>.Builder priorities, ImmutableArray<ImmutableArray<WorldAiActionKind>> tiers, int lowest)
    {
        if (tiers.IsDefaultOrEmpty)
        {
            return;
        }

        for (int i = 0; i < tiers.Length; i++)
        {
            int priority = lowest + tiers.Length - 1 - i;

            foreach (WorldAiActionKind kind in tiers[i])
            {
                // An action listed twice keeps its first (higher) place.
                priorities.TryAdd(kind, priority);
            }
        }
    }
}
