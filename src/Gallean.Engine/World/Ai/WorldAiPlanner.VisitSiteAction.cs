using Gallean.Common;

namespace Gallean.Engine;

internal sealed partial class WorldAiPlanner
{
    // Gallean Upgrade, docs/ai-unused-features.md phase 4: fighting armies of our AIs (profile VisitSites) walk to
    // sites where they can buy something useful and buy it there: mercenaries into free slots, training at trainers,
    // spells the AI can cast at mage towers, healing and revive potions at merchants. The richer the player, the
    // further they walk (answer B). Classic never visits sites (only its thieves do, for other reasons).
    private sealed record VisitSiteAction(WorldAiContext Context, int Priority) : AbstractStackAction(Context, Priority)
    {
        private const int MaxInterest = 200;
        private const int BaseTurnLimit = 2;
        private const int GoldPerExtraTurn = 2000;
        private const int MaxTurnLimit = 6;
        // Site purchases keep this much gold, except while gold spending (overflow spending phase 3).
        public const int GoldKept = 500;
        private const int MaxPotions = 2;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Normal order, ActionContext context) => PlanVisit(context);

        public override WorldAiActionPlan? Visit(WorldStackOrder.Stand order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Guard order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.AttackStack order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.DefendStack order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.SecureCity order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Roam order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Explore order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.MoveToLocation order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.DefendLocation order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Berserk order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Assist order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Steal order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.DefendCity order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Heal order, ActionContext context) => null;

        public override WorldAiActionPlan? Visit(WorldStackOrder.Reinforce order, ActionContext context) => null;

        private WorldAiActionPlan? PlanVisit(ActionContext context)
        {
            if (!Context.Profile.VisitSites)
            {
                return null;
            }

            WorldState state = context.State;
            WorldStack stack = context.Stack;
            (WorldUnit leader, _, UnitDefinition leaderDefinition) = stack.GetLeader(Context.Engine.Definitions);

            if (!leader.IsAlive || leaderDefinition.LeaderCategory is not (UnitLeaderCategory.Fighter or UnitLeaderCategory.Mage))
            {
                return null;
            }

            WorldPlan plan = WorldPlan.For(state);

            // At a site: buy the most useful thing there, one purchase per decision.
            if (plan.GetSiteAt(stack.Position) is { } here && here.Entrance == stack.Position &&
                FindPurchase(state, stack, here) is { } purchase)
            {
                return MakePlan(MaxScore, purchase, context.Memory);
            }

            if (stack.MovementPoints <= 0)
            {
                return null;
            }

            WorldPathMap? pathMap = Context.Pathfinding.GetPathMap(state, stack);

            if (pathMap is null)
            {
                return null;
            }

            WorldPlayer player = state.Players[stack.OwnerId];
            WorldAiAttitudeDefinition attitude = Context.Engine.Definitions.GetAiAttitude(player.AiAttitude);
            int maxMovement = Context.Engine.UnitStats.GetWorldStackStats(stack).MaxMovementPoints;
            int turnLimit = Math.Min(MaxTurnLimit, BaseTurnLimit + Math.Max(0, player.Resources.Gold) / GoldPerExtraTurn);
            int costLimit = maxMovement * turnLimit;
            (WorldSite Site, int Score)? best = null;

            foreach (WorldSite site in state.Sites.Values)
            {
                if (!player.FogOfWar.Get(site.Entrance.X, site.Entrance.Y) ||
                    !pathMap.TryGetPath(site.Entrance, WorldMovementLimit.Unlimited, out WorldPath? path) ||
                    path.Cost > costLimit ||
                    FindPurchase(state, stack, site) is null)
                {
                    continue;
                }

                int interest = site switch
                {
                    WorldMercenarySite => attitude.MercenaryInterest,
                    WorldTrainerSite => attitude.TrainerInterest,
                    WorldMageSite => attitude.MageInterest,
                    WorldMerchantSite => attitude.MerchantInterest,
                    _ => 0
                };

                int score = UtilityScore.Combine(
                [
                    UtilityScore.Ratio(interest, MaxInterest),
                    UtilityScore.InverseRatio(path.Cost, costLimit)
                ]);

                if (score > 0 && (best is null || score > best.Value.Score))
                {
                    best = (site, score);
                }
            }

            if (best is not { } target)
            {
                return null;
            }

            WorldPosition? move = Context.Pathfinding.FindMoveTowardCell(state, stack, pathMap, target.Site.Entrance);

            return move is null ? null : MakePlan(target.Score, new WorldAiMoveStackCommand(stack.Id, move.Value), context.Memory);
        }

        // The purchase this army would make at the site now (checked as if it stood at the entrance), or null.
        private IWorldCommand<Empty>? FindPurchase(WorldState state, WorldStack stack, WorldSite site)
        {
            WorldPlayer player = state.Players[stack.OwnerId];
            bool spending = WorldAiSpending.IsGoldSpending(Context.Profile, player, Context.Engine.Globals);

            bool Keeps(WorldResourceSet cost) => spending || player.Resources.Gold - cost.Gold >= GoldKept;

            WorldStack atEntrance = stack with { Position = site.Entrance };

            switch (site)
            {
                case WorldMercenarySite mercenaries:
                {
                    // The strongest affordable unit that fits (the most expensive one).
                    IWorldCommand<Empty>? best = null;
                    int bestGold = -1;

                    foreach (WorldMercenaryUnit unit in mercenaries.Units)
                    {
                        MercenarySiteCalculator.PurchaseInfo info = MercenarySiteCalculator.GetPurchaseInfo(mercenaries, state, atEntrance, player, unit.DefinitionId, unit.Level, Context.Engine);

                        if (info.Error is null && Keeps(info.Cost) && info.Cost.Gold > bestGold)
                        {
                            foreach (GridPosition position in GridPosition.All)
                            {
                                WorldPurchaseMercenaryCommand command = new(stack.Id, site.Id, unit.DefinitionId, unit.Level, position);

                                if (stack.Position != site.Entrance || !command.Execute(state, Context.Engine).Failed)
                                {
                                    best = command;
                                    bestGold = info.Cost.Gold;
                                    break;
                                }
                            }
                        }
                    }

                    return best;
                }
                case WorldTrainerSite:
                {
                    // The unit that gains the most experience.
                    IWorldCommand<Empty>? best = null;
                    int bestExperience = 0;

                    foreach ((WorldUnit unit, _) in stack.Units.GetUnits())
                    {
                        TrainingCalculator.TrainingInfo info = TrainingCalculator.GetTrainingInfo(unit, player.Resources, Context.Engine);

                        if (info.Error is null && info.TrainableExperience > bestExperience && Keeps(info.Cost))
                        {
                            best = new WorldTrainUnitCommand(stack.Id, unit.Id);
                            bestExperience = info.TrainableExperience;
                        }
                    }

                    return best;
                }
                case WorldMageSite mage:
                {
                    foreach (WorldSpellDefinitionId spellId in mage.Spells)
                    {
                        WorldSpellDefinition spell = Context.Engine.Definitions.GetSpell(spellId);
                        MageSiteCalculator.PurchaseInfo info = MageSiteCalculator.GetPurchaseInfo(mage, atEntrance, player, spellId, Context.Engine);

                        if (info.Error is null && Keeps(info.Cost) &&
                            spell.Effect.Accept(WorldAiSpellEffectTypeVisitor.Instance, Empty.Value) is not null)
                        {
                            return new WorldPurchaseSpellCommand(stack.Id, site.Id, spellId);
                        }
                    }

                    return null;
                }
                case WorldMerchantSite merchant:
                {
                    int potions = stack.Inventory.Items.Count(item => IsPotion(Context.Engine.Definitions.GetItem(item.DefinitionId)));

                    if (potions >= MaxPotions)
                    {
                        return null;
                    }

                    foreach (WorldMerchantItem item in merchant.Items)
                    {
                        ItemDefinition definition = Context.Engine.Definitions.GetItem(item.ItemId);
                        MerchantSiteCalculator.PurchaseInfo info = MerchantSiteCalculator.GetPurchaseInfo(merchant, atEntrance, player, item.ItemId, Context.Engine);

                        if (IsPotion(definition) && info.Error is null && Keeps(info.Cost))
                        {
                            return new WorldPurchaseItemCommand(stack.Id, site.Id, item.ItemId);
                        }
                    }

                    return null;
                }
                default:
                    return null;
            }
        }

        // Items the world AI already uses (UseItemAction): healing and revive potions.
        private static bool IsPotion(ItemDefinition item)
        {
            return item.Effect is HealItemEffect or ReviveItemEffect;
        }
    }
}
