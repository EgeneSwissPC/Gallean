# Town income and neutral economy

Status: proposal, nothing built. The user asked for two features, in this order:

1. Every town (village) brings a small amount of gold every turn, depending on its level.
2. Neutrals then have gold of their own and buy units on their own.

## Decisions

- Order: town income first, neutral economy second (user).
- Neutrals pay normal hire prices: Gunits `ENROLL_C` (+ GDynUpgr per level above the base level). Neutral soldiers and leaders already have prices in the data (79 soldiers, 30-9950 gold; 94 leaders, 30-9950 gold).
- Standard (Classic) AI stays as upstream's. Neutral buying is new code that runs only when the map rule for it is on. It is never an edit of the race players' economy code.

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

- Engine: `PlayerIncomeCalculator.GetIncomeInfo` adds `TownIncome` = the sum over the player's `WorldCity` fortifications of the table value for each city's level. `IncomeInfo` gets a `Towns` part, which `WorldGetPlayerInfoCommand` passes to the client. It goes through the same `Scale` as the other income, so difficulty and AI attitude apply. Capitals keep paying the race base income only.
- Data: a per-map table in `SRules.json` (new column, e.g. `TOWN_INCOME` = `"10,20,30,40,50"` gold for levels 1-5). Missing or empty = no town income, so original campaigns stay as they are. Read into `ScenarioDefinition.TownIncome` (init property, like `ForeignSpellMarkup`).
- Editor: `CustomMap.TownIncome` + a box in the economy panel; `CustomMapEconomy` adds the column through the same `PlayerIncomeCalculator` call it already uses. MapFromPlan: `rules.townIncome`.
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
5. Neutral armies: leaders hired at villages, orders, measurement. ~2-3 days, specced after phase 4's numbers.

## Open questions

A. Scope. A1: per-map table in `SRules.json`, off unless the map sets it (original campaigns unchanged). A2: global gallean-upgrade rule. A3: on for large maps only (`LargeMapRules.MinMapSize` 256). Recommendation: A1, with new editor maps defaulting to the recommended table.

B. Amounts. B1: level x 10 (10/20/30/40/50). B2: level x 5. B3: steeper, so upgrades pay back faster (e.g. 10/25/50/75/100). For scale: race base income is 100 a turn, a resource 50. With B1 the Empire's 8 War of the Ring villages bring 220 a turn; an upgrade from level 1 to 2 (150 gold, +10) pays back in 15 turns, from 4 to 5 (750 gold, +10) in 75. Recommendation: B1 as the default, editable per map.

C. Capitals. C1: capitals keep only the race base income. C2: capitals also pay as a level-5 town. Recommendation: C1.

D. Rioting villages. D1: a rioting village pays nothing. D2: pays anyway. Recommendation: D1 (the game already disables hiring there).

E. Difficulty and AI attitude scaling. E1: town income is scaled like all other income. E2: never scaled. Recommendation: E1, so the editor prediction and the game use one formula.

F. Neutral income source (phase 3). F1: town income only. F2: towns + the neutral race's data base income (100 gold) per side. F3: towns + a per-map flat amount. Recommendation: F1, plus the starting bank box.

G. What neutrals buy. G1: garrison units only. G2: garrisons + new armies with leaders. Recommendation: G1 in phase 4, then decide G2 (phase 5) from the War of the Ring numbers.

H. Which units a neutral side may buy. H1: all soldiers of its kind (plain neutrals include the 9950 monsters). H2: the kind's soldiers, gated by village level (e.g. a level-N village sells units up to a price ceiling per level). H3: a per-map list per kind in the editor. Recommendation: H2 with a default ceiling table, and H3 later if needed.

I. The War of the Ring's own neutral life (Shadow stages, spawns every 4 turns, retakes). I1: keep it as is and add buying on top. I2: lower the scripted spawns once neutrals buy. Recommendation: I1, then tune from the measurement.
