using Gallean.Common;

namespace Gallean.Engine;

// Tuning for auto rod (docs/auto-rod.md). Interests are the original AI's attitude values (GAI I_P_ROD 120,
// I_A_ROD 105); turn limits are the AI's RodAction limits (10, 30 for a needed mana).
public sealed record AutoRodSettings(
    int PlantInterest = 120,
    int RemoveInterest = 105,
    int TurnLimit = 10,
    int NeededManaTurnLimit = 30)
{
    public static AutoRodSettings Default { get; } = new();
}

public abstract record AutoRodJob
{
    private AutoRodJob()
    {
    }

    // Walk to StandAt (or stay, PathCost 0) and plant a rod on Target (asks the player first, decision B3).
    public sealed record Plant(WorldPosition Target, WorldPosition StandAt, int PathCost, WorldResourceType Resource, bool IsNeededMana) : AutoRodJob;

    // Walk to StandAt (or stay) and pull up the hostile rod on Target (automatic, decision B3).
    public sealed record Remove(WorldPosition Target, WorldPosition StandAt, int PathCost) : AutoRodJob;

    // No rod job known: take this auto explore leg.
    public sealed record Explore(AutoExploreResult.Target Target) : AutoRodJob;

    // No job known and no land left to explore.
    public sealed record NothingLeft : AutoRodJob
    {
        public static NothingLeft Instance { get; } = new();
    }

    // No usable job or fog edge right now (blocked, danger, battle on every path); try again later.
    public sealed record NoTargetNow : AutoRodJob
    {
        public static NoTargetNow Instance { get; } = new();
    }

    // The army's leader can't plant rods (no Rod ability or dead).
    public sealed record NotRodPlanter : AutoRodJob
    {
        public static NotRodPlanter Instance { get; } = new();
    }
}

// ShortOfGold: the player can't pay RodCost, so plant jobs were left out (decision C1).
public sealed record AutoRodPick(AutoRodJob Job, bool ShortOfGold);

// Picks the next job for a rod planter on auto rod. Same targets as the AI's RodAction (decision A1) but only from
// what the player has seen; falls back to auto explore when no job is known.
internal static class AutoRodCalculator
{
    public static AutoRodPick FindJob(
        WorldState state,
        WorldStack stack,
        IEngineContext context,
        IReadOnlySet<WorldPosition> skippedSpots,
        WorldPosition? previousExploreTarget,
        AutoRodSettings settings,
        AutoExploreSettings exploreSettings)
    {
        WorldPlayer player = state.Players[stack.OwnerId];
        bool canAfford = ResourceSetCalculator.CanAfford(player.Resources, context.Globals.RodCost);
        (WorldUnit leader, _, _) = stack.GetLeader(context.Definitions);

        if (!leader.IsAlive || !context.UnitStats.GetWorldUnitAbilities(leader).HasFlag(LeaderAbility.Rod))
        {
            return new AutoRodPick(AutoRodJob.NotRodPlanter.Instance, !canAfford);
        }

        if (FindBestJob(state, stack, context, player, canAfford, skippedSpots, settings, exploreSettings) is { } job)
        {
            return new AutoRodPick(job, !canAfford);
        }

        AutoRodJob explore = AutoExploreCalculator.FindTarget(state, stack, context, previousExploreTarget, exploreSettings) switch
        {
            AutoExploreResult.Target target => new AutoRodJob.Explore(target),
            AutoExploreResult.NothingLeft => AutoRodJob.NothingLeft.Instance,
            _ => AutoRodJob.NoTargetNow.Instance
        };

        return new AutoRodPick(explore, !canAfford);
    }

    private static AutoRodJob? FindBestJob(
        WorldState state,
        WorldStack stack,
        IEngineContext context,
        WorldPlayer player,
        bool canAfford,
        IReadOnlySet<WorldPosition> skippedSpots,
        AutoRodSettings settings,
        AutoExploreSettings exploreSettings)
    {
        ImmutableBitmap seen = player.FogOfWar;
        WorldPlan plan = WorldPlan.For(state);
        WorldMovementCalculator.MovementProfile profile = WorldMovementCalculator.GetMovementProfile(stack, context.Definitions);
        WorldPathMap? pathMap = plan.GetPathMap(stack.Position, seen, profile);

        if (pathMap is null)
        {
            return null;
        }

        StackStats stats = context.UnitStats.GetWorldStackStats(stack);
        WorldMovementLimit limit = new(stack.MovementPoints, stats.MaxMovementPoints, TurnLimit: int.MaxValue);
        ImmutableBitmap danger = AutoExploreCalculator.BuildDangerZone(state, plan, seen, stack, context.Definitions, exploreSettings.SafetyDistance);
        bool inCity = plan.GetFortificationAt(stack.Position) is not null;
        List<Job> jobs = [];

        foreach (WorldRod rod in state.Rods.Values)
        {
            if (IsSeen(rod.Position) && DiplomacyCalculator.IsHostile(state, stack.OwnerId, rod.OwnerId))
            {
                AddJob(new Job(rod.Position, context.Globals.RodRemoveRange, settings.RemoveInterest, settings.TurnLimit, null, false));
            }
        }

        if (canAfford)
        {
            WorldAiProfile neededProfile = WorldAiProfile.Classic with { SpendAbovePercent = 1, UseAllSpells = true };
            IReadOnlySet<WorldResourceType> needed = WorldAiSpending.GetNeededMana(neededProfile, player, context);

            foreach (WorldResourceNode node in state.ResourceNodes.Values)
            {
                WorldPlayerId? landOwner = plan.GetLandOwner(node.Position);

                if (!IsSeen(node.Position) ||
                    (landOwner is not null && !DiplomacyCalculator.IsHostile(state, stack.OwnerId, landOwner.Value)) ||
                    HasRodAround(plan, state.Map, node.Position))
                {
                    continue;
                }

                bool isNeeded = needed.Contains(node.ResourceType);

                foreach (WorldPosition spot in ScenarioLocationCalculator.EnumerateTiles(node.Position, 1, state.Map))
                {
                    if (IsSeen(spot) && !skippedSpots.Contains(spot) &&
                        WorldCreateRodCommand.GetSpotError(state, plan, context.Globals, stack.OwnerId, spot) is null)
                    {
                        AddJob(new Job(
                            spot,
                            context.Globals.RodPlantRange,
                            isNeeded ? Math.Min(settings.PlantInterest * 2, MaxInterest) : settings.PlantInterest,
                            isNeeded ? settings.NeededManaTurnLimit : settings.TurnLimit,
                            node.ResourceType,
                            isNeeded));
                    }
                }
            }
        }

        // Best score first; ties: lower target index, then lower stand tile index (deterministic).
        jobs.Sort((a, b) => a.Score != b.Score
            ? b.Score.CompareTo(a.Score)
            : (Index(a.Target), Index(a.StandAt)).CompareTo((Index(b.Target), Index(b.StandAt))));

        foreach (Job job in jobs)
        {
            if (job.Cost == 0 || IsSafePath(job.StandAt))
            {
                return job.Resource is { } resource
                    ? new AutoRodJob.Plant(job.Target, job.StandAt, job.Cost, resource, job.IsNeeded)
                    : new AutoRodJob.Remove(job.Target, job.StandAt, job.Cost);
            }
        }

        return null;

        // One candidate per stand tile within range of the target, like RodAction.FindBestCandidate; the path check
        // runs later, best first.
        void AddJob(Job target)
        {
            long turnLimit = (long)stats.MaxMovementPoints * target.TurnLimit;

            foreach (WorldPosition standAt in ScenarioLocationCalculator.EnumerateTiles(target.Target, target.Range, state.Map))
            {
                int cost;

                if (standAt == stack.Position)
                {
                    if (inCity)
                    {
                        continue;
                    }

                    cost = 0;
                }
                else
                {
                    if (standAt == target.Target ||
                        !IsSeen(standAt) ||
                        danger.Get(standAt.X, standAt.Y) ||
                        !plan.CanPlaceStackAt(standAt, profile) ||
                        plan.GetFortificationAt(standAt) is not null ||
                        plan.GetSiteAt(standAt) is not null)
                    {
                        continue;
                    }

                    cost = pathMap.GetCost(standAt);

                    if (cost >= int.MaxValue)
                    {
                        continue;
                    }
                }

                if (cost >= turnLimit)
                {
                    continue;
                }

                // RodAction's utility: interest share times the share of the move budget left.
                long score = target.Interest * 1000L * (turnLimit - cost) / turnLimit;
                jobs.Add(target with { StandAt = standAt, Cost = cost, Score = score });
            }
        }

        bool IsSafePath(WorldPosition standAt)
        {
            if (!pathMap.TryGetPath(standAt, limit, out WorldPath? path) || path.Positions.IsEmpty)
            {
                return false;
            }

            PathTraversalCalculator.TraversalInfo traversal = PathTraversalCalculator.Calculate(state, stack, path, profile, context.Definitions);

            return traversal.Encounter is null or PathTraversalCalculator.Encounter.Bag;
        }

        bool IsSeen(WorldPosition position)
        {
            return state.Map.IsValidPosition(position) && seen.Get(position.X, position.Y);
        }

        int Index(WorldPosition position)
        {
            return position.Y * state.Map.Width + position.X;
        }
    }

    private const int MaxInterest = 200;

    private static bool HasRodAround(WorldPlan plan, WorldMap map, WorldPosition center)
    {
        foreach (WorldPosition position in ScenarioLocationCalculator.EnumerateTiles(center, 1, map))
        {
            if (plan.GetRodAt(position) is not null)
            {
                return true;
            }
        }

        return false;
    }

    private readonly record struct Job(
        WorldPosition Target,
        int Range,
        int Interest,
        int TurnLimit,
        WorldResourceType? Resource,
        bool IsNeeded)
    {
        public WorldPosition StandAt { get; init; }

        public int Cost { get; init; }

        public long Score { get; init; }
    }
}
