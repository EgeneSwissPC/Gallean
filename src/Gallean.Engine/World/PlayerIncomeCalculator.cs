using System.Collections.Immutable;

namespace Gallean.Engine;

internal static class PlayerIncomeCalculator
{
    public sealed record IncomeInfo(
        WorldResourceSet Total,
        WorldResourceSet Capital,
        WorldResourceSet ResourceNodes)
    {
        // Gallean Upgrade: gold from towns (map rule "town income", docs/town-income.md); part of Total.
        public WorldResourceSet Towns { get; init; } = WorldResourceSet.Empty;
    }

    private const int PercentMax = 100;

    public static IncomeInfo GetIncomeInfo(WorldPlayer player, WorldState state, IEngineContext context)
    {
        return GetIncomeInfo(player, state, context, context.Scenario.Definition.TownIncome);
    }

    // The map editor passes the table being edited instead of the loaded scenario's.
    public static IncomeInfo GetIncomeInfo(WorldPlayer player, WorldState state, IEngineContext context, ImmutableArray<int> townIncome)
    {
        if (WorldHelpers.IsRacePlayable(player.Race))
        {
            WorldResourceSet raceIncome = context.Definitions.GetRace(player.Race).Income;
            WorldResourceSet nodesIncome = WorldResourceSet.Empty;
            WorldPlan plan = WorldPlan.For(state);

            foreach (WorldResourceNode node in state.ResourceNodes.Values)
            {
                if (plan.GetLandOwner(node.Position) == player.Id)
                {
                    nodesIncome = ResourceSetCalculator.Add(nodesIncome, ResourceNodeCalculator.GetIncome(node, context.Globals));
                }
            }

            WorldResourceSet townsIncome = WorldResourceSet.Empty with { Gold = TownIncomeCalculator.GetGold(player, state, townIncome, context.Definitions) };
            WorldResourceSet income = Scale(ResourceSetCalculator.Add(ResourceSetCalculator.Add(raceIncome, nodesIncome), townsIncome), player, state, context);
            WorldResourceSet paidRaceIncome = Scale(raceIncome, player, state, context);
            WorldResourceSet paidTownsIncome = Scale(townsIncome, player, state, context);
            WorldResourceSet paidNodesIncome = ResourceSetCalculator.Subtract(ResourceSetCalculator.Subtract(income, paidRaceIncome), paidTownsIncome);

            return new IncomeInfo(income, paidRaceIncome, paidNodesIncome) { Towns = paidTownsIncome };
        }
        else if (player.Race == WorldRace.Neutral && !townIncome.IsDefaultOrEmpty)
        {
            // Gallean Upgrade, town income phase 3: a neutral side earns from its own towns only (no base income, and
            // no land, so no resources), scaled like all other income.
            WorldResourceSet townsIncome = Scale(WorldResourceSet.Empty with { Gold = TownIncomeCalculator.GetGold(player, state, townIncome, context.Definitions) }, player, state, context);

            return new IncomeInfo(townsIncome, WorldResourceSet.Empty, WorldResourceSet.Empty) { Towns = townsIncome };
        }
        else
        {
            return new IncomeInfo(WorldResourceSet.Empty, WorldResourceSet.Empty, WorldResourceSet.Empty);
        }
    }

    public static WorldResourceSet GetResourceNodeIncome(WorldPlayer player, WorldResourceNode node, WorldState state, IEngineContext context)
    {
        WorldResourceSet income = ResourceNodeCalculator.GetIncome(node, context.Globals);

        return Scale(income, player, state, context);
    }

    private static WorldResourceSet Scale(WorldResourceSet income, WorldPlayer player, WorldState state, IEngineContext context)
    {
        WorldResourceSet result = ResourceSetCalculator.ScalePercent(income, PercentMax + GetDifficultyBonusPercent(state.Difficulty, context.Globals));

        if (player.IsAi)
        {
            WorldAiAttitudeDefinition attitude = context.Definitions.GetAiAttitude(player.AiAttitude);

            result = ResourceSetCalculator.ScalePercent(result, GetAttitudePercent(attitude, state.Difficulty));
        }

        return result;
    }

    private static int GetDifficultyBonusPercent(WorldDifficulty difficulty, GlobalVariables globals)
    {
        return difficulty switch
        {
            WorldDifficulty.Easy => globals.IncomeBonusPercentEasy,
            WorldDifficulty.Average => globals.IncomeBonusPercentAverage,
            WorldDifficulty.Hard => globals.IncomeBonusPercentHard,
            WorldDifficulty.VeryHard => globals.IncomeBonusPercentVeryHard
        };
    }

    private static int GetAttitudePercent(WorldAiAttitudeDefinition attitude, WorldDifficulty difficulty)
    {
        return difficulty switch
        {
            WorldDifficulty.Easy => attitude.EasyIncomePercent,
            WorldDifficulty.Average => attitude.AverageIncomePercent,
            WorldDifficulty.Hard => attitude.HardIncomePercent,
            WorldDifficulty.VeryHard => attitude.VeryHardIncomePercent
        };
    }
}
