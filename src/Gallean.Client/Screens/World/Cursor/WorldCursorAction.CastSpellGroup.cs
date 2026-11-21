using System.Collections.Immutable;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: a spell group with at least one spell cast on the map. One click casts the whole group, in
// order, at that spot (spells without a target are cast too). The cursor is checked against the first targeted
// spell; WorldScreenViewModel dispatches the group itself.
internal closed partial record WorldCursorAction
{
    public sealed record CastSpellGroup(string Name, ImmutableArray<WorldSpellDefinitionId> Spells, WorldSpellDefinitionId FirstTargeted) : WorldCursorAction
    {
        public override WorldStackId? SelectedStackId => null;
        public override string SelectionText => Tr("Select a target for the spell group on the map.");

        public override IWorldCommand<Empty> CreateCommand(WorldPosition position)
        {
            return new WorldCastSpellCommand(FirstTargeted, new WorldSpellTarget(position, Radius: 0));
        }

        public bool Equals(CastSpellGroup? other)
        {
            return other is not null && Name == other.Name && Spells.SequenceEqual(other.Spells) && FirstTargeted == other.FirstTargeted;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Name, Spells.Length, FirstTargeted);
        }
    }
}
