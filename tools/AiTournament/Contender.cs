namespace AiTournament;

// "--ai world/battle" -> AiContender ("builder" alone = same ID for both if it is a battle AI too, else
// the Classic battle AI).
internal static class Contender
{
    public static AiContender Parse(string text)
    {
        string[] parts = text.Split('/', 2);
        string world = parts[0];
        string battle = parts.Length > 1
            ? parts[1]
            : AiBrainRegistry.BattleBrains.Any(brain => brain.Id == world) ? world : AiBrainRegistry.ClassicId;

        return new AiContender(world, battle);
    }

    public static bool IsKnown(AiContender contender)
    {
        return AiBrainRegistry.WorldBrains.Any(brain => brain.Id == contender.WorldAi) &&
               AiBrainRegistry.BattleBrains.Any(brain => brain.Id == contender.BattleAi);
    }
}
