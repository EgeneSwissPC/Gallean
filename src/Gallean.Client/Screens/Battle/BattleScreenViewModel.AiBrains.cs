using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: AI-controlled sides fight with their player's own battle AI, else the one chosen
// in the settings; the player's own side (auto-play, instant resolve) keeps Classic (docs/ai-switching.md).
internal sealed partial class BattleScreenViewModel
{
    private string? GetBattleAiId(BattleSide side)
    {
        bool isAi = side == BattleSide.Attacker ? battle.IsAttackerAi : battle.IsDefenderAi;

        return isAi ? GameModel.Session.GetBattleAiOverride(side) ?? Game.Settings.BattleAi : null;
    }

    private BattleAutoResolveCommand WithBattleAis(BattleAutoResolveCommand command)
    {
        return command with
        {
            AttackerAiId = GetBattleAiId(BattleSide.Attacker),
            DefenderAiId = GetBattleAiId(BattleSide.Defender)
        };
    }
}
