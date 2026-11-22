using System.Collections.Immutable;

namespace Gallean.Engine;

// Writes a CustomMap as a regular scenario: <maps>/<id>/*.json tables plus a row in <maps>/Sinfo.json, so the
// generated ScenarioDatabaseLoader and ScenarioLoader read it like a bundled scenario.
public sealed class CustomMapWriter(GlobalDatabase globalDb)
{
    public const string IndexFileName = "Sinfo.json";
    public const string SkirmishCampaignId = "C000CC0001";

    private const int UnsetPlayer = 99;
    private const int MaxPlayerSlots = 13;
    private const int GuardianGridPosition = 2;
    private const int BlockWidth = 8;
    private const int BlockHeight = 4;
    private const int BlockDataLength = 171;
    // Army IDs start after the capitals' (one per player slot): stack KC0100 + n, units UN0600 + 6n + k.
    internal const int FirstArmyNumber = 0x100;
    // Village IDs start after the capitals' too: city FT0100 + n, garrison units UN8000 + 6n + k (after the armies').
    internal const int FirstVillageNumber = 0x100;
    internal const int FirstVillageUnitNumber = 0x8000;
    // Ruin guards: UNA000 + 6n + k. Ruins RU + n, bags BG + n, sites SI + n, scenario items IM + running number,
    // landmarks MM + n (the original scenarios use MM for landmarks too), rods RO + n.
    internal const int FirstRuinUnitNumber = 0xA000;

    // Capital garrisons set in the editor: units UNB000 + 6i + k for player slot i.
    internal const int FirstCapitalUnitNumber = 0xB000;

    // The slot the guardian takes in a new capital (database position 2).
    internal static readonly GridPosition GuardianSlot = new(GuardianGridPosition % GridPosition.LineCount, GuardianGridPosition / GridPosition.LineCount);

    public void Write(CustomMap map, string mapsDirectory)
    {
        Db.ScenarioId scenarioId = new(map.Id);
        string prefix = map.Id[..4];
        string scenarioDirectory = Path.Combine(mapsDirectory, map.Id);
        string stagingDirectory = scenarioDirectory + ".saving";

        if (Directory.Exists(stagingDirectory))
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }

        Directory.CreateDirectory(stagingDirectory);

        NeutralSides sides = BuildNeutralSides(map);
        Rows rows = BuildRows(map, scenarioId, prefix, sides);

        WriteTable(stagingDirectory, "Splayer.json", rows.Players);
        WriteTable(stagingDirectory, "SSubRace.json", rows.Subraces);
        WriteTable(stagingDirectory, "SFort.json", rows.Fortifications);
        WriteTable(stagingDirectory, "Sstack.json", rows.Stacks);
        WriteTable(stagingDirectory, "SUnit.json", rows.Units);
        WriteTable(stagingDirectory, "SUGroup.json", rows.UnitGroups);
        WriteTable(stagingDirectory, "SRel.json", rows.Relations);
        WriteTable(stagingDirectory, "SMapBlck.json", BuildMapBlocks(map, scenarioId, prefix));
        WriteTable(stagingDirectory, "SMounts.json", BuildMountains(map, scenarioId));
        WriteTable(stagingDirectory, "SRoad.json", BuildRoads(map, scenarioId, prefix));
        WriteTable(stagingDirectory, "SCrys.json", BuildResources(map, scenarioId, prefix));
        WriteTable(stagingDirectory, "SRuin.json", rows.Ruins);
        WriteTable(stagingDirectory, "SBag.json", rows.Bags);
        WriteTable(stagingDirectory, "SItem.json", rows.Items);
        WriteTable(stagingDirectory, "SInventr.json", rows.Inventories);
        WriteTable(stagingDirectory, "SSite.json", rows.Sites);
        WriteTable(stagingDirectory, "SMerch.json", rows.Merchants);
        WriteTable(stagingDirectory, "SMage.json", rows.Mages);
        WriteTable(stagingDirectory, "SMercs.json", rows.Mercenaries);
        WriteTable(stagingDirectory, "SLmark.json", BuildLandmarks(map, scenarioId, prefix));
        WriteTable(stagingDirectory, "SMapEl.json", BuildMapElements(map, scenarioId, prefix));
        WriteTable(stagingDirectory, "SRod.json", BuildRods(map, scenarioId, prefix));
        WriteTable(stagingDirectory, "SLoc.json", BuildLocations(map, scenarioId, prefix, rows.Texts));
        EventRows events = BuildEvents(map, scenarioId, prefix, rows.Texts);
        WriteTable(stagingDirectory, "SEvent.json", events.Events);
        WriteTable(stagingDirectory, "SEvCond.json", events.Conditions);
        WriteTable(stagingDirectory, "SEvEffec.json", events.Effects);
        WriteTable(stagingDirectory, "SEvUnfog.json", events.Unfogs);
        WriteTable(stagingDirectory, "Tscen.json", rows.Texts);
        CustomMapEventsFile.Write(Path.Combine(stagingDirectory, CustomMapEventsFile.FileName), map.Events, map.Templates, map.Variables);
        WriteTable(stagingDirectory, "SVars.json", BuildVariables(map, scenarioId));
        WriteTable(stagingDirectory, "SStkTemp.json", BuildTemplates(map, scenarioId, prefix, sides));
        WriteTable(stagingDirectory, "SRules.json", new List<Db.ScenarioRules> { new(scenarioId, MakeId(prefix, "RL", 1), map.ForeignSpellMarkup, map.NeutralKinds, TownIncomeCalculator.Format(map.TownIncome), map.NeutralBuying, map.NeutralUnitFactor) });

        if (Directory.Exists(scenarioDirectory))
        {
            Directory.Delete(scenarioDirectory, recursive: true);
        }

        Directory.Move(stagingDirectory, scenarioDirectory);
        WriteTranslations(mapsDirectory, map.Id, events.Translations);

        string indexPath = Path.Combine(mapsDirectory, IndexFileName);
        List<Db.ScenarioInfo> index = File.Exists(indexPath) ? DbTableFile.Read<Db.ScenarioInfo>(indexPath) : [];
        index.RemoveAll(info => info.ScenarioId == scenarioId);
        index.Add(BuildInfo(map, scenarioId, prefix, rows.PlayerRaces));
        index.Sort((a, b) => string.CompareOrdinal(a.ScenarioId.Value, b.ScenarioId.Value));
        WriteTable(mapsDirectory, IndexFileName, index);
    }

    // The world stack ID an army gets in the saved scenario.
    public static string GetArmyStackId(string mapId, int armyId)
    {
        return MakeId(mapId[..4], "KC", FirstArmyNumber + armyId).Value;
    }

    // The fortification ID a village gets in the saved scenario.
    public static string GetVillageId(string mapId, int villageId)
    {
        return MakeId(mapId[..4], "FT", FirstVillageNumber + villageId).Value;
    }

    // The resource node ID a resource gets in the saved scenario.
    public static string GetResourceId(string mapId, int resourceId)
    {
        return MakeId(mapId[..4], "CR", resourceId).Value;
    }

    public static string GetRuinId(string mapId, int ruinId)
    {
        return MakeId(mapId[..4], "RU", ruinId).Value;
    }

    public static string GetBagId(string mapId, int bagId)
    {
        return MakeId(mapId[..4], "BG", bagId).Value;
    }

    public static string GetSiteId(string mapId, int siteId)
    {
        return MakeId(mapId[..4], "SI", siteId).Value;
    }

    public static string GetLandmarkId(string mapId, int landmarkId)
    {
        return MakeId(mapId[..4], "MM", landmarkId).Value;
    }

    public static string GetRodId(string mapId, int rodId)
    {
        return MakeId(mapId[..4], "RO", rodId).Value;
    }

    // The scenario location ID (SLoc LOC_ID) a location gets; events and army orders point at it.
    public static string GetLocationId(string mapId, int locationId)
    {
        return MakeId(mapId[..4], "LO", locationId).Value;
    }

    public static void Delete(string mapsDirectory, string mapId)
    {
        string indexPath = Path.Combine(mapsDirectory, IndexFileName);

        if (File.Exists(indexPath))
        {
            List<Db.ScenarioInfo> index = DbTableFile.Read<Db.ScenarioInfo>(indexPath);
            index.RemoveAll(info => info.ScenarioId.Value == mapId);
            WriteTable(mapsDirectory, IndexFileName, index);
        }

        string scenarioDirectory = Path.Combine(mapsDirectory, mapId);

        if (Directory.Exists(scenarioDirectory))
        {
            Directory.Delete(scenarioDirectory, recursive: true);
        }

        WriteTranslations(mapsDirectory, mapId, new Dictionary<string, List<Db.TextScenario>>());
    }

    // The folder whose <map id>/Tscen.json holds a language's translated texts (the game's text provider lays them
    // over the scenario's own texts, like the bundled Languages/<code>/Scens folders).
    public static string GetLanguageDirectory(string mapsDirectory, string languageCode)
    {
        return Path.Combine(mapsDirectory, "Languages", languageCode);
    }

    public static IReadOnlyList<Db.ScenarioInfo> ReadIndex(string mapsDirectory)
    {
        string indexPath = Path.Combine(mapsDirectory, IndexFileName);

        return File.Exists(indexPath) ? DbTableFile.Read<Db.ScenarioInfo>(indexPath) : [];
    }

    private sealed record Rows(
        List<Db.ScenarioPlayer> Players,
        List<Db.ScenarioSubrace> Subraces,
        List<Db.ScenarioFortification> Fortifications,
        List<Db.ScenarioStack> Stacks,
        List<Db.ScenarioUnit> Units,
        List<Db.ScenarioUnitGroup> UnitGroups,
        List<Db.ScenarioRelation> Relations,
        List<Db.TextScenario> Texts,
        List<Db.RaceType> PlayerRaces,
        List<Db.ScenarioRuin> Ruins,
        List<Db.ScenarioBag> Bags,
        List<Db.ScenarioItem> Items,
        List<Db.ScenarioInventory> Inventories,
        List<Db.ScenarioSite> Sites,
        List<Db.ScenarioMerchant> Merchants,
        List<Db.ScenarioMage> Mages,
        List<Db.ScenarioMercenary> Mercenaries);

    private Rows BuildRows(CustomMap map, Db.ScenarioId scenarioId, string prefix, NeutralSides sides)
    {
        Rows rows = new([], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);

        // Player 0 is the (primary) neutral player; it owns nothing on a new map but the engine requires one.
        List<(Db.RaceType Race, CustomMapPlayer? Player, Db.SubraceType? Kind)> players = [(Db.RaceType.Neutral, null, null)];
        players.AddRange(map.Players.Select(player => (ToRaceType(player.Race), (CustomMapPlayer?)player, (Db.SubraceType?)null)));

        if (players.Count > MaxPlayerSlots)
        {
            throw new InvalidOperationException($"A map can have at most {MaxPlayerSlots - 1} players.");
        }

        // Neutral kinds (map rule) come after the races: they take no quest-list slot, so existing IDs stay put.
        players.AddRange(sides.Kinds.Select(kind => (Db.RaceType.Neutral, (CustomMapPlayer?)null, (Db.SubraceType?)kind)));

        for (int i = 0; i < players.Count; i++)
        {
            (Db.RaceType raceType, CustomMapPlayer? mapPlayer, Db.SubraceType? kind) = players[i];
            Db.Race race = globalDb.Races.Values.Single(candidate => candidate.RaceType == raceType);
            Db.Lord lord = globalDb.Lords.Values
                .Where(candidate => candidate.RaceId == race.RaceId)
                .OrderBy(candidate => candidate.LordId.Value, StringComparer.OrdinalIgnoreCase)
                .First();
            Db.Id playerId = MakeId(prefix, "PL", i);
            Db.Id subraceId = MakeId(prefix, "SR", i);

            if (kind is null)
            {
                rows.PlayerRaces.Add(raceType);
            }

            rows.Players.Add(new Db.ScenarioPlayer(
                scenarioId,
                playerId,
                race.RaceId,
                lord.LordId,
                MakeId(prefix, "FG", i),
                ResearchTurn: 0,
                ConstructTurn: 0,
                MakeId(prefix, "PB", i),
                MakeId(prefix, "KS", i),
                mapPlayer is null ? new Db.ResourceSet(map.NeutralStartingGold, 0, 0, 0, 0, 0) : GetStartingBank(mapPlayer),
                Db.ResourceSet.Empty,
                mapPlayer is null ? Db.AiAttitudeType.Medium : Db.AiAttitudeType.Humongous,
                Face: 0,
                Spy1Id: null,
                Spy2Id: null,
                Spy3Id: null,
                QuantityBreaks: 0,
                IsHuman: false,
                CapturedById: null,
                AlwaysAi: false,
                ExMapId1: null,
                ExMapId2: null,
                ExMapId3: null,
                ExMapTurn1: 0,
                ExMapTurn2: 0,
                ExMapTurn3: 0));
            rows.Subraces.Add(new Db.ScenarioSubrace(scenarioId, subraceId, playerId, kind is { } subrace ? (int)subrace : GetSubrace(raceType), GetBanner(raceType)));
            rows.Texts.Add(Text(scenarioId, playerId, Db.TextScenario.NameTextId, kind is { } named ? GetKindName(named) : GetPlayerName(raceType)));

            if (mapPlayer?.Capital is not { } capital)
            {
                continue;
            }

            // IDs follow the player index, so they stay the same whichever capitals are placed.
            Db.Id capitalId = MakeId(prefix, "FT", i);
            CustomMapStack? stationed = map.GetStationedStack(mapPlayer);
            Db.Id stackId = stationed is null ? MakeId(prefix, "KC", i) : MakeId(prefix, "KC", FirstArmyNumber + stationed.Id);
            Db.Id guardianId = MakeId(prefix, "UN", i * 2);
            Db.Id leaderId = MakeId(prefix, "UN", (i * 2) + 1);
            Db.Unit guardian = globalDb.Units[race.GuardianId ?? throw new InvalidDataException($"Race {raceType} has no guardian.")];
            Db.Unit leader = globalDb.Units[race.Leader1Id ?? throw new InvalidDataException($"Race {raceType} has no leader.")];

            rows.Fortifications.Add(new Db.ScenarioFortification(
                scenarioId,
                capitalId,
                capital.X,
                capital.Y,
                playerId,
                subraceId,
                stackId,
                Size: null,
                SizeMax: null,
                RegenerationBonus: null,
                ProtectionBuildingId: null,
                Morale: null,
                GrowthTurn: null,
                ProtectedFromUndead: null,
                ProtectedFromHeretic: null,
                ProtectedFromHuman: null,
                ProtectedFromDwarf: null,
                ProtectedFromElf: null,
                AiPriority: 3,
                RiotTurns: null));
            rows.Texts.Add(Text(scenarioId, capitalId, Db.TextScenario.NameTextId, $"{GetPlayerName(raceType)} Capital"));

            if (mapPlayer.Garrison.Any(unit => unit is not null))
            {
                rows.UnitGroups.Add(BuildGroup(rows, mapPlayer.Garrison, scenarioId, prefix, capitalId, FirstCapitalUnitNumber + (i * GridPosition.Count)));
            }
            else
            {
                rows.Units.Add(new Db.ScenarioUnit(scenarioId, guardianId, guardian.Hitpoints, 0, 0, guardian.UnitId, 1, false, false));
                rows.UnitGroups.Add(SingleUnitGroup(scenarioId, capitalId, guardianId));
            }

            // An army set in the editor is written with the others (AddArmy). Without one, the race's first leader
            // waits in the capital. Either way the human player's leader is swapped for the one chosen at the start.
            if (stationed is not null)
            {
                continue;
            }

            rows.Units.Add(new Db.ScenarioUnit(scenarioId, leaderId, leader.Hitpoints, 0, 0, leader.UnitId, 1, false, false));
            rows.UnitGroups.Add(SingleUnitGroup(scenarioId, stackId, leaderId));
            rows.Stacks.Add(new Db.ScenarioStack(
                scenarioId,
                stackId,
                SourceTemplateId: null,
                capital.X,
                capital.Y,
                leaderId,
                LeaderAlive: true,
                Morale: 0,
                Move: leader.Move ?? 20,
                Facing: 0,
                BannerId: null,
                TomeId: null,
                Battle1Id: null,
                Battle2Id: null,
                Artifact1Id: null,
                Artifact2Id: null,
                BootsId: null,
                playerId,
                subraceId,
                capitalId,
                Invisible: false,
                UpgradeCount: 0,
                AiIgnore: false,
                AiPriority: 3,
                Db.OrderType.Normal,
                OrderTargetId: null,
                CreationLevel: 1,
                NumberOfBattles: 0,
                Db.OrderType.Normal,
                AiOrderTargetId: null));
        }

        foreach (CustomMapStack stack in map.Stacks)
        {
            AddArmy(rows, map, stack, scenarioId, prefix, sides.GetIndex(map, stack.Owner, 'S', stack.Id));
        }

        foreach (CustomMapVillage village in map.Villages)
        {
            AddVillage(rows, map, village, scenarioId, prefix, sides.GetIndex(map, village.Owner, 'V', village.Id));
        }

        foreach (CustomMapRuin ruin in map.Ruins)
        {
            AddRuin(rows, ruin, scenarioId, prefix);
        }

        foreach (CustomMapBag bag in map.Bags)
        {
            AddBag(rows, bag, scenarioId, prefix);
        }

        foreach (CustomMapSite site in map.Sites)
        {
            AddSite(rows, site, scenarioId, prefix);
        }

        for (int i = 0; i < rows.PlayerRaces.Count; i++)
        {
            for (int j = i + 1; j < rows.PlayerRaces.Count; j++)
            {
                rows.Relations.Add(new Db.ScenarioRelation(scenarioId, rows.PlayerRaces[i], rows.PlayerRaces[j], 0));
            }
        }

        return rows;
    }

    private static void AddArmy(Rows rows, CustomMap map, CustomMapStack stack, Db.ScenarioId scenarioId, string prefix, int playerIndex)
    {

        if (playerIndex < 0)
        {
            throw new InvalidOperationException($"Army {stack.Id} belongs to {stack.Owner}, who is not on the map.");
        }

        int number = FirstArmyNumber + stack.Id;
        Db.Id stackId = MakeId(prefix, "KC", number);
        Db.Id playerId = MakeId(prefix, "PL", playerIndex);
        Db.Id subraceId = MakeId(prefix, "SR", playerIndex);
        int firstUnitNumber = (FirstArmyNumber * 6) + (stack.Id * GridPosition.Count);
        int leaderSlot = stack.Slots.ToList().FindIndex(unit => unit is { IsLeader: true });
        Db.Id? leaderId = leaderSlot >= 0 ? MakeId(prefix, "UN", firstUnitNumber + leaderSlot) : null;
        int capitalIndex = map.Players.ToList().FindIndex(player => player.Entrance == stack.Position);
        Db.Id? stationedIn = map.Villages.FirstOrDefault(village => village.Entrance == stack.Position) is { } village
            ? MakeId(prefix, "FT", FirstVillageNumber + village.Id)
            : capitalIndex >= 0 ? MakeId(prefix, "FT", 1 + capitalIndex) : null;

        rows.UnitGroups.Add(BuildGroup(rows, stack.Slots, scenarioId, prefix, stackId, firstUnitNumber));
        rows.Stacks.Add(new Db.ScenarioStack(
            scenarioId,
            stackId,
            SourceTemplateId: null,
            stack.Position.X,
            stack.Position.Y,
            leaderId ?? throw new InvalidOperationException($"Army {stack.Id} has no leader."),
            LeaderAlive: true,
            Morale: 0,
            Move: stack.Leader.MovementPoints,
            Facing: stack.Id % 8,
            BannerId: null,
            TomeId: null,
            Battle1Id: null,
            Battle2Id: null,
            Artifact1Id: null,
            Artifact2Id: null,
            BootsId: null,
            playerId,
            subraceId,
            stationedIn,
            Invisible: false,
            UpgradeCount: 0,
            AiIgnore: false,
            AiPriority: 3,
            stack.Owner == WorldRace.Neutral ? Db.OrderType.Stand : Db.OrderType.Normal,
            OrderTargetId: null,
            CreationLevel: 1,
            NumberOfBattles: 0,
            Db.OrderType.Normal,
            AiOrderTargetId: null));
    }

    // Map rule "neutral kinds": each neutral subrace type in use (greenskins, water creatures, barbarians, ...) gets its
    // own neutral player, like tools/NeutralSplitter, so only neutrals of one kind share a side; different kinds are
    // hostile. An army's kind is its leader's subrace; a village's is its garrison's most common subrace (else its
    // stationed army's leader), and an army stationed in a neutral village joins the village's side. Plain neutral
    // units (spiders, thugs, ...) and anything else stay with player 0, as do all neutrals when the rule is off.
    private sealed class NeutralSides
    {
        public List<Db.SubraceType> Kinds { get; } = [];

        public Dictionary<(char Kind, int Id), Db.SubraceType> Objects { get; } = [];

        public int GetIndex(CustomMap map, WorldRace owner, char kind, int id)
        {
            return owner == WorldRace.Neutral && Objects.TryGetValue((kind, id), out Db.SubraceType subrace)
                ? 1 + map.Players.Count + Kinds.IndexOf(subrace)
                : GetPlayerIndex(map, owner);
        }
    }

    private NeutralSides BuildNeutralSides(CustomMap map)
    {
        NeutralSides sides = new();

        if (!map.NeutralKinds)
        {
            return sides;
        }

        static bool IsKind(Db.SubraceType subrace) => subrace is >= Db.SubraceType.NeutralHuman and <= Db.SubraceType.NeutralWolf;

        // The leader's kind; without a leader (garrisons) the most common kind among the units, ignoring units of no
        // neutral kind (an ent of the elven race among wild elves doesn't make the village plain neutral).
        Db.SubraceType? KindOf(IEnumerable<CustomMapUnit?> slots)
        {
            CustomMapUnit[] units = [.. slots.OfType<CustomMapUnit>()];
            Db.SubraceType Subrace(CustomMapUnit unit) => globalDb.Units[new Db.Id(unit.TypeId.Value)].SubraceType;

            if (units.FirstOrDefault(unit => unit.IsLeader) is { } leader)
            {
                return IsKind(Subrace(leader)) ? Subrace(leader) : null;
            }

            return units.Select(Subrace).Where(IsKind).GroupBy(subrace => subrace)
                .OrderByDescending(group => group.Count()).ThenBy(group => group.Key)
                .Select(group => (Db.SubraceType?)group.Key).FirstOrDefault();
        }

        foreach (CustomMapVillage village in map.Villages.Where(village => village.Owner == WorldRace.Neutral))
        {
            CustomMapStack? stationed = map.GetStationedStack(village);

            if ((KindOf(village.Garrison) ?? (stationed is null ? null : KindOf(stationed.Slots))) is { } kind)
            {
                sides.Objects[('V', village.Id)] = kind;

                if (stationed is not null)
                {
                    sides.Objects[('S', stationed.Id)] = kind;
                }
            }
        }

        foreach (CustomMapStack stack in map.Stacks.Where(stack => stack.Owner == WorldRace.Neutral && !sides.Objects.ContainsKey(('S', stack.Id))))
        {
            if (map.Villages.Any(village => village.Owner == WorldRace.Neutral && village.Entrance == stack.Position))
            {
                continue;
            }

            if (KindOf(stack.Slots) is { } kind)
            {
                sides.Objects[('S', stack.Id)] = kind;
            }
        }

        foreach (CustomMapArmyTemplate template in map.Templates.Where(template => template.Owner == WorldRace.Neutral))
        {
            if (KindOf(template.Slots) is { } kind)
            {
                sides.Objects[('T', template.Id)] = kind;
            }
        }

        sides.Kinds.AddRange(sides.Objects.Values.Distinct().Order());

        return sides;
    }

    private static string GetKindName(Db.SubraceType kind)
    {
        return kind switch
        {
            Db.SubraceType.NeutralHuman => "Free Men",
            Db.SubraceType.NeutralElf => "Wild Elves",
            Db.SubraceType.NeutralGreenSkin => "Greenskins",
            Db.SubraceType.NeutralDragon => "Dragons",
            Db.SubraceType.NeutralMarsh => "Marsh Folk",
            Db.SubraceType.NeutralWater => "Sea Creatures",
            Db.SubraceType.NeutralBarbarian => "Barbarians",
            Db.SubraceType.NeutralWolf => "Wolves",
            _ => "Neutrals"
        };
    }

    private static int GetPlayerIndex(CustomMap map, WorldRace owner)
    {
        return owner == WorldRace.Neutral
            ? 0
            : 1 + map.Players.Select(player => player.Race).ToList().IndexOf(owner);
    }

    private static void AddVillage(Rows rows, CustomMap map, CustomMapVillage village, Db.ScenarioId scenarioId, string prefix, int playerIndex)
    {

        if (playerIndex < 0)
        {
            throw new InvalidOperationException($"Village {village.Id} belongs to {village.Owner}, who is not on the map.");
        }

        Db.Id cityId = MakeId(prefix, "FT", FirstVillageNumber + village.Id);
        Db.Id playerId = MakeId(prefix, "PL", playerIndex);
        Db.Id subraceId = MakeId(prefix, "SR", playerIndex);
        CustomMapStack? stationed = map.GetStationedStack(village);

        rows.Fortifications.Add(new Db.ScenarioFortification(
            scenarioId,
            cityId,
            village.TopLeft.X,
            village.TopLeft.Y,
            playerId,
            subraceId,
            Stack: stationed is null ? null : MakeId(prefix, "KC", FirstArmyNumber + stationed.Id),
            Size: village.Level,
            SizeMax: null,
            RegenerationBonus: 0,
            ProtectionBuildingId: null,
            Morale: 0,
            GrowthTurn: 0,
            ProtectedFromUndead: false,
            ProtectedFromHeretic: false,
            ProtectedFromHuman: false,
            ProtectedFromDwarf: false,
            ProtectedFromElf: false,
            AiPriority: 3,
            RiotTurns: 0));
        rows.Texts.Add(Text(scenarioId, cityId, Db.TextScenario.NameTextId, string.IsNullOrWhiteSpace(village.Name) ? $"Village {village.Id}" : village.Name.Trim()));
        rows.UnitGroups.Add(BuildGroup(rows, village.Garrison, scenarioId, prefix, cityId, FirstVillageUnitNumber + (village.Id * GridPosition.Count)));
    }

    private static void AddRuin(Rows rows, CustomMapRuin ruin, Db.ScenarioId scenarioId, string prefix)
    {
        Db.Id ruinId = MakeId(prefix, "RU", ruin.Id);
        Db.ResourceSet cash = new(Math.Clamp(ruin.Gold, 0, CustomMapRuin.MaxGold), 0, 0, 0, 0, 0);

        rows.Ruins.Add(new Db.ScenarioRuin(
            scenarioId,
            ruinId,
            ruin.Variant,
            ruin.TopLeft.X,
            ruin.TopLeft.Y,
            cash.IsEmpty ? "g0000:r0000:y0000:e0000:w0000:b0000" : cash.Value,
            ruin.Item is { } item ? new Db.Id(item.Value) : null,
            Looter: null,
            AiPriority: 3));
        rows.Texts.Add(Text(scenarioId, ruinId, Db.TextScenario.RuinTitleTextId, string.IsNullOrWhiteSpace(ruin.Name) ? $"Ruin {ruin.Id}" : ruin.Name.Trim()));
        rows.UnitGroups.Add(BuildGroup(rows, ruin.Guards, scenarioId, prefix, ruinId, FirstRuinUnitNumber + (ruin.Id * GridPosition.Count)));
    }

    private static void AddBag(Rows rows, CustomMapBag bag, Db.ScenarioId scenarioId, string prefix)
    {
        Db.Id bagId = MakeId(prefix, "BG", bag.Id);

        rows.Bags.Add(new Db.ScenarioBag(scenarioId, bagId, bag.Position.X, bag.Position.Y, ObjValue: null, bag.Variant, AiPriority: 3));

        foreach (ItemDefinitionId item in bag.Items)
        {
            Db.Id itemId = MakeId(prefix, "IM", rows.Items.Count);
            rows.Items.Add(new Db.ScenarioItem(scenarioId, itemId, new Db.Id(item.Value)));
            rows.Inventories.Add(new Db.ScenarioInventory(scenarioId, bagId, itemId));
        }
    }

    // The loader tells site types apart by which quantity is set: items (merchant), units (mercenaries), spells
    // (mage tower) or none (trainer).
    private static void AddSite(Rows rows, CustomMapSite site, Db.ScenarioId scenarioId, string prefix)
    {
        Db.Id siteId = MakeId(prefix, "SI", site.Id);

        rows.Sites.Add(new Db.ScenarioSite(
            scenarioId,
            siteId,
            site.Variant,
            ImageInterface: null,
            VisiterId: null,
            site.TopLeft.X,
            site.TopLeft.Y,
            QuantityVisit: null,
            QuantitySpell: site.Type == CustomMapSiteType.MageTower ? site.Spells.Length : null,
            QuantityUnit: site.Type == CustomMapSiteType.Mercenaries ? site.Units.Length : null,
            QuantityItem: site.Type == CustomMapSiteType.Merchant ? site.Items.Length : null,
            BuyArmor: null,
            BuyJewel: null,
            BuyWeapon: null,
            BuyBanner: null,
            BuyPotion: null,
            BuyScroll: null,
            BuyWand: null,
            BuyValue: null,
            Mission: null,
            AiPriority: 3));
        rows.Texts.Add(Text(scenarioId, siteId, Db.TextScenario.SiteTitleTextId, string.IsNullOrWhiteSpace(site.Name) ? $"Site {site.Id}" : site.Name.Trim()));
        rows.Texts.Add(Text(scenarioId, siteId, Db.TextScenario.SiteDescriptionTextId, site.Greeting.Trim()));

        switch (site.Type)
        {
            case CustomMapSiteType.Merchant:
                rows.Merchants.AddRange(site.Items.Select(item => new Db.ScenarioMerchant(scenarioId, siteId, new Db.Id(item.Item.Value), item.Count)));
                break;

            case CustomMapSiteType.MageTower:
                rows.Mages.AddRange(site.Spells.Select(spell => new Db.ScenarioMage(scenarioId, siteId, new Db.Id(spell.Value))));
                break;

            case CustomMapSiteType.Mercenaries:
                rows.Mercenaries.AddRange(site.Units.Select(unit => new Db.ScenarioMercenary(scenarioId, siteId, new Db.Id(unit.Unit.Value), unit.Level, UnitUniq: false)));
                break;
        }
    }

    // Adds the units of a slot layout (GridPosition.ToIndex(), a large unit in its front slot) and returns their
    // group. Group positions use the database layout: position p is line p % 2, flank p / 2; a large unit fills both
    // lines.
    private static Db.ScenarioUnitGroup BuildGroup(Rows rows, IReadOnlyList<CustomMapUnit?> slots, Db.ScenarioId scenarioId, string prefix, Db.Id groupId, int firstUnitNumber)
    {
        List<Db.Id> unitIds = [];

        // The unit index of each slot's unit, kept per slot so one unit object placed in two slots stays two units.
        int[] unitIndexes = [.. Enumerable.Repeat(-1, GridPosition.Count)];

        for (int slot = 0; slot < GridPosition.Count; slot++)
        {
            if (slots[slot] is not { } unit)
            {
                continue;
            }

            Db.Id unitId = MakeId(prefix, "UN", firstUnitNumber + slot);
            unitIndexes[slot] = unitIds.Count;
            unitIds.Add(unitId);
            rows.Units.Add(new Db.ScenarioUnit(scenarioId, unitId, unit.Hitpoints, Xp: 0, Creation: 0, new Db.Id(unit.TypeId.Value), unit.Level, Transf: false, DynLevel: false));
        }

        int[] positions = new int[GridPosition.Count];

        for (int p = 0; p < GridPosition.Count; p++)
        {
            GridPosition position = new(p % GridPosition.LineCount, p / GridPosition.LineCount);
            int slot = position.ToIndex();

            if (slots[slot] is null && position.IsBackLine && slots[GridPosition.AtFrontLine(position.Flank).ToIndex()] is { IsLarge: true })
            {
                slot = GridPosition.AtFrontLine(position.Flank).ToIndex();
            }

            positions[p] = unitIndexes[slot];
        }

        Db.Id? Unit(int index) => index < unitIds.Count ? unitIds[index] : null;

        return new Db.ScenarioUnitGroup(
            scenarioId, groupId, Unit(0), Unit(1), Unit(2), Unit(3), Unit(4), Unit(5),
            positions[0], positions[1], positions[2], positions[3], positions[4], positions[5]);
    }

    private static List<Db.ScenarioCrystal> BuildResources(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        return
        [
            .. map.Resources.Select(resource => new Db.ScenarioCrystal(
                scenarioId,
                MakeId(prefix, "CR", resource.Id),
                (int)ToResourceType(resource.Type),
                resource.Position.X,
                resource.Position.Y,
                AiPriority: 3))
        ];
    }

    // Inverse of ScenarioLoader.MapCrystalType.
    private static Db.ResourceType ToResourceType(WorldResourceType type)
    {
        return type switch
        {
            WorldResourceType.Gold => Db.ResourceType.Gold,
            WorldResourceType.InfernalMana => Db.ResourceType.Red,
            WorldResourceType.LifeMana => Db.ResourceType.Yellow,
            WorldResourceType.DeathMana => Db.ResourceType.Orange,
            WorldResourceType.RuneMana => Db.ResourceType.White,
            WorldResourceType.GroveMana => Db.ResourceType.Blue,
            _ => throw new ArgumentOutOfRangeException(nameof(type), type, null)
        };
    }

    private static Db.ScenarioInfo BuildInfo(CustomMap map, Db.ScenarioId scenarioId, string prefix, List<Db.RaceType> playerRaces)
    {
        int[] slots = Enumerable.Repeat(UnsetPlayer, MaxPlayerSlots).ToArray();

        for (int i = 0; i < playerRaces.Count; i++)
        {
            slots[i] = (int)playerRaces[i];
        }

        string description = string.IsNullOrWhiteSpace(map.Description) ? "A custom map." : map.Description;

        return new Db.ScenarioInfo(
            scenarioId,
            MakeId(prefix, "IF", 0),
            CurrentTurn: 0,
            MapSeed: map.Id.Aggregate(17, (hash, ch) => (hash * 31) + ch) & 0x7FFFFFFF,
            map.Size,
            QuantityCities: map.Villages.Count,
            Creator: "Map editor",
            SourceM: false,
            Db.DifficultyType.Average,
            Db.DifficultyType.Average,
            new Db.Id(SkirmishCampaignId),
            MaxUnit: 5,
            MaxLeader: 10,
            MaxCity: 5,
            MaxSpell: 5,
            slots[0], slots[1], slots[2], slots[3], slots[4], slots[5], slots[6],
            slots[7], slots[8], slots[9], slots[10], slots[11], slots[12],
            map.Name,
            description,
            Briefing: "Destroy the enemy capitals.",
            BriefingLong1: description,
            BriefingLong2: null,
            BriefingLong3: null,
            BriefingLong4: null,
            BriefingLong5: null,
            DebunkWin: "Victory! Your enemies have been defeated.",
            DebunkWin2: null,
            DebunkWin3: null,
            DebunkWin4: null,
            DebunkWin5: null,
            DebunkLose: "Your capital has fallen.",
            SuggestedLevel: 1,
            O: false);
    }

    private static List<Db.ScenarioMapBlock> BuildMapBlocks(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        List<Db.ScenarioMapBlock> blocks = [];
        // Original block IDs hold two hex digits per axis; larger maps use three (see ScenarioMapDecoder).
        int digits = map.Size > 256 ? 3 : 2;
        byte[] data = new byte[BlockWidth * BlockHeight * sizeof(int)];

        for (int blockY = 0; blockY < map.Size; blockY += BlockHeight)
        {
            for (int blockX = 0; blockX < map.Size; blockX += BlockWidth)
            {
                Array.Clear(data);

                for (int i = 0; i < BlockWidth * BlockHeight; i++)
                {
                    WorldPosition position = new(blockX + (i % BlockWidth), blockY + (i / BlockWidth));

                    if (map.IsValidPosition(position))
                    {
                        BitConverter.TryWriteBytes(data.AsSpan(i * sizeof(int)), EncodeTile(map.GetCell(position)));
                    }
                }

                string blockId = $"{prefix}MB{blockY.ToString($"x{digits}")}{blockX.ToString($"x{digits}")}";
                blocks.Add(new Db.ScenarioMapBlock(scenarioId, new Db.Id(blockId), SixbitEncode(data)));
            }
        }

        return blocks;
    }

    private static List<Db.ScenarioLandmark> BuildLandmarks(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        return
        [
            .. map.Landmarks.Select(landmark => new Db.ScenarioLandmark(
                scenarioId,
                MakeId(prefix, "MM", landmark.Id),
                new Db.Id(landmark.Type.Value),
                landmark.TopLeft.X,
                landmark.TopLeft.Y,
                DescriptionText: null))
        ];
    }

    // Map elements tell the loader which tiles an object covers. It reads them only for landmarks of the mountain
    // type (their tiles become mountains), so only those are written, one row per tile as in the original scenarios.
    private List<Db.ScenarioMapElement> BuildMapElements(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        List<Db.ScenarioMapElement> elements = [];

        foreach (CustomMapLandmark landmark in map.Landmarks)
        {
            if (globalDb.Landmarks.TryGetValue(new Db.Id(landmark.Type.Value), out Db.Landmark? definition) && definition.Mountain == true)
            {
                Db.Id landmarkId = MakeId(prefix, "MM", landmark.Id);
                elements.AddRange(landmark.Footprint.EnumerateTiles().Select(tile => new Db.ScenarioMapElement(scenarioId, tile.X, tile.Y, landmarkId)));
            }
        }

        return elements;
    }

    // One SLoc row per location (LO + id), with its name as the Tscen NAME_TXT of that ID, as in the original maps.
    private static List<Db.ScenarioLocation> BuildLocations(CustomMap map, Db.ScenarioId scenarioId, string prefix, List<Db.TextScenario> texts)
    {
        List<Db.ScenarioLocation> rows = [];

        foreach (CustomMapLocation location in map.Locations.OrderBy(location => location.Id))
        {
            Db.Id locationId = MakeId(prefix, "LO", location.Id);
            rows.Add(new Db.ScenarioLocation(scenarioId, locationId, location.Center.X, location.Center.Y, location.Radius));
            texts.Add(Text(scenarioId, locationId, Db.TextScenario.NameTextId, location.Name));
        }

        return rows;
    }

    // One SRod row per rod; the owner is the race's player. (None of the original scenarios has rods.)
    private static List<Db.ScenarioRod> BuildRods(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        List<Db.ScenarioRod> rows = [];

        foreach (CustomMapRod rod in map.Rods)
        {
            if (rod.Owner == WorldRace.Neutral || !map.Players.Any(player => player.Race == rod.Owner))
            {
                throw new InvalidOperationException($"Rod {rod.Id} belongs to {rod.Owner}, who is not a player on the map.");
            }

            rows.Add(new Db.ScenarioRod(scenarioId, MakeId(prefix, "RO", rod.Id), MakeId(prefix, "PL", GetPlayerIndex(map, rod.Owner)), rod.Position.X, rod.Position.Y));
        }

        return rows;
    }

    private static List<Db.ScenarioMountain> BuildMountains(CustomMap map, Db.ScenarioId scenarioId)
    {
        return
        [
            .. map.Mountains.Select(mountain => new Db.ScenarioMountain(
                scenarioId,
                mountain.Id,
                mountain.Size,
                mountain.Size,
                mountain.TopLeft.X,
                mountain.TopLeft.Y,
                mountain.Variant,
                (int)Db.RaceType.Neutral))
        ];
    }

    // The original maps' SRoad INDEX for each neighbour mask (north 1, east 2, south 4, west 8): one fixed number
    // per road piece, measured on all 95 scenarios (e.g. east-west 2, north-south 3, crossroads 1, no neighbour 0).
    private static readonly int[] RoadIndexByMask = [0, 14, 12, 8, 13, 3, 9, 7, 15, 11, 2, 5, 10, 4, 6, 1];

    // One SRoad row per road tile. The loader only reads the position (the renderer picks the piece from the road
    // neighbours); INDEX is written as the original maps write it.
    private static List<Db.ScenarioRoad> BuildRoads(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        List<Db.ScenarioRoad> roads = [];

        bool IsRoad(int x, int y)
        {
            return x >= 0 && y >= 0 && x < map.Size && y < map.Size && map.Cells[(y * map.Size) + x].Feature == WorldMapFeature.Road;
        }

        for (int y = 0; y < map.Size; y++)
        {
            for (int x = 0; x < map.Size; x++)
            {
                if (!IsRoad(x, y))
                {
                    continue;
                }

                int mask = (IsRoad(x, y - 1) ? 1 : 0) | (IsRoad(x + 1, y) ? 2 : 0) | (IsRoad(x, y + 1) ? 4 : 0) | (IsRoad(x - 1, y) ? 8 : 0);
                roads.Add(new Db.ScenarioRoad(scenarioId, MakeId(prefix, "RA", roads.Count), x, y, RoadIndexByMask[mask], 0));
            }
        }

        return roads;
    }

    // Inverse of ScenarioMapDecoder.ParseTileInfo: terrain in bits 0-2, ground in bits 3-7, variant in bits 26-31.
    internal static int EncodeTile(WorldMapCell cell)
    {
        int terrain = cell.Terrain switch
        {
            WorldMapTerrain.Human => 1,
            WorldMapTerrain.Dwarf => 2,
            WorldMapTerrain.Heretic => 3,
            WorldMapTerrain.Undead => 4,
            WorldMapTerrain.Neutral or WorldMapTerrain.Water => 5,
            WorldMapTerrain.Elf => 6,
            _ => throw new ArgumentOutOfRangeException(nameof(cell), cell.Terrain, null)
        };
        int ground = cell.Terrain == WorldMapTerrain.Water
            ? 3
            : cell.Feature switch
            {
                WorldMapFeature.Forest => 1,
                WorldMapFeature.Mountain => 4,
                _ => 0
            };
        int variant = cell.Feature == WorldMapFeature.Forest ? cell.FeatureVariant.Value & 0b111111 : 0;

        return terrain | (ground << 3) | (variant << 26);
    }

    internal static string SixbitEncode(byte[] data)
    {
        char[] result = new char[BlockDataLength];
        int bits = 0;
        int bitCount = 0;
        int index = 0;

        foreach (byte value in data)
        {
            bits = (bits << 8) | value;
            bitCount += 8;

            while (bitCount >= 6)
            {
                bitCount -= 6;
                result[index++] = (char)('!' + ((bits >> bitCount) & 0x3F));
            }
        }

        if (bitCount > 0)
        {
            result[index++] = (char)('!' + ((bits << (6 - bitCount)) & 0x3F));
        }

        while (index < result.Length)
        {
            result[index++] = '!';
        }

        return new string(result);
    }

    private static Db.ScenarioUnitGroup SingleUnitGroup(Db.ScenarioId scenarioId, Db.Id groupId, Db.Id unitId)
    {
        int[] positions = [-1, -1, -1, -1, -1, -1];
        positions[GuardianGridPosition] = 0;

        return new Db.ScenarioUnitGroup(
            scenarioId, groupId, unitId, null, null, null, null, null,
            positions[0], positions[1], positions[2], positions[3], positions[4], positions[5]);
    }

    private sealed record EventRows(
        List<Db.ScenarioEvent> Events,
        List<Db.ScenarioEventCondition> Conditions,
        List<Db.ScenarioEventEffect> Effects,
        List<Db.ScenarioEventUnfog> Unfogs,
        Dictionary<string, List<Db.TextScenario>> Translations);

    // Events: each saveable editor event becomes one game event per condition group ("or"), all with the same
    // effects; game IDs EV + running number. A copy whose group has a "turn N" block occurs once. When the editor
    // event occurs once and has several copies, each copy first switches the others off, so it fires once in all.
    // Win/lose effects go last (the game counts an event as a win event only when Win is its last effect).
    private static EventRows BuildEvents(CustomMap map, Db.ScenarioId scenarioId, string prefix, List<Db.TextScenario> texts)
    {
        EventRows rows = new([], [], [], [], []);
        IReadOnlyList<CustomMapEvent> events = map.GetSaveableEvents();
        Dictionary<int, List<Db.Id>> gameIds = [];
        int number = 1;

        foreach (CustomMapEvent ev in events.OrderBy(ev => ev.Id))
        {
            List<Db.Id> ids = [];

            foreach (ImmutableArray<CustomMapCondition> group in ev.Groups.Where(group => !group.IsEmpty))
            {
                ids.Add(MakeId(prefix, "EV", number++));
            }

            gameIds[ev.Id] = ids;
        }

        foreach (CustomMapEvent ev in events.OrderBy(ev => ev.Id))
        {
            List<Db.Id> ids = gameIds[ev.Id];
            ImmutableArray<WorldRace> races = map.GetEventRaces(ev.Races);
            ImmutableArray<WorldRace> concerns = map.GetEventRaces(ev.Concerns);
            List<ImmutableArray<CustomMapCondition>> groups = [.. ev.Groups.Where(group => !group.IsEmpty)];
            List<CustomMapEffect> effects =
            [
                .. ev.Effects.Where(effect => effect is not (CustomMapWinEffect or CustomMapLoseEffect)),
                .. ev.Effects.Where(effect => effect is CustomMapWinEffect or CustomMapLoseEffect)
            ];
            ImmutableArray<WorldRace> revealRaces =
            [
                .. ev.Effects.OfType<CustomMapRevealZoneEffect>()
                    .SelectMany(reveal => reveal.Races.IsEmpty ? races : map.GetEventRaces(reveal.Races))
                    .Distinct()
            ];

            for (int g = 0; g < groups.Count; g++)
            {
                Db.Id eventId = ids[g];
                List<Db.ScenarioEventEffect> eventEffects = [];
                bool turnOnce = groups[g].Any(condition => condition is CustomMapTurnCondition { Repeat: false });

                // Switch-offs first, so a Win effect stays last.
                foreach (Db.Id sibling in ev.Once ? ids.Where(id => id != eventId) : [])
                {
                    eventEffects.Add(Effect(scenarioId, eventId, 0, Db.ScenarioEventEffectType.EnableEvent) with { IdEvent = sibling, Enable = false });
                }

                foreach (CustomMapEffect effect in effects)
                {
                    eventEffects.AddRange(BuildEffect(map, effect, scenarioId, prefix, eventId, gameIds, rows.Translations, texts, eventEffects.Count));
                }

                for (int i = 0; i < eventEffects.Count; i++)
                {
                    eventEffects[i] = eventEffects[i] with { Number = i };
                }

                foreach (Db.ScenarioEventCondition condition in groups[g].Select(condition => BuildCondition(map, condition, scenarioId, prefix, eventId)))
                {
                    rows.Conditions.Add(condition);
                }

                rows.Effects.AddRange(eventEffects);
                texts.Add(Text(scenarioId, eventId, Db.TextScenario.NameTextId, ev.Name));

                foreach (WorldRace race in revealRaces)
                {
                    rows.Unfogs.Add(new Db.ScenarioEventUnfog(scenarioId, eventId, MakeId(prefix, "PL", GetPlayerIndex(map, race))));
                }

                rows.Events.Add(new Db.ScenarioEvent(
                    scenarioId,
                    eventId.Value,
                    Human: concerns.Contains(WorldRace.Human),
                    Dwarf: concerns.Contains(WorldRace.Dwarf),
                    Undead: concerns.Contains(WorldRace.Undead),
                    Heretic: concerns.Contains(WorldRace.Heretic),
                    Neutral: concerns.Contains(WorldRace.Neutral),
                    Elf: concerns.Contains(WorldRace.Elf),
                    Enabled: ev.Enabled,
                    OccurOnce: ev.Once || turnOnce,
                    Chance: Math.Clamp(ev.Chance, 0, CustomMap.MaxEventChance),
                    CondQuantity: groups[g].Length,
                    EffectQuantity: eventEffects.Count,
                    VerifiedHuman: races.Contains(WorldRace.Human),
                    VerifiedDwarf: races.Contains(WorldRace.Dwarf),
                    VerifiedUndead: races.Contains(WorldRace.Undead),
                    VerifiedHeretic: races.Contains(WorldRace.Heretic),
                    VerifiedNeutral: races.Contains(WorldRace.Neutral),
                    VerifiedElf: races.Contains(WorldRace.Elf),
                    Order: ev.Order));
            }
        }

        return rows;
    }

    private static Db.ScenarioEventCondition BuildCondition(CustomMap map, CustomMapCondition condition, Db.ScenarioId scenarioId, string prefix, Db.Id eventId)
    {
        Db.ScenarioEventCondition Make(Db.ScenarioEventConditionType category)
        {
            return new Db.ScenarioEventCondition(
                scenarioId, eventId, null, category, null, null, null, null, null, null, null, null, null, null, null, null,
                null, null, null, null, null, null, null);
        }

        return condition switch
        {
            CustomMapTurnCondition turn => Make(Db.ScenarioEventConditionType.Frequency) with { Frequency = turn.Turn },
            CustomMapEnterZoneCondition zone => Make(Db.ScenarioEventConditionType.EnteringZone) with { IdLocation = MakeId(prefix, "LO", zone.LocationId) },
            CustomMapOwnCityCondition own => Make(Db.ScenarioEventConditionType.OwningCity) with { IdCity = GetCityId(map, prefix, own.City) },
            CustomMapEnterCityCondition enter => Make(Db.ScenarioEventConditionType.EnteringCity) with { IdCity = GetCityId(map, prefix, enter.City) },
            CustomMapLootRuinCondition loot => Make(Db.ScenarioEventConditionType.LootingRuin) with { IdRuin = MakeId(prefix, "RU", loot.RuinId) },
            CustomMapDestroyArmyCondition destroy => Make(Db.ScenarioEventConditionType.DestroyingStack) with { IdStack = ArmyRefId(prefix, destroy.Army) },
            // The game's MISC_INT: 0 = the army must exist, 1 = it must not.
            CustomMapArmyExistsCondition exists => Make(Db.ScenarioEventConditionType.StackExistence) with
            {
                IdStack = ArmyRefId(prefix, exists.Army),
                MiscInt = exists.Exists ? 0 : 1
            },
            CustomMapOwnItemCondition own => Make(Db.ScenarioEventConditionType.OwningItem) with { TypeItem = new Db.Id(own.Item.Value) },
            CustomMapLeaderItemCondition leader => Make(Db.ScenarioEventConditionType.LeaderOwningItem) with { IdStack = ArmyRefId(prefix, leader.Army), TypeItem = new Db.Id(leader.Item.Value) },
            CustomMapRelationCondition relation => Make(Db.ScenarioEventConditionType.DiplomacyRelations) with
            {
                IdPlayer1 = PlayerId(map, prefix, relation.Race1),
                IdPlayer2 = PlayerId(map, prefix, relation.Race2),
                Diplomacy = RelationValue(relation.Level)
            },
            CustomMapAllianceCondition alliance => Make(Db.ScenarioEventConditionType.Alliance) with { IdPlayer1 = PlayerId(map, prefix, alliance.Race1), IdPlayer2 = PlayerId(map, prefix, alliance.Race2) },
            CustomMapLandCondition land => Make(Db.ScenarioEventConditionType.TransformingLand) with { PercentLand = land.Percent },
            CustomMapVisitSiteCondition visit => Make(Db.ScenarioEventConditionType.VisitingSite) with { IdSite = MakeId(prefix, "SI", visit.SiteId) },
            CustomMapArmyToZoneCondition toZone => Make(Db.ScenarioEventConditionType.StackToLocation) with { IdStack = ArmyRefId(prefix, toZone.Army), IdLocation = MakeId(prefix, "LO", toZone.LocationId) },
            CustomMapArmyToCityCondition toCity => Make(Db.ScenarioEventConditionType.StackToCity) with { IdStack = ArmyRefId(prefix, toCity.Army), IdCity = GetCityId(map, prefix, toCity.City) },
            CustomMapItemToZoneCondition item => Make(Db.ScenarioEventConditionType.ItemToLocation) with { TypeItem = new Db.Id(item.Item.Value), IdLocation = MakeId(prefix, "LO", item.LocationId) },
            CustomMapVariableCondition variable => Make(Db.ScenarioEventConditionType.VariableInRange) with
            {
                MiscInt = variable.VariableId,
                MiscInt2 = variable.Minimum,
                MiscInt3 = variable.Maximum,
                MiscInt7 = (int)variable.Join,
                MiscInt4 = variable.Join == CustomMapVariableJoin.None ? null : variable.SecondVariableId,
                MiscInt5 = variable.Join == CustomMapVariableJoin.None ? null : variable.SecondMinimum,
                MiscInt6 = variable.Join == CustomMapVariableJoin.None ? null : variable.SecondMaximum
            },
            _ => throw new ArgumentOutOfRangeException(nameof(condition), condition, null)
        };
    }

    private static IEnumerable<Db.ScenarioEventEffect> BuildEffect(
        CustomMap map,
        CustomMapEffect effect,
        Db.ScenarioId scenarioId,
        string prefix,
        Db.Id eventId,
        Dictionary<int, List<Db.Id>> gameIds,
        Dictionary<string, List<Db.TextScenario>> translations,
        List<Db.TextScenario> texts,
        int number)
    {
        switch (effect)
        {
            case CustomMapMessageEffect message:
                AddText(scenarioId, eventId, Db.TextScenario.PopupTextId, number, message.Text, texts, translations);
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Popup) with
                {
                    Image = message.Portrait,
                    LeftSide = message.PortraitLeft,
                    BoolValue = false,
                    PopupShow = message.Audience switch
                    {
                        CustomMapMessageAudience.AllPlayers => "ALL",
                        CustomMapMessageAudience.ConcernedRaces => "AFF",
                        _ => "TRI"
                    }
                };
                break;
            case CustomMapWinEffect win:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Win) with
                {
                    WinScenario = true,
                    IdPlayer1 = MakeId(prefix, "PL", GetPlayerIndex(map, win.Winner))
                };
                break;
            case CustomMapLoseEffect:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Win) with { WinScenario = false };
                break;
            case CustomMapSwitchEventEffect switchEvent:
                // An "or" event is several game events: switch them all.
                foreach (Db.Id target in gameIds[switchEvent.EventId])
                {
                    yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.EnableEvent) with { IdEvent = target, Enable = switchEvent.On };
                }

                break;
            case CustomMapRevealZoneEffect reveal:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Unfog) with
                {
                    IdLocation = MakeId(prefix, "LO", reveal.LocationId),
                    Enable = true
                };
                break;
            case CustomMapGiveItemEffect give:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.GiveItem) with
                {
                    TypeItem = new Db.Id(give.Item.Value),
                    GiveTo = give.ToCapital ? 1 : 0
                };
                break;
            case CustomMapCreateArmyEffect create:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.CreateStack) with
                {
                    IdStackTemplate = MakeId(prefix, "TM", create.TemplateId),
                    IdLocation = MakeId(prefix, "LO", create.LocationId)
                };
                break;
            case CustomMapRemoveArmyEffect remove:
                yield return WithArmy(Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.RemoveStack), remove.Army, prefix) with { FirstOnly = false };
                break;
            case CustomMapMoveArmyEffect move:
                yield return WithArmy(Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.MoveStack), move.Army, prefix) with
                {
                    IdLocation = MakeId(prefix, "LO", move.LocationId),
                    BoolValue = move.Army is CustomMapArmyRef.Triggerer
                };
                break;
            case CustomMapMoveBesideEffect beside:
                yield return WithArmy(Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.MoveBeside), beside.Army, prefix);
                break;
            case CustomMapChangeOwnerEffect owner:
                // The game's new owner is a subrace (SR + player slot); none = the player who fired the event.
                yield return WithArmy(Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.ChangeOwner), owner.Army, prefix) with
                {
                    IdPlayer1 = owner.NewOwner is { } race ? MakeId(prefix, "SR", GetPlayerIndex(map, race)) : null,
                    FirstOnly = false,
                    PlayAnim = true
                };
                break;
            case CustomMapChangeOrderEffect order:
                yield return WithArmy(Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.ChangeOrder), order.Army, prefix) with
                {
                    Order = ToOrderType(order.Order.Kind),
                    OrderTarget = GetOrderTarget(map, prefix, order.Order),
                    FirstOnly = false
                };
                break;
            case CustomMapBattleEffect battle:
                yield return WithArmy(Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Battle), battle.Army, prefix);
                break;
            case CustomMapCastSpellEffect cast:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.CastSpell) with
                {
                    TypeSpell = new Db.Id(cast.Spell.Value),
                    IdPlayer1 = cast.Caster is { } caster ? PlayerId(map, prefix, caster) : null
                };
                break;
            case CustomMapCastSpellZoneEffect cast:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.CastSpellMap) with
                {
                    TypeSpell = new Db.Id(cast.Spell.Value),
                    IdLocation = MakeId(prefix, "LO", cast.LocationId),
                    IdPlayer1 = cast.Caster is { } zoneCaster ? PlayerId(map, prefix, zoneCaster) : null
                };
                break;
            case CustomMapGiveSpellEffect give:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.GiveSpell) with { TypeSpell = new Db.Id(give.Spell.Value) };
                break;
            case CustomMapAllyEffect ally:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Ally) with
                {
                    IdPlayer1 = PlayerId(map, prefix, ally.Race1),
                    IdPlayer2 = PlayerId(map, prefix, ally.Race2),
                    PermanentAlliance = ally.Permanent
                };
                break;
            case CustomMapRelationEffect relation:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.Diplomacy) with
                {
                    IdPlayer1 = PlayerId(map, prefix, relation.Race1),
                    IdPlayer2 = PlayerId(map, prefix, relation.Race2),
                    Diplomacy = RelationValue(relation.Level)
                };
                break;
            case CustomMapRemoveMountainsEffect mountains:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.RemoveMountain) with { IdLocation = MakeId(prefix, "LO", mountains.LocationId) };
                break;
            case CustomMapRemoveLandmarkEffect remove:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.RemoveLandmark) with { IdLandmark = MakeId(prefix, "MM", remove.LandmarkId) };
                break;
            case CustomMapChangeLandmarkEffect change:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.ChangeLandmark) with
                {
                    IdLandmark = MakeId(prefix, "MM", change.LandmarkId),
                    TypeLandmark = new Db.Id(change.Type.Value)
                };
                break;
            case CustomMapChangeTerrainEffect terrain:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.ChangeTerrain) with
                {
                    IdLocation = MakeId(prefix, "LO", terrain.LocationId),
                    Lookup = TerrainLookup(terrain.Terrain),
                    NumValue = terrain.Size
                };
                break;
            case CustomMapVariableEffect variable:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.ModifyVariable) with
                {
                    NumValue2 = variable.VariableId,
                    Lookup = variable.Operation switch
                    {
                        ScenarioVariableOperation.Add => 0,
                        ScenarioVariableOperation.Subtract => 1,
                        ScenarioVariableOperation.Multiply => 2,
                        ScenarioVariableOperation.Divide => 3,
                        _ => 4
                    },
                    NumValue = variable.Value
                };
                break;
            case CustomMapDestroyItemEffect destroy:
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.DestroyItem) with
                {
                    TypeItem = new Db.Id(destroy.Item.Value),
                    TriggerOnly = destroy.TriggererOnly
                };
                break;
            case CustomMapObjectiveEffect objective:
                AddText(scenarioId, eventId, Db.TextScenario.ObjectiveTextId, 0, objective.Text, texts, translations);
                yield return Effect(scenarioId, eventId, number, Db.ScenarioEventEffectType.ChangeObjective);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(effect), effect, null);
        }
    }

    private static Db.Id ArmyRefId(string prefix, CustomMapArmyRef army)
    {
        return army switch
        {
            CustomMapArmyRef.Template template => MakeId(prefix, "TM", template.Id),
            CustomMapArmyRef.Army placed => MakeId(prefix, "KC", FirstArmyNumber + placed.Id),
            _ => throw new ArgumentOutOfRangeException(nameof(army), army, null)
        };
    }

    private static Db.Id PlayerId(CustomMap map, string prefix, WorldRace race)
    {
        return MakeId(prefix, "PL", GetPlayerIndex(map, race));
    }

    // War, neutral and peace as the original maps write them (0, 49, 100).
    private static int RelationValue(CustomMapRelation level)
    {
        return level switch
        {
            CustomMapRelation.War => 0,
            CustomMapRelation.Neutral => 49,
            _ => 100
        };
    }

    // Inverse of ScenarioLoader.MapTerrain.
    private static int TerrainLookup(WorldMapTerrain terrain)
    {
        return terrain switch
        {
            WorldMapTerrain.Human => 1,
            WorldMapTerrain.Dwarf => 2,
            WorldMapTerrain.Heretic => 3,
            WorldMapTerrain.Undead => 4,
            WorldMapTerrain.Elf => 6,
            _ => 5
        };
    }

    private static List<Db.ScenarioVariable> BuildVariables(CustomMap map, Db.ScenarioId scenarioId)
    {
        return [.. map.Variables.OrderBy(variable => variable.Id).Select(variable => new Db.ScenarioVariable(scenarioId, variable.Id, variable.Name, variable.Initial))];
    }

    private static Db.ScenarioEventEffect WithArmy(Db.ScenarioEventEffect effect, CustomMapArmyRef army, string prefix)
    {
        return army switch
        {
            CustomMapArmyRef.Army placed => effect with { IdStack = MakeId(prefix, "KC", FirstArmyNumber + placed.Id) },
            CustomMapArmyRef.Template template => effect with { IdStackTemplate = MakeId(prefix, "TM", template.Id) },
            _ => effect
        };
    }

    // Templates: SStkTemp rows TM + id, slots laid out like an army's unit group (POS p = line p % 2, flank p / 2).
    private static List<Db.ScenarioStackTemplate> BuildTemplates(CustomMap map, Db.ScenarioId scenarioId, string prefix, NeutralSides sides)
    {
        List<Db.ScenarioStackTemplate> rows = [];

        foreach (CustomMapArmyTemplate template in map.Templates.Where(map.IsTemplateSaveable).OrderBy(template => template.Id))
        {
            List<CustomMapUnit> units = [.. template.Slots.OfType<CustomMapUnit>()];
            int[] positions = new int[GridPosition.Count];

            for (int p = 0; p < GridPosition.Count; p++)
            {
                GridPosition position = new(p % GridPosition.LineCount, p / GridPosition.LineCount);
                CustomMapUnit? unit = template.Slots[position.ToIndex()];

                if (unit is null && position.IsBackLine && template.Slots[GridPosition.AtFrontLine(position.Flank).ToIndex()] is { IsLarge: true } large)
                {
                    unit = large;
                }

                positions[p] = unit is not null ? units.FindIndex(candidate => ReferenceEquals(candidate, unit)) : -1;
            }

            CustomMapUnit leader = units.First(unit => unit.Leadership > 0 && !unit.IsGuardian);
            Db.Id? Unit(int index) => index < units.Count ? new Db.Id(units[index].TypeId.Value) : null;
            int Level(int index) => index < units.Count ? units[index].Level : 0;
            int playerIndex = sides.GetIndex(map, template.Owner, 'T', template.Id);

            rows.Add(new Db.ScenarioStackTemplate(
                scenarioId,
                MakeId(prefix, "TM", template.Id),
                new Db.Id(leader.TypeId.Value),
                leader.Level,
                MakeId(prefix, "PL", playerIndex),
                MakeId(prefix, "SR", playerIndex),
                Unit(0)!.Value, Level(0),
                Unit(1), Level(1),
                Unit(2), Level(2),
                Unit(3), Level(3),
                Unit(4), Level(4),
                Unit(5), Level(5),
                positions[0], positions[1], positions[2], positions[3], positions[4], positions[5],
                ToOrderType(template.Order.Kind),
                GetOrderTarget(map, prefix, template.Order),
                UseFacing: false,
                Facing: 0,
                AiPriority: 3));
        }

        return rows;
    }

    private static Db.OrderType ToOrderType(CustomMapOrderKind kind)
    {
        return kind switch
        {
            CustomMapOrderKind.Normal => Db.OrderType.Normal,
            CustomMapOrderKind.Stand => Db.OrderType.Stand,
            CustomMapOrderKind.Guard => Db.OrderType.Guard,
            CustomMapOrderKind.Roam => Db.OrderType.Roam,
            CustomMapOrderKind.Berserk => Db.OrderType.Berserk,
            CustomMapOrderKind.Explore => Db.OrderType.Explore,
            CustomMapOrderKind.MoveToZone => Db.OrderType.MoveToLocation,
            CustomMapOrderKind.DefendZone => Db.OrderType.DefendLocation,
            CustomMapOrderKind.DefendCity => Db.OrderType.DefendCity,
            CustomMapOrderKind.AttackArmy => Db.OrderType.AttackStack,
            CustomMapOrderKind.SecureCity => Db.OrderType.SecureCity,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }

    private static Db.Id? GetOrderTarget(CustomMap map, string prefix, CustomMapArmyOrder order)
    {
        return order.Kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => MakeId(prefix, "LO", order.LocationId),
            CustomMapOrderKind.DefendCity or CustomMapOrderKind.SecureCity => GetCityId(map, prefix, order.City!),
            CustomMapOrderKind.AttackArmy => MakeId(prefix, "KC", FirstArmyNumber + order.ArmyId),
            _ => null
        };
    }

    private static Db.ScenarioEventEffect Effect(Db.ScenarioId scenarioId, Db.Id eventId, int number, Db.ScenarioEventEffectType category)
    {
        return new Db.ScenarioEventEffect(
            scenarioId, eventId, number, category, null, null, null, null, null, null, null, null, null, null, null, null, null,
            null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, null);
    }

    // The default text goes into the scenario's Tscen; each translation into that language's Tscen.
    private static void AddText(
        Db.ScenarioId scenarioId,
        Db.Id objectId,
        string textId,
        int sequence,
        CustomMapText text,
        List<Db.TextScenario> texts,
        Dictionary<string, List<Db.TextScenario>> translations)
    {
        texts.Add(new Db.TextScenario(scenarioId, objectId, textId, text.Default, sequence));

        foreach ((string language, string translated) in text.Translations)
        {
            if (!string.IsNullOrWhiteSpace(translated))
            {
                if (!translations.TryGetValue(language, out List<Db.TextScenario>? rows))
                {
                    translations[language] = rows = [];
                }

                rows.Add(new Db.TextScenario(scenarioId, objectId, textId, translated, sequence));
            }
        }
    }

    // Writes <maps>/Languages/<code>/<map id>/Tscen.json for every language with translations and removes the
    // folders of languages that have none (any more).
    private static void WriteTranslations(string mapsDirectory, string mapId, Dictionary<string, List<Db.TextScenario>> translations)
    {
        foreach (string language in CustomMapText.Languages)
        {
            string directory = Path.Combine(GetLanguageDirectory(mapsDirectory, language), mapId);

            if (translations.TryGetValue(language, out List<Db.TextScenario>? rows) && rows.Count > 0)
            {
                Directory.CreateDirectory(directory);
                WriteTable(directory, "Tscen.json", rows);
            }
            else if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Db.Id GetCityId(CustomMap map, string prefix, CustomMapCity city)
    {
        return city switch
        {
            CustomMapCity.Village village => MakeId(prefix, "FT", FirstVillageNumber + village.Id),
            CustomMapCity.Capital capital => MakeId(prefix, "FT", GetPlayerIndex(map, capital.Race)),
            _ => throw new ArgumentOutOfRangeException(nameof(city), city, null)
        };
    }

    private static Db.TextScenario Text(Db.ScenarioId scenarioId, Db.Id objectId, string textId, string text)
    {
        return new Db.TextScenario(scenarioId, objectId, textId, text, 0);
    }

    private static Db.Id MakeId(string prefix, string kind, int index)
    {
        return new Db.Id($"{prefix}{kind}{index:x4}");
    }

    // The player bank: the gold and mana set in the editor (CustomMapPlayer.StartingBank).
    private static Db.ResourceSet GetStartingBank(CustomMapPlayer player)
    {
        WorldResourceSet bank = player.StartingBank;

        return new Db.ResourceSet(bank.Gold, bank.DeathMana, bank.RuneMana, bank.LifeMana, bank.InfernalMana, bank.GroveMana);
    }

    private static int GetSubrace(Db.RaceType race)
    {
        return race switch
        {
            Db.RaceType.Human => (int)Db.SubraceType.Human,
            Db.RaceType.Undead => (int)Db.SubraceType.Undead,
            Db.RaceType.Heretic => (int)Db.SubraceType.Heretic,
            Db.RaceType.Dwarf => (int)Db.SubraceType.Dwarf,
            Db.RaceType.Elf => (int)Db.SubraceType.Elf,
            _ => (int)Db.SubraceType.Neutral
        };
    }

    // Banner colours used by the bundled scenarios for each race.
    private static int GetBanner(Db.RaceType race)
    {
        return race switch
        {
            Db.RaceType.Human => 0,
            Db.RaceType.Undead => 1,
            Db.RaceType.Heretic => 2,
            Db.RaceType.Dwarf => 3,
            Db.RaceType.Elf => 14,
            _ => 4
        };
    }

    private static string GetPlayerName(Db.RaceType race)
    {
        return race switch
        {
            Db.RaceType.Human => "The Empire",
            Db.RaceType.Undead => "Undead Hordes",
            Db.RaceType.Heretic => "Legions of the Damned",
            Db.RaceType.Dwarf => "Mountain Clans",
            Db.RaceType.Elf => "Elven Alliance",
            _ => "Neutrals"
        };
    }

    internal static Db.RaceType ToRaceType(WorldRace race)
    {
        return race switch
        {
            WorldRace.Human => Db.RaceType.Human,
            WorldRace.Undead => Db.RaceType.Undead,
            WorldRace.Heretic => Db.RaceType.Heretic,
            WorldRace.Dwarf => Db.RaceType.Dwarf,
            WorldRace.Elf => Db.RaceType.Elf,
            _ => Db.RaceType.Neutral
        };
    }

    private static void WriteTable<T>(string directory, string fileName, List<T> rows)
    {
        DbTableFile.Write(Path.Combine(directory, fileName), rows);
    }
}
