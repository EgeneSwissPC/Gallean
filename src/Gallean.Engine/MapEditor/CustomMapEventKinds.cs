using Gallean.Common;

namespace Gallean.Engine;

// The remaining event kinds (docs/map-editor-events.md, phase 6) and the map's variables: whole numbers events can
// test ("variable in a range") and change ("set / add / ..."), saved as SVars rows. Variables let a map count things
// ("3 villages taken") or hold state across events ("the elder was met").
public sealed partial class CustomMap
{
    // The land a race turns tiles into (for "change the land" blocks).
    public static WorldMapTerrain GetRaceTerrain(WorldRace race)
    {
        return WorldHelpers.GetNativeTerrain(race);
    }

    public CustomMapVariable? GetVariable(int id)
    {
        return variables.FirstOrDefault(variable => variable.Id == id);
    }

    public CustomMapVariable AddVariable(string name, int initial = 0)
    {
        int id = variables.Count == 0 ? 1 : variables.Max(variable => variable.Id) + 1;
        CustomMapVariable added = new(id, string.IsNullOrWhiteSpace(name) ? $"Variable {id}" : name.Trim(), initial);
        variables.Add(added);

        return added;
    }

    public bool UpdateVariable(CustomMapVariable variable)
    {
        int index = variables.FindIndex(existing => existing.Id == variable.Id);

        if (index < 0)
        {
            return false;
        }

        variables[index] = variable;

        return true;
    }

    public bool RemoveVariable(int id)
    {
        return variables.RemoveAll(variable => variable.Id == id) > 0;
    }

    private bool RaceOnMap(WorldRace race)
    {
        return players.Any(player => player.Race == race);
    }

    private bool KindConditionTargetExists(CustomMapCondition condition)
    {
        return condition switch
        {
            CustomMapOwnItemCondition own => !string.IsNullOrEmpty(own.Item.Value),
            CustomMapLeaderItemCondition leader => ArmyRefExists(leader.Army, triggererAllowed: false) && !string.IsNullOrEmpty(leader.Item.Value),
            CustomMapRelationCondition relation => RaceOnMap(relation.Race1) && RaceOnMap(relation.Race2) && relation.Race1 != relation.Race2,
            CustomMapAllianceCondition alliance => RaceOnMap(alliance.Race1) && RaceOnMap(alliance.Race2) && alliance.Race1 != alliance.Race2,
            CustomMapLandCondition land => land.Percent is > 0 and <= 100,
            CustomMapVisitSiteCondition visit => sites.Any(site => site.Id == visit.SiteId),
            CustomMapArmyToZoneCondition toZone => ArmyRefExists(toZone.Army, triggererAllowed: false) && GetLocation(toZone.LocationId) is not null,
            CustomMapArmyToCityCondition toCity => ArmyRefExists(toCity.Army, triggererAllowed: false) && CityExists(toCity.City),
            CustomMapItemToZoneCondition item => !string.IsNullOrEmpty(item.Item.Value) && GetLocation(item.LocationId) is not null,
            CustomMapVariableCondition variable => GetVariable(variable.VariableId) is not null &&
                                                   (variable.Join == CustomMapVariableJoin.None || GetVariable(variable.SecondVariableId) is not null),
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
        };
    }

    private bool KindEffectTargetExists(CustomMapEffect effect)
    {
        return effect switch
        {
            CustomMapCastSpellEffect cast => !string.IsNullOrEmpty(cast.Spell.Value) && (cast.Caster is not { } caster || RaceOnMap(caster)),
            CustomMapCastSpellZoneEffect cast => !string.IsNullOrEmpty(cast.Spell.Value) && GetLocation(cast.LocationId) is not null && (cast.Caster is not { } caster || RaceOnMap(caster)),
            CustomMapGiveSpellEffect give => !string.IsNullOrEmpty(give.Spell.Value),
            CustomMapAllyEffect ally => RaceOnMap(ally.Race1) && RaceOnMap(ally.Race2) && ally.Race1 != ally.Race2,
            CustomMapRelationEffect relation => RaceOnMap(relation.Race1) && RaceOnMap(relation.Race2) && relation.Race1 != relation.Race2,
            CustomMapRemoveMountainsEffect mountains => GetLocation(mountains.LocationId) is not null,
            CustomMapRemoveLandmarkEffect remove => landmarks.Any(landmark => landmark.Id == remove.LandmarkId),
            CustomMapChangeLandmarkEffect change => landmarks.Any(landmark => landmark.Id == change.LandmarkId) && !string.IsNullOrEmpty(change.Type.Value),
            CustomMapChangeTerrainEffect terrain => GetLocation(terrain.LocationId) is not null && terrain.Size is > 0 and <= 15,
            CustomMapVariableEffect variable => GetVariable(variable.VariableId) is not null && !(variable.Operation == ScenarioVariableOperation.Divide && variable.Value == 0),
            CustomMapDestroyItemEffect destroy => !string.IsNullOrEmpty(destroy.Item.Value),
            _ => throw new ArgumentOutOfRangeException(nameof(effect), effect, null)
        };
    }

    private static bool KindUses(CustomMapCondition condition, CustomMapEventTarget target)
    {
        return (condition, target) switch
        {
            (CustomMapLeaderItemCondition leader, _) => UsesArmy(leader.Army, target),
            (CustomMapVisitSiteCondition visit, CustomMapEventTarget.Site site) => visit.SiteId == site.Id,
            (CustomMapArmyToZoneCondition toZone, CustomMapEventTarget.Location location) => toZone.LocationId == location.Id,
            (CustomMapArmyToZoneCondition toZone, _) => UsesArmy(toZone.Army, target),
            (CustomMapArmyToCityCondition toCity, _) => UsesArmy(toCity.Army, target) || IsCity(toCity.City, target),
            (CustomMapItemToZoneCondition item, CustomMapEventTarget.Location location) => item.LocationId == location.Id,
            (CustomMapVariableCondition variable, CustomMapEventTarget.Variable other) => variable.VariableId == other.Id ||
                                                                                          (variable.Join != CustomMapVariableJoin.None && variable.SecondVariableId == other.Id),
            _ => false
        };
    }

    private static bool KindUses(CustomMapEffect effect, CustomMapEventTarget target)
    {
        return (effect, target) switch
        {
            (CustomMapCastSpellZoneEffect cast, CustomMapEventTarget.Location location) => cast.LocationId == location.Id,
            (CustomMapRemoveMountainsEffect mountains, CustomMapEventTarget.Location location) => mountains.LocationId == location.Id,
            (CustomMapChangeTerrainEffect terrain, CustomMapEventTarget.Location location) => terrain.LocationId == location.Id,
            (CustomMapRemoveLandmarkEffect remove, CustomMapEventTarget.Landmark landmark) => remove.LandmarkId == landmark.Id,
            (CustomMapChangeLandmarkEffect change, CustomMapEventTarget.Landmark landmark) => change.LandmarkId == landmark.Id,
            (CustomMapVariableEffect variable, CustomMapEventTarget.Variable other) => variable.VariableId == other.Id,
            _ => false
        };
    }
}

// A map variable: a whole number every event sees, starting at Initial.
public sealed record CustomMapVariable(int Id, string Name, int Initial);

// The game's three relation levels (saved as 0, 49 and 100: war below 34, peace above 66).
public enum CustomMapRelation
{
    War,
    Neutral,
    Peace
}

public enum CustomMapVariableJoin
{
    None,
    And,
    Or
}

// A concerned race has the item (any army or city of it).
public sealed record CustomMapOwnItemCondition(ItemDefinitionId Item) : CustomMapCondition;

// The army (or any army from the template) carries the item.
public sealed record CustomMapLeaderItemCondition(CustomMapArmyRef Army, ItemDefinitionId Item) : CustomMapCondition;

public sealed record CustomMapRelationCondition(WorldRace Race1, WorldRace Race2, CustomMapRelation Level) : CustomMapCondition;

public sealed record CustomMapAllianceCondition(WorldRace Race1, WorldRace Race2) : CustomMapCondition;

// A concerned race's land covers at least Percent of the map.
public sealed record CustomMapLandCondition(int Percent) : CustomMapCondition;

// A concerned race has visited the site.
public sealed record CustomMapVisitSiteCondition(int SiteId) : CustomMapCondition;

// That army (or one from the template, of the triggering player) is in the zone.
public sealed record CustomMapArmyToZoneCondition(CustomMapArmyRef Army, int LocationId) : CustomMapCondition;

public sealed record CustomMapArmyToCityCondition(CustomMapArmyRef Army, CustomMapCity City) : CustomMapCondition;

// An army of a concerned race carrying the item enters the zone.
public sealed record CustomMapItemToZoneCondition(ItemDefinitionId Item, int LocationId) : CustomMapCondition;

// Minimum <= variable <= Maximum, optionally joined with a second range by and / or.
public sealed record CustomMapVariableCondition(int VariableId, int Minimum, int Maximum, CustomMapVariableJoin Join = CustomMapVariableJoin.None, int SecondVariableId = 0, int SecondMinimum = 0, int SecondMaximum = 0) : CustomMapCondition;

// Casts the spell on the army that fired the event; Caster null = nobody (a summon then belongs to the triggering player).
public sealed record CustomMapCastSpellEffect(WorldSpellDefinitionId Spell, WorldRace? Caster) : CustomMapEffect;

public sealed record CustomMapCastSpellZoneEffect(WorldSpellDefinitionId Spell, int LocationId, WorldRace? Caster) : CustomMapEffect;

// The triggering player learns the spell.
public sealed record CustomMapGiveSpellEffect(WorldSpellDefinitionId Spell) : CustomMapEffect;

public sealed record CustomMapAllyEffect(WorldRace Race1, WorldRace Race2, bool Permanent) : CustomMapEffect;

// Sets the relation. The game only acts on it when it ends an alliance (allies set to neutral or war).
public sealed record CustomMapRelationEffect(WorldRace Race1, WorldRace Race2, CustomMapRelation Level) : CustomMapEffect;

public sealed record CustomMapRemoveMountainsEffect(int LocationId) : CustomMapEffect;

public sealed record CustomMapRemoveLandmarkEffect(int LandmarkId) : CustomMapEffect;

public sealed record CustomMapChangeLandmarkEffect(int LandmarkId, WorldLandmarkDefinitionId Type) : CustomMapEffect;

// Turns a Size x Size square centred on the zone into the terrain (resources nearby flip like with land spread).
public sealed record CustomMapChangeTerrainEffect(int LocationId, WorldMapTerrain Terrain, int Size) : CustomMapEffect;

public sealed record CustomMapVariableEffect(int VariableId, ScenarioVariableOperation Operation, int Value) : CustomMapEffect;

// Destroys every copy of the item (TriggererOnly: only those the triggering army carries).
public sealed record CustomMapDestroyItemEffect(ItemDefinitionId Item, bool TriggererOnly) : CustomMapEffect;
