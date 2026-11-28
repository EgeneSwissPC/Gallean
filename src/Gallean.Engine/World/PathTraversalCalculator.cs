namespace Gallean.Engine;

internal static partial class PathTraversalCalculator
{
    public sealed record TraversalInfo(WorldPath Path, WorldPath Remainder, Encounter? Encounter);

    public static TraversalInfo Calculate(
        WorldState state,
        WorldStack stack,
        WorldPath path,
        WorldMovementCalculator.MovementProfile profile,
        IGameDefinitionProvider definitions)
    {
        WorldPlan plan = WorldPlan.For(state);

        // Gallean Upgrade (engine speed): the cost of the steps before position i is summed as the loop goes, and the
        // approach path is only built for the encounter that is returned. The original built path.Truncate(i) (a copy
        // of i segments) for every position, quadratic in the path length on large maps.
        int approachCost = 0;

        for (int i = 0; i < path.Positions.Length; i++)
        {
            if (i > 0)
            {
                approachCost += path.Segments[i - 1].Cost;
            }

            WorldPosition position = path.Positions[i];
            bool canBegin = approachCost < stack.MovementPoints;

            WorldBag? traverseBag = plan.GetBagAt(position);
            if (traverseBag is not null)
            {
                return new TraversalInfo(path.Truncate(i), path.Skip(i), new Encounter.Bag(traverseBag, canBegin));
            }

            WorldStack? traverseStack = plan.GetStackAt(position);
            if (traverseStack is not null && DiplomacyCalculator.IsHostile(state, stack.OwnerId, traverseStack.OwnerId))
            {
                return new TraversalInfo(path.Truncate(i), path.Skip(i), new Encounter.StackBattle(traverseStack, canBegin));
            }

            WorldFortification? traverseFortification = plan.GetFortificationAt(position);
            if (traverseFortification is not null && traverseFortification.Garrison.HasAliveUnits())
            {
                WorldPlayerId? fortificationOwner = FortificationCalculator.GetOwner(traverseFortification);

                if (fortificationOwner is null || DiplomacyCalculator.IsHostile(state, stack.OwnerId, fortificationOwner.Value))
                {
                    return new TraversalInfo(path.Truncate(i), path.Skip(i), new Encounter.FortificationBattle(traverseFortification, canBegin));
                }
            }

            WorldSite? traverseSite = plan.GetSiteAt(position);
            if (traverseSite is not null)
            {
                return new TraversalInfo(path.Truncate(i), path.Skip(i), new Encounter.Site(traverseSite, canBegin));
            }

            WorldStack? interceptingStack = FindInterceptingStack(state, plan, stack, profile, position, definitions);
            if (interceptingStack is not null)
            {
                (_, _, UnitDefinition leaderDefinition) = interceptingStack.GetLeader(definitions);
                Encounter encounter;

                if (profile.IsThief && leaderDefinition.Category is not UnitCategory.Illusion)
                {
                    encounter = new Encounter.Thief(interceptingStack, canBegin);
                }
                else
                {
                    encounter = new Encounter.StackBattle(interceptingStack, canBegin);
                }

                return new TraversalInfo(path.Truncate(i + 1), path.Skip(i + 1), encounter);
            }
        }

        return new TraversalInfo(path, new WorldPath([]), null);
    }

    public static WorldStack? FindInterceptingStack(
        WorldState state,
        WorldPlan plan,
        WorldStack stack,
        WorldMovementCalculator.MovementProfile profile,
        WorldPosition position,
        IGameDefinitionProvider definitions)
    {
        if (profile.IsIllusion)
        {
            return null;
        }

        foreach (WorldPosition adjacentPosition in position.GetAdjacentPositions())
        {
            WorldStack? adjacentStack = plan.GetStackAt(adjacentPosition);

            if (adjacentStack is not null &&
                plan.GetFortificationAt(adjacentPosition) is null &&
                DiplomacyCalculator.IsHostile(state, stack.OwnerId, adjacentStack.OwnerId) &&
                !StackCalculator.IsSameSubrace(stack, adjacentStack, definitions))
            {
                return adjacentStack;
            }
        }

        return null;
    }
}
