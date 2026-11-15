namespace Gallean.Client;

internal static class MapEditorLabels
{
    public static string Race(WorldRace race)
    {
        return race switch
        {
            WorldRace.Human => Tr("Empire"),
            WorldRace.Undead => Tr("Undead Hordes"),
            WorldRace.Heretic => Tr("Legions of the Damned"),
            WorldRace.Dwarf => Tr("Mountain Clans"),
            WorldRace.Elf => Tr("Elven Alliance"),
            _ => Tr("Neutral")
        };
    }

    public static string Terrain(WorldMapTerrain terrain)
    {
        return terrain switch
        {
            WorldMapTerrain.Neutral => Tr("Neutral land"),
            WorldMapTerrain.Water => Tr("Water"),
            WorldMapTerrain.Human => Tr("Empire land"),
            WorldMapTerrain.Undead => Tr("Undead land"),
            WorldMapTerrain.Heretic => Tr("Legions land"),
            WorldMapTerrain.Dwarf => Tr("Clans land"),
            WorldMapTerrain.Elf => Tr("Elven land"),
            _ => terrain.ToString()
        };
    }
}
