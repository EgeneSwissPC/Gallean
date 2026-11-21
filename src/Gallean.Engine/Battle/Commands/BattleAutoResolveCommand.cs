using Gallean.Common;

namespace Gallean.Engine;

public sealed record BattleAutoResolveCommand : IBattleCommand<Empty>
{
    // Gallean Upgrade: units of this side never flee while the battle is resolved (the
    // "Refuse to flee" battle mode); they keep fighting like in the rest of the auto-resolve.
    public BattleSide? NoRetreatSide { get; init; }

    // Gallean Upgrade: the battle AI of each side (AiBrainRegistry ID); null = Classic.
    public string? AttackerAiId { get; init; }
    public string? DefenderAiId { get; init; }

    // Gallean Upgrade (battle sandbox Watch): play only the current unit's action, exactly as the auto-resolve
    // would, so a screen can animate an auto-resolved battle step by step. StartRoundNumber = the round the
    // auto-resolve started in (its round limits count from there); null = the current round.
    public bool SingleStep { get; init; }
    public int? StartRoundNumber { get; init; }

    public BattleCommandResult Execute(BattleState state, IEngineContext context)
    {
        return BattleCommandResult.Success(
            Empty.Value,
            [new BattleAutoResolveStepEvent(StartRoundNumber ?? state.RoundNumber, NoRetreatSide) { AttackerAiId = AttackerAiId, DefenderAiId = DefenderAiId, SingleStep = SingleStep }]);
    }
}
