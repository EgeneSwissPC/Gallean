using Gallean.Common;

namespace Gallean.Engine;

internal sealed partial class WorldAiPlanner
{
    private sealed record ResearchSpellAction(WorldAiContext Context, int Priority) : AbstractEconomyAction(Context, Priority)
    {
        private const int ResearchTurnInterval = 5;
        private const int MaxResearchLevel = 4;
        private const int MaxKnownSpells = 12;
        private const int SpendingMaxResearchLevel = 5;

        private static readonly WorldAiSpellEffectType[] ResearchTypes =
        [
            WorldAiSpellEffectType.Summon,
            WorldAiSpellEffectType.Attack,
            WorldAiSpellEffectType.Heal,
            WorldAiSpellEffectType.Weaken,
            WorldAiSpellEffectType.RestoreMovement
        ];

        private static readonly WorldAiSpellEffectType[] SpendingResearchTypes =
        [
            WorldAiSpellEffectType.Summon,
            WorldAiSpellEffectType.Attack,
            WorldAiSpellEffectType.Heal,
            WorldAiSpellEffectType.Weaken,
            WorldAiSpellEffectType.Boost,
            WorldAiSpellEffectType.RestoreMovement,
            WorldAiSpellEffectType.Invisibility
        ];

        public override WorldAiActionPlan? Plan(WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
        {
            WorldPlayer player = state.Players[playerId];

            // Gallean Upgrade, overflow spending: research every turn, without the spell count and level caps
            // (the lord's own level limit still applies), boosts and invisibility included.
            bool spending = WorldAiSpending.IsManaSpending(Context.Profile, player, Context.Engine.Globals);

            if (!spending && state.TurnInfo.TurnNumber % ResearchTurnInterval != 0)
            {
                return null;
            }

            if (!spending && player.Spellbook.Acquired.Count >= MaxKnownSpells)
            {
                return null;
            }

            int maxLevel = spending ? SpendingMaxResearchLevel : MaxResearchLevel;
            WorldAiSpellEffectType[] types = spending ? SpendingResearchTypes : ResearchTypes;

            for (int level = 1; level <= maxLevel; level++)
            {
                foreach (WorldAiSpellEffectType type in types)
                {
                    foreach (WorldSpellDefinitionId spellId in player.Spellbook.Available)
                    {
                        WorldSpellDefinition spell = Context.Engine.Definitions.GetSpell(spellId);
                        WorldAiSpellEffectType? spellType = spell.Effect.Accept(WorldAiSpellEffectTypeVisitor.Instance, Empty.Value);

                        if ((IsResearchable(spell.AiType) || Context.Profile.UseAllSpells) && spell.Level == level && spellType == type)
                        {
                            WorldResearchSpellCommand command = new(playerId, spellId);
                            WorldCommandResult result = command.Execute(state, Context.Engine);

                            if (!result.Failed)
                            {
                                return MakePlan(MaxScore, command, memory);
                            }
                        }
                    }
                }
            }

            return null;
        }

        private static bool IsResearchable(WorldAiSpellType type)
        {
            return type switch
            {
                WorldAiSpellType.All => true,
                WorldAiSpellType.None => false,
                WorldAiSpellType.Human => true,
                WorldAiSpellType.Undead => true,
                WorldAiSpellType.Heretic => true,
                WorldAiSpellType.Dwarf => true
            };
        }
    }
}
