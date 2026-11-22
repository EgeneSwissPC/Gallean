using Gallean.Common;

namespace Gallean.Engine;

internal sealed partial class WorldAiPlanner
{
    private sealed record CastSpellAction(WorldAiContext Context, int Priority) : AbstractEconomyAction(Context, Priority)
    {
        private const int CityThreatRange = 7;
        private const int NearRange = 7;

        public override WorldAiActionPlan? Plan(WorldState state, WorldPlayerId playerId, WorldAiMemory memory)
        {
            WorldPlayer player = state.Players[playerId];
            List<WorldSpellDefinition> targetedSpells = [];
            List<WorldSpellDefinition> globalSpells = [];

            foreach (WorldSpellDefinitionId spellId in player.Spellbook.Acquired)
            {
                WorldSpellDefinition spell = Context.Engine.Definitions.GetSpell(spellId);

                if (spell.AreaRadius is 0)
                {
                    targetedSpells.Add(spell);
                }

                if (spell.AreaRadius is null)
                {
                    globalSpells.Add(spell);
                }
            }

            targetedSpells.Sort(CompareSpellLevel);
            globalSpells.Sort(CompareSpellLevel);

            WorldCastSpellCommand? command = FindTargetedCast(state, playerId, targetedSpells);

            if (command is null && IsGlobalCastAllowed(player, globalSpells))
            {
                command = FindGlobalCast(state, playerId, globalSpells);
            }

            // Gallean Upgrade, overflow spending: only when Classic found nothing to cast.
            if (command is null && WorldAiSpending.IsManaSpending(Context.Profile, player, Context.Engine.Globals))
            {
                command = FindSpendingCast(state, playerId, targetedSpells, globalSpells);
            }

            if (command is null)
            {
                return null;
            }

            return MakePlan(MaxScore, command, memory);
        }

        private WorldCastSpellCommand? FindTargetedCast(WorldState state, WorldPlayerId playerId, List<WorldSpellDefinition> spells)
        {
            WorldPlayer player = state.Players[playerId];
            List<WorldSpellDefinition> attackSpells = [];
            List<WorldSpellDefinition> weakenSpells = [];
            List<WorldSpellDefinition> healSpells = [];
            List<WorldSpellDefinition> restoreMovementSpells = [];

            foreach (WorldSpellDefinition spell in spells)
            {
                WorldAiSpellEffectType? type = GetSpellEffectType(spell);

                List<WorldSpellDefinition>? group = type switch
                {
                    WorldAiSpellEffectType.Attack => attackSpells,
                    WorldAiSpellEffectType.Summon => attackSpells,
                    WorldAiSpellEffectType.Weaken => weakenSpells,
                    WorldAiSpellEffectType.Heal => healSpells,
                    WorldAiSpellEffectType.RestoreMovement => restoreMovementSpells,
                    WorldAiSpellEffectType.Boost => null,
                    WorldAiSpellEffectType.Invisibility => null,
                    null => null
                };

                if (group is not null)
                {
                    group.Add(spell);
                }
            }

            WorldCastSpellCommand? command = null;

            if (!CanCast(player, attackSpells) && !CanCast(player, weakenSpells))
            {
                command = FindEnemyCast(state, playerId, attackSpells, EnumerateCityThreats(state, playerId));

                if (command is null)
                {
                    command = FindEnemyCast(state, playerId, weakenSpells, EnumerateNearEnemies(state, playerId));
                }
            }

            if (command is null)
            {
                command = FindCast(state, healSpells, EnumerateWoundedStacks(state, playerId));
            }

            if (command is null)
            {
                command = FindCast(state, restoreMovementSpells, EnumerateStacksOutOfMovement(state, playerId));
            }

            return command;
        }

        private WorldCastSpellCommand? FindEnemyCast(WorldState state, WorldPlayerId playerId, List<WorldSpellDefinition> spells, IEnumerable<WorldStack> targets)
        {
            if (spells.Count > 0)
            {
                WorldCastSpellCommand? command = FindCast(state, spells, targets);

                if (command is null && Context.Engine.Random.Next(100) < Context.Engine.Globals.AiRandomCastChance)
                {
                    command = FindCast(state, spells, EnumerateEnemyStacks(state, playerId));
                }

                return command;
            }
            else
            {
                return null;
            }
        }

        private WorldCastSpellCommand? FindCast(WorldState state, List<WorldSpellDefinition> spells, IEnumerable<WorldStack> targets)
        {
            foreach (WorldSpellDefinition spell in spells)
            {
                foreach (WorldStack target in targets)
                {
                    WorldCastSpellCommand command = new(spell.Id, new WorldSpellTarget(target.Position, 1));
                    WorldCommandResult result = command.Execute(state, Context.Engine);

                    if (!result.Failed)
                    {
                        return command;
                    }
                }
            }

            return null;
        }

        private static bool CanCast(WorldPlayer player, List<WorldSpellDefinition> spells)
        {
            return spells.Any(spell => player.TurnActions.SpellCasts.ContainsKey(spell.Id));
        }

        private bool IsGlobalCastAllowed(WorldPlayer player, List<WorldSpellDefinition> globalSpells)
        {
            GlobalVariables globals = Context.Engine.Globals;
            WorldResourceSet resources = player.Resources;

            return Context.Engine.Scenario.Definition.SuggestedLevel >= globals.AiGlobalSpellMinSuggestedLevel &&
                   resources.InfernalMana > globals.AiGlobalSpellMinMana &&
                   resources.LifeMana > globals.AiGlobalSpellMinMana &&
                   resources.DeathMana > globals.AiGlobalSpellMinMana &&
                   resources.RuneMana > globals.AiGlobalSpellMinMana &&
                   !CanCast(player, globalSpells);
        }

        private WorldCastSpellCommand? FindGlobalCast(WorldState state, WorldPlayerId playerId, List<WorldSpellDefinition> spells)
        {
            List<WorldStack> stacks = [.. EnumerateOutsideStacks(state, playerId)];
            List<WorldStack> stacksOutOfMovement = [.. EnumerateStacksOutOfMovement(state, playerId)];

            foreach (WorldSpellDefinition spell in spells)
            {
                WorldAiSpellEffectType? type = GetSpellEffectType(spell);

                bool castSpell = type switch
                {
                    WorldAiSpellEffectType.Heal => stacks.Any(HasDamagedUnit),
                    WorldAiSpellEffectType.Boost => stacks.Count >= Context.Engine.Globals.AiGlobalSpellMinStacks,
                    WorldAiSpellEffectType.Invisibility => stacks.Count >= Context.Engine.Globals.AiGlobalSpellMinStacks,
                    WorldAiSpellEffectType.RestoreMovement => stacksOutOfMovement.Count >= Context.Engine.Globals.AiGlobalSpellMinStacks,
                    WorldAiSpellEffectType.Summon => false,
                    WorldAiSpellEffectType.Attack => false,
                    WorldAiSpellEffectType.Weaken => false,
                    null => false
                };

                if (castSpell)
                {
                    WorldCastSpellCommand command = new(spell.Id, Target: null);
                    WorldCommandResult result = command.Execute(state, Context.Engine);

                    if (!result.Failed)
                    {
                        return command;
                    }
                }
            }

            return null;
        }

        // Gallean Upgrade, overflow spending (docs/ai-overflow-spending.md): spells that pay with an overflowing
        // mana, without Classic's one-offensive-spell-per-turn and near-a-city limits. Each spell can still be
        // cast only as often per turn as the lord allows; only useful casts are made.
        private WorldCastSpellCommand? FindSpendingCast(
            WorldState state,
            WorldPlayerId playerId,
            List<WorldSpellDefinition> targetedSpells,
            List<WorldSpellDefinition> globalSpells)
        {
            WorldPlayer player = state.Players[playerId];
            GlobalVariables globals = Context.Engine.Globals;
            List<WorldSpellDefinition> offensive = [];
            List<WorldSpellDefinition> weaken = [];
            List<WorldSpellDefinition> boost = [];
            List<WorldSpellDefinition> heal = [];
            List<WorldSpellDefinition> restoreMovement = [];

            foreach (WorldSpellDefinition spell in targetedSpells)
            {
                if (!WorldAiSpending.UsesOverflowingMana(Context.Profile, player, spell.CastingCost, globals))
                {
                    continue;
                }

                List<WorldSpellDefinition>? group = GetSpellEffectType(spell) switch
                {
                    WorldAiSpellEffectType.Attack => offensive,
                    WorldAiSpellEffectType.Summon => offensive,
                    WorldAiSpellEffectType.Weaken => weaken,
                    WorldAiSpellEffectType.Boost => boost,
                    WorldAiSpellEffectType.Invisibility => boost,
                    WorldAiSpellEffectType.Heal => heal,
                    WorldAiSpellEffectType.RestoreMovement => restoreMovement,
                    null => null
                };

                group?.Add(spell);
            }

            List<WorldStack> ownStacks = [.. state.Stacks.Values.Where(stack => stack.OwnerId == playerId)];
            List<WorldStack> enemies = [.. EnumerateVisibleEnemies(state, player, ownStacks)];

            WorldCastSpellCommand? command = FindCast(state, offensive, enemies);
            command ??= FindCast(state, weaken, enemies);
            command ??= FindCast(state, boost, EnumerateStacksNearEnemies(state, playerId, enemies));
            command ??= FindCast(state, heal, ownStacks.Where(HasDamagedUnit));
            command ??= FindCast(state, restoreMovement, EnumerateStacksOutOfMovement(state, playerId));

            if (command is null)
            {
                List<WorldStack> outside = [.. EnumerateOutsideStacks(state, playerId)];
                int outOfMovement = outside.Count(stack => stack.MovementPoints <= 0);

                foreach (WorldSpellDefinition spell in globalSpells)
                {
                    if (!WorldAiSpending.UsesOverflowingMana(Context.Profile, player, spell.CastingCost, globals))
                    {
                        continue;
                    }

                    bool useful = GetSpellEffectType(spell) switch
                    {
                        WorldAiSpellEffectType.Heal => ownStacks.Any(HasDamagedUnit),
                        WorldAiSpellEffectType.Boost => outside.Count >= globals.AiGlobalSpellMinStacks,
                        WorldAiSpellEffectType.Invisibility => outside.Count >= globals.AiGlobalSpellMinStacks,
                        WorldAiSpellEffectType.RestoreMovement => outOfMovement >= globals.AiGlobalSpellMinStacks,
                        _ => false
                    };

                    if (useful)
                    {
                        WorldCastSpellCommand global = new(spell.Id, Target: null);

                        if (!global.Execute(state, Context.Engine).Failed)
                        {
                            return global;
                        }
                    }
                }
            }

            return command;
        }

        // Hostile armies on explored tiles, nearest to one of the player's own armies first (at most 30).
        private static IEnumerable<WorldStack> EnumerateVisibleEnemies(WorldState state, WorldPlayer player, List<WorldStack> ownStacks)
        {
            const int MaxTargets = 30;

            return state.Stacks.Values
                .Where(stack => DiplomacyCalculator.IsHostile(state, player.Id, stack.OwnerId) &&
                                player.FogOfWar.Get(stack.Position.X, stack.Position.Y))
                .OrderBy(stack => ownStacks.Count == 0 ? 0 : ownStacks.Min(own => own.Position.GetDistance(stack.Position)))
                .ThenBy(stack => stack.Id.Value, StringComparer.Ordinal)
                .Take(MaxTargets);
        }

        // Own armies outside cities, with movement left, within NearRange of a visible enemy: about to fight.
        private static IEnumerable<WorldStack> EnumerateStacksNearEnemies(WorldState state, WorldPlayerId playerId, List<WorldStack> enemies)
        {
            foreach (WorldStack stack in EnumerateOutsideStacks(state, playerId))
            {
                if (stack.MovementPoints > 0 && enemies.Any(enemy => stack.Position.GetDistance(enemy.Position) <= NearRange))
                {
                    yield return stack;
                }
            }
        }

        private static WorldAiSpellEffectType? GetSpellEffectType(WorldSpellDefinition spell)
        {
            return spell.Effect.Accept(WorldAiSpellEffectTypeVisitor.Instance, Empty.Value);
        }

        private static int CompareSpellLevel(WorldSpellDefinition left, WorldSpellDefinition right)
        {
            return right.Level.CompareTo(left.Level);
        }

        private static IEnumerable<WorldStack> EnumerateCityThreats(WorldState state, WorldPlayerId playerId)
        {
            WorldPlan plan = WorldPlan.For(state);

            foreach (WorldFortification fortification in state.Fortifications.Values)
            {
                WorldPosition entrance = FortificationCalculator.GetEntrance(fortification.Footprint);

                if (FortificationCalculator.GetOwner(fortification) == playerId && plan.GetStackAt(entrance) is null)
                {
                    foreach (WorldStack stack in state.Stacks.Values)
                    {
                        if (DiplomacyCalculator.IsHostile(state, playerId, stack.OwnerId) &&
                            stack.Order is WorldStackOrder.Normal &&
                            entrance.GetDistance(stack.Position) <= CityThreatRange)
                        {
                            yield return stack;
                        }
                    }
                }
            }
        }

        private static IEnumerable<WorldStack> EnumerateEnemyStacks(WorldState state, WorldPlayerId playerId)
        {
            foreach (WorldStack stack in state.Stacks.Values)
            {
                WorldPlayer owner = state.Players[stack.OwnerId];

                if (DiplomacyCalculator.IsHostile(state, playerId, stack.OwnerId) && WorldHelpers.IsRacePlayable(owner.Race))
                {
                    yield return stack;
                }
            }
        }

        private static IEnumerable<WorldStack> EnumerateNearEnemies(WorldState state, WorldPlayerId playerId)
        {
            foreach (WorldStack stack in EnumerateOutsideStacks(state, playerId))
            {
                if (stack.MovementPoints > 0)
                {
                    foreach (WorldStack enemy in state.Stacks.Values)
                    {
                        if (DiplomacyCalculator.IsHostile(state, playerId, enemy.OwnerId) &&
                            stack.Position.GetDistance(enemy.Position) <= NearRange)
                        {
                            yield return enemy;
                        }
                    }
                }
            }
        }

        private static IEnumerable<WorldStack> EnumerateStacksOutOfMovement(WorldState state, WorldPlayerId playerId)
        {
            foreach (WorldStack stack in EnumerateOutsideStacks(state, playerId))
            {
                if (stack.MovementPoints <= 0)
                {
                    yield return stack;
                }
            }
        }

        private IEnumerable<WorldStack> EnumerateWoundedStacks(WorldState state, WorldPlayerId playerId)
        {
            foreach (WorldStack stack in state.Stacks.Values)
            {
                if (stack.OwnerId == playerId && IsStackWounded(stack))
                {
                    yield return stack;
                }
            }
        }

        private static IEnumerable<WorldStack> EnumerateOutsideStacks(WorldState state, WorldPlayerId playerId)
        {
            WorldPlan plan = WorldPlan.For(state);

            foreach (WorldStack stack in state.Stacks.Values)
            {
                if (stack.OwnerId == playerId && plan.GetFortificationAt(stack.Position) is null)
                {
                    yield return stack;
                }
            }
        }

        private bool IsStackWounded(WorldStack stack)
        {
            int hitpoints = 0;
            int maxHitpoints = 0;

            foreach ((WorldUnit unit, GridPosition _) in stack.Units.GetUnits())
            {
                UnitStats stats = Context.Engine.UnitStats.GetWorldUnitStats(unit);

                hitpoints += unit.Hitpoints;
                maxHitpoints += stats.MaxHitpoints;
            }

            return hitpoints * 2 <= maxHitpoints;
        }

        private bool HasDamagedUnit(WorldStack stack)
        {
            foreach ((WorldUnit unit, GridPosition _) in stack.Units.GetUnits())
            {
                UnitStats stats = Context.Engine.UnitStats.GetWorldUnitStats(unit);

                if (unit.Hitpoints < stats.MaxHitpoints)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
