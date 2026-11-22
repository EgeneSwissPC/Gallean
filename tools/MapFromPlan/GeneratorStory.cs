using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Gallean.Engine;

namespace MapFromPlan;

// The parts of a plan that use the map editor's later features: map rules, starting banks, capital garrisons and
// armies, rods, zones (locations), variables, army templates, neutral life (bands near villages, armies that retake
// lost villages, patrols, an escalating "shadow") and story events written as plain JSON blocks.
internal sealed partial class Generator
{
    private readonly Dictionary<string, int> zoneIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> templateIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> variableIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, int> eventIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<(int StackId, string Item, string? Text)> bounties = [];

    public int LootBags { get; private set; }

    private IEnumerable<WorldRace> MapRaces => map.Players.Select(player => player.Race);

    // A bag next to an army ("loot": { "chance", "items" }) and a reward for beating it ("bounty": item name).
    private void AddLoot(CustomMapStack stack, JsonNode node)
    {
        if (node["bounty"] is not null)
        {
            bounties.Add((stack.Id, Plan.Text(node["bounty"]), node["bountyText"] is null ? null : Plan.Text(node["bountyText"])));
        }

        if (node["loot"] is not { } loot || random.NextDouble() >= Plan.Number(loot["chance"], 1))
        {
            return;
        }

        ItemDefinitionId[] items = [.. Plan.Strings(loot["items"]).Select(names.Item)];
        WorldPosition[] around =
        [
            .. Enumerable.Range(-1, 3).SelectMany(dy => Enumerable.Range(-1, 3).Select(dx => new WorldPosition(stack.Position.X + dx, stack.Position.Y + dy)))
                .Where(position => position != stack.Position)
                .OrderBy(_ => random.Next())
        ];

        foreach (WorldPosition position in around)
        {
            if (!map.IsValidPosition(position) || !IsFreeTile(position) || !map.PreviewBag(position).Fits)
            {
                continue;
            }

            int variant = random.Next(8);

            if (map.PlaceBag(position, variant).Bag is { } bag)
            {
                ItemDefinitionId[] contents = [.. Enumerable.Range(0, 1 + random.Next(Plan.Integer(loot["max"], 1))).Select(_ => items[random.Next(items.Length)])];
                map.UpdateBag(bag.Id, variant, contents);
                LootBags++;
            }

            return;
        }
    }

    private void ApplyRulesAndBanks()
    {
        if (plan["rules"] is { } rules)
        {
            map.SetForeignSpellMarkup(Plan.Integer(rules["spellMarkup"], 0));
            map.NeutralKinds = rules["neutralKinds"] is { } kinds && kinds.GetValue<bool>();
        }

        // Town income: on with the editor's default table like every new editor map, unless the plan sets
        // rules.townIncome to false (off) or to its own five values (levels 1-5).
        map.SetNeutralStartingGold(Plan.Integer(plan["rules"]?["neutralGold"], 0));
        map.NeutralBuying = plan["rules"]?["neutralBuying"] is not { } buying || buying.GetValue<bool>();
        map.SetNeutralUnitFactor(Plan.Integer(plan["rules"]?["neutralUnitFactor"], ScenarioDefinition.DefaultNeutralUnitFactor));

        JsonNode? townIncome = plan["rules"]?["townIncome"];
        map.SetTownIncome(townIncome switch
        {
            null => CustomMap.DefaultTownIncome,
            JsonArray values => [.. values.Select(value => value!.GetValue<int>())],
            _ when townIncome.GetValue<bool>() => CustomMap.DefaultTownIncome,
            _ => []
        });

        if (plan["startingBanks"] is JsonObject banks)
        {
            foreach ((string raceName, JsonNode? bank) in banks)
            {
                WorldRace race = Enum.Parse<WorldRace>(raceName);
                WorldResourceSet set = new(
                    Plan.Integer(bank?["Gold"], 0),
                    Plan.Integer(bank?["DeathMana"], 0),
                    Plan.Integer(bank?["RuneMana"], 0),
                    Plan.Integer(bank?["LifeMana"], 0),
                    Plan.Integer(bank?["InfernalMana"], 0),
                    Plan.Integer(bank?["GroveMana"], 0));

                if (!map.SetStartingBank(race, set))
                {
                    Warn($"Starting bank for {race} not set.");
                }
            }
        }
    }

    // Capital garrisons ("garrison": { template, tier }) and the army in the capital ("army": { template, tier }),
    // and armies stationed in villages ("army" on a village).
    private void PlaceCapitalForces()
    {
        foreach (JsonNode capital in Plan.List(plan["capitals"]))
        {
            WorldRace race = Enum.Parse<WorldRace>(Plan.Text(capital["race"]));
            CustomMapPlayer player = map.Players.First(candidate => candidate.Race == race);

            if (capital["garrison"] is { } garrison)
            {
                // The guardian stays where a new capital has it (a capital keeps exactly one); the plan's units fill the rest.
                CustomMapUnit?[] units = names.CapitalGarrison(race);
                CustomMapUnit guardian = units.OfType<CustomMapUnit>().Single();
                CustomMapUnit?[] extra = BuildGarrison(Template(Plan.Text(garrison["template"])), Plan.Integer(garrison["tier"], 2), CustomMapPlayer.GarrisonCapacity - (guardian.IsLarge ? 2 : 1));

                foreach (CustomMapUnit unit in extra.OfType<CustomMapUnit>())
                {
                    foreach (GridPosition position in SlotOrder)
                    {
                        if (units[position.ToIndex()] is not null || (position.IsBackLine && units[GridPosition.AtFrontLine(position.Flank).ToIndex()] is { IsLarge: true }) ||
                            (unit.IsLarge && (position.IsBackLine || units[GridPosition.AtBackLine(position.Flank).ToIndex()] is not null)))
                        {
                            continue;
                        }

                        units[position.ToIndex()] = unit;
                        break;
                    }
                }

                if (map.UpdateCapital(race, units) is { } error)
                {
                    Warn($"Capital garrison {race}: {error}.");
                }
            }

            if (capital["army"] is { } army && player.Entrance is { } entrance)
            {
                CustomMapUnit?[] slots = BuildArmy(Template(Plan.Text(army["template"])), Plan.Integer(army["tier"], 1));
                (CustomMapStack? stack, CustomMapStackError? error) = map.PlaceStack(entrance, race, slots);

                if (stack is null)
                {
                    Warn($"Capital army {race}: {error}.");
                }
                else if (army["name"] is not null)
                {
                    armyIds[Plan.Text(army["name"])] = stack.Id;
                }
            }
        }

        foreach (JsonNode village in Plan.List(plan["villages"]).Where(village => village["army"] is not null))
        {
            string name = Plan.Text(village["name"]);

            if (!villageIds.TryGetValue(name, out int id))
            {
                continue;
            }

            CustomMapVillage placed = map.Villages.First(candidate => candidate.Id == id);
            JsonNode army = village["army"]!;
            CustomMapUnit?[] slots = BuildArmy(Template(Plan.Text(army["template"])), Plan.Integer(army["tier"], 1));
            (CustomMapStack? stack, CustomMapStackError? error) = map.PlaceStack(placed.Entrance, placed.Owner, slots);

            if (stack is null)
            {
                Warn($"Army in village '{name}': {error}.");
            }
            else if (army["name"] is not null)
            {
                armyIds[Plan.Text(army["name"])] = stack.Id;
            }
        }
    }

    // "rods": [{ owner, at }] or [{ owner, count, polygon|around }]: rods that already claim land at the start.
    private void PlaceRods()
    {
        int placed = 0;
        int wanted = 0;

        foreach (JsonNode item in Plan.List(plan["rods"]))
        {
            WorldRace owner = Enum.Parse<WorldRace>(Plan.Text(item["owner"]));

            foreach ((Miles at, _, double radius) in Spots(item))
            {
                wanted++;
                WorldPosition? position = Search(geo.ToPosition(at), Math.Max(3, (int)radius), p =>
                    map.IsValidPosition(p) && !keepClear[Index(p.X, p.Y)] && map.PreviewRod(p, owner).Fits);

                if (position is { } tile && map.PlaceRod(tile, owner).Rod is not null)
                {
                    placed++;
                }
            }
        }

        if (placed < wanted)
        {
            Warn($"Rods: placed {placed} of {wanted}.");
        }
    }

    // A zone's centre: a point in miles, or "@Structure" = the tile in front of its entrance.
    private WorldPosition ZoneCenter(JsonNode at)
    {
        if (at is JsonValue && Plan.Text(at).StartsWith('@'))
        {
            string name = Plan.Text(at)[1..];

            if (approaches.TryGetValue(name, out WorldPosition approach))
            {
                return new WorldPosition(approach.X + 1, approach.Y + 1);
            }

            throw new InvalidOperationException($"Zone at unknown place '{name}'.");
        }

        return geo.ToPosition(Plan.Point(at));
    }

    private int AddZone(string name, WorldPosition center, int radius)
    {
        if (zoneIds.TryGetValue(name, out int existing))
        {
            return existing;
        }

        CustomMapLocation location = map.PlaceLocation(center, Math.Clamp(radius, 0, 3), name);
        zoneIds[name] = location.Id;

        return location.Id;
    }

    private void PlaceLocations()
    {
        foreach (JsonNode zone in Plan.List(plan["locations"]))
        {
            AddZone(Plan.Text(zone["name"]), ZoneCenter(zone["at"]!), Plan.Integer(zone["radius"], 2));
        }
    }

    private void AddVariables()
    {
        foreach (JsonNode variable in Plan.List(plan["variables"]))
        {
            variableIds[Plan.Text(variable["name"])] = map.AddVariable(Plan.Text(variable["name"]), Plan.Integer(variable["initial"], 0)).Id;
        }
    }

    private int AddTemplate(string name, WorldRace owner, CustomMapUnit?[] slots, CustomMapArmyOrder order)
    {
        CustomMapArmyTemplate template = map.AddTemplate(new CustomMapArmyTemplate(0, name, owner, [.. slots], order));
        templateIds[name] = template.Id;

        if (!map.IsTemplateSaveable(template))
        {
            Warn($"Template '{name}' can't be saved: {map.GetTemplateError(template)?.ToString() ?? "order target missing"}.");
        }

        return template.Id;
    }

    // "templates": { name: { army, tier, owner, order } }: armies that events create.
    private void AddTemplates()
    {
        if (plan["templates"] is not JsonObject templates)
        {
            return;
        }

        foreach ((string name, JsonNode? node) in templates)
        {
            WorldRace owner = Enum.Parse<WorldRace>(Plan.Text(node?["owner"], "Neutral"));
            AddTemplate(name, owner, BuildArmy(Template(Plan.Text(node?["army"])), Plan.Integer(node?["tier"], 1)), Order(node?["order"]));
        }
    }

    private CustomMapCity City(string text)
    {
        if (text.StartsWith("capital:", StringComparison.OrdinalIgnoreCase))
        {
            return new CustomMapCity.Capital(Enum.Parse<WorldRace>(text["capital:".Length..]));
        }

        string name = text.TrimStart('@');

        return villageIds.TryGetValue(name, out int id) ? new CustomMapCity.Village(id) : throw new InvalidOperationException($"No village '{name}'.");
    }

    private int Zone(JsonNode? node)
    {
        string name = Plan.Text(node);

        return zoneIds.TryGetValue(name, out int id) ? id : throw new InvalidOperationException($"No zone '{name}'.");
    }

    private CustomMapArmyOrder Order(JsonNode? node)
    {
        if (node is null)
        {
            return CustomMapArmyOrder.Normal;
        }

        CustomMapOrderKind kind = Enum.Parse<CustomMapOrderKind>(Plan.Text(node["kind"]));

        return kind switch
        {
            CustomMapOrderKind.MoveToZone or CustomMapOrderKind.DefendZone => new CustomMapArmyOrder(kind, LocationId: Zone(node["zone"])),
            CustomMapOrderKind.DefendCity or CustomMapOrderKind.SecureCity => new CustomMapArmyOrder(kind, City: City(Plan.Text(node["city"]))),
            CustomMapOrderKind.AttackArmy => new CustomMapArmyOrder(kind, ArmyId: armyIds[Plan.Text(node["army"])]),
            _ => new CustomMapArmyOrder(kind)
        };
    }

    private CustomMapArmyRef ArmyRef(string text)
    {
        if (text == "triggerer")
        {
            return new CustomMapArmyRef.Triggerer();
        }

        if (text.StartsWith("template:", StringComparison.Ordinal))
        {
            return new CustomMapArmyRef.Template(templateIds.TryGetValue(text[9..], out int template) ? template : throw new InvalidOperationException($"No template '{text[9..]}'."));
        }

        string name = text.StartsWith("army:", StringComparison.Ordinal) ? text[5..] : text;

        return new CustomMapArmyRef.Army(armyIds.TryGetValue(name, out int army) ? army : throw new InvalidOperationException($"No army '{name}'."));
    }

    private ImmutableArray<WorldRace> Races(JsonNode? node)
    {
        List<WorldRace> races = [];

        foreach (string race in node is JsonArray ? Plan.Strings(node) : [Plan.Text(node, "all")])
        {
            if (race == "all")
            {
                races.AddRange(MapRaces);
            }
            else
            {
                races.Add(Enum.Parse<WorldRace>(race));
            }
        }

        return [.. races.Distinct()];
    }

    private CustomMapCondition Condition(JsonNode node)
    {
        JsonObject o = node.AsObject();
        string kind = o.First().Key;
        JsonNode? value = o.First().Value;

        return kind switch
        {
            "turn" => new CustomMapTurnCondition(Plan.Integer(value, 1), Repeat: false),
            "every" => new CustomMapTurnCondition(Plan.Integer(value, 1), Repeat: true),
            "enterZone" => new CustomMapEnterZoneCondition(Zone(value)),
            "ownCity" => new CustomMapOwnCityCondition(City(Plan.Text(value))),
            "enterCity" => new CustomMapEnterCityCondition(City(Plan.Text(value))),
            "lootRuin" => new CustomMapLootRuinCondition(ruinIds[Plan.Text(value)]),
            "destroyArmy" => new CustomMapDestroyArmyCondition(ArmyRef(Plan.Text(value))),
            "armyExists" => new CustomMapArmyExistsCondition(ArmyRef(Plan.Text(value)), o["exists"] is not { } exists || exists.GetValue<bool>()),
            "ownItem" => new CustomMapOwnItemCondition(names.Item(Plan.Text(value))),
            "leaderItem" => new CustomMapLeaderItemCondition(ArmyRef(Plan.Text(o["army"])), names.Item(Plan.Text(value))),
            "relation" => new CustomMapRelationCondition(Enum.Parse<WorldRace>(Plan.Text(value![0])), Enum.Parse<WorldRace>(Plan.Text(value[1])), Enum.Parse<CustomMapRelation>(Plan.Text(value[2]))),
            "alliance" => new CustomMapAllianceCondition(Enum.Parse<WorldRace>(Plan.Text(value![0])), Enum.Parse<WorldRace>(Plan.Text(value[1]))),
            "land" => new CustomMapLandCondition(Plan.Integer(value, 10)),
            "visitSite" => new CustomMapVisitSiteCondition(siteIds[Plan.Text(value)]),
            "armyToZone" => new CustomMapArmyToZoneCondition(ArmyRef(Plan.Text(value)), Zone(o["zone"])),
            "armyToCity" => new CustomMapArmyToCityCondition(ArmyRef(Plan.Text(value)), City(Plan.Text(o["city"]))),
            "itemToZone" => new CustomMapItemToZoneCondition(names.Item(Plan.Text(value)), Zone(o["zone"])),
            "variable" => new CustomMapVariableCondition(variableIds[Plan.Text(value)], Plan.Integer(o["min"], 0), Plan.Integer(o["max"], 99999)),
            _ => throw new InvalidOperationException($"Unknown condition '{kind}'.")
        };
    }

    private static CustomMapText Text(JsonNode? node)
    {
        if (node is JsonObject translated)
        {
            CustomMapText text = CustomMapText.Of(Plan.Text(translated["en"]));

            foreach ((string language, JsonNode? value) in translated.Where(pair => pair.Key != "en"))
            {
                text = text.WithTranslation(language, Plan.Text(value));
            }

            return text;
        }

        return CustomMapText.Of(Plan.Text(node));
    }

    // Switch effects name their event; IDs are filled in once every event exists.
    private CustomMapEffect Effect(JsonNode node, List<(CustomMapEffect Placeholder, string EventName, bool On)> switches)
    {
        JsonObject o = node.AsObject();
        string kind = o.First().Key;
        JsonNode? value = o.First().Value;

        switch (kind)
        {
            case "message":
                return new CustomMapMessageEffect(
                    Text(value),
                    Enum.Parse<CustomMapMessageAudience>(Plan.Text(o["audience"], "Triggerer")),
                    o["portrait"] is null ? null : names.Portrait(Plan.Text(o["portrait"])),
                    o["left"] is not { } left || left.GetValue<bool>());
            case "objective":
                return new CustomMapObjectiveEffect(Text(value));
            case "win":
                return new CustomMapWinEffect(Enum.Parse<WorldRace>(Plan.Text(value)));
            case "lose":
                return new CustomMapLoseEffect();
            case "switch":
                CustomMapSwitchEventEffect placeholder = new(0, o["on"] is not { } on || on.GetValue<bool>());
                switches.Add((placeholder, Plan.Text(value), placeholder.On));

                return placeholder;
            case "reveal":
                return new CustomMapRevealZoneEffect(Zone(value), Races(o["races"]));
            case "giveItem":
                return new CustomMapGiveItemEffect(names.Item(Plan.Text(value)), o["toCapital"] is { } capital && capital.GetValue<bool>());
            case "createArmy":
                return new CustomMapCreateArmyEffect(templateIds[Plan.Text(value)], Zone(o["zone"]));
            case "removeArmy":
                return new CustomMapRemoveArmyEffect(ArmyRef(Plan.Text(value)));
            case "moveArmy":
                return new CustomMapMoveArmyEffect(ArmyRef(Plan.Text(value)), Zone(o["zone"]));
            case "moveBeside":
                return new CustomMapMoveBesideEffect(ArmyRef(Plan.Text(value)));
            case "changeOwner":
                return new CustomMapChangeOwnerEffect(ArmyRef(Plan.Text(value)), o["to"] is null || Plan.Text(o["to"]) == "triggerer" ? null : Enum.Parse<WorldRace>(Plan.Text(o["to"])));
            case "order":
                return new CustomMapChangeOrderEffect(ArmyRef(Plan.Text(value)), Order(o));
            case "battle":
                return new CustomMapBattleEffect(ArmyRef(Plan.Text(value)));
            case "castSpell":
                return new CustomMapCastSpellEffect(names.Spell(Plan.Text(value)), o["caster"] is null ? null : Enum.Parse<WorldRace>(Plan.Text(o["caster"])));
            case "castSpellZone":
                return new CustomMapCastSpellZoneEffect(names.Spell(Plan.Text(value)), Zone(o["zone"]), o["caster"] is null ? null : Enum.Parse<WorldRace>(Plan.Text(o["caster"])));
            case "giveSpell":
                return new CustomMapGiveSpellEffect(names.Spell(Plan.Text(value)));
            case "ally":
                return new CustomMapAllyEffect(Enum.Parse<WorldRace>(Plan.Text(value![0])), Enum.Parse<WorldRace>(Plan.Text(value[1])), o["permanent"] is { } permanent && permanent.GetValue<bool>());
            case "setRelation":
                return new CustomMapRelationEffect(Enum.Parse<WorldRace>(Plan.Text(value![0])), Enum.Parse<WorldRace>(Plan.Text(value[1])), Enum.Parse<CustomMapRelation>(Plan.Text(value[2])));
            case "removeMountains":
                return new CustomMapRemoveMountainsEffect(Zone(value));
            case "removeLandmark":
                return new CustomMapRemoveLandmarkEffect(landmarkIds[Plan.Text(value)]);
            case "changeLandmark":
                return new CustomMapChangeLandmarkEffect(landmarkIds[Plan.Text(value)], names.Landmarks(Plan.Text(o["to"]), o["size"] is null ? null : Plan.Integer(o["size"], 1))[0].Id);
            case "changeTerrain":
                return new CustomMapChangeTerrainEffect(Zone(value), Enum.Parse<WorldMapTerrain>(Plan.Text(o["terrain"])), Plan.Integer(o["size"], 5));
            case "setVariable":
                return new CustomMapVariableEffect(variableIds[Plan.Text(value)], Enum.Parse<ScenarioVariableOperation>(Plan.Text(o["op"], "Add")), Plan.Integer(o["value"], 1));
            case "destroyItem":
                return new CustomMapDestroyItemEffect(names.Item(Plan.Text(value)), o["triggererOnly"] is { } only && only.GetValue<bool>());
            default:
                throw new InvalidOperationException($"Unknown effect '{kind}'.");
        }
    }

    private CustomMapEvent AddEvent(string name, ImmutableArray<WorldRace> races, bool once, bool enabled, int chance,
        IEnumerable<IEnumerable<CustomMapCondition>> groups, IEnumerable<CustomMapEffect> effects, ImmutableArray<WorldRace>? concerns = null)
    {
        CustomMapEvent added = map.AddEvent(new CustomMapEvent(
            0, name, races, concerns, once, enabled, Math.Clamp(chance, 1, CustomMap.MaxEventChance), 0,
            [.. groups.Select(group => group.ToImmutableArray())], [.. effects]));
        eventIds[name] = added.Id;

        return added;
    }

    // Neutral life: bands spawn near neutral villages, lost villages are fought for, patrols walk their rounds, and
    // all of it gets stronger as a variable (the shadow) grows. Stages pick their armies by the variable's range,
    // so pushing the shadow back weakens them again. Armies are always of their village's kind (its garrison's
    // template), so only one kind of neutral fights for a village.
    private void AddNeutralLife()
    {
        if (plan["neutralLife"] is not { } life)
        {
            return;
        }

        string variableName = Plan.Text(life["variable"], "Shadow");

        if (!variableIds.ContainsKey(variableName))
        {
            variableIds[variableName] = map.AddVariable(variableName, 0).Id;
        }

        int shadow = variableIds[variableName];
        JsonNode[] stages = [.. Plan.List(life["stages"])];

        // The shadow grows at the start of every turn of its race (Legions by default): it stops when they fall.
        WorldRace grower = Enum.Parse<WorldRace>(Plan.Text(life["growsWith"], "Heretic"));
        AddEvent($"{variableName} grows", [grower], once: false, enabled: true, 100,
            [[new CustomMapTurnCondition(1, Repeat: true)]], [new CustomMapVariableEffect(shadow, ScenarioVariableOperation.Add, Plan.Integer(life["perTurn"], 1))]);

        for (int s = 1; s < stages.Length; s++)
        {
            if (stages[s]["message"] is { } message)
            {
                AddEvent($"{variableName}: {Plan.Text(stages[s]["name"])}", [grower], once: true, enabled: true, 100,
                    [[new CustomMapVariableCondition(shadow, Plan.Integer(stages[s]["at"], 0), 99999)]],
                    [new CustomMapMessageEffect(Text(message), CustomMapMessageAudience.AllPlayers, stages[s]["portrait"] is null ? null : names.Portrait(Plan.Text(stages[s]["portrait"])), true)]);
            }
        }

        (int Min, int Max) Range(int s) => (Plan.Integer(stages[s]["at"], 0), s + 1 < stages.Length ? Plan.Integer(stages[s + 1]["at"], 0) - 1 : 99999);
        int TierOf(int s, int villageTier) => Math.Clamp(villageTier + Plan.Integer(stages[s]["tierBonus"], s), 1, 5);

        JsonNode spawn = life["spawn"] ?? new JsonObject();
        JsonNode retake = life["retake"] ?? new JsonObject();
        ImmutableArray<WorldRace> races = [.. MapRaces];
        int villagesWithLife = 0;

        foreach ((string name, JsonNode village) in villageNodes)
        {
            if (village["garrison"] is null || village["life"] is { } flag && !flag.GetValue<bool>() ||
                !villageIds.TryGetValue(name, out int villageId) || map.Villages.First(candidate => candidate.Id == villageId).Owner != WorldRace.Neutral)
            {
                continue;
            }

            villagesWithLife++;
            JsonObject garrison = Template(Plan.Text(village["garrison"]));
            int tier = Plan.Integer(village["tier"], 1);
            CustomMapCity city = new CustomMapCity.Village(villageId);
            int zone = AddZone($"{name} outskirts", ZoneCenter(JsonValue.Create("@" + name)!), Plan.Integer(spawn["radius"], 2));
            string bandOrder = Plan.Text(village["bandOrder"], Plan.Text(spawn["order"], "DefendZone"));

            for (int s = 0; s < stages.Length; s++)
            {
                string stage = Plan.Text(stages[s]["name"], $"stage {s + 1}");
                (int min, int max) = Range(s);
                CustomMapArmyOrder order = bandOrder == "Roam" ? new CustomMapArmyOrder(CustomMapOrderKind.Roam) : new CustomMapArmyOrder(CustomMapOrderKind.DefendZone, LocationId: zone);
                int band = AddTemplate($"{name} band ({stage})", WorldRace.Neutral, BuildArmy(garrison, TierOf(s, tier)), order);
                AddEvent($"{name}: a band gathers ({stage})", [WorldRace.Neutral], once: false, enabled: true, Plan.Integer(spawn["chance"], 50),
                    [[
                        new CustomMapTurnCondition(Plan.Integer(spawn["every"], 4), Repeat: true),
                        new CustomMapOwnCityCondition(city),
                        new CustomMapVariableCondition(shadow, min, max),
                        new CustomMapArmyExistsCondition(new CustomMapArmyRef.Template(band), false)
                    ]],
                    [new CustomMapCreateArmyEffect(band, zone)]);

                int avengers = AddTemplate($"{name} avengers ({stage})", WorldRace.Neutral,
                    BuildArmy(garrison, Math.Min(5, TierOf(s, tier) + Plan.Integer(retake["tierBonus"], 1))), new CustomMapArmyOrder(CustomMapOrderKind.SecureCity, City: city));
                List<CustomMapEffect> effects = [new CustomMapCreateArmyEffect(avengers, zone)];

                if (retake["message"] is { } message)
                {
                    effects.Add(new CustomMapMessageEffect(CustomMapText.Of(Plan.Text(message).Replace("{village}", name, StringComparison.Ordinal)), CustomMapMessageAudience.Triggerer, null, true));
                }

                AddEvent($"{name}: they want it back ({stage})", races, once: false, enabled: true, Plan.Integer(retake["chance"], 60),
                    [[
                        new CustomMapTurnCondition(Plan.Integer(retake["every"], 3), Repeat: true),
                        new CustomMapOwnCityCondition(city),
                        new CustomMapVariableCondition(shadow, min, max),
                        new CustomMapArmyExistsCondition(new CustomMapArmyRef.Template(avengers), false)
                    ]],
                    effects);
            }
        }

        // Spawners: bands that gather in a zone of their own (a sea lane, a haunted isle) with no village to own;
        // one at a time, stronger by stage like the village bands.
        int spawners = 0;

        foreach (JsonNode spawner in Plan.List(life["spawners"]))
        {
            string name = Plan.Text(spawner["name"]);
            int zone = AddZone(name, ZoneCenter(spawner["at"]!), Plan.Integer(spawner["radius"], 2));
            JsonObject army = Template(Plan.Text(spawner["army"]));
            int tier = Plan.Integer(spawner["tier"], 1);

            for (int s = 0; s < stages.Length; s++)
            {
                string stage = Plan.Text(stages[s]["name"], $"stage {s + 1}");
                (int min, int max) = Range(s);
                CustomMapArmyOrder order = Plan.Text(spawner["order"], "DefendZone") == "Roam" ? new CustomMapArmyOrder(CustomMapOrderKind.Roam) : new CustomMapArmyOrder(CustomMapOrderKind.DefendZone, LocationId: zone);
                int band = AddTemplate($"{name} ({stage})", WorldRace.Neutral, BuildArmy(army, TierOf(s, tier)), order);
                AddEvent($"{name}: they gather ({stage})", [WorldRace.Neutral], once: false, enabled: true, Plan.Integer(spawner["chance"], 50),
                    [[
                        new CustomMapTurnCondition(Plan.Integer(spawner["every"], 5), Repeat: true),
                        new CustomMapVariableCondition(shadow, min, max),
                        new CustomMapArmyExistsCondition(new CustomMapArmyRef.Template(band), false)
                    ]],
                    [new CustomMapCreateArmyEffect(band, zone)]);
            }

            spawners++;
        }

        int patrols = 0;

        foreach (JsonNode patrol in Plan.List(life["patrols"]))
        {
            string name = Plan.Text(patrol["name"]);
            JsonNode[] path = [.. Plan.List(patrol["path"])];
            string? missing = path.Select(point => point is JsonValue && Plan.Text(point).StartsWith('@') && !approaches.ContainsKey(Plan.Text(point)[1..]) ? Plan.Text(point) : null).FirstOrDefault(text => text is not null);

            if (missing is not null || (patrol["home"] is { } homeName && !villageIds.ContainsKey(Plan.Text(homeName).TrimStart('@'))))
            {
                Warn($"Patrol '{name}' left out: {missing ?? Plan.Text(patrol["home"])} is not on the map.");
                continue;
            }

            int[] zones = [.. path.Select((point, i) => AddZone($"{name} {i + 1}", ZoneCenter(point), 1))];
            int template = AddTemplate(name, WorldRace.Neutral, BuildArmy(Template(Plan.Text(patrol["army"])), Plan.Integer(patrol["tier"], 2)),
                new CustomMapArmyOrder(CustomMapOrderKind.MoveToZone, LocationId: zones[1 % zones.Length]));
            CustomMapArmyRef patrolRef = new CustomMapArmyRef.Template(template);
            List<CustomMapCondition> home = patrol["home"] is { } homeVillage ? [new CustomMapOwnCityCondition(City(Plan.Text(homeVillage)))] : [];

            // The leg the patrol walks is kept in a variable (= the waypoint it heads for). Each leg event needs the
            // patrol in its waypoint AND the variable on that leg, so it fires once per arrival: an event that fires
            // stops the army that is moving at that moment, and a condition that stays true would fire on every
            // step of every neutral army.
            int leg = map.AddVariable($"{name} leg", 1 % zones.Length).Id;
            CustomMapEffect[] start = [new CustomMapCreateArmyEffect(template, zones[0]), new CustomMapVariableEffect(leg, ScenarioVariableOperation.Replace, 1 % zones.Length)];

            AddEvent($"{name}: sets out", [WorldRace.Neutral], once: true, enabled: true, 100,
                [[new CustomMapTurnCondition(1, Repeat: false)]], start);

            for (int i = 0; i < zones.Length; i++)
            {
                int next = (i + 1) % zones.Length;
                AddEvent($"{name}: leg {i + 1}", [WorldRace.Neutral], once: false, enabled: true, 100,
                    [[new CustomMapArmyToZoneCondition(patrolRef, zones[i]), new CustomMapVariableCondition(leg, i, i)]],
                    [
                        new CustomMapChangeOrderEffect(patrolRef, new CustomMapArmyOrder(CustomMapOrderKind.MoveToZone, LocationId: zones[next])),
                        new CustomMapVariableEffect(leg, ScenarioVariableOperation.Replace, next)
                    ]);
            }

            AddEvent($"{name}: a new patrol", [WorldRace.Neutral], once: false, enabled: true, Plan.Integer(patrol["respawnChance"], 50),
                [[new CustomMapTurnCondition(Plan.Integer(patrol["respawnEvery"], 8), Repeat: true), new CustomMapArmyExistsCondition(patrolRef, false), .. home]],
                start);
            patrols++;
        }

        Console.WriteLine($"  neutral life: {villagesWithLife} villages with bands and avengers, {spawners} spawners, {patrols} patrols, {stages.Length} stages");
    }

    private void AddStoryEvents()
    {
        List<(CustomMapEffect Placeholder, string EventName, bool On)> switches = [];
        List<CustomMapEvent> added = [];

        foreach ((int stackId, string item, string? text) in bounties)
        {
            List<CustomMapEffect> effects = [new CustomMapGiveItemEffect(names.Item(item), false)];

            if (text is not null)
            {
                effects.Add(new CustomMapMessageEffect(CustomMapText.Of(text), CustomMapMessageAudience.Triggerer, null, true));
            }

            AddEvent($"Bounty: {item} ({stackId})", [.. MapRaces], once: true, enabled: true, 100,
                [[new CustomMapDestroyArmyCondition(new CustomMapArmyRef.Army(stackId))]], effects);
        }

        foreach (JsonNode story in Plan.List(plan["events"]))
        {
            string name = Plan.Text(story["name"]);
            IEnumerable<IEnumerable<CustomMapCondition>> groups = story["whenAny"] is JsonArray any
                ? any.Select(group => Plan.List(group).Select(Condition))
                : [Plan.List(story["when"]).Select(Condition)];
            CustomMapEffect[] effects = [.. Plan.List(story["then"]).Select(effect => Effect(effect, switches))];
            added.Add(AddEvent(name, Races(story["races"]), story["once"] is not { } once || once.GetValue<bool>(),
                story["enabled"] is not { } enabled || enabled.GetValue<bool>(), Plan.Integer(story["chance"], 100), groups, effects,
                story["concerns"] is null ? null : Races(story["concerns"])));
        }

        foreach (CustomMapEvent ev in added.Where(ev => ev.Effects.Any(effect => effect is CustomMapSwitchEventEffect)))
        {
            CustomMapEffect[] resolved =
            [
                .. ev.Effects.Select(effect =>
                {
                    (CustomMapEffect Placeholder, string EventName, bool On) found = switches.FirstOrDefault(entry => ReferenceEquals(entry.Placeholder, effect));

                    return found.Placeholder is null ? effect : new CustomMapSwitchEventEffect(eventIds.TryGetValue(found.EventName, out int id) ? id : throw new InvalidOperationException($"No event '{found.EventName}'."), found.On);
                })
            ];
            map.UpdateEvent(map.GetEvent(ev.Id)! with { Effects = [.. resolved] });
        }

        HashSet<int> saveable = [.. map.GetSaveableEvents().Select(ev => ev.Id)];

        foreach (CustomMapEvent ev in map.Events.Where(ev => !saveable.Contains(ev.Id)))
        {
            Warn($"Event '{ev.Name}' can't be saved: {string.Join(", ", map.GetEventProblems(ev).Select(problem => problem.Kind))}.");
        }
    }

    private void AddConquestWins()
    {
        if (plan["conquestWins"] is { } wins && wins.GetValue<bool>())
        {
            foreach (WorldRace race in map.GetRacesNeedingConquestWin())
            {
                map.AddConquestWin(race, $"{race}: conquest");
            }
        }
    }
}
