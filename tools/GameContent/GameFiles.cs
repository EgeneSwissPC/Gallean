using Gallean.Configuration;

namespace GameContent;

public sealed record GameFiles(string Root)
{
    public static GameFiles Original { get; } = new(AppDirectories.OriginalAssets);

    public string ImagesRoot => GamePath.Resolve(Root, "Imgs");
    public string InterfaceRoot => GamePath.Resolve(Root, "Interf");
    public string SoundsRoot => GamePath.Resolve(Root, "Sounds");
    public string MusicRoot => GamePath.Resolve(Root, "Music");
    public string BriefingRoot => GamePath.Resolve(Root, "Briefing");
    public string ScenarioDataRoot => GamePath.Resolve(Root, "ScenData");
}
