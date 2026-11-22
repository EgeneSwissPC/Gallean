using Gallean.Common;

namespace Gallean.Engine;

internal sealed partial class WorldAiPlanner
{
    // Gallean Upgrade, docs/ai-unused-features.md phase 5: leaders of our AIs (profile EquipItems) put banners,
    // tomes, artifacts and boots from the army's bag into their equipment slots, the most valuable item first, and
    // take off a worse item when the bag holds a better one for that slot (equipping only fills empty slots).
    // Battle slots stay as they are. Classic never equips anything.
    private sealed record EquipItemAction(WorldAiContext Context, int Priority) : AbstractStackAction(Context, Priority)
    {
        public static readonly LeaderEquipmentSlot[] Slots =
        [
            LeaderEquipmentSlot.Banner,
            LeaderEquipmentSlot.Tome,
            LeaderEquipmentSlot.Artifact1,
            LeaderEquipmentSlot.Artifact2,
            LeaderEquipmentSlot.Boots
        ];

        public override WorldAiActionPlan? Visit(WorldStackOrder.Normal order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Stand order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Guard order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.AttackStack order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.DefendStack order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.SecureCity order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Roam order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Explore order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.MoveToLocation order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.DefendLocation order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Berserk order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Assist order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Steal order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.DefendCity order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Heal order, ActionContext context) => PlanEquip(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Reinforce order, ActionContext context) => PlanEquip(context);

        private WorldAiActionPlan? PlanEquip(ActionContext context)
        {
            if (!Context.Profile.EquipItems)
            {
                return null;
            }

            IWorldCommand<Empty>? command = FindEquipCommand(context.State, context.Stack, Context.Engine);

            return command is null ? null : MakePlan(MaxScore, command, context.Memory);
        }

        // The next equip (or the take-off that makes room for a better item), or null when the leader is set.
        public static IWorldCommand<Empty>? FindEquipCommand(WorldState state, WorldStack stack, IEngineContext engine)
        {
            List<(WorldItem Item, ItemDefinition Definition)> bag = [.. stack.Inventory.Items
                .Select(item => (item, engine.Definitions.GetItem(item.DefinitionId)))
                .Where(entry => Slots.Any(slot => LeaderEquipmentCalculator.CanPlace(entry.Item2.Category, slot)))
                .OrderByDescending(entry => GetValue(entry.Item2))
                .ThenBy(entry => entry.item.Id.Value, StringComparer.Ordinal)];

            foreach ((WorldItem item, ItemDefinition definition) in bag)
            {
                foreach (LeaderEquipmentSlot slot in Slots)
                {
                    if (LeaderEquipmentCalculator.CanPlace(definition.Category, slot) && stack.Equipment[slot] is null)
                    {
                        WorldEquipItemCommand equip = new(stack.Id, item.Id, slot);

                        if (!equip.Execute(state, engine).Failed)
                        {
                            return equip;
                        }
                    }
                }
            }

            foreach ((WorldItem item, ItemDefinition definition) in bag)
            {
                // No free slot fits: take off the cheapest worse item of a slot this item fits, if it could be equipped.
                LeaderEquipmentSlot? worst = null;
                int worstValue = GetValue(definition);

                foreach (LeaderEquipmentSlot slot in Slots)
                {
                    if (LeaderEquipmentCalculator.CanPlace(definition.Category, slot) && stack.Equipment[slot] is { } equipped)
                    {
                        int value = GetValue(engine.Definitions.GetItem(equipped.DefinitionId));

                        if (value < worstValue)
                        {
                            worst = slot;
                            worstValue = value;
                        }
                    }
                }

                if (worst is { } slotToFree && CanEquip(stack, definition, engine))
                {
                    WorldUnequipItemCommand unequip = new(stack.Id, slotToFree);

                    if (!unequip.Execute(state, engine).Failed)
                    {
                        return unequip;
                    }
                }
            }

            return null;
        }

        // The leader has the ability the item needs (the same check the equip command makes).
        public static bool CanEquip(WorldStack stack, ItemDefinition definition, IEngineContext engine)
        {
            (WorldUnit leader, _, _) = stack.GetLeader(engine.Definitions);
            LeaderAbility? required = LeaderEquipmentCalculator.GetRequiredAbility(definition.Category);

            return leader.IsAlive && (required is null || engine.UnitStats.GetWorldUnitAbilities(leader).HasFlag(required.Value));
        }

        public static int GetValue(ItemDefinition definition)
        {
            WorldResourceSet value = definition.Value;

            return value.Gold + value.DeathMana + value.RuneMana + value.LifeMana + value.InfernalMana + value.GroveMana;
        }
    }
}
