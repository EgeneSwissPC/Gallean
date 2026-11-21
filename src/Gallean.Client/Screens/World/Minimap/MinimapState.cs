using Avalonia.Media;
using Gallean.Common;

namespace Gallean.Client;

internal sealed record MinimapState(
    int MapWidth,
    int MapHeight,
    IReadOnlyList<WorldPosition> HiddenPositions,
    IReadOnlyList<MinimapState.Marker> Markers)
{
    public enum MarkerType
    {
        Capital,
        City,
        Stack
    }

    public sealed record Marker(
        WorldPosition Position,
        Color Color,
        MarkerType Type);

    public bool Equals(MinimapState? other)
    {
        if (other is null)
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return MapWidth == other.MapWidth &&
               MapHeight == other.MapHeight &&
               HiddenEquals(HiddenPositions, other.HiddenPositions) &&
               Markers.SequenceEqual(other.Markers);
    }

    public override int GetHashCode()
    {
        HashCode hash = new();

        hash.Add(MapWidth);
        hash.Add(MapHeight);
        // Only the count: hashing every hidden position costs a million steps on a 1024 map.
        hash.Add(HiddenPositions.Count);
        hash.AddSequence(Markers);

        return hash.ToHashCode();
    }

    private static bool HiddenEquals(IReadOnlyList<WorldPosition> left, IReadOnlyList<WorldPosition> right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        // A million hidden tiles on a 1024 map: compare the bitmaps' words, not the positions.
        if (left is HiddenPositionList leftList && right is HiddenPositionList rightList)
        {
            return leftList.Explored.Equals(rightList.Explored);
        }

        return left.SequenceEqual(right);
    }
}
