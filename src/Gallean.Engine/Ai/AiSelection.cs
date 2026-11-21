namespace Gallean.Engine;

// Gallean Upgrade: the AIs chosen in the settings (docs/ai-switching.md). Null means Classic.
public sealed record AiSelection(string? WorldAi, string? BattleAi)
{
    public static AiSelection Classic { get; } = new(null, null);
}
