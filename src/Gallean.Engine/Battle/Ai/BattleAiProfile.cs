namespace Gallean.Engine;

// Gallean Upgrade: the knobs a battle AI personality turns on the Classic battle AI
// (docs/ai-switching.md). Classic holds the original values.
internal sealed record BattleAiProfile
{
    public static BattleAiProfile Classic { get; } = new();

    // A leader thinks about fleeing when its side holds less than this share (per mille) of the
    // battle's power.
    public int RetreatPowerShare { get; init; } = 250;

    public bool CanRetreat { get; init; } = true;
    public bool CanDefend { get; init; } = true;

    // Gallean Upgrade (battle AI designer, docs/battle-ai-designer.md): target preferences, each a bonus in percent
    // on Classic's own score for an enemy target that matches (0 = off, the Classic value). Several matching
    // preferences add up.
    public int HealersFirst { get; init; }        // enemy units that heal, cure or revive
    public int CastersFirst { get; init; }        // enemy units whose attack hits all (mages), healers excluded
    public int LeaderFirst { get; init; }         // the enemy leader
    public int HardestHitterFirst { get; init; }  // the active enemy with the highest attack power
    public int WoundedFirst { get; init; }        // the active enemy with the fewest hit points left
    public int FocusFire { get; init; }           // the active enemy that has lost the largest share of its hit points

    // Protect the leader: heals, cures and protections on the own leader count five times, and a leader below half
    // its hit points defends.
    public bool ProtectLeader { get; init; }

    public bool HasTargetPreferences => HealersFirst != 0 || CastersFirst != 0 || LeaderFirst != 0 || HardestHitterFirst != 0 || WoundedFirst != 0 || FocusFire != 0;
}
