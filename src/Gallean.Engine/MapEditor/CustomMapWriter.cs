namespace Gallean.Engine;

// Writes a CustomMap as a regular scenario: <maps>/<id>/*.json tables plus a row in <maps>/Sinfo.json, so the
// generated ScenarioDatabaseLoader and ScenarioLoader read it like a bundled scenario.
public sealed class CustomMapWriter(GlobalDatabase globalDb)
{
    public const string IndexFileName = "Sinfo.json";
    public const string SkirmishCampaignId = "C000CC0001";

    private const int UnsetPlayer = 99;
    private const int MaxPlayerSlots = 13;
    private const int StartingGold = 500;
    private const int StartingMana = 1500;
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
    // landmarks MM + n (the original scenarios use MM for landmarks too).
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

        Rows rows = BuildRows(map, scenarioId, prefix);

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
        WriteTable(stagingDirectory, "Tscen.json", rows.Texts);
        // Original scenarios all ship an event table; custom maps get an empty one so they load the same way.
        WriteTable(stagingDirectory, "SEvent.json", new List<Db.ScenarioEvent>());

        if (Directory.Exists(scenarioDirectory))
        {
            Directory.Delete(scenarioDirectory, recursive: true);
        }

        Directory.Move(stagingDirectory, scenarioDirectory);

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

    private Rows BuildRows(CustomMap map, Db.ScenarioId scenarioId, string prefix)
    {
        Rows rows = new([], [], [], [], [], [], [], [], [], [], [], [], [], [], [], [], []);

        // Player 0 is the (primary) neutral player; it owns nothing on a new map but the engine requires one.
        List<(Db.RaceType Race, CustomMapPlayer? Player)> players = [(Db.RaceType.Neutral, null)];
        players.AddRange(map.Players.Select(player => (ToRaceType(player.Race), (CustomMapPlayer?)player)));

        if (players.Count > MaxPlayerSlots)
        {
            throw new InvalidOperationException($"A map can have at most {MaxPlayerSlots - 1} players.");
        }

        for (int i = 0; i < players.Count; i++)
        {
            (Db.RaceType raceType, CustomMapPlayer? mapPlayer) = players[i];
            Db.Race race = globalDb.Races.Values.Single(candidate => candidate.RaceType == raceType);
            Db.Lord lord = globalDb.Lords.Values
                .Where(candidate => candidate.RaceId == race.RaceId)
                .OrderBy(candidate => candidate.LordId.Value, StringComparer.OrdinalIgnoreCase)
                .First();
            Db.Id playerId = MakeId(prefix, "PL", i);
            Db.Id subraceId = MakeId(prefix, "SR", i);

            rows.PlayerRaces.Add(raceType);
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
                mapPlayer is null ? Db.ResourceSet.Empty : GetStartingBank(raceType),
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
            rows.Subraces.Add(new Db.ScenarioSubrace(scenarioId, subraceId, playerId, GetSubrace(raceType), GetBanner(raceType)));
            rows.Texts.Add(Text(scenarioId, playerId, Db.TextScenario.NameTextId, GetPlayerName(raceType)));

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
            AddArmy(rows, map, stack, scenarioId, prefix);
        }

        foreach (CustomMapVillage village in map.Villages)
        {
            AddVillage(rows, map, village, scenarioId, prefix);
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

    private static void AddArmy(Rows rows, CustomMap map, CustomMapStack stack, Db.ScenarioId scenarioId, string prefix)
    {
        int playerIndex = GetPlayerIndex(map, stack.Owner);

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

    private static int GetPlayerIndex(CustomMap map, WorldRace owner)
    {
        return owner == WorldRace.Neutral
            ? 0
            : 1 + map.Players.Select(player => player.Race).ToList().IndexOf(owner);
    }

    private static void AddVillage(Rows rows, CustomMap map, CustomMapVillage village, Db.ScenarioId scenarioId, string prefix)
    {
        int playerIndex = GetPlayerIndex(map, village.Owner);

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
        Dictionary<CustomMapUnit, int> unitIndexes = new(ReferenceEqualityComparer.Instance);

        for (int slot = 0; slot < GridPosition.Count; slot++)
        {
            if (slots[slot] is not { } unit)
            {
                continue;
            }

            Db.Id unitId = MakeId(prefix, "UN", firstUnitNumber + slot);
            unitIndexes[unit] = unitIds.Count;
            unitIds.Add(unitId);
            rows.Units.Add(new Db.ScenarioUnit(scenarioId, unitId, unit.Hitpoints, Xp: 0, Creation: 0, new Db.Id(unit.TypeId.Value), unit.Level, Transf: false, DynLevel: false));
        }

        int[] positions = new int[GridPosition.Count];

        for (int p = 0; p < GridPosition.Count; p++)
        {
            GridPosition position = new(p % GridPosition.LineCount, p / GridPosition.LineCount);
            CustomMapUnit? unit = slots[position.ToIndex()];

            if (unit is null && position.IsBackLine && slots[GridPosition.AtFrontLine(position.Flank).ToIndex()] is { IsLarge: true } large)
            {
                unit = large;
            }

            positions[p] = unit is not null ? unitIndexes[unit] : -1;
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

    // One SRoad row per road tile. The loader only reads the position; INDEX holds the neighbour mask like the
    // original maps (north 1, east 2, south 4, west 8), which the renderer works out again by itself.
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
                roads.Add(new Db.ScenarioRoad(scenarioId, MakeId(prefix, "RA", roads.Count), x, y, mask, 0));
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

    private static Db.TextScenario Text(Db.ScenarioId scenarioId, Db.Id objectId, string textId, string text)
    {
        return new Db.TextScenario(scenarioId, objectId, textId, text, 0);
    }

    private static Db.Id MakeId(string prefix, string kind, int index)
    {
        return new Db.Id($"{prefix}{kind}{index:x4}");
    }

    private static Db.ResourceSet GetStartingBank(Db.RaceType race)
    {
        return race switch
        {
            Db.RaceType.Human => new Db.ResourceSet(StartingGold, 0, 0, StartingMana, 0, 0),
            Db.RaceType.Undead => new Db.ResourceSet(StartingGold, StartingMana, 0, 0, 0, 0),
            Db.RaceType.Heretic => new Db.ResourceSet(StartingGold, 0, 0, 0, StartingMana, 0),
            Db.RaceType.Dwarf => new Db.ResourceSet(StartingGold, 0, StartingMana, 0, 0, 0),
            Db.RaceType.Elf => new Db.ResourceSet(StartingGold, 0, 0, 0, 0, StartingMana),
            _ => Db.ResourceSet.Empty
        };
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
