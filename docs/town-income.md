# Town income and neutral economy

Status: all questions answered (H's price ceiling table is asked when phase 4 starts); nothing built. The user asked for two features, in this order:

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
- I (user): The War of the Ring's scripted neutral life is out of scope here; the user will rework it separately later.

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

1. Town income in the engine: SRules table, `PlayerIncomeCalculator`, `IncomeInfo.Towns`, tests. ~1 day.
2. Town income on screen: income table line, city screen line, editor economy box and column, MapFromPlan `rules.townIncome`, War of the Ring setting. ~1-1.5 days.
3. Neutral treasury: neutral sides earn from their towns, neutral starting bank in the editor. ~0.5-1 day.
4. Neutral garrison buying: kind unit pools, neutral purchase command, neutral planner action, headless War of the Ring measurement. ~2 days.
5. Neutral armies: leaders hired at villages once the garrisons are full and gold is left, attacking enemy armies near home towns or conquering nearby towns, measurement. ~2-3 days, specced after phase 4's numbers.

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
