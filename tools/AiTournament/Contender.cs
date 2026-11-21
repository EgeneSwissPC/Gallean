namespace AiTournament;

// A world AI / battle AI pair ("builder/ruthless"; "builder" alone = same ID for both if it is a
// battle AI too, else Classic battle AI).
internal sealed record Contender(string WorldAi, string BattleAi)
{
    public static Contender Parse(string text)
    {
        string[] parts = text.Split('/', 2);
        string world = parts[0];
        string battle = parts.Length > 1
            ? parts[1]
            : AiBrainRegistry.BattleBrains.Any(brain => brain.Id == world) ? world : AiBrainRegistry.ClassicId;

        return new Contender(world, battle);
    }

    public bool IsKnown()
    {
        return AiBrainRegistry.WorldBrains.Any(brain => brain.Id == WorldAi) &&
               AiBrainRegistry.BattleBrains.Any(brain => brain.Id == BattleAi);
    }

    public override string ToString() => $"{WorldAi}/{BattleAi}";
}
