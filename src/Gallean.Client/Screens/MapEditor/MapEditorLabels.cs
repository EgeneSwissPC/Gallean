using Gallean.Engine;

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

    public static string RaceShort(WorldRace race)
    {
        return race switch
        {
            WorldRace.Human => Tr("Empire"),
            WorldRace.Undead => Tr("Undead"),
            WorldRace.Heretic => Tr("Legions"),
            WorldRace.Dwarf => Tr("Clans"),
            WorldRace.Elf => Tr("Elves"),
            _ => Tr("Neutral")
        };
    }

    public static string Owner(WorldRace owner)
    {
        return owner == WorldRace.Neutral ? Tr("Neutral") : Race(owner);
    }

    public static string StackError(CustomMapStackError error, int leadership)
    {
        return error switch
        {
            CustomMapStackError.OutsideMap => Tr("Armies must stand inside the map."),
            CustomMapStackError.Blocked => Tr("Armies cannot stand on water, mountains or capitals."),
            CustomMapStackError.Occupied => Tr("Another army already stands there."),
            CustomMapStackError.UnknownOwner => Tr("That player is not on this map."),
            CustomMapStackError.NoLeader => Tr("An army needs a leader."),
            CustomMapStackError.MultipleLeaders => Tr("An army can have only one leader."),
            CustomMapStackError.LeadershipExceeded => Tr("Too many units for the leader's leadership ({0}).", leadership),
            _ => Tr("A large unit fills both lines of its row.")
        };
    }
}
