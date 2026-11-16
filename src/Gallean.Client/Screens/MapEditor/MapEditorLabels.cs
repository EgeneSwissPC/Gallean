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

    public static string Resource(WorldResourceType type)
    {
        return type switch
        {
            WorldResourceType.Gold => Tr("Gold mine"),
            WorldResourceType.LifeMana => Tr("Life mana crystal"),
            WorldResourceType.InfernalMana => Tr("Infernal mana crystal"),
            WorldResourceType.DeathMana => Tr("Death mana crystal"),
            WorldResourceType.RuneMana => Tr("Rune mana crystal"),
            _ => Tr("Grove mana crystal")
        };
    }

    public static IReadOnlyList<MapEditorOption<int>> VillageLevels()
    {
        return
        [
            .. Enumerable.Range(CustomMapVillage.MinLevel, CustomMapVillage.MaxLevel - CustomMapVillage.MinLevel + 1)
                .Select(level => new MapEditorOption<int>(level, Tr("Level {0}", level)))
        ];
    }

    public static string VillageError(CustomMapPlacementError error)
    {
        return error switch
        {
            CustomMapPlacementError.OutsideMap => Tr("A village must fit inside the map."),
            CustomMapPlacementError.Blocked => Tr("A village cannot overlap a capital, village, ruin or site."),
            CustomMapPlacementError.Occupied => Tr("Move the armies, resources and treasure out of the way first."),
            CustomMapPlacementError.UnknownOwner => Tr("That player is not on this map."),
            CustomMapPlacementError.LeaderInGarrison => Tr("Leaders cannot join a garrison."),
            CustomMapPlacementError.GarrisonFull => Tr("Too many units for the village's level."),
            CustomMapPlacementError.InvalidLevel => Tr("Villages have levels 1 to 5."),
            CustomMapPlacementError.InvalidUnitLevel => Tr("Unit levels go from the unit's own level to {0}.", CustomMapUnit.MaxLevel),
            CustomMapPlacementError.Guardian => Tr("The guardian stays in its capital."),
            _ => Tr("A large unit fills both lines of its row.")
        };
    }

    public static string SiteType(CustomMapSiteType type)
    {
        return type switch
        {
            CustomMapSiteType.Merchant => Tr("Merchant"),
            CustomMapSiteType.MageTower => Tr("Mage tower"),
            CustomMapSiteType.Mercenaries => Tr("Mercenary camp"),
            _ => Tr("Trainer")
        };
    }

    public static IReadOnlyList<MapEditorOption<int>> Looks(int count)
    {
        return [.. Enumerable.Range(0, count).Select(variant => new MapEditorOption<int>(variant, Tr("Look {0}", variant + 1)))];
    }

    // Problems placing or changing ruins, sites and bags (and garrison problems from their screens).
    public static string ObjectError(CustomMapPlacementError error)
    {
        return error switch
        {
            CustomMapPlacementError.OutsideMap => Tr("It must fit inside the map."),
            CustomMapPlacementError.Blocked => Tr("It cannot overlap a capital, village, ruin, site or landmark, or stand on a mountain."),
            CustomMapPlacementError.Occupied => Tr("Move the armies, resources and treasure out of the way first."),
            CustomMapPlacementError.TooManyGoods => Tr("That is more than it can hold."),
            _ => VillageError(error)
        };
    }

    public static string LandmarkCategory(WorldLandmarkType type)
    {
        return type switch
        {
            WorldLandmarkType.Misc => Tr("Misc"),
            WorldLandmarkType.Building => Tr("Buildings"),
            WorldLandmarkType.Structure => Tr("Structures"),
            _ => Tr("Terrain")
        };
    }

    public static string ResourceError(CustomMapPlacementError error)
    {
        return error switch
        {
            CustomMapPlacementError.OutsideMap => Tr("Resources must lie inside the map."),
            CustomMapPlacementError.Occupied => Tr("An army or treasure is there."),
            _ => Tr("Resources need free land: no water, mountains or buildings.")
        };
    }

    public static string StackError(CustomMapStackError error, int leadership)
    {
        return error switch
        {
            CustomMapStackError.OutsideMap => Tr("Armies must stand inside the map."),
            CustomMapStackError.Blocked => Tr("Armies cannot stand on water, mountains, buildings, resources or treasure. In a village, use its entrance."),
            CustomMapStackError.VillageOwner => Tr("An army in a village or capital belongs to its owner."),
            CustomMapStackError.InvalidLevel => Tr("Unit levels go from the unit's own level to {0}.", CustomMapUnit.MaxLevel),
            CustomMapStackError.Guardian => Tr("The guardian stays in its capital."),
            CustomMapStackError.Occupied => Tr("Another army already stands there."),
            CustomMapStackError.UnknownOwner => Tr("That player is not on this map."),
            CustomMapStackError.NoLeader => Tr("An army needs a leader."),
            CustomMapStackError.MultipleLeaders => Tr("An army can have only one leader."),
            CustomMapStackError.LeadershipExceeded => Tr("Too many units for the leader's leadership ({0}).", leadership),
            _ => Tr("A large unit fills both lines of its row.")
        };
    }
}
