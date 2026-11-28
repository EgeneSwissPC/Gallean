using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: auto explore (docs/auto-explore.md). An exploring army is a move order that picks
// its next destination itself (AutoExploreCalculator in the engine) each time it arrives. It walks
// right away when Explore is pressed (L or the card button) and then on every End turn with the
// move orders, so the order prompt, bag pick-ups and site stops all work as for move orders. It
// stops for the turn when it sees a new enemy army near it. When nothing this army can reach is
// left hidden, the player is told and the army becomes a normal army; the button is greyed for it.
// Not saved, like move orders.
internal sealed partial class WorldScreenViewModel
{
    // Each leg ends at an edge of the fog, often only a tile or two away; the cap only guards against a loop.
    private const int MaxExploreLegsPerTurn = 64;

    private readonly HashSet<WorldStackId> exploringStacks = [];
    private (WorldState State, WorldStackId StackId, bool CanExplore)? exploreCheck;
    private bool isSettingExploreDestination;
    private bool holdEventQueue;

    public bool IsExploring(WorldStackId stackId)
    {
        return exploringStacks.Contains(stackId);
    }

    [RelayCommand(CanExecute = nameof(CanToggleAutoExplore))]
    private async Task ToggleAutoExploreAsync()
    {
        WorldStack? stack = ResolveSelectedStack();

        if (stack is null)
        {
            return;
        }

        if (IsExploring(stack.Id))
        {
            StopExploring(stack.Id);

            if (HasMoveOrder(stack))
            {
                SetSelectedPath(stack, null);
            }

            RefreshAutoExploreState();

            return;
        }

        ICommandResult<WorldCommandError, WorldEvent, AutoExploreResult> result =
            GameModel.World.DryRunCommand(new WorldGetAutoExploreTargetCommand(stack.Id));

        if (result.Failed)
        {
            return;
        }

        if (result.Data is AutoExploreResult.NothingLeft)
        {
            Game.ShowInfo(IconMoniker.Vision, Tr("No more land to explore"), Tr("{0} has no more land it can reach.", ResolveLeaderName(stack)));
            RefreshAutoExploreState();

            return;
        }

        exploringStacks.Add(stack.Id);
        ForgetOrderReport(stack.Id);
        RefreshAutoExploreState();

        if (stack.MovementPoints > 0 && !IsFinished(stack))
        {
            await ExploreNowAsync(stack.Id);
        }
    }

    private bool CanToggleAutoExplore()
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

        return IsExploring(stack.Id) || CanExplore(stack);
    }

    // Cached per state and army: the button's CanExecute is asked often.
    private bool CanExplore(WorldStack stack)
    {
        if (exploreCheck is { } check && ReferenceEquals(check.State, WorldState) && check.StackId == stack.Id)
        {
            return check.CanExplore;
        }

        ICommandResult<WorldCommandError, WorldEvent, AutoExploreResult> result =
            GameModel.World.DryRunCommand(new WorldGetAutoExploreTargetCommand(stack.Id));
        bool canExplore = !result.Failed && result.Data is not AutoExploreResult.NothingLeft;

        exploreCheck = (WorldState, stack.Id, canExplore);

        return canExplore;
    }

    // Explore right away with the move points the army has (decision A1).
    private async Task ExploreNowAsync(WorldStackId stackId)
    {
        if (reportedOrdersTurn != WorldState.TurnInfo)
        {
            reportedOrdersTurn = WorldState.TurnInfo;
            reportedOrders.Clear();
            acceptedAttacks.Clear();
        }

        List<OrderAttention> attention = [];

        cancelledOrders.Clear();
        SetAutoWalking(true);

        try
        {
            OrderStop stop = await WalkExplorerAsync(stackId, attention);

            if (!await HandleOrderStopAsync(stackId, stop, attention))
            {
                return;
            }
        }
        finally
        {
            SetAutoWalking(false);
        }

        if (attention.Count > 0)
        {
            ReportOrderAttention(attention);
        }
        else if (WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack))
        {
            FocusStack(stack);
        }
    }

    // Gives every exploring army without a destination its next one (start of the End turn walks).
    private void PrepareExplorers(List<OrderAttention> attention)
    {
        foreach (WorldStackId stackId in exploringStacks.ToList())
        {
            if (WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) &&
                stack.OwnerId == ViewPlayerId &&
                stack.MovementPoints > 0 &&
                !IsFinished(stack) &&
                !reportedOrders.Contains(stackId) &&
                !HasMoveOrder(stack))
            {
                TryAssignExploreTarget(stack, attention);
            }
        }
    }

    // Walks an exploring army: to its destination, then on to the next one while it has move points.
    private async Task<OrderStop> WalkExplorerAsync(WorldStackId stackId, List<OrderAttention> attention)
    {
        HashSet<WorldStackId> knownEnemies = FindSeenHostileStacks();
        OrderStop stop = OrderStop.Skipped;

        for (int leg = 0; leg < MaxExploreLegsPerTurn; leg++)
        {
            if (!WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) || !IsExploring(stackId))
            {
                return OrderStop.None;
            }

            if (!HasMoveOrder(stack) && !TryAssignExploreTarget(stack, attention))
            {
                return leg == 0 ? OrderStop.Skipped : OrderStop.None;
            }

            stop = await WalkMoveOrderAsync(stackId);

            if (!WorldState.Stacks.TryGetValue(stackId, out WorldStack? after) || after.OwnerId != ViewPlayerId || !IsExploring(stackId))
            {
                return OrderStop.None;
            }

            if (SpottedNewEnemy(after, knownEnemies))
            {
                AddOrderAttention(attention, after, Tr("{0} spotted an enemy army.", ResolveLeaderName(after)));

                return OrderStop.None;
            }

            // Arrived with move points left: pick the next destination and go on. Anything else ends the walk.
            if (stop is not (OrderStop.None or OrderStop.MovesLeft) || after.MovementPoints <= 0 || HasMoveOrder(after))
            {
                return stop == OrderStop.MovesLeft ? OrderStop.None : stop;
            }
        }

        return OrderStop.None;
    }

    // Decision (user, after phase 2): during End turn all explorers walk in the background at the same time. Their
    // moves are made in the engine first with the event queue held, then their walks play as one group animation
    // without moving the camera. Questions (battle, site) still come one by one afterwards, like move orders.
    private async Task<Dictionary<WorldStackId, OrderStop>> WalkExplorersInBackgroundAsync(
        IReadOnlyList<WorldStackId> explorers,
        List<OrderAttention> attention)
    {
        Dictionary<WorldStackId, OrderStop> stops = [];
        HashSet<WorldStackId> walked = [];

        await WaitForIdleAsync();

        holdEventQueue = true;

        try
        {
            HashSet<WorldStackId> knownEnemies = FindSeenHostileStacks();

            foreach (WorldStackId stackId in explorers)
            {
                stops[stackId] = await StepExplorerAsync(stackId, attention, knownEnemies, walked);
            }

            // Retry pass for explorers blocked by our own armies, as for move orders.
            foreach (WorldStackId stackId in explorers)
            {
                if (stops[stackId] == OrderStop.Blocked)
                {
                    stops[stackId] = await StepExplorerAsync(stackId, attention, knownEnemies, walked);
                }
            }
        }
        finally
        {
            holdEventQueue = false;
            eventQueue.GroupStackMoves(walked);
            ProcessEventQueue();
        }

        await WaitForIdleAsync();

        return stops;
    }

    private async Task<OrderStop> StepExplorerAsync(
        WorldStackId stackId,
        List<OrderAttention> attention,
        HashSet<WorldStackId> knownEnemies,
        HashSet<WorldStackId> walked)
    {
        for (int leg = 0; leg < MaxExploreLegsPerTurn; leg++)
        {
            if (!WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) ||
                stack.OwnerId != ViewPlayerId ||
                !IsExploring(stackId) ||
                stack.MovementPoints <= 0 ||
                IsFinished(stack) ||
                reportedOrders.Contains(stackId))
            {
                return leg == 0 ? OrderStop.Skipped : OrderStop.None;
            }

            if (!HasMoveOrder(stack) && !TryAssignExploreTarget(stack, attention))
            {
                return leg == 0 ? OrderStop.Skipped : OrderStop.None;
            }

            if (await StepToDestinationAsync(stackId, stackDestinations[stackId], attention, knownEnemies, walked) is { } stop)
            {
                return stop;
            }
        }

        return OrderStop.None;
    }

    // Steps without animation until the army stops. Null = arrived with move points left (the next leg follows).
    private async Task<OrderStop?> StepToDestinationAsync(
        WorldStackId stackId,
        WorldPosition destination,
        List<OrderAttention> attention,
        HashSet<WorldStackId> knownEnemies,
        HashSet<WorldStackId> walked)
    {
        while (true)
        {
            if (FindAutoWalkStop(stackId, destination) is { } before)
            {
                return before;
            }

            WorldCommandResult result = await GameModel.World.DispatchCommandAsync(new WorldStepStackCommand(stackId, destination));

            if (result.Failed)
            {
                return result.Error == WorldCommandError.InsufficientMovement ? OrderStop.None : OrderStop.Blocked;
            }

            walked.Add(stackId);

            if (!WorldState.Stacks.TryGetValue(stackId, out WorldStack? after) || after.OwnerId != ViewPlayerId)
            {
                return OrderStop.None;
            }

            if (SpottedNewEnemy(after, knownEnemies))
            {
                AddOrderAttention(attention, after, Tr("{0} spotted an enemy army.", ResolveLeaderName(after)));

                return OrderStop.None;
            }

            if (after.Position == destination)
            {
                if (WorldPlan.For(WorldState).GetSiteAt(destination) is not null)
                {
                    return OrderStop.Site;
                }

                return after.MovementPoints > 0 ? null : OrderStop.None;
            }

            // Stopped by something other than a step (a scenario event, a bag): the player is told.
            if (result.Events is not [.., WorldStackMovedEvent moved] || moved.StackId != stackId)
            {
                return after.MovementPoints > 0 ? OrderStop.Other : OrderStop.None;
            }

            if (after.MovementPoints <= 0)
            {
                return OrderStop.None;
            }
        }
    }

    // Returns true when the army got a destination.
    private bool TryAssignExploreTarget(WorldStack stack, List<OrderAttention>? attention)
    {
        ICommandResult<WorldCommandError, WorldEvent, AutoExploreResult> result =
            GameModel.World.DryRunCommand(new WorldGetAutoExploreTargetCommand(stack.Id));

        if (result.Failed)
        {
            return false;
        }

        switch (result.Data)
        {
            case AutoExploreResult.Target target:
                SetExploreDestination(stack, target.Position);

                return true;

            case AutoExploreResult.NothingLeft:
                StopExploring(stack.Id);
                RefreshAutoExploreState();

                if (attention is not null)
                {
                    AddOrderAttention(attention, stack, Tr("{0} has no more land to explore.", ResolveLeaderName(stack)));
                }

                return false;

            default:
                return false;
        }
    }

    private void SetExploreDestination(WorldStack stack, WorldPosition destination)
    {
        isSettingExploreDestination = true;

        try
        {
            if (stack.Id == cursor.StackId && BuildPathSelection(stack, destination) is { } path)
            {
                SetSelectedPath(stack, path);
            }
            else
            {
                ForgetMoveOrder(stack.Id);
                stackDestinations = stackDestinations.SetItem(stack.Id, destination);
            }

            confirmedOrders.Add(stack.Id);
            orderTurnsState = null;
            NotifyMoveOrderCommandsChanged();
            UpdateStackActions();
            UpdateEndTurnHighlighted();
        }
        finally
        {
            isSettingExploreDestination = false;
        }
    }

    // The player took the army over (a new path, X / Stop, Cancel order in the prompt, Explore again).
    private void StopExploring(WorldStackId stackId)
    {
        if (!isSettingExploreDestination && exploringStacks.Remove(stackId))
        {
            orderTurnsState = null;
            NotifyMoveOrderCommandsChanged();
            UpdateStackActions();
            UpdateEndTurnHighlighted();
        }
    }

    private void ForgetMissingExplorers()
    {
        exploringStacks.RemoveWhere(stackId => !WorldState.Stacks.ContainsKey(stackId));
    }

    private void RefreshAutoExploreState()
    {
        exploreCheck = null;
        ToggleAutoExploreCommand.NotifyCanExecuteChanged();
        UpdateStackActions();
        UpdateEndTurnHighlighted();
    }

    // Hostile armies on tiles the player has seen (the same armies the explorer keeps its distance from).
    private HashSet<WorldStackId> FindSeenHostileStacks()
    {
        HashSet<WorldStackId> result = [];
        ImmutableBitmap fog = WorldState.Players[ViewPlayerId].FogOfWar;
        WorldPlan plan = WorldPlan.For(WorldState);

        foreach (WorldStack other in WorldState.Stacks.Values)
        {
            if (IsHostileTo(other) && fog.Get(other.Position.X, other.Position.Y) && plan.GetFortificationAt(other.Position) is null)
            {
                result.Add(other.Id);
            }
        }

        return result;
    }

    // Decision C1: an enemy army seen for the first time during this walk within the safety distance stops the explorer for the turn.
    private bool SpottedNewEnemy(WorldStack explorer, HashSet<WorldStackId> knownEnemies)
    {
        int distance = AutoExploreSettings.Default.SafetyDistance;
        bool spotted = false;

        foreach (WorldStackId stackId in FindSeenHostileStacks())
        {
            if (knownEnemies.Add(stackId) &&
                WorldState.Stacks[stackId].Position is var position &&
                Math.Max(Math.Abs(position.X - explorer.Position.X), Math.Abs(position.Y - explorer.Position.Y)) <= distance)
            {
                spotted = true;
            }
        }

        return spotted;
    }

    private bool IsHostileTo(WorldStack other)
    {
        if (other.OwnerId == ViewPlayerId)
        {
            return false;
        }

        WorldRelationId relationId = new(WorldState.Players[ViewPlayerId].Race, WorldState.Players[other.OwnerId].Race);

        return !WorldState.Relations.TryGetValue(relationId, out WorldRelation? relation) || !relation.Alliance;
    }
}
