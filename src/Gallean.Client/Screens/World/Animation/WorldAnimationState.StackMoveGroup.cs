using Gallean.Common;

namespace Gallean.Client;

internal closed partial record WorldAnimationState
{
    // Gallean Upgrade: several armies walking at the same time (auto explore, docs/auto-explore.md). No focus, so
    // the camera stays where the player left it.
    public sealed record StackMoveGroup(IReadOnlyList<StackMove> Moves) : WorldAnimationState
    {
        public bool Equals(StackMoveGroup? other)
        {
            return other is not null && (ReferenceEquals(this, other) || Moves.SequenceEqual(other.Moves));
        }

        public override int GetHashCode()
        {
            HashCode hash = new();

            hash.AddSequence(Moves);

            return hash.ToHashCode();
        }
    }
}
