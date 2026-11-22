using Gallean.Configuration;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (battle AI designer, docs/battle-ai-designer.md phase 8): the player's designed battle AIs.
// Reload() reads the folder and hands them to AiBrainRegistry, so Settings, the AI players panel, the battle
// sandbox and the game use them. Tests point Directory at a temporary folder.
internal static class DesignedBattleAiLibrary
{
    public static string Directory { get; set; } = AppDirectories.BattleAis;

    public static DesignedBattleAiStore Store => new(Directory);

    public static IReadOnlyList<DesignedBattleAi> Reload()
    {
        IReadOnlyList<DesignedBattleAi> designs = Store.LoadAll();
        AiBrainRegistry.SetDesignedBattleAis(designs);

        return designs;
    }

    // Same rules as map AIs; the file name is the part of the ID after "custom-battle:".
    public static string MakeFileName(string name, IEnumerable<string> existingIds)
    {
        return DesignedAiLibrary.MakeFileName(name, existingIds.Select(DesignedBattleAiStore.GetFileName));
    }
}
