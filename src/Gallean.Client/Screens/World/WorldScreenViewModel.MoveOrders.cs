using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: move orders (docs/move-orders.md). An army has a move order when the player
// confirmed a move to a stored destination that it hasn't reached yet. Ordered armies sit at the
// end of the army bar, Tab can skip them, O cycles through them and X cancels the order. With the
// hover path on, an ordered army keeps showing its order path; the first map click switches it to
// the hover path (re-plan mode) and the next click sets the new order.
internal sealed partial class WorldScreenViewModel
{
    private readonly HashSet<WorldStackId> confirmedOrders = [];
    private readonly Dictionary<WorldStackId, int> orderTurns = [];
    private readonly HashSet<WorldStackId> orderTurnsComputed = [];
    private WorldState? orderTurnsState;
    private WorldStackId? replanStackId;

    public bool HasMoveOrder(WorldStackId stackId)
    {
        return WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) && HasMoveOrder(stack);
    }

    private bool HasMoveOrder(WorldStack stack)
    {
        return confirmedOrders.Contains(stack.Id) &&
               stackDestinations.TryGetValue(stack.Id, out WorldPosition destination) &&
               destination != stack.Position;
    }

    private bool IsReplanning(WorldStack stack)
    {
        return replanStackId == stack.Id;
    }

    // A confirmed move becomes an order once the army stops short of its stored destination
    // (see EndMove), so a move that arrives this turn never flips the card to the order look.
    // An army without move points can't start walking: its confirmed path is an order at once.
    private void ConfirmMoveOrder(WorldStack stack, WorldPathSelection path)
    {
        replanStackId = null;

        ForgetOrderReport(stack.Id);

        if (stack.MovementPoints == 0)
        {
            TryConfirmMoveOrder(stack.Id, path.Destination);
        }
    }

    private void TryConfirmMoveOrder(WorldStackId stackId, WorldPosition destination)
    {
        if (WorldState.Stacks.TryGetValue(stackId, out WorldStack? stack) &&
            stack.Position != destination &&
            stackDestinations.TryGetValue(stackId, out WorldPosition stored) &&
            stored == destination &&
            confirmedOrders.Add(stackId))
        {
            orderTurnsState = null;
            NotifyMoveOrderCommandsChanged();
            UpdateStackActions();
            UpdateEndTurnHighlighted();
        }
    }


    // Called when the stored destination of an army changes or is removed.
    private void ForgetMoveOrder(WorldStackId stackId)
    {
        if (confirmedOrders.Remove(stackId))
        {
            orderTurns.Remove(stackId);
            orderTurnsState = null;
        }
    }

    private void ForgetMissingMoveOrders()
    {
        foreach (WorldStackId stackId in confirmedOrders.ToList())
        {
            if (!WorldState.Stacks.ContainsKey(stackId))
            {
                ForgetMoveOrder(stackId);
            }
        }
    }

    // First click on the map with an ordered army selected: show the hover path instead of
    // moving. Returns true when the click was used for that.
    private bool TryEnterReplan(WorldStack stack)
    {
        if (!Game.Settings.ShowPathOnHover || !HasMoveOrder(stack) || IsReplanning(stack))
        {
            return false;
        }

        replanStackId = stack.Id;
        hoverPathKey = null;
        hoverPath = null;
        RefreshRenderRequest();
        UpdateCursorType();

        return true;
    }

    // Turns in which the army still walks before it reaches its order destination (the current
    // turn counts when it has move points left for the first step). Kept from the last
    // successful calculation while the path can't be computed (e.g. during other players' turns).
    private int? GetOrderTurns(WorldStack stack)
    {
        if (!HasMoveOrder(stack))
        {
            return null;
        }

        if (!ReferenceEquals(orderTurnsState, WorldState))
        {
            orderTurnsState = WorldState;
            orderTurnsComputed.Clear();
        }

        if (orderTurnsComputed.Add(stack.Id) && Snapshot.IsViewPlayerTurn)
        {
            WorldPathSelection? path = BuildPathSelection(stack, stackDestinations[stack.Id]);
            if (path is not null)
            {
                IEnumerable<WorldPathSegment> segments = path.Path.Segments.Concat(path.Remainder.Segments);

                if (segments.Any())
                {
                    orderTurns[stack.Id] = segments.Last().Turn - segments.First().Turn + 1;
                }
            }
        }

        return orderTurns.TryGetValue(stack.Id, out int turns) ? turns : null;
    }

    [RelayCommand(CanExecute = nameof(CanCancelMoveOrder))]
    private void CancelMoveOrder()
    {
        WorldStack? stack = ResolveSelectedStack();

        if (stack is null)
        {
            return;
        }

        replanStackId = null;
        SetSelectedPath(stack, null);
    }

    private bool CanCancelMoveOrder()
    {
        if (IsBusy)
        {
            return false;
        }

        WorldStack? stack = ResolveSelectedStack();

        return stack is not null && HasMoveOrder(stack);
    }

    [RelayCommand(CanExecute = nameof(CanSelectNextOrderedStack))]
    private void SelectNextOrderedStack()
    {
        WorldStackItemViewModel? next = FindNextOrderedStackItem(StackItems, cursor.StackId);

        if (next is not null)
        {
            FocusStack(WorldState.Stacks[next.StackId]);
        }
    }

    private bool CanSelectNextOrderedStack()
    {
        return !IsBusy && FindNextOrderedStackItem(StackItems, cursor.StackId) is not null;
    }

    [RelayCommand(CanExecute = nameof(CanSelectPreviousOrderedStack))]
    private void SelectPreviousOrderedStack()
    {
        WorldStackItemViewModel? previous = FindNextOrderedStackItem([.. StackItems.Reverse()], cursor.StackId);

        if (previous is not null)
        {
            FocusStack(WorldState.Stacks[previous.StackId]);
        }
    }

    private bool CanSelectPreviousOrderedStack()
    {
        return !IsBusy && FindNextOrderedStackItem([.. StackItems.Reverse()], cursor.StackId) is not null;
    }

    private static WorldStackItemViewModel? FindNextOrderedStackItem(IReadOnlyList<WorldStackItemViewModel> items, WorldStackId? stackId)
    {
        int index = FindStackItemIndex(items, stackId);

        for (int offset = 1; offset <= items.Count; offset++)
        {
            WorldStackItemViewModel item = items[(index + offset).Mod(items.Count)];

            if (item.HasMoveOrder && item.StackId != stackId)
            {
                return item;
            }
        }

        return null;
    }

    private void NotifyMoveOrderCommandsChanged()
    {
        CancelMoveOrderCommand.NotifyCanExecuteChanged();
        SelectNextOrderedStackCommand.NotifyCanExecuteChanged();
        SelectPreviousOrderedStackCommand.NotifyCanExecuteChanged();
    }
}
