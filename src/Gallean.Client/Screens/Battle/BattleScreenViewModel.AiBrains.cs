using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: AI-controlled sides fight with their player's own battle AI, else the one chosen
// in the settings; the player's own side (auto-play, instant resolve) keeps Classic (docs/ai-switching.md).
// Gallean Upgrade (battle sandbox Watch): the battle sandbox's Watch, where both sides fight with their chosen AI
// under the auto-resolve rules, one unit action at a time (same battle as Run for the seed).
internal sealed record BattleSandboxWatch(string AttackerAiId, string DefenderAiId, int StartRound);

internal sealed partial class BattleScreenViewModel
{
    public BattleSandboxWatch? SandboxWatch { get; init; }

    private BattleAutoResolveCommand CreateWatchStep(BattleSandboxWatch watch, bool singleStep)
    {
        return new BattleAutoResolveCommand
        {
            SingleStep = singleStep,
            StartRoundNumber = watch.StartRound,
            AttackerAiId = watch.AttackerAiId,
            DefenderAiId = watch.DefenderAiId
        };
    }

    private string? GetBattleAiId(BattleSide side)
    {
        bool isAi = side == BattleSide.Attacker ? battle.IsAttackerAi : battle.IsDefenderAi;

        return isAi ? GameModel.Session.GetBattleAiOverride(side) ?? Game.Settings.BattleAi : null;
    }

    private BattleAutoResolveCommand WithBattleAis(BattleAutoResolveCommand command)
    {
        if (SandboxWatch is { } watch)
        {
            return CreateWatchStep(watch, singleStep: false);
        }

        return command with
        {
            AttackerAiId = GetBattleAiId(BattleSide.Attacker),
            DefenderAiId = GetBattleAiId(BattleSide.Defender)
        };
    }
}
