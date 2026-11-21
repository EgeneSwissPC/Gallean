using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: the AI players panel during a game: rows from the world state, changes
// applied as engine commands (saved with the game). Neutral players are listed too, under their
// own names (maps with neutral kinds have one neutral player per kind) (docs/ai-switching.md).
internal static class AiPlayersPanel
{
    public static IReadOnlyList<AiPlayerRowViewModel> CreateRows(GameModel gameModel)
    {
        List<AiPlayerRowViewModel> rows = [];

        foreach (WorldPlayer player in gameModel.Snapshot.WorldState.Players.Values)
        {
            if (player.IsAi)
            {
                string name = FormatName(gameModel, player);
                rows.Add(new AiPlayerRowViewModel(player.Id, name, player.AiOverride?.WorldAi, player.AiOverride?.BattleAi));
            }
        }

        return rows;
    }

    // The original maps have one neutral player, named after its lord ("Neutral Male"): shown as
    // "Neutrals". Maps with neutral kinds have one per kind, named after the kind ("Wolves").
    private static string FormatName(GameModel gameModel, WorldPlayer player)
    {
        if (player.Race is not WorldRace.Neutral)
        {
            return WorldPlayerFormatter.FormatRaceName(gameModel.Definitions, gameModel.Texts, player);
        }

        int neutralCount = gameModel.Snapshot.WorldState.Players.Values.Count(other => other.Race is WorldRace.Neutral);
        string name = WorldPlayerFormatter.FormatLordName(gameModel.Texts, player);

        return neutralCount == 1 || string.IsNullOrWhiteSpace(name) ? Tr("Neutrals") : name;
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
