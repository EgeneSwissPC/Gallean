using System.Diagnostics.CodeAnalysis;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: move orders, phases 2-3 (docs/move-orders.md). On End turn the armies with an
// order walk one by one in bar order before the turn ends.
// - Before anyone walks, every order that would fight this turn is asked about, army by army in
//   bar order (Attack / Plan another path / Cancel order). Plan another path keeps the turn open.
// - A walk never starts a battle the player didn't accept and never acts as a thief on its own:
//   it stops one step before. A bag is picked up and the walk goes on.
// - After the walks, in bar order: armies stopped before a battle nobody saw coming (fog,
//   interception) are asked the same way; a thief next to its target opens the thief window; an
//   army that reached a site opens it.
// - Armies that end with move points left keep the turn open once (one alert lists them); the
//   next End turn ends the turn.
internal sealed partial class WorldScreenViewModel
{
    private enum OrderStop
    {
        None,
        Skipped,
        Blocked,
        Battle,
        Thief,
        Site,
        MovesLeft,
        Other
    }

    private sealed record OrderAttention(WorldStackId StackId, string Text);

    private readonly HashSet<WorldStackId> reportedOrders = [];
    private readonly HashSet<WorldStackId> acceptedAttacks = [];
    private readonly List<WorldStackId> cancelledOrders = [];
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
            acceptedAttacks.Clear();
        }

        List<OrderAttention> attention = [];

        // Auto explore: explorers without a destination pick their next one first.
        PrepareExplorers(attention);

        List<WorldStackId> ordered = [.. StackItems.Where(item => item.HasMoveOrder && HasMoveOrder(item.StackId)).Select(item => item.StackId)];

        if (ordered.Count == 0)
        {
            if (attention.Count == 0)
            {
                return true;
            }

            ReportOrderAttention(attention);

            return false;
        }

        cancelledOrders.Clear();
        SetAutoWalking(true);

        try
        {
            // 1. Planned attacks.
            foreach (WorldStackId stackId in ordered)
            {
                if (CanWalk(stackId, out WorldStack? stack) &&
                    !acceptedAttacks.Contains(stackId) &&
                    FindOrderBattle(stack) is { } target &&
                    !await AskAboutAttackAsync(stack, target))
                {
                    return false;
                }
            }

            // 2. The walks, then the retry pass for armies blocked by our own armies.
            // Explorers first, all at the same time in the background (auto explore), then the move orders one by one.
            List<WorldStackId> explorers = [.. ordered.Where(IsExploring)];
            Dictionary<WorldStackId, OrderStop> stops = explorers.Count > 0 ? await WalkExplorersInBackgroundAsync(explorers, attention) : [];
            List<WorldStackId> blocked = [];

            foreach (WorldStackId stackId in ordered.Where(stackId => !IsExploring(stackId) && !stops.ContainsKey(stackId)))
            {
                OrderStop stop = await WalkMoveOrderAsync(stackId);

                if (stop == OrderStop.Blocked)
                {
                    blocked.Add(stackId);
                }

                stops[stackId] = stop;
            }

            foreach (WorldStackId stackId in blocked)
            {
                stops[stackId] = await WalkMoveOrderAsync(stackId);
            }

            // 3. What needs the player, in bar order.
            foreach (WorldStackId stackId in ordered)
            {
                if (!await HandleOrderStopAsync(stackId, stops[stackId], attention))
                {
                    return false;
                }
            }

            // Orders cancelled in a prompt leave armies standing with move points: they count too.
            foreach (WorldStackId stackId in cancelledOrders)
            {
                if (WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) && stack.MovementPoints > 0 && !reportedOrders.Contains(stackId))
                {
                    AddOrderAttention(attention, stack, Tr("{0} stopped with {1} move points left.", ResolveLeaderName(stack), stack.MovementPoints));
                }
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

    private bool CanWalk(WorldStackId stackId, [NotNullWhen(true)] out WorldStack? stack)
    {
        return WorldState.Stacks.TryGetValue(stackId, out stack) &&
               stack.OwnerId == ViewPlayerId &&
               HasMoveOrder(stack) &&
               stack.MovementPoints > 0 &&
               !IsFinished(stack) &&
               !reportedOrders.Contains(stackId);
    }

    private async Task<OrderStop> WalkMoveOrderAsync(WorldStackId stackId)
    {
        if (!CanWalk(stackId, out _))
        {
            return OrderStop.Skipped;
        }

        WorldPosition destination = stackDestinations[stackId];
        bool guarded = !acceptedAttacks.Contains(stackId);

        if (guarded && FindAutoWalkStop(stackId, destination) is { } before)
        {
            return before;
        }

        ICommandResult<WorldCommandError, WorldEvent, Common.Empty> check =
            GameModel.World.DryRunCommand(new WorldStepStackCommand(stackId, destination));

        if (check.Failed)
        {
            // Not enough move points left for the next step: it walks on next turn.
            return check.Error.Value == WorldCommandError.InsufficientMovement ? OrderStop.Skipped : OrderStop.Blocked;
        }

        await WaitForIdleAsync();
        SelectStack(stackId);

        bool arrived = false;
        WorldStack? after = null;

        // A bag pick-up ends a move; the walk then goes on (a few bags in a row at most).
        for (int walk = 0; walk < 4; walk++)
        {
            int bags = WorldState.Bags.Count;

            arrived = await AutoWalkStackAsync(stackId, destination, guarded);

            await WaitForIdleAsync();

            if (!WorldState.Stacks.TryGetValue(stackId, out after))
            {
                return OrderStop.None;
            }

            if (arrived || after.MovementPoints <= 0 || WorldState.Bags.Count >= bags ||
                FindAutoWalkStop(stackId, destination) is not null ||
                GameModel.World.DryRunCommand(new WorldStepStackCommand(stackId, destination)).Failed)
            {
                break;
            }
        }

        if (after is null || after.OwnerId != ViewPlayerId)
        {
            return OrderStop.None;
        }

        if (arrived)
        {
            if (WorldPlan.For(WorldState).GetSiteAt(destination) is not null)
            {
                return OrderStop.Site;
            }

            return after.MovementPoints > 0 ? OrderStop.MovesLeft : OrderStop.None;
        }

        if (after.MovementPoints <= 0 || !HasMoveOrder(after))
        {
            return OrderStop.None;
        }

        if (FindAutoWalkStop(stackId, destination) is { } stop)
        {
            return guarded || stop == OrderStop.Site ? stop : OrderStop.Other;
        }

        ICommandResult<WorldCommandError, WorldEvent, Common.Empty> next =
            GameModel.World.DryRunCommand(new WorldStepStackCommand(stackId, destination));

        // Stopped by something else (a scenario event, a blocked tile) with move points left.
        return !next.Failed || next.Error.Value != WorldCommandError.InsufficientMovement ? OrderStop.Other : OrderStop.None;
    }

    // Returns false when the turn has to stay open right away (the player plans another path).
    private async Task<bool> HandleOrderStopAsync(WorldStackId stackId, OrderStop stop, List<OrderAttention> attention)
    {
        if (!WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) ||
            stack.OwnerId != ViewPlayerId ||
            reportedOrders.Contains(stackId))
        {
            return true;
        }

        string name = ResolveLeaderName(stack);

        switch (stop)
        {
            case OrderStop.Battle:
            {
                if (FindOrderBattle(stack) is not { } target)
                {
                    AddOrderAttention(attention, stack, Tr("{0} stopped: something is in its way.", name));

                    return true;
                }

                if (!await AskAboutAttackAsync(stack, target))
                {
                    return false;
                }

                if (acceptedAttacks.Contains(stackId))
                {
                    reportedOrders.Add(stackId);
                    await WalkAcceptedAsync(stackId);
                }

                return true;
            }

            case OrderStop.Thief:
            {
                reportedOrders.Add(stackId);

                if (BuildPathSelection(stack, stackDestinations[stackId])?.Encounter is WorldPathEncounter.Thief thief)
                {
                    FocusStack(stack);
                    EnqueueEntry(new WorldEventQueue.Entry.ThiefAction(WorldState, stackId, thief.TargetPosition));
                    await WaitForIdleAsync();
                }

                return true;
            }

            case OrderStop.Site:
            {
                reportedOrders.Add(stackId);

                if (WorldPlan.For(WorldState).GetSiteEntranceAt(stack.Position) is { } site)
                {
                    FocusStack(stack);
                    EnqueueEntry(new WorldEventQueue.Entry.SiteEnter(WorldState, stackId, site.Id));
                    await WaitForIdleAsync();

                    if (WorldState.Stacks.TryGetValue(stackId, out WorldStack? after) && after.MovementPoints > 0)
                    {
                        AddOrderAttention(attention, after, Tr("{0} reached {1}.", name, GameModel.Texts.GetScenarioText(site.TitleTextId)));
                    }
                }

                return true;
            }

            case OrderStop.MovesLeft:
                AddOrderAttention(attention, stack, Tr("{0} arrived with {1} move points left.", name, stack.MovementPoints));

                return true;

            case OrderStop.Blocked:
                AddOrderAttention(attention, stack, Tr("{0} could not continue its path.", name));

                return true;

            case OrderStop.Other:
                AddOrderAttention(attention, stack, Tr("{0} stopped with {1} move points left.", name, stack.MovementPoints));

                return true;

            default:
                return true;
        }
    }

    // Asks Attack / Plan another path / Cancel order. Returns false for Plan another path.
    private async Task<bool> AskAboutAttackAsync(WorldStack stack, WorldPosition target)
    {
        FocusStack(stack);

        (WorldPositionInfoViewModel? info, string targetName, bool isKnown) = ResolveOrderTarget(target);
        MoveOrderPromptViewModel prompt = new(info, ResolveLeaderName(stack), targetName, isKnown);

        Game.OpenScreen(prompt);

        switch (await prompt.Choice)
        {
            case MoveOrderPromptChoice.Attack:
                acceptedAttacks.Add(stack.Id);

                return true;

            case MoveOrderPromptChoice.CancelOrder:
                StopExploring(stack.Id);
                SetSelectedPath(stack, null);
                cancelledOrders.Add(stack.Id);

                return true;

            default:
                reportedOrders.Remove(stack.Id);

                // Re-plan mode right away: the path follows the mouse, the next click sets it.
                if (Game.Settings.ShowPathOnHover && HasMoveOrder(stack))
                {
                    replanStackId = stack.Id;
                    hoverPathKey = null;
                    hoverPath = null;
                    RefreshRenderRequest();
                }

                return false;
        }
    }

    private async Task WalkAcceptedAsync(WorldStackId stackId)
    {
        if (WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) && HasMoveOrder(stack) && stack.MovementPoints > 0)
        {
            SelectStack(stackId);
            await AutoWalkStackAsync(stackId, stackDestinations[stackId], guarded: false);
            await WaitForIdleAsync();
        }
    }

    // The battle this order runs into this turn, if any (WorldGetPathCommand only reports battles
    // the army can reach with the move points it has).
    private WorldPosition? FindOrderBattle(WorldStack stack)
    {
        return BuildPathSelection(stack, stackDestinations[stack.Id])?.Encounter is WorldPathEncounter.Battle battle
            ? battle.TargetPosition
            : null;
    }

    // Same name as the game's own attack prompt (CreateAttackPrompt), plus the info the world's
    // right-click popup shows for that army or city (units only when the player may see them).
    private (WorldPositionInfoViewModel? Info, string Name, bool IsKnown) ResolveOrderTarget(WorldPosition position)
    {
        WorldPlan plan = WorldPlan.For(WorldState);
        WorldVisibility visibility = WorldVisibility.Create(WorldState, ViewPlayerId, FogEnabled);

        if (visibility.GetStackAt(plan, position, ViewPlayerId) is { } enemy)
        {
            (WorldUnit leader, GridPosition _, UnitDefinition _) = enemy.GetLeader(GameModel.Definitions);

            return (CreateStackInfo(enemy.Id),
                    UnitNameResolver.Resolve(leader, GameModel.Definitions, GameModel.Texts),
                    true);
        }

        if (visibility.GetFortificationAt(plan, position) is { } fortification)
        {
            return (CreatePositionInfo(position, plan, visibility), GameModel.Texts.GetScenarioText(fortification.NameTextId), true);
        }

        return (null, Tr("an unknown enemy"), false);
    }

    // Like MoveStackAsync, but quiet on errors and, when guarded, checked before every step (see StepMoveAsync).
    private async Task<bool> AutoWalkStackAsync(WorldStackId stackId, WorldPosition destination, bool guarded)
    {
        WorldCommandResult commandResult = await GameModel.World.DispatchCommandAsync(new WorldStepStackCommand(stackId, destination));

        if (commandResult.Failed)
        {
            return false;
        }

        ActiveMove move = new(stackId, destination, new TaskCompletionSource<bool>(), Guarded: guarded);
        activeMove = move;
        FinishMoveStep(move, commandResult);

        return await move.Completion.Task;
    }

    // What stops a guarded walk before its next step: a battle (an army, a city, an interception:
    // the engine resolves an encounter at the end of the approach), or a thief already next to its
    // target. Null when the next step is safe.
    private OrderStop? FindAutoWalkStop(WorldStackId stackId, WorldPosition destination)
    {
        WorldStack stack = WorldState.Stacks[stackId];
        WorldPathSelection? path = BuildPathSelection(stack, destination);

        return path?.Encounter switch
        {
            WorldPathEncounter.Battle when path.Path.Segments.Length <= 1 => OrderStop.Battle,
            WorldPathEncounter.Thief when path.Path.Segments.IsEmpty => OrderStop.Thief,
            // Since upstream 0.17 a tile next to the entrance counts as the site (651e4200): stop there and open it.
            WorldPathEncounter.Site when path.Path.Segments.IsEmpty => OrderStop.Site,
            _ => null
        };
    }

    private bool IsAutoWalkBlocked(WorldStackId stackId, WorldPosition destination)
    {
        return FindAutoWalkStop(stackId, destination) is not null;
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
        if (eventQueue.IsIdle && activeMove is null && IsActive)
        {
            return Task.CompletedTask;
        }

        idleWaiter ??= new TaskCompletionSource();

        return idleWaiter.Task;
    }

    private void SignalIdle()
    {
        if (idleWaiter is not null && eventQueue.IsIdle && activeMove is null && IsActive)
        {
            TaskCompletionSource waiter = idleWaiter;
            idleWaiter = null;
            waiter.TrySetResult();
        }
    }

    // A new or changed order is asked about and reported again.
    private void ForgetOrderReport(WorldStackId stackId)
    {
        reportedOrders.Remove(stackId);
        acceptedAttacks.Remove(stackId);
    }
}
