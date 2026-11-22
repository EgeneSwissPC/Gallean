# Town income and neutral economy

Status: phases 1-4 done (merges ee232a30, 9f1a4423, 1d6c92ce, c6a700b5); phases 1-5 done (merges ee232a30, 9f1a4423, 1d6c92ce, c6a700b5, ff0f9676). H's price ceiling table is asked when phase 4 starts. The user asked for two features, in this order:

1. Every town (village) brings a small amount of gold every turn, depending on its level.
2. Neutrals then have gold of their own and buy units on their own.

## Decisions

- Order: town income first, neutral economy second (user).
- Neutrals pay normal hire prices: Gunits `ENROLL_C` (+ GDynUpgr per level above the base level). Neutral soldiers and leaders already have prices in the data (79 soldiers, 30-9950 gold; 94 leaders, 30-9950 gold).
- Standard (Classic) AI stays as upstream's. Neutral buying is new code that runs only when the map rule for it is on. It is never an edit of the race players' economy code.
- A (user): a per-map on/off setting, on by default in every new editor map and in The War of the Ring. Original campaigns have no setting, so they stay off.
- B (user): the steeper table, 10 / 25 / 50 / 75 / 100 gold for levels 1-5, editable per map.
- C (user): capitals benefit too. A capital pays the level-5 value of the table (100 by default) on top of the race base income (taken unless the user objects; a separate capital box in the editor would be the alternative).
- D (user): rioting towns pay nothing.
- E (user): E1. Town income goes through the same `PlayerIncomeCalculator.Scale` as all other income: +30 % for everyone on Easy (`INCOME_E`), then for computer players (neutrals included) `GAI` D_EASY_M / D_AVG_M / D_HARD_M / D_VHARD_M = 70 / 100 / 130 / 145 %. A level-3 town (50) pays a human 65 on Easy and 50 otherwise, a computer player 45 / 50 / 65 / 72. The editor prediction uses the same formula.
- F (user): neutrals earn only from their own towns, plus a starting bank.
- G (user): garrisons first. Once a neutral side has more money, it sends out armies that attack enemy armies near its home towns or conquer nearby towns. Phase 5 builds this, after phase 4's measurement.
- H (user): a neutral side buys only its own kind's soldiers, and the village level caps the price.
- N (user): neutrals upgrade their villages, but only once a village's garrison is full AND the side owns at least factor x (sum of its villages' levels) units; below that they buy units first, including new leaders whose armies they also fill. Example from the user: 7 level-3, 2 level-2 and 1 level-1 villages with factor 3 = 7x3x3 + 2x2x3 + 1x1x3 = 78 units. Factor 3 to start with.
  - Readings taken unless the user objects: every unit the side owns counts once (garrisons and armies, leaders included, a large unit counts 1); "towns" are its villages (neutrals have no capital); the factor is a per-map setting (SRules, editor Neutrals section, MapFromPlan `rules.neutralUnitFactor`), default 3, so it can be tuned without a new build.
  - What it means for The War of the Ring (generated map, units now vs target with factor 3): only the Free Men are below it (25 units in 16 villages vs 75), so they would hire leaders and fill armies first; every other side already has far more (Greenskins 475 vs 63, Wolves 274 vs 9) and would upgrade as soon as a garrison is full and gold allows. Sea Creatures own no village and all their leaders are water-only, so they buy nothing.
  - New phase split (decision N makes leader hiring part of the buying order): phase 4 = the whole neutral economy (garrisons, leaders with their armies filled, village upgrades, in the user's order), new armies wait at their village; phase 5 = what those armies do (attack enemy armies near their home towns, conquer nearby towns).
- H2 (user): H2b, to try first and revisit if it plays badly. A level-N village sells the cheapest N/5 (rounded up, at least one) of its kind's soldiers, and of its kind's leaders, by gold price at level 1; ties keep the data order.
- J (user): J2. Each neutral side makes at most one purchase per village it owns per turn (a soldier, a leader or a village upgrade); while its gold is above the spending line (70 % of the cap, `WorldAiSpending.DefaultSpendAbovePercent`) the limit is lifted, so piled-up gold gets spent.
- K (user): random for now. When gold doesn't reach every village, the villages take their turn in a random order (the engine's random provider, so a seeded headless run repeats exactly); threatened-first can come later if random plays badly.
- L (user): random, weighted by the side's gold: the more gold, the more likely an expensive unit, but any allowed, affordable unit that fits can come up. Formula taken unless the user objects (tuned after the measurement): weight = price ^ t with t = clamp(gold / 2000 - 1, -1, 1), so cheap units are favoured below 2000 gold, all equally likely at 2000, dear ones favoured above. Level-3 Greenskin village: with 300 gold Goblin 45 %, Goblin Archer 45 %, Orc 10 %; with 900 gold 35 / 35 / 13 / Goblin Elder 9 / Orc Champion 7 %; with 2000 gold each of the six 17 %; with 4000 gold Goblin 2, Goblin Archer 2, Orc 10, Goblin Elder 22, Orc Champion 29, Cyclops 34 %. The same draw picks the leader when a leader is hired.
- M (user): M2. A separate map rule "Neutrals buy units" (SRules, editor Neutrals section, MapFromPlan `rules.neutralBuying`), on by default in new editor maps, MapFromPlan plans and The War of the Ring; off in maps made in code and in original scenarios. Off = neutrals still earn (phase 3) but never buy or upgrade.
- I (user): The War of the Ring's scripted neutral life is out of scope here; the user will rework it separately later.
- Taken while building phase 1: `CustomMap.Create` starts with the rule off. The editor's new-map path turns it on with `CustomMap.DefaultTownIncome` (phase 2), and MapFromPlan sets it for The War of the Ring (phase 2). Maps built in code (tests, the battle bench, AI designer test maps) keep their economy, so no existing measurement changes.
- Taken while building phase 4: the buying runs as an engine rule right after a neutral side's income (`TurnResourcesGeneratedNeutralEconomyRule`, like summon upkeep), not as an AI action. So it works the same whichever AI the neutrals use, needs no AI memory, and the standard AI code is untouched.
- Taken while building phase 4: a village whose garrison still has room for one of its allowed soldiers saves its gold until it can buy one (it doesn't hire leaders meanwhile). An army waiting at a village entrance blocks new leaders there until phase 5 moves armies out; a full waiting army means that village buys nothing more while the side is below the unit target. With neutral kinds off, the single neutral side buys plain neutrals only.
- Taken while building phase 3: neutral income follows the town income rule (no separate switch): with town income on, every neutral side earns from its own towns. Their starting gold is one value for all neutral sides (`CustomMap.NeutralStartingGold`, gold only, 0 by default); per-side banks would need the writer's kind split in the editor. The War of the Ring keeps 0 for now (its neutral life gets reworked later, decision I).
- Taken while building phase 2: the world screen's income table is laid out in columns per source (Capital, Sources, Total), so towns became a "Towns" column instead of a line; it only appears when towns pay (the same in the editor's economy panel).
- Taken while building phase 2: MapFromPlan plans get town income like new editor maps unless `rules.townIncome` is `false`; a five-value array sets the table. The War of the Ring sets it explicitly.
- Taken while building phase 1: the table is one SRules column, `TOWN_INCOME` = `"10,25,50,75,100"` (null = off), so SRules files written before keep loading.

## The problem today

- Towns bring no gold. `PlayerIncomeCalculator.GetIncomeInfo` = race base income (`Grace.INCOME`: 100 gold + 25 of the race's own mana for every playable race) + `ResourceNodeIncome` (50) for each resource on the player's land. The total is scaled by the difficulty bonus (`IncomeBonusPercent*`: Easy 30, otherwise 0) and, for AI players, by the AI attitude's income percent. Villages only matter through land spread (more land, more resources) and garrisons.
- The village level matters for land spread (15/15/20/20/25 tiles a turn), healing, protection and scouting. Levels only rise when the owner pays `WorldUpgradeCityCommand` (`GcityInf.GROWTH`: 150 / 250 / 500 / 750 gold to reach levels 2 / 3 / 4 / 5, Guildmaster lords get a discount), at most once per turn.
- Neutrals have no economy at all:
  - `PlayerIncomeCalculator` returns zero income for any non-playable race, although `Grace` gives the neutral race `INCOME` 100 gold.
  - Neutral players start with an empty bank (the editor writes no `BANK` for them).
  - `WorldAiController.DecideAction` runs `DecideEconomyAction` (buying units, leaders, spells, buildings) only for playable races. Neutral players only move armies.
  - The neutral race has no unit or leader list (`Grace` SOLDIER_1-5 and LEADER_1-4 are empty), so `WorldPurchaseUnitCommand`/`WorldPurchaseLeaderCommand` can't sell them anything.

### How many towns there are

- Original scenarios: 111 maps with 874 villages (level 1: 289, 2: 202, 3: 176, 4: 110, 5: 97).
- The War of the Ring (S601): 65 villages, levels summed 143.
  - Races: Empire 8 (level sum 22), Elves 5 (15), Clans 4 (11), Legions 4 (9), Undead 2 (7).
  - Neutral sides: Free Men 16 (25), Greenskins 7 (21), Barbarians 8 (13), plain neutrals 3 (6), Wild Elves 2 (4), Dragons 1 (4), Marsh Folk 3 (3), Wolves 2 (3), Sea Creatures 0.

## What the player would see

- World screen income table (`WorldIncomeViewModel` / `WorldIncomeRowViewModel`, `WorldScreenView.axaml`): a new "Towns" line next to the capital and resources lines, with the gold from towns.
- City screen of an own village: "+N gold per turn" next to the level, and in the upgrade offer what the next level would pay.
- Map editor economy panel: a towns column, so its income prediction still equals the game's turn-1 numbers. The per-level table sits under the starting bank box, like the spell markup.
- Phase 4 onward: neutral villages whose garrisons fill up over time, and neutral gold visible in the AI players panel (dev tools).

## Technical approach

### Town income (phases 1-2)

- Engine: `PlayerIncomeCalculator.GetIncomeInfo` adds `TownIncome` = the sum over the player's non-rioting `WorldCity` fortifications of the table value for each city's level, plus the level-5 value for an uncaptured own capital. `IncomeInfo` gets a `Towns` part, which `WorldGetPlayerInfoCommand` passes to the client. Scaling: the existing `Scale` (decision E).
- Data: a per-map table in `SRules.json` (new column, e.g. `TOWN_INCOME` = `"10,25,50,75,100"` gold for levels 1-5). Missing or empty = no town income, so original campaigns stay as they are. Read into `ScenarioDefinition.TownIncome` (init property, like `ForeignSpellMarkup`).
- Editor: `CustomMap.TownIncome` (on with the default table for new maps) + an on/off box and the five values in the economy panel; `CustomMapEconomy` adds the column through the same `PlayerIncomeCalculator` call it already uses. MapFromPlan: `rules.townIncome`.
- Turn flow: no change. `TurnStartedGenerateResourcesRule` already pays `IncomeInfo.Total` at each player's turn start.

### Neutral treasury (phase 3)

- `PlayerIncomeCalculator`: when the map's neutral-economy rule is on, neutral players get the town part only (no race base income, no resources, since neutrals own no land). Each neutral side pays from its own towns, which fits NEUTRAL_KINDS: Free Men with 16 villages earn much more than Wolves with 2.
- Starting bank per neutral side: editor box (one value for all neutral sides, or per side) written to Splayer `BANK`.

### Neutral buying (phases 4-5)

- What a neutral side can buy: its kind's units, from Gunits by subrace. Soldiers per kind: plain neutrals 40, Wild Elves 11, Greenskins 9, Dragons 5, Barbarians 4, Sea Creatures 4, Free Men 3, Marsh Folk 2, Wolves 1. Leaders per kind: plain neutrals 60, Free Men 9, Wild Elves 6, Greenskins 6, Dragons 5, Barbarians 3, Sea Creatures 3, Marsh Folk 1, Wolves 1.
- Garrisons (phase 4): a neutral `WorldAiPlanner` action, used only for neutral players with the rule on, that buys into its own villages' garrisons like the race AI's `PurchaseUnitAction` (any owned fortification, Classic limit 1 unit per turn). It needs a neutral purchase path: either per-kind unit lists on the neutral player (`WorldPlayer`, init property, saved) that `WorldPurchaseUnitCommand` accepts, or a separate `WorldNeutralPurchaseUnitCommand`. Recommendation: the separate command, so upstream's purchase command stays untouched.
- New armies (phase 5): hire a kind leader at a village with a free entrance tile and give it an order (DefendZone around home, Roam, or SecureCity to retake lost villages).
- Cap: neutral gold also stops at 9999. The user's rule "never sit at the cap" applies to our AIs; the neutral buyer gets the same `SpendAbovePercent` behaviour.

## Testing

- Engine: income with 0, 1 and 5 villages of mixed levels, captured and lost villages, rioting villages, difficulty and attitude scaling, maps without the rule (income unchanged), save round trip of the new rule.
- Editor agreement test: `CustomMapEconomy` prediction = game turn-1 treasury on a saved-and-loaded map.
- Neutral phases: decision tests with Classic as the control (Classic race players buy exactly as before), a headless War of the Ring run (neutral gold, purchases per side, neutral turn time on the 1024 map before and after), and the S099 Classic CSV diff to prove race AIs are unchanged.
- Real client: the income table line and the city screen line in an AOT build (`mapgen` map with villages: turn-2 treasury = turn-1 + predicted income).

## Phases

1. ~~Town income in the engine: SRules table, `PlayerIncomeCalculator`, `IncomeInfo.Towns`, tests. ~1 day.~~ Done, see below.
2. ~~Town income on screen: income table line, city screen line, editor economy box and column, MapFromPlan `rules.townIncome`, War of the Ring setting. ~1-1.5 days.~~ Done, see below.
3. ~~Neutral treasury: neutral sides earn from their towns, neutral starting bank in the editor. ~0.5-1 day.~~ Done, see below.
4. ~~Neutral economy: kind unit and leader pools, neutral purchase and hire commands, neutral planner actions in the user's order (fill garrisons; below factor x levels units, hire leaders at villages and fill their armies; then upgrade villages with full garrisons), the unit factor setting, headless War of the Ring measurement. New armies wait at their village. ~3 days.~~ Done, see below.
5. Neutral armies: what the armies from phase 4 do (attack enemy armies near home towns, conquer nearby towns), measurement. ~2 days, specced after phase 4's numbers.

## Open questions

~~A. Scope.~~ Answered: per-map on/off, on in new maps and The War of the Ring (Decisions).

~~B. Amounts.~~ Answered: 10/25/50/75/100, editable per map (Decisions).

~~C. Capitals.~~ Answered: capitals benefit too, at the level-5 value (Decisions).

~~D. Rioting villages.~~ Answered: they pay nothing.

~~E. Difficulty and AI attitude scaling.~~ Answered: E1, scaled like all other income (Decisions).

~~F. Neutral income source.~~ Answered: towns only, plus a starting bank.

~~G. What neutrals buy.~~ Answered: garrisons first, then armies that hunt near home or take nearby towns (Decisions).

~~H. Which units.~~ Answered: own kind only, village level caps the price. The ceiling table per level is still open (asked when phase 4 starts).

~~I. The War of the Ring's neutral life.~~ Answered: out of scope, reworked separately later.

### Phase 4 questions (asked when the user started phase 4)

Facts they build on (counted from the data and a generated War of the Ring):
- A village's garrison holds as many slots as its level (`GarrisonCalculator.GetTotalCapacity`: level 1 = 1 slot ... level 5 = 5; a large unit takes 2), so a level-1 village can never hold a large unit.
- Soldiers per kind and their price range (gold at level 1): plain neutrals 40 (40-9950), Wild Elves 11 (40-9950), Greenskins 9 (50-2800), Dragons 5 (4500-5500, all large), Barbarians 4 (850-1800), Sea Creatures 4 (300-3500), Free Men 3 (30-300), Marsh Folk 2 (850-1310), Wolves 1 (300).
- The War of the Ring as generated: every neutral village garrison is already full except the Dragons' (2 free slots of 4). So garrison buying there only starts once garrisons lose units in battles; the race villages all start empty.
- The race AI's own garrison buying (`WorldAiPlanner.PurchaseUnitAction`): any owned fortification, scored per unit, at most 1 unit per turn per player (Classic; our AIs lift the limit above the spending line).

~~N. Do neutrals upgrade their villages?~~ Answered with the user's own rule (Decisions, N).

~~H2. The price cap per village level.~~ Answered: H2b for now (Decisions).

~~J. How many units per turn.~~ Answered: J2 (Decisions).

~~K. Which village first.~~ Answered: random for now (Decisions).

~~L. Which unit.~~ Answered: weighted random, richer sides lean to dearer units (Decisions).

~~M. When it applies.~~ Answered: M2, a map rule (Decisions).

## Phase 1 - done

Commit b6387faa, merged as ee232a30.

What was built:
- `Db.ScenarioRules.TownIncome` (SRules `TOWN_INCOME`, optional string) -> `ScenarioDefinition.TownIncome` (init property, `ImmutableArray<int>`, empty = off), parsed in `ScenarioLoader`.
- `TownIncomeCalculator` (new file, `World/`): parse/format the table; gold per player = table value of each owned, non-rioting `WorldCity` by level + the level-5 value for an uncaptured own capital. A captured capital pays neither its owner nor the captor.
- `PlayerIncomeCalculator`: `IncomeInfo.Towns` (init property, gold only) added to the total before `Scale`, so difficulty and AI attitude apply (decision E). An overload takes the table explicitly for the editor. Non-playable (neutral) players still earn nothing until phase 3.
- Map editor engine: `CustomMap.TownIncome`, `SetTownIncome` (5 values or none, each 0-9999), `DefaultTownIncome`, `Clone`; the writer saves it, the store reads it back. `CustomMapEconomy` rows get `TownsIncome` (part of `Total`), and `Calculate` accepts the table being edited.
- No screen shows it yet (phase 2). `WorldGetPlayerInfoCommand.TotalIncome` already includes it.

Tests: `CustomMapTests.TownIncomeTest` covers the setter (no-change, clamping, wrong count), clone, save and reopen, the loaded definition, Empire 10 + 50 + capital 100 = 160 and Elves 25 + 100 = 125, neutrals 0, a rioting level-3 town (160 -> 110), a captured capital (Elves 125 -> 25, Empire unchanged), scaling (human Easy 208, computer Easy 145, computer Very hard 232), the editor economy row with the map's and an edited table, a real turn start (the Elves' treasury grows by exactly 100 + 125), and the rule off (saved empty table, original scenario S135). Mutation checks, each built and run: riot check dropped, capital part dropped, captured check dropped, towns not scaled, scenario table ignored -> the test fails every time.

Suite 5456 (5455 on the branch point + this test; gallean-upgrade gained one test from AI unused features phase 2 meanwhile, rerun on the merged tree).

Real-client check: none in this phase. No map turns the rule on yet, so nothing in the game changes; the client check (income table line, turn-2 treasury = turn-1 + predicted income) moves to phase 2.

Known limits: a rounding remainder from scaling goes to the resources part of `IncomeInfo` (at most 2 gold, only for computer players or on Easy; the total is exact).

## Phase 2 - done

Commits d44cf700 (feature), 22f3ceac and 6ac7b715 (visual test), 8881fdc3 (panel width); merged as 9f1a4423.

What was built:
- World screen: the "Income per turn" tooltip (`WorldScreenView.axaml`, `WorldIncomeViewModel`, `WorldIncomeRowViewModel`) has a "Towns" column between Sources and Total, shown only when towns pay. `WorldGetPlayerInfoCommand.Result.TownsIncome` (init property) carries it.
- City screen (`CityTabViewModel.BuildCityStats`): a "Gold per turn" row under Armor and Regeneration, "+25 (next level +50)" for a village that can still grow, "+100" for a capital, 0 while rioting, no row when the rule is off. `WorldGetCityInfoCommand.Result.TownIncome` / `NextLevelTownIncome` (init properties, unscaled), via the new `TownIncomeCalculator.GetFortificationGold`.
- Map editor economy panel (`MapEditorScreenViewModel.Economy.cs`, `MapEditorScreenView.axaml`): a "Towns" section under the starting bank with an on/off box and five level boxes (one undo step while typing a box, one for the switch; switching back on restores the last table); the income table gets the towns column; the panel is 470 px wide instead of 430 so names like "Infernal mana" don't run into the count.
- New maps: `MapEditorMenuScreenViewModel` turns town income on with 10/25/50/75/100.
- MapFromPlan: `rules.townIncome` (README updated); The War of the Ring plan sets `[10, 25, 50, 75, 100]`, and a generator run writes `TOWN_INCOME` "10,25,50,75,100" to its SRules.json.
- Translations: 5 new strings in all 7 catalogs (Russian translated).

Tests: `CustomMapTests.TownIncomeTest` extended (city info for a level-3 town 50 / next 75, the capital 100 / no next level, another race's town no row, a rioting town 0, the income query's towns part 160, no row with the rule off); new `MapEditorScreenViewModelTests.TownIncomeTest` (on in a new map, boxes, capital column 100, typing, clamping, undo steps, off/on, save and reopen) and `WorldIncomeViewModelTests.TownsColumnTest`; `EconomyPanelTest` now expects the capital's 100 in new maps (+200 instead of +100). All 16 map editor visual tests passed; `MapEditorEconomyTest` has two new shots (towns column, towns boxes), reviewed.

Suite 5475 (5473 on gallean-upgrade after AI unused features phase 3 + these 2 tests), rerun on the merged tree.

Real-client check (AOT build, mapgen map with town income on, Average): the editor's economy panel predicted Empire gold +275 (100 capital, 50 resources, 125 towns = capital 100 + Ford level 2 25) and Undead +300; in the game the turn-1 treasury was 775 = 500 bank + 275, the income tooltip showed the Towns column (Gold 100 / 50 / 125 / +275), the capital screen "Gold per turn +100", Ford "+25 (next level +50)". After upgrading Ford to level 3 (250 gold) it showed "+50 (next level +75)", and the turn-2 treasury was 825 = 525 + 300.

Known limits: the game's upgrade prompt ("This will increase garrison capacity, regeneration and armor bonus") doesn't mention the extra gold; the city screen row shows it instead.

## Phase 3 - done

Commit 44852d2b, merged as 1d6c92ce.

What was built:
- `PlayerIncomeCalculator`: with town income on, a neutral player gets `Towns` (its own towns by level, scaled like all other income; neutrals are computer players, so the AI attitude percent applies) and nothing else. With the rule off neutrals still earn nothing.
- `CustomMap.NeutralStartingGold` + `SetNeutralStartingGold` (0-9999), cloned, written as the Splayer `BANK` of every neutral player (player 0 and each kind's side), read back from the primary neutral player.
- `CustomMapEconomy.NeutralTownsIncome`: what all neutral towns bring together.
- Map editor economy panel, Neutrals section: "Starting gold of each neutral side" box (one undo step while typing) and "Neutral towns bring N gold a turn in all." while the map has town income.
- MapFromPlan: `rules.neutralGold` (README updated).
- 2 new strings in all 7 catalogs.

Not visible in the game yet: neutral gold is kept but not spent until phase 4, and no screen shows it (the Classic economy code never runs for neutral players, so it piles up to the 9999 cap on long maps until phase 4's buyer spends it).

Tests: `CustomMapTests.TownIncomeTest` now expects the neutral level-4 town's 75 (total 75, nothing else; 0 with the rule off). New `CustomMapTests.NeutralTreasuryTest`: setter, clamping, clone, save and reopen; with neutral kinds on, the greenskin, wolf and plain neutral sides all start with 300; each earns its own towns (50 / 10 / 25) and after a full round each treasury grew by exactly that; the editor total is 85; no income with the rule off. New `MapEditorScreenViewModelTests.NeutralStartingGoldTest` (box, clamping, one undo step, total line, save and reopen). Mutation checks, each built and run: neutral income branch dropped, writer bank dropped, store read dropped -> a test fails every time. `MapEditorEconomyTest` visual run passed; its last shot shows the Neutrals section (reviewed).

Suite 5484 (5482 on gallean-upgrade after move orders phase 2 + these 2 tests), rerun on the merged tree.

Real-client check (AOT build, mapgen map with town income on and 300 neutral gold, one neutral side owning villages of level 1, 3 and 4): the editor showed 300 and "Neutral towns bring 135 gold a turn in all."; the turn-1 save had the neutral player at 300 gold, the turn-2 save at 435 (300 + 135); the Empire went from 775 to 1050 (+275, as in phase 2).

## Phase 4 - done

Commits 2e586957 (engine, editor, tests) and 369f0edb (rule-switch test); merged as c6a700b5.

What was built:
- Map rules in SRules: `NEUTRAL_BUYING` (bool) and `NEUTRAL_UNIT_FACTOR` (int, null = 3) -> `ScenarioDefinition.NeutralBuying` / `NeutralUnitFactor`.
- `NeutralEconomyPlanner` (new, `World/`): kind pools from `IGameDefinitionProvider.GetNeutralUnits()` (new default interface member, `GameDefinitionProvider` lists the neutral race's units) by the side's subrace, without water-only units, sorted by price; the price rank per village level (H2b); the gold-weighted draw (L); the buying order per village (garrison; below factor x levels units the army at the entrance or a new leader with the Stand order; then the upgrade of a full village); one purchase per village per turn in random village order (J, K), any number while gold is above the spending line (6999).
- `TurnResourcesGeneratedNeutralEconomyRule` runs it right after a neutral side's income when the rule is on. Purchases use the game's own events (`WorldUnitPurchasedEvent`, `WorldStackCreatedEvent`, `WorldCityUpgradedEvent`) plus one new `WorldNeutralUnitHiredEvent` (a unit added to a waiting army, paid).
- Map editor: `CustomMap.NeutralBuying` / `NeutralUnitFactor` (1-20), written and read back; on in new editor maps; Neutrals section checkbox "Neutrals buy units", "Units per village level before upgrading" box and an explanation. MapFromPlan `rules.neutralBuying` (on when left out) and `rules.neutralUnitFactor`; The War of the Ring sets buying on. 3 new strings in all 7 catalogs.

Tests: new `CustomMapTests.NeutralEconomyTest` (setters, clamping, clone, save and reopen; pools 9 greenskin soldiers / 6 leaders, 0 sea creature leaders; the rank 2 / 4 / 6 / 8 / 9 per level; the weights; a garrison purchase from the allowed six; saving gold when too poor; a new wolf leader waiting at the entrance with the Stand order; the next purchase filling that army; an upgrade to level 2 for 150 once the unit target is met; several purchases above the spending line; a played round with the state following; a played round with the rule off where nothing is bought) and `MapEditorScreenViewModelTests.NeutralBuyingTest`. Mutation checks, each built and run: unit target ignored, spending line ignored, rank cap removed, weight flattened, rule switch ignored -> the test fails every time (the last one only after adding the played round with the rule off). `MapEditorEconomyTest` visual run passed, Neutrals section reviewed.

Suite 5505 (5503 on the branch, then gallean-upgrade gained the other agent's neutral guard tests), rerun on the merged tree.

Headless War of the Ring, 20 turns, seed 1, every player AI, buying on vs off (`NeutralEconomyBenchTests`, not committed):
- Neutral turn time: 1523 ms vs 1522 ms on average, so no measurable cost.
- Free Men: village levels 25 -> 51 (off: 24, they lost a village), units 25 -> 153 (off: 72, from the scripted spawns), garrisons 25 -> 50, armies 0 -> 33 (off: 17), gold 215 left (off: 6090 piled up).
- Greenskins: levels 21 -> 35, garrisons 16 -> 30, 2250 gold left (off: 7000). Barbarians: levels 13 -> 24 (off 13), 210 gold (off 3270). Wild Elves 4 -> 8, Marsh Folk 3 -> 6, Wolves 3 -> 5, plain neutrals 6 -> 12.
- Dragons saved 1500 gold without buying: their level-4 village sells its 4 cheapest dragons (4500-5300) and has a free slot, so it saves for one (the H2b rule working as written; they'd buy around turn 60 at 75 a turn).
- Sea Creatures own no village and have no land leaders: nothing.
- Every side upgraded steadily; the unit target held them back only where they were below it (the Free Men hired leaders first, as asked).

Real-client check (AOT build, mapgen map, town income on, neutral gold 300, buying on, one neutral side): turn 1 save: 300 gold, Gallymar (level 1) and Vinusi (level 4) empty, Errgok (level 3) full. Turn 2 save: Gallymar bought an Air Elemental (50), Vinusi a large 300-gold unit, Errgok (full) hired a 40-gold leader waiting at its entrance with the Stand order; gold 45 = 300 + 135 income - 50 - 300 - 40.

Known limits: waiting armies never leave (phase 5); a village with an allowed soldier it can't afford yet saves rather than buying something else.

## Phase 5 - what neutral armies do (spec)

The user's wish (from the answers to G and N): neutrals protect their villages first; once they have more money they send units out, and those armies attack enemy armies near their home towns or conquer nearby towns.

### What phase 4 leaves

- Armies bought by the neutral economy wait at their village entrance with the Stand order. A waiting army blocks new leaders at that village; once it is full (its leader's leadership), the village buys nothing more for it.
- In the 20-turn War of the Ring run the Free Men alone hired 33 armies; with phase 5 they would start moving.

### What the engine already offers (no AI change needed)

- Orders the AI already plays for any army (`WorldAiPlanner.AttackAction` / `MoveAction`):
  - `DefendLocation(zone, centre, radius)`: attacks enemy armies inside the square around the centre and walks back into it when outside. That is "hunt near home" if the centre is the home village.
  - `SecureCity(town)`: walks to a town and attacks or takes it (`CustomMapEventTests.SecureCityPlayTest` shows a neutral template taking an Empire village).
  - `Roam`: attacks anything hostile anywhere, but only fights it is clearly stronger in (`PowerFilter`, `RoamingPowerPercent`), scored by interest and distance; it never stays home.
  - `Guard`: only attacks moving enemies within 2 tiles (`AiStackGuardRange` 5 / 2).
- Ordered fights (`DefendLocation`, `SecureCity`) use the ordered scoring (power ratio and survival, no hard "only if stronger" filter); roaming fights use the filter.
- Engine rule: plain neutrals (subrace Neutral, player 0) can't take the SecureCity order (`StackOrderCalculator`); the kinds' sides (greenskins, wolves, ...) can.
- Changing an order is an existing event (`WorldStackOrderChangedEvent`), so the neutral economy rule can hand out orders at the same turn start where it buys.

### Technical approach

- T1 (recommended to start): the neutral economy rule also hands out orders each turn, using only existing orders: a full army that should go out gets `DefendLocation` around its home village (radius from the map setting; a synthetic zone ID per village, nothing is added to the map's zones), or `SecureCity` on a chosen nearby town; when the target is gone it gets its home order back. No AI code changes, Classic untouched; the risk is that ordered fights have no "only if stronger" filter.
- T2 (only if T1 plays badly): a new order "raid from home" with roaming scoring limited to the area around the home village. Cleaner behaviour, but every order visitor (AI, client order labels, saves, editor order kinds) needs a line.
- Measured like phase 4: the headless War of the Ring bench, buying with and without phase 5 (neutral turn time, armies out, battles neutrals vs races, towns changing hands, losses), plus a real-client check.

### Phase 5 decisions

- P (user): P1 for now. An army leaves home once it is full (its leader's leadership used up); the village keeps its garrison and the free entrance lets the next leader be hired.
- Q (user): Q2. Two per-map settings: armies hunt hostile armies within 10 tiles of their home village and go for towns within 60 tiles (SRules, editor Neutrals section, MapFromPlan). On The War of the Ring a 60-tile town radius reaches another side's town from 18 of the 42 neutral villages.
- R (user): R1 for now. A full army first goes for hostile armies within the army radius of its home; only when none there is a fight it would take (question S) does it go for a town within the town radius.
- S (user): S3. An army only takes a fight where the AI's strength estimate (`WorldAiEstimator`, garrison included for towns) gives it at least the map's margin over the target; the margin is a per-map setting, default 140 % (the game's own roaming margin, `RoamingPowerPercent`).
- T (user): T2. An army that captures a town makes it its new home (it defends it, refills and leaves from there, and the radii are measured from it); after any other fight it goes back to its home village, where it heals and, being no longer full, waits to be refilled (P1).
- U (user): U1, no switch for placed armies. Only armies the neutral economy bought follow these rules; armies placed on the map and the map's scripted ones (patrols, spawns) keep their own orders.
- V (user): V2. The engine rule that keeps plain-neutral armies from the SecureCity order (`StackOrderCalculator`) gets one exception: armies bought by the neutral economy, while the map rule is on. Original campaigns and placed armies are unchanged; without it, maps with neutral kinds off would have no conquest at all.
- W (user): W1. Every side hostile to the army's side counts, other neutral kinds included (they are hostile by design).
- X (user, after the 5a measurement): X1. A neutral village fills the army waiting at its entrance until it is full whatever the unit target; the target only decides between a new leader and a village upgrade.
- Taken while building phase 5: only villages (`WorldCity`) are town targets, never capitals; armies that aren't full walk home with `MoveToLocation` (radius 1, so a new leader at the entrance doesn't block them).

### Phase 5 questions

~~P. When does an army leave home?~~ Answered: P1 for now (once it is full).

~~Q. How far from home do they go?~~ Answered: Q2 (two radii, map settings).

~~R. What they go for.~~ Answered: R1 for now.

~~S. Which fights they take.~~ Answered: S3 (Decisions).

~~T. After a win or a captured town.~~ Answered: T2.

~~U. Which armies.~~ Answered: U1.

~~V. Plain neutrals and towns.~~ Answered: V2.

~~W. Who counts as an enemy.~~ Answered: W1.

Phases: 5a orders handed out by the rule (T1) with the strength-margin check, the V2 exception, the three map settings (army radius 10, town radius 60, margin 140 %) in SRules, the editor's Neutrals section and MapFromPlan, and tests, ~1.5 days; 5b War of the Ring measurement (neutral turn time, armies out, battles by side, towns changing hands, losses) and tuning, ~0.5 day.

### Phase 5a - built, measured, not merged

Commit 8bcb4422 on `upgrade/neutral-armies`: `NeutralArmyPlanner` (orders from the neutral economy rule), `WorldStack.NeutralHomeId` (saved), `WorldNeutralArmyOrderedEvent`, map settings NEUTRAL_ARMY_RADIUS / NEUTRAL_TOWN_RADIUS / NEUTRAL_ATTACK_MARGIN with editor boxes and MapFromPlan keys, `CustomMapTests.NeutralArmiesTest` (5 mutation checks caught), editor test. Suite 5506.

Finding while building: the SecureCity restriction for plain neutrals lives only in `StackOrderCalculator`, which only scenario events use; orders set by the rule aren't affected, so V2 needed no engine change (the test shows a plain-neutral army taking an Empire village in play).

Measurement (War of the Ring, 20 turns, seed 1): identical to phase 4, no army ever left. Diagnostic: the Free Men's 14-16 bought armies held 2-3 units of 6 leadership after 10 turns. Cause: decision N fills waiting armies only while the side is below its unit target, and decision P1 sends an army out only when it is full; once the side reaches its target it upgrades instead, each upgrade adds a garrison slot that is filled first, so armies stay half full.

Experiment (reverted): a waiting army is filled whatever the target, the target only decides between a new leader and an upgrade. Result after 20 turns: 5 Free Men armies out (one with SecureCity, others hunting or returning), the Free Men took an Empire village (Free Men 16 -> 17 villages, Empire 8 -> 7), neutral turns 1392 ms vs 1376 ms. The other kinds stayed above their unit targets the whole time, so they never hired leaders and sent nothing out.

~~X. Fix options.~~ Answered: X1 (Decisions).

## Phase 5 - done

Commits 8bcb4422 (armies, settings, tests), 463d0b17 (decision X1), e9953414 (editor text); merged as ff0f9676.

What was built (on top of the 5a list above): decision X1 in `NeutralEconomyPlanner` (fill the waiting army before anything but the garrison; new leaders only below the unit target; upgrades last); the editor explanation updated (one string replaced in all 7 catalogs).

Tests: `CustomMapTests.NeutralArmiesTest` (settings, clamping, clone, save and reopen; a full plain-neutral army sent to a weak Empire village 8 tiles away; hunting first when a weak Empire army stands 5 tiles from home; an army that isn't full staying or walking home; a captured town becoming home; the home surviving a save; a 1000 % margin refusing a guarded village; in play, the plain-neutral army taking the village while a placed army never moves). `NeutralEconomyTest` now checks X1 (a waiting army above the target is filled first; with none, the village grows). Mutation checks, each built and run: margin ignored, hunting skipped, full check removed, captured home ignored, rule hook removed, X1 removed -> a test fails every time. `MapEditorEconomyTest` visual run passed; the Neutrals section with the three new boxes reviewed (no truncation).

Suite 5506, rerun on the merged tree.

Headless War of the Ring, 20 turns, seed 1 (final code): 14 Free Men armies bought, 5 out at turn 21 (one on a capture order), the Free Men took an Empire village (17 villages vs 16, Empire 7 vs 8), neutral turns 1425 ms vs 1376 ms without armies going out. The other kinds stay above their unit targets (Greenskins 504 units vs 105), so they never hire leaders and send nothing out: on this map phase 5 shows mostly through the Free Men.

Real-client check: none in this phase (no client screen changed apart from three editor boxes and a text, checked in the visual test); the behaviour shows in the headless run and the engine test plays it. Check it in a real game next time the War of the Ring is played.

Known limits: hunting uses the DefendLocation order, which picks its own targets inside the radius with the ordered scoring, so the margin only gates whether the army goes out, not which army it then fights; capitals are never targets; sides above their unit target (most War of the Ring kinds, thanks to the placed armies) don't hire new leaders.
