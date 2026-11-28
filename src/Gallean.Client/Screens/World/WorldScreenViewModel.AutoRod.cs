using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: auto rod (docs/auto-rod.md). A rod planter on auto rod is an explorer (it is in
// exploringStacks, so it walks with the explorers and looks like an order) whose next destination
// comes from the engine's AutoRodCalculator: a seen rod job if one is known, an explore leg
// otherwise. When it stands next to a job: a hostile rod is pulled up at once, planting asks
// Plant / Skip this spot / Stop (decision B3). Below the rod price it skips plant jobs and says so
// once (C1). R or the card button toggles it (D1). Not saved, like move orders.
internal sealed partial class WorldScreenViewModel
{
    private readonly HashSet<WorldStackId> roddingStacks = [];
    private readonly Dictionary<WorldStackId, AutoRodJob> pendingRodJobs = [];
    private readonly Dictionary<WorldStackId, HashSet<WorldPosition>> skippedRodSpots = [];
    private readonly HashSet<WorldStackId> toldShortOfGold = [];
    private (WorldState State, WorldStackId StackId, bool CanRod)? rodCheck;

    public bool IsAutoRodding(WorldStackId stackId)
    {
        return roddingStacks.Contains(stackId);
    }

    [RelayCommand(CanExecute = nameof(CanToggleAutoRod))]
    private async Task ToggleAutoRodAsync()
    {
        WorldStack? stack = ResolveSelectedStack();

        if (stack is null)
        {
            return;
        }

        if (IsAutoRodding(stack.Id))
        {
            StopExploring(stack.Id);

            if (HasMoveOrder(stack))
            {
                SetSelectedPath(stack, null);
            }

            RefreshAutoExploreState();

            return;
        }

        // Auto rod and Explore are separate modes: starting one ends the other.
        if (IsExploring(stack.Id))
        {
            StopExploring(stack.Id);

            if (HasMoveOrder(stack))
            {
                SetSelectedPath(stack, null);
            }
        }

        AutoRodPick? pick = PickRodJob(stack);

        if (pick is null || pick.Job is AutoRodJob.NotRodPlanter)
        {
            RefreshAutoExploreState();

            return;
        }

        if (pick.Job is AutoRodJob.NothingLeft)
        {
            Game.ShowInfo(IconMoniker.PlantRod, Tr("Nothing left for the rod planter"), Tr("{0} has no rod jobs and no more land to explore.", ResolveLeaderName(stack)));
            RefreshAutoExploreState();

            return;
        }

        exploringStacks.Add(stack.Id);
        roddingStacks.Add(stack.Id);
        ForgetOrderReport(stack.Id);
        RefreshAutoExploreState();

        if (stack.MovementPoints > 0 && !IsFinished(stack))
        {
            await ExploreNowAsync(stack.Id);
        }
    }

    private bool CanToggleAutoRod()
    {
        if (IsBusy || !Snapshot.IsViewPlayerTurn)
        {
            return false;
        }

        WorldStack? stack = ResolveSelectedStack();

        if (stack is null || stack.OwnerId != ViewPlayerId)
        {
            return false;
        }

        return IsAutoRodding(stack.Id) || CanAutoRod(stack);
    }

    // Cached per state and army: the button's CanExecute is asked often.
    private bool CanAutoRod(WorldStack stack)
    {
        if (rodCheck is { } check && ReferenceEquals(check.State, WorldState) && check.StackId == stack.Id)
        {
            return check.CanRod;
        }

        bool canRod = IsRodPlanter(stack) && PickRodJob(stack)?.Job is not (null or AutoRodJob.NothingLeft or AutoRodJob.NotRodPlanter);

        rodCheck = (WorldState, stack.Id, canRod);

        return canRod;
    }

    // Only rod planters get the card button (decision D1).
    private bool IsRodPlanter(WorldStack stack)
    {
        (WorldUnit leader, GridPosition _, UnitDefinition _) = stack.GetLeader(GameModel.Definitions);

        return leader.IsAlive && GameModel.Stats.GetWorldUnitAbilities(leader).HasFlag(LeaderAbility.Rod);
    }

    private AutoRodPick? PickRodJob(WorldStack stack)
    {
        IReadOnlySet<WorldPosition> skipped = skippedRodSpots.TryGetValue(stack.Id, out HashSet<WorldPosition>? spots) ? spots : new HashSet<WorldPosition>();
        ICommandResult<WorldCommandError, WorldEvent, AutoRodPick> result =
            GameModel.World.DryRunCommand(new WorldGetAutoRodJobCommand(stack.Id, skipped));

        return result.Failed ? null : result.Data;
    }

    // Auto explore's TryAssignExploreTarget for a rod planter. True when it got a destination; a job
    // it can do from where it stands goes to pendingRodJobs (the walk then stops with OrderStop.RodJob).
    private bool TryAssignRodJob(WorldStack stack, List<OrderAttention>? attention)
    {
        if (PickRodJob(stack) is not { } pick)
        {
            return false;
        }

        if (!pick.ShortOfGold)
        {
            toldShortOfGold.Remove(stack.Id);
        }
        else if (attention is not null && toldShortOfGold.Add(stack.Id))
        {
            attention.Add(new OrderAttention(stack.Id, Tr("{0}: not enough gold to plant rods.", ResolveLeaderName(stack))));
        }

        switch (pick.Job)
        {
            case AutoRodJob.Plant plant when plant.StandAt == stack.Position:
                pendingRodJobs[stack.Id] = plant;

                return false;

            case AutoRodJob.Remove remove when remove.StandAt == stack.Position:
                pendingRodJobs[stack.Id] = remove;

                return false;

            case AutoRodJob.Plant plant:
                SetExploreDestination(stack, plant.StandAt);

                return true;

            case AutoRodJob.Remove remove:
                SetExploreDestination(stack, remove.StandAt);

                return true;

            case AutoRodJob.Explore explore:
                SetExploreDestination(stack, explore.Target.Position);

                return true;

            case AutoRodJob.NothingLeft:
            case AutoRodJob.NotRodPlanter:
                StopExploring(stack.Id);
                RefreshAutoExploreState();

                if (attention is not null)
                {
                    AddOrderAttention(attention, stack, Tr("{0} has no rod jobs and no more land to explore.", ResolveLeaderName(stack)));
                }

                return false;

            default:
                return false;
        }
    }

    // What a walk returns when TryAssign stopped it: a job right here, or nothing.
    private OrderStop StopAfterNoTarget(WorldStackId stackId, int leg)
    {
        if (pendingRodJobs.ContainsKey(stackId))
        {
            return OrderStop.RodJob;
        }

        return leg == 0 ? OrderStop.Skipped : OrderStop.None;
    }

    // Does the job the planter stands next to. Returns true when it may walk on (the player skipped the spot).
    private async Task<bool> HandleRodJobAsync(WorldStackId stackId, List<OrderAttention> attention)
    {
        if (!pendingRodJobs.Remove(stackId, out AutoRodJob? job) ||
            !WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) ||
            !IsAutoRodding(stackId))
        {
            return false;
        }

        string name = ResolveLeaderName(stack);

        switch (job)
        {
            case AutoRodJob.Remove remove:
                await WaitForIdleAsync();

                // Straight to the engine: the screen counts as busy while the walk command runs.
                if (!(await GameModel.World.DispatchCommandAsync(new WorldRemoveRodCommand(stackId, remove.Target))).Failed)
                {
                    await WaitForIdleAsync();
                    AddOrderAttention(attention, stack, Tr("{0} pulled up an enemy rod.", name));
                }

                return false;

            case AutoRodJob.Plant plant:
            {
                await WaitForIdleAsync();
                FocusStack(stack);

                RodPlantPromptViewModel prompt = new(name);

                Game.OpenScreen(prompt);

                switch (await prompt.Choice)
                {
                    case RodPlantPromptChoice.Plant:
                        WorldCommandResult planted = await GameModel.World.DispatchCommandAsync(new WorldCreateRodCommand(stackId, plant.Target));

                        if (planted.Failed)
                        {
                            Game.ShowError(WorldCommandErrorFormatter.Format(planted.Error.Value));
                        }
                        else
                        {
                            await WaitForIdleAsync();
                        }

                        return false;

                    case RodPlantPromptChoice.Skip:
                        if (!skippedRodSpots.TryGetValue(stackId, out HashSet<WorldPosition>? spots))
                        {
                            spots = [];
                            skippedRodSpots[stackId] = spots;
                        }

                        spots.Add(plant.Target);
                        reportedOrders.Remove(stackId);

                        return WorldState.Stacks.TryGetValue(stackId, out WorldStack? after) && after.MovementPoints > 0;

                    default:
                        StopExploring(stackId);
                        RefreshAutoExploreState();

                        return false;
                }
            }

            default:
                return false;
        }
    }

    // Start of the End turn walks: planters that already stand next to a job do it first (a skipped
    // spot gives them a new destination, which the walks then use).
    private async Task HandlePendingRodJobsAsync(List<OrderAttention> attention)
    {
        foreach (WorldStackId stackId in pendingRodJobs.Keys.ToList())
        {
            for (int round = 0; round < MaxRodJobsPerTurn; round++)
            {
                if (!await HandleRodJobAsync(stackId, attention) ||
                    !WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) ||
                    HasMoveOrder(stack) ||
                    TryAssignRodJob(stack, attention) ||
                    !pendingRodJobs.ContainsKey(stackId))
                {
                    break;
                }
            }
        }
    }

    // Skips in a row on one turn (each skip picks the next spot); a loop guard only.
    private const int MaxRodJobsPerTurn = 8;

    private void ForgetAutoRod(WorldStackId stackId)
    {
        if (roddingStacks.Remove(stackId))
        {
            pendingRodJobs.Remove(stackId);
            skippedRodSpots.Remove(stackId);
            toldShortOfGold.Remove(stackId);
            rodCheck = null;
        }
    }
}
