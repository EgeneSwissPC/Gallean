using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: the AI players panel during a game: rows from the world state, changes
// applied as engine commands (saved with the game). Neutral players aren't listed: they always
// follow the settings (docs/ai-switching.md).
internal static class AiPlayersPanel
{
    public static IReadOnlyList<AiPlayerRowViewModel> CreateRows(GameModel gameModel)
    {
        List<AiPlayerRowViewModel> rows = [];

        foreach (WorldPlayer player in gameModel.Snapshot.WorldState.Players.Values)
        {
            if (player.IsAi && player.Race is not WorldRace.Neutral)
            {
                string name = WorldPlayerFormatter.FormatRaceName(gameModel.Definitions, gameModel.Texts, player);
                rows.Add(new AiPlayerRowViewModel(player.Id, name, player.AiOverride?.WorldAi, player.AiOverride?.BattleAi));
            }
        }

        return rows;
    }

    public static async Task ApplyAsync(GameModel gameModel, IReadOnlyList<AiPlayerRowViewModel> rows)
    {
        foreach (AiPlayerRowViewModel row in rows)
        {
            await gameModel.World.DispatchCommandAsync(new WorldSetAiOverrideCommand((WorldPlayerId)row.Key, row.ToOverride()));
        }
    }

    public static int CountOwnAis(WorldState state, Func<WorldAiOverride, string?> part)
    {
        return state.Players.Values.Count(player => player.IsAi && player.AiOverride is { } own && part(own) is not null);
    }

    public static async Task ClearAsync(GameModel gameModel, Func<WorldAiOverride, WorldAiOverride> clearPart)
    {
        foreach (WorldPlayer player in gameModel.Snapshot.WorldState.Players.Values)
        {
            if (player.IsAi && player.AiOverride is { } own)
            {
                await gameModel.World.DispatchCommandAsync(new WorldSetAiOverrideCommand(player.Id, clearPart(own)));
            }
        }
    }
}
