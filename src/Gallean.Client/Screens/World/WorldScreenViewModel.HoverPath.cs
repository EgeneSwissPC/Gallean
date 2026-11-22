using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: with the "show the army path under the cursor" setting on, the path of the
// selected army follows the mouse, and a single click on a tile moves the army there.
internal sealed partial class WorldScreenViewModel
{
    private sealed record HoverPathKey(WorldState State, WorldStackId StackId, WorldPosition Target, bool FogEnabled)
    {
        // Compare the world state by reference: a new state means a new path, and a deep
        // comparison of the whole world on every mouse move would be far too slow.
        public bool Equals(HoverPathKey? other)
        {
            return other is not null &&
                   ReferenceEquals(State, other.State) &&
                   StackId == other.StackId &&
                   Target == other.Target &&
                   FogEnabled == other.FogEnabled;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(StackId, Target, FogEnabled);
        }
    }

    private HoverPathKey? hoverPathKey;
    private WorldPathSelection? hoverPath;

    private WorldCursor ResolveRenderCursor()
    {
        UpdateHoverPath();

        if (hoverPath is null)
        {
            return cursor;
        }
        else
        {
            return cursor with { Path = hoverPath };
        }
    }

    private void UpdateHoverPath()
    {
        HoverPathKey? key = ResolveHoverPathKey();

        if (key is null)
        {
            hoverPathKey = null;
            hoverPath = null;
        }
        else if (key != hoverPathKey)
        {
            hoverPathKey = key;
            hoverPath = BuildPathSelection(WorldState.Stacks[key.StackId], key.Target);
        }
    }

    private HoverPathKey? ResolveHoverPathKey()
    {
        if (!Game.Settings.ShowPathOnHover || cursor.Action is not null || cursor.HoverTile is null || IsBusy)
        {
            return null;
        }

        WorldStack? stack = ResolveSelectedStack();

        if (stack is null)
        {
            return null;
        }

        // Move orders: an ordered army keeps showing its order path until the first map click.
        if (HasMoveOrder(stack) && !IsReplanning(stack))
        {
            return null;
        }

        WorldPosition? target = ResolveHoverPathTarget(stack, cursor.HoverTile.Value, cursor.HoverObject);

        if (target is null)
        {
            return null;
        }

        return new HoverPathKey(WorldState, stack.Id, target.Value, FogEnabled);
    }

    // Mirrors HandleMapClickAsync: no path where a click would do something else
    // (select another own army, open an own city) or where the army already stands.
    private WorldPosition? ResolveHoverPathTarget(WorldStack stack, WorldPosition tile, WorldPosition? objectPosition)
    {
        WorldPosition position = ResolveClickTarget(ResolvePointerTile(tile, objectPosition));

        if (!WorldState.Map.IsValidPosition(position) || position == stack.Position)
        {
            return null;
        }

        WorldPlan plan = WorldPlan.For(WorldState);
        WorldVisibility visibility = WorldVisibility.Create(WorldState, ViewPlayerId, FogEnabled);
        WorldStack? stackAtTarget = visibility.GetStackAt(plan, position, ViewPlayerId);

        if (stackAtTarget is not null && stackAtTarget.OwnerId == ViewPlayerId)
        {
            return null;
        }

        WorldFortification? fortification = ResolveFortificationBodyAt(position);

        if (fortification is not null && fortification.Owner == ViewPlayerId)
        {
            return null;
        }

        return position;
    }

    private bool TryMoveAlongHoverPath(WorldStack stack, WorldPosition position)
    {
        UpdateHoverPath();

        WorldPathSelection? path = hoverPath;

        if (path is null || path.Destination != position || hoverPathKey?.StackId != stack.Id)
        {
            return false;
        }

        // Remember the destination like a confirmed path, so the army keeps it when it runs
        // out of movement points and "Continue path" works on the next turn.
        SetSelectedPath(stack, path);
        MoveSelectedStackAsync(stack, path).FileAndForget();

        return true;
    }
}
