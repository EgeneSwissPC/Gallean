namespace Gallean.Engine;

// Gallean Upgrade: the AIs used for AI players (docs/ai-switching.md). The client sets Ai from the
// settings before every AI action, so changing the settings mid-game takes effect at the next AI
// action. A player's own AI (WorldPlayer.AiOverride, saved with the game) wins over the settings.
public sealed partial class GameSession
{
    public AiSelection Ai { get; set; } = AiSelection.Classic;

    public string? GetWorldAiId(WorldPlayerId playerId)
    {
        WorldAiOverride? aiOverride = World.State.Players.GetValueOrDefault(playerId)?.AiOverride;

        return aiOverride?.WorldAi ?? Ai.WorldAi;
    }

    // The player's own battle AI for a side of the running battle, or null when it has none
    // (neutral armies, players without an override): then the settings choice applies.
    public string? GetBattleAiOverride(BattleSide side)
    {
        if (Battle is null)
        {
            return null;
        }

        WorldPlayerId? ownerId = side == BattleSide.Attacker ? Battle.AttackerOwnerId : Battle.DefenderOwnerId;

        return ownerId is null ? null : World.State.Players.GetValueOrDefault(ownerId.Value)?.AiOverride?.BattleAi;
    }

    public string? GetBattleAiId(BattleSide side)
    {
        return GetBattleAiOverride(side) ?? Ai.BattleAi;
    }
}
