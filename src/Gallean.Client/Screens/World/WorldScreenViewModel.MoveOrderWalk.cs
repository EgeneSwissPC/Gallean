using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: move orders, phase 2 (docs/move-orders.md). On End turn the armies with an order
// walk one by one in bar order before the turn ends. A walk never starts a battle or acts as a thief
// on its own: it stops one step before. A bag on the way is picked up (the game shows its treasure
// popup) and the walk goes on (decision H). Armies that stop with move points left
// (something in the way, arrived early, reached a site, still blocked after the retry pass) keep
// the turn open and are reported once; the next End turn ends the turn.
internal sealed partial class WorldScreenViewModel
{
    private enum OrderWalkResult
    {
        Skipped,
        Walked,
        Blocked
    }

    private sealed record OrderAttention(WorldStackId StackId, string Text);

    private readonly HashSet<WorldStackId> reportedOrders = [];
    private WorldTurnInfo? reportedOrdersTurn;
    private TaskCompletionSource? idleWaiter;
    private bool isAutoWalking;

    public bool IsAutoWalking => isAutoWalking;

    // Returns true when the turn may end.
    private async Task<bool> WalkMoveOrdersAsync()
    {
        if (reportedOrdersTurn != WorldState.TurnInfo)
        {
            reportedOrdersTurn = WorldState.TurnInfo;
            reportedOrders.Clear();
        }

        List<WorldStackId> ordered = [.. StackItems.Where(item => item.HasMoveOrder).Select(item => item.StackId)];

        if (ordered.Count == 0)
        {
            return true;
        }

        List<OrderAttention> attention = [];

        SetAutoWalking(true);

        try
        {
            List<WorldStackId> blocked = [];

            foreach (WorldStackId stackId in ordered)
            {
                if (await WalkMoveOrderAsync(stackId, attention, isRetry: false) == OrderWalkResult.Blocked)
                {
                    blocked.Add(stackId);
                }
            }

            // Retry pass: an army blocked by one of our own armies gets through once that one has walked.
            foreach (WorldStackId stackId in blocked)
            {
                await WalkMoveOrderAsync(stackId, attention, isRetry: true);
            }
        }
        finally
        {
            SetAutoWalking(false);
        }

        if (attention.Count == 0)
        {
            return true;
        }

        ReportOrderAttention(attention);

        return false;
    }

    private async Task<OrderWalkResult> WalkMoveOrderAsync(WorldStackId stackId, List<OrderAttention> attention, bool isRetry)
    {
        if (!WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) ||
            stack.OwnerId != ViewPlayerId ||
            !HasMoveOrder(stack) ||
            stack.MovementPoints <= 0 ||
            IsFinished(stack) ||
            reportedOrders.Contains(stackId))
        {
            return OrderWalkResult.Skipped;
        }

        WorldPosition destination = stackDestinations[stackId];

        if (IsAutoWalkBlocked(stackId, destination))
        {
            AddOrderAttention(attention, stack, Tr("{0} stopped: something is in its way.", ResolveLeaderName(stack)));

            return OrderWalkResult.Skipped;
        }

        ICommandResult<WorldCommandError, WorldEvent, Common.Empty> check =
            GameModel.World.DryRunCommand(new WorldStepStackCommand(stackId, destination));

        if (check.Failed)
        {
            if (check.Error.Value == WorldCommandError.InsufficientMovement)
            {
                // Not enough move points left for the next step: it walks on next turn.
                return OrderWalkResult.Skipped;
            }

            if (isRetry)
            {
                AddOrderAttention(attention, stack, Tr("{0} could not continue its path.", ResolveLeaderName(stack)));
            }

            return OrderWalkResult.Blocked;
        }

        await WaitForIdleAsync();
        SelectStack(stackId);

        bool arrived = false;
        WorldStack? after = null;

        // A bag pick-up ends a move; the walk then goes on (a few bags in a row at most).
        for (int walk = 0; walk < 4; walk++)
        {
            int bags = WorldState.Bags.Count;

            arrived = await AutoWalkStackAsync(stackId, destination);

            await WaitForIdleAsync();

            if (!WorldState.Stacks.TryGetValue(stackId, out after))
            {
                return OrderWalkResult.Walked;
            }

            if (arrived || after.MovementPoints <= 0 || WorldState.Bags.Count >= bags ||
                IsAutoWalkBlocked(stackId, destination) ||
                GameModel.World.DryRunCommand(new WorldStepStackCommand(stackId, destination)).Failed)
            {
                break;
            }
        }

        if (after is null)
        {
            return OrderWalkResult.Walked;
        }

        string name = ResolveLeaderName(after);

        if (arrived)
        {
            WorldSite? site = WorldPlan.For(WorldState).GetSiteAt(destination);

            if (site is not null)
            {
                AddOrderAttention(attention, after, Tr("{0} reached {1}.", name, GameModel.Texts.GetScenarioText(site.TitleTextId)));
            }
            else if (after.MovementPoints > 0)
            {
                AddOrderAttention(attention, after, Tr("{0} arrived with {1} move points left.", name, after.MovementPoints));
            }
        }
        else if (after.MovementPoints > 0)
        {
            ICommandResult<WorldCommandError, WorldEvent, Common.Empty> next =
                GameModel.World.DryRunCommand(new WorldStepStackCommand(stackId, destination));

            if (IsAutoWalkBlocked(stackId, destination))
            {
                AddOrderAttention(attention, after, Tr("{0} stopped: something is in its way.", name));
            }
            else if (!next.Failed || next.Error.Value != WorldCommandError.InsufficientMovement)
            {
                // Stopped by something else (a scenario event, a blocked tile) with move points left.
                AddOrderAttention(attention, after, Tr("{0} stopped with {1} move points left.", name, after.MovementPoints));
            }
        }

        return OrderWalkResult.Walked;
    }

    // Like MoveStackAsync, but quiet on errors and guarded on every step (see StepMoveAsync).
    private async Task<bool> AutoWalkStackAsync(WorldStackId stackId, WorldPosition destination)
    {
        WorldCommandResult commandResult = await GameModel.World.DispatchCommandAsync(new WorldStepStackCommand(stackId, destination));

        if (commandResult.Failed)
        {
            return false;
        }

        ActiveMove move = new(stackId, destination, new TaskCompletionSource<bool>(), Guarded: true);
        activeMove = move;
        FinishMoveStep(move, commandResult);

        return await move.Completion.Task;
    }

    // True when the next step towards the destination would start a battle, or the thief already
    // stands next to its target: the engine resolves an encounter at the end of the approach.
    private bool IsAutoWalkBlocked(WorldStackId stackId, WorldPosition destination)
    {
        WorldStack stack = WorldState.Stacks[stackId];
        WorldPathSelection? path = BuildPathSelection(stack, destination);

        return path?.Encounter switch
        {
            WorldPathEncounter.Battle => path.Path.Segments.Length <= 1,
            WorldPathEncounter.Thief => path.Path.Segments.IsEmpty,
            _ => false
        };
    }

    private void AddOrderAttention(List<OrderAttention> attention, WorldStack stack, string text)
    {
        reportedOrders.Add(stack.Id);
        attention.Add(new OrderAttention(stack.Id, text));
    }

    private void ReportOrderAttention(List<OrderAttention> attention)
    {
        string caption = attention.Count == 1
            ? Tr("An army with an order needs you")
            : Tr("{0} armies with orders need you", attention.Count);
        string details = string.Join("\n", attention.Select(item => item.Text)) + "\n" + Tr("End the turn again to continue.");

        Game.ShowInfo(IconMoniker.Warning, caption, details);

        if (WorldState.Stacks.TryGetValue(attention[0].StackId, out WorldStack? first))
        {
            FocusStack(first);
        }
    }

    private string ResolveLeaderName(WorldStack stack)
    {
        (WorldUnit leader, GridPosition _, UnitDefinition _) = stack.GetLeader(GameModel.Definitions);

        return UnitNameResolver.Resolve(leader, GameModel.Definitions, GameModel.Texts);
    }

    private void SetAutoWalking(bool value)
    {
        isAutoWalking = value;

        if (!value)
        {
            IsFastForward = false;
        }

        FastForwardCommand.NotifyCanExecuteChanged();
        NotifySnapshotGatedCommandsChanged();
    }

    private Task WaitForIdleAsync()
    {
        if (eventQueue.IsIdle && activeMove is null)
        {
            return Task.CompletedTask;
        }

        idleWaiter ??= new TaskCompletionSource();

        return idleWaiter.Task;
    }

    private void SignalIdle()
    {
        if (idleWaiter is not null && eventQueue.IsIdle && activeMove is null)
        {
            TaskCompletionSource waiter = idleWaiter;
            idleWaiter = null;
            waiter.TrySetResult();
        }
    }

    private void ForgetOrderReport(WorldStackId stackId)
    {
        reportedOrders.Remove(stackId);
    }
}
