using CommandResult = Gallean.Engine.ICommandResult<Gallean.Engine.WorldCommandError, Gallean.Engine.WorldEvent, Gallean.Engine.WorldGetPlayerInfoCommand.Result>;

namespace Gallean.Engine;

public sealed record WorldGetPlayerInfoCommand(WorldPlayerId PlayerId) : IWorldCommand<WorldGetPlayerInfoCommand.Result>
{
    public sealed record Result(
        WorldResourceSet TotalIncome,
        WorldResourceSet CapitalIncome,
        WorldResourceSet ResourceNodesIncome)
    {
        // Gallean Upgrade: gold from towns (map rule "town income"); part of TotalIncome.
        public WorldResourceSet TownsIncome { get; init; } = WorldResourceSet.Empty;
    }

    public CommandResult Execute(WorldState state, IEngineContext context)
    {
        WorldPlayer player = state.Players[PlayerId];
        PlayerIncomeCalculator.IncomeInfo info = PlayerIncomeCalculator.GetIncomeInfo(player, state, context);
        Result result = new(info.Total, info.Capital, info.ResourceNodes) { TownsIncome = info.Towns };

        return CommandResult.Success(result, []);
    }
}
