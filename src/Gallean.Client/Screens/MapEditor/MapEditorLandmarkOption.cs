using Gallean.Engine;

namespace Gallean.Client;

// One landmark in the editor's palette: its picture (the game's land sprite), name and size.
internal sealed record MapEditorLandmarkOption(WorldLandmarkDefinition Definition, string Name)
{
    public AssetAnimationKey PreviewKey => new AssetAnimationKey.Landmark(Definition.Id);

    public string SizeText => $"{Definition.Width}x{Definition.Height}";

    public string Caption => $"{Name} · {SizeText}";

    public static IReadOnlyList<MapEditorLandmarkOption> Create(IEnumerable<WorldLandmarkDefinition> landmarks, GameModel game)
    {
        return
        [
            .. landmarks
                .Select(definition => new MapEditorLandmarkOption(definition, GetName(game, definition)))
                .OrderBy(option => option.Definition.Width)
                .ThenBy(option => option.Name, StringComparer.CurrentCulture)
                .ThenBy(option => option.Definition.Id.Value, StringComparer.OrdinalIgnoreCase)
        ];
    }

    private static string GetName(GameModel game, WorldLandmarkDefinition definition)
    {
        try
        {
            return game.Texts.GetGlobalText(definition.NameTextId).Text;
        }
        catch (Exception ex) when (ex is KeyNotFoundException or InvalidDataException)
        {
            return definition.Id.Value;
        }
    }
}
