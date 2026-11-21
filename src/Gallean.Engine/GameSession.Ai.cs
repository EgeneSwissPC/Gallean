namespace Gallean.Engine;

// Gallean Upgrade: the AIs used for AI players. The client sets this from the settings before
// every AI action, so changing the settings mid-game takes effect at the next AI action.
public sealed partial class GameSession
{
    public AiSelection Ai { get; set; } = AiSelection.Classic;
}
