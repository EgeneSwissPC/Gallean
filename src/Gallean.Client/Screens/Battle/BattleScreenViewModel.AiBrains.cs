using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade: AI-controlled sides fight with their player's own battle AI, else the one chosen
// in the settings (docs/ai-switching.md). The player's own side (Auto-play, Instant resolve) uses the battle AI
// picked on the battle screen for this battle, Classic by default (battle AI designer phase 10, BD c); Heal and
// defend and Refuse to flee keep their own rules.
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

    private IReadOnlyList<MapEditorOption<string>>? playerAis;

    // The battle AIs the player can pick for their own side, Classic first.
    public IReadOnlyList<MapEditorOption<string>> PlayerAis => playerAis ??=
        [.. AiBrainRegistry.BattleBrains.Select(info => new MapEditorOption<string>(info.Id, AiBrainFormatter.FormatName(info.Id)))];

    private MapEditorOption<string>? selectedPlayerAi;

    public MapEditorOption<string>? SelectedPlayerAi
    {
        get => selectedPlayerAi ??= PlayerAis.FirstOrDefault(option => option.Value == AiBrainRegistry.ClassicId);
        set => SetProperty(ref selectedPlayerAi, value);
    }

    // The picker shows when the player has a side in this battle (not in the sandbox's Watch).
    public bool ShowPlayerAiPicker => SandboxWatch is null && (!battle.IsAttackerAi || !battle.IsDefenderAi);

    private string? GetBattleAiId(BattleSide side)
    {
        bool isAi = side == BattleSide.Attacker ? battle.IsAttackerAi : battle.IsDefenderAi;

        return isAi ? GameModel.Session.GetBattleAiOverride(side) ?? Game.Settings.BattleAi : SelectedPlayerAi?.Value;
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
