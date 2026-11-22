# AI overflow spending: never sit at the 9999 cap

Status: spec answered (all four recommendations taken, 2026-11-22); phases 1-3 done (diagnosis, mana, gold); user picked both 2b (foreign mana) and 3b (more gold sinks); answers E-H taken (all recommendations); 3b-A city upgrades done; 2b next, then 3b-B site shopping (only if still needed); 4 left.

## Decisions

- **User rule (2026-11-22):** every one of our world AIs, however different, must never reach 9999 mana or gold. When an amount gets close to the cap, the AI starts spending, and keeps spending until it is back below.
- It applies to every personality (Classic+, Aggressive, Builder, Expansionist) and to every designed AI (AI designer), as a shared rule on top of their own knobs, not as a separate personality.
- Classic stays identical to upstream (answer A): Classic+ gets the rule, so Classic+ is the original AI that spends.
- Spending starts at 70 % of the cap = 6999, for gold and mana alike (user change after phase 2; answer B was 80 %), editable per designed AI in phase 4.
- Only useful casts (answer C); a summon next to its own armies counts as useful. Mana that nothing useful can use is still lost.
- Gold sinks are units, buildings and leaders (answer D); merchant items and mage tower spells only if the bench still shows gold at the cap after phase 3. It did (Dwarves), so phase 3b follows.
- 3b order (answer E): city upgrades alone first and measured, then site shopping.
- Site shopping detours (answer F): only sites an army reaches this turn or next, only armies on a Normal order.
- Need-weighted rods (answer G): always for our AIs, not only in spending mode; Classic untouched.
- "Needed" mana (answer H): the mana types of the cheapest level 3 spell the race could research.

## The problem today (measured)

Resources are clamped to `GlobalVariables.MaxResourceAmount` = 9999 when income arrives (`WorldEngine.Events.PlayerResourcesGenerated.cs`), so anything above the cap is lost.

Measurement: Classic AI, War of the Ring 1024 (`S601SC0000`, suggested level 1), all 5 races AI, 60 turns, seed 1 (bench `~/work/gallean-tools/manabench/`):

- Mana earned 260,650, spent 19,800 (7.6 %) on casts and research, lost 184,181 (70.7 %) at the cap.
- Every race reached the cap around turn 10-20 and started 39-52 of its 60 turns with a mana at 9999.
- Lost share / casts per race: Human 76 % / 8, Elf 74 % / 1, Dwarf 53 % / 0, Heretic 74 % / 21, Undead 65 % / 32.
- Research stalled at 3-9 known spells (the AI's own limit is 12). Cause: see "Phase 1 — done".
- Gold lost at the cap: Human 11,882, Dwarf 6,764.

## Why (Classic code, `World/Ai/WorldAiPlanner.CastSpellAction.cs` / `ResearchSpellAction.cs`)

- Research only on turns divisible by 5, levels 1-4, stops at 12 known spells, never boosts or invisibility.
- At most one attack/summon/weaken spell per turn, aimed only at enemies within 7 tiles of an own city (otherwise a 2 % chance).
- Heal only armies at or below 50 % HP; restore movement only armies outside with 0 MP; boost and invisibility never cast on a target.
- Global spells only with scenario suggested level >= 10, all four manas above 1000, one per turn, 2+ armies outside. On this map: never.
- Gold: at most 1 building and 1 unit purchase per turn (economy actions in `WorldAiController`).

## What the player would see

AI races cast noticeably more spells on big maps once they are rich (summons, attack spells on armies anywhere in sight, heals, boosts before attacks), research their whole spellbook, and spend gold on more units, buildings and leaders instead of banking it.

## Behaviour

Spending mode switches on per resource when it is above the threshold (open question B, default 80 % of the cap = 8000) and off when it falls back below.

Mana, in spending mode:
1. Research every turn (not only every 5th), any level the capital allows, no 12-spell limit, boosts and invisibility included.
2. Several targeted casts per turn: attack/summon/weaken on any hostile army it can see, not only near its cities.
3. Heal any wounded army (not only at or below 50 %), boost and invisibility on armies that are about to attack.
4. Global spells without the suggested-level gate and the all-four-manas gate: only the cast cost is checked.

Gold, in spending mode:
1. No 1-per-turn limit on buildings and unit purchases.
2. Fill garrisons and armies with units, hire leaders while there is room.
3. Later, if still needed: items at merchants, spells at mage towers (open question D).

## Technical approach

- New `WorldAiProfile` knob, e.g. `SpendAbovePercent` (0 = off = Classic). Every registered personality sets it; `DesignedStageEvaluator` sets it for designed AIs (default on, editable by the designer block).
- `CastSpellAction` / `ResearchSpellAction` / the economy per-turn limits read the knob; Classic keeps the original constants, guarded by `AiPersonalitiesTests` and the S099 tournament CSV diff.
- AI designer: a Rules block "Spend resources above N %" (default 80).

## Testing

- Engine tests per rule, each with a Classic control (Classic does not cast / research / buy here, the spender does).
- Rerun the mana bench on War of the Ring: success = lost share under 5 % and no turn starting at the cap, for mana and gold.
- Tournament vs Classic on S100 and S099 so the change does not make the personalities weaker.

## Phases

1. ~~Find why research stalls at 3-9 spells (headless diagnosis, no screen; ~0.5 day).~~ Done, see "Phase 1 — done".
2. ~~Mana spending in the engine for all personalities and designed AIs (~1 day), mana bench + tournament.~~ Done, see "Phase 2 — done".
2b. Foreign mana: rod planters prefer the mana sources the race needs (~1 day), see "Phase 2b — foreign mana".
3. ~~Gold spending (~1 day), same checks.~~ Done, see "Phase 3 — done".
3b. More gold sinks: ~~city upgrades first (~0.5 day)~~ done, see "Phase 3b-A — done"; then shopping at sites (~2 days), see "Phase 3b — more gold sinks".
4. AI designer block "Spend resources above N %" (~0.5 day), the visible part in the AI editor.

## Phase 1 — done (why research stalls)

Headless diagnosis, no code change: `ResearchTest` in `~/work/gallean-tools/manabench/TmpManaBenchTests.cs` plays War of the Ring (all AI, seed 1, 40 turns) and, every 10 turns, runs `WorldResearchSpellCommand` for every available unknown spell of every race and logs the refusal reason. All 5 races have Warrior lords on this map.

Findings:
- **Level 3 and 4 spells need two or three mana types** (`GspellR.json`: levels 1-2 cost one type, 100-400; level 3 two types, 300-680 each; level 4 two or three types up to 1600). Every race earns essentially its own mana only (Human life, Heretic infernal, Undead death; Elf and Dwarf rune + grove). From turn 20 on, every level 3 and 4 spell of every race was refused with InsufficientResources while its own mana sat at 9999.
- **Level 5 is refused by the lord:** Warrior and Guildmaster lords research up to level 4, only Mage lords reach 5 (`GlobalVariables.Lord*SpellResearchLevelLimit`).
- **The level 1-2 spells left over are ones Classic never wants:** boosts (Classic never researches them) and spells whose data says the AI shouldn't use them (`AI_CAT` None). The Elves have the most of these: 11 of their 24 spells are marked None.
- Research itself works: the AI took one spell on almost every 5th turn (25 researches in 40 turns), always the cheapest level first.
- Spell research needs a building (`SpellBuildingRequired` until the capital has it, turn 10-20 here).

What it means for phase 2:
- A race can only spend its mana on level 1-2 spells on this map. Casting costs 25-100 (level 1) and 50-200 (level 2), and a Warrior lord casts each spell once per turn, while income is about 775 (Human) to about 960 grove (Elf) per turn. Casting every known spell every turn barely keeps up, so casting alone may not stop the cap.
- The biggest continuous sink is summons: on maps 256 and up our summon-upkeep rule charges the casting cost again every turn, so each summon kept alive keeps draining mana.
- Lasting fix for levels 3-4: the AI must own other mana sources (capture or rod foreign mana crystals). That is an expansion change, proposed as phase 2b below.

Proposed phase 2 scope (spending mode only): research every turn including boosts and restore movement (still skipping `AI_CAT` None), several casts per turn on any visible enemy, summons as the main sink, boosts on armies about to attack. Proposed phase 2b (~1 day, after measuring phase 2): in spending mode, foreign mana crystals get extra weight as rod and capture targets.

## Phase 2 — done (mana spending)

Merged d9eb5927 (feature commit 0819774b on `upgrade/ai-overflow-spending`).

What was built:
- `WorldAiProfile.SpendAbovePercent` (0 = off = Classic). Classic+, Aggressive, Builder and Expansionist set 80; every designed AI gets 80 (stage profiles in `DesignedStageEvaluator.ToProfile`, and the no-stage fallback in `DesignedWorldAiBrain`). The designer block that edits it is phase 4.
- `World/Ai/WorldAiSpending.cs`: spending mode is on while any mana is at or above 80 % of `MaxResourceAmount` (7999); a spell counts only if its cost uses a mana that is over the line.
- Research (`ResearchSpellAction`), in spending mode: every turn, no 12-spell cap, levels up to 5 (the lord's limit still applies), boosts and invisibility included; `AI_CAT` None spells are still skipped.
- Casting (`CastSpellAction.FindSpendingCast`), only when Classic found nothing to cast: attack/summon and weaken spells on the 30 visible hostile armies nearest to its own (neutrals included), boosts and invisibility on its armies with movement left within 7 tiles of a visible enemy, heals on any damaged own army, restore movement as in Classic, global spells without the suggested-level and all-four-manas gates (heal/boost/invisibility/restore movement only). Each spell is still limited by the lord's casts per turn.
- Area spells (radius 1 or more) are still not cast by the AI, as in Classic.

Tests: `WorldAiOverflowSpendingTests` (5: attack spell on a far neutral army, no spending at 7998 mana, heal on an army at 80 % HP, research on a non-research turn, every personality and designed AI has 80), each with Classic as the control. Mutation check: with spending forced off, 3 of 5 fail (the other two assert no spending / the profile values). Two designed-AI tests now expect the spending knob (a designed copy of Classic = Classic + spending, per the decision). Suite 5434 on the branch, 5443 after the merge (gallean-upgrade had gained battle AI designer phases 8-10).

Classic unchanged: the S099 tournament CSV (Classic, 8 games x 50 turns) is identical before and after apart from the timing columns. S100 Classic+ vs Classic (12 games x 60 turns): identical results, since nobody reaches 8000 mana on that map.

Measured, War of the Ring 1024, all races Classic + spending, 60 turns, seed 1, compared with the Classic baseline above:
- Mana lost at the cap: 184,181 -> 28,379 (70.7 % -> 10.9 % of income). Spent on spells and research: 19,800 -> 138,000 (7.6 % -> 53.1 %). Casts: 62 -> 842.
- Lost share / turns starting at the cap: Human 10 % / 9 (was 76 % / 49), Elf 23 % / 35 (74 % / 51), Dwarf 0 % / 0 (53 % / 39), Heretic 8 % / 8 (74 % / 52), Undead 0 % / 0 (65 % / 48).
- The Elves' rest is mostly rune mana (11,001 of 18,852 lost): no Elf spell the AI researches costs rune, so nothing useful can use it (answer C). Their grove loss fell to 13.6 %.
- AI time: the 60 turns took 25.5 min instead of 23.2 min (about +10 %).
- The target (under 5 % lost, no turn at the cap) is reached for Dwarves and Undead, not yet for Humans, Heretics and Elves. Phase 2b (foreign mana, so level 3-4 spells become affordable) is the next lever; casting is already near the per-turn limit of a Warrior lord with 4-8 spells.
- No real-client run: no client code changed and nothing new is on screen; the AOT check moves to phase 4 (designer block).

Known limits: one 60-turn run on one map; no strength comparison on a big map yet (the S100 tournament never enters spending mode).

## Phase 3 — done (gold, threshold 70 %)

Merged 8d7edcd6 (feature commit 914068e2 on `upgrade/ai-gold-spending`).

What was built:
- `WorldAiSpending.DefaultSpendAbovePercent` 80 -> 70 (user decision), so mana and gold spending start at 6999.
- `WorldAiSpending.IsGoldSpending`: gold at or above the threshold.
- Units (`PurchaseUnitAction`): no 1-unit-per-turn limit while gold spending; units still go into free garrison slots and AutoFit moves them into armies, so full garrisons end it.
- Leaders (`PurchaseLeaderAction.GetSpendingExtraLeaders`): quota + 2 fighting leaders and + 1 rod planter while gold spending.
- Not liftable: one building per turn is a game rule (`WorldConstructBuildingCommand`, `TurnActions.BuildingConstructed`) for every player.

Tests: `CustomMapTests.GoldSpendingTest` (fresh 48x48 custom map, every player at 9999 gold, first player's first turn: Classic +1 unit, 9649 gold left; Classic+ +21 units and leaders, stops at 6729 gold, just under the line). `WorldAiOverflowSpendingTests` follow the 70 % (no spending at 6998). Suite 5444.

Classic unchanged: S099 Classic CSV identical apart from timings (gallean-upgrade 9d75dd6b vs the branch). S100 Classic+ vs Classic, 12 x 60: identical results, nobody reaches 6999 there.

Measured, War of the Ring, Classic + spending at 70 %, 60 turns, seed 1:
- Gold lost at the cap: 21,329 (Classic) -> 9,345 (phase 2, side effect) -> 5,142. Left: Dwarf 3,644 (gold above 7000 from turn 42, at the cap from turn 51 with garrisons and leader quota full: nothing left to buy), Human 1,498.
- Mana lost: 12.3 % of income (32,404 of 262,450; phase 2 at 80 %: 10.9 %, Classic: 70.7 %). Casts 902. Dwarf and Undead 0 %, Heretic 8 %, Human 10 %, Elf 28 % (11,001 rune, which no Elf spell uses, and 11,601 grove: the Elves know only 4 spells). Lowering the line does not help much here: the races already cast everything they know every turn.
- AI time: 22.5 min for the 60 turns (Classic baseline 23.2, phase 2 25.5); the games differ, so read it as "no slowdown".
- No real-client run: no client code changed; moves to phase 4.

What's left for "never at the cap":
- Gold (answer D was "later, only if gold still hits the cap"): it does, for the Dwarves. Candidate sinks: items at merchants, spells at mage towers, mercenaries, unit training at trainer sites.
- Mana: phase 2b (foreign mana sources, so level 3-4 research becomes affordable), and for the Elves the spells marked `AI_CAT` None.

## Phase 3b — more gold sinks (spec, not built)

Problem: after phase 3 the Dwarves still sat at 9999 gold from turn 51 on War of the Ring: garrisons full, leader quota full, one building per turn already taken. Gold needs things to buy that don't run out as fast.

What the engine already offers (Classic uses none of it):
- **City upgrades** (`WorldUpgradeCityCommand`): no army needed; one level per city per turn (`CityAlreadyGrownThisTurn`), up to the scenario's max city level; costs from `GcityInf.json` 150 / 250 / 500 / 750 gold for levels 1->2 ... 4->5 (1650 per city in total; Guildmaster lords get a discount). Higher levels give more protection and regeneration. No AI code calls it today.
- **Sites** (army must stand at the site): merchants (`WorldPurchaseItemCommand`), mage towers (`WorldPurchaseSpellCommand`), mercenary camps (`WorldPurchaseMercenaryCommand`, units into free slots of the visiting army), trainers (`WorldTrainUnitCommand`, experience for gold). Classic armies never go to sites; only thieves target them (`ThiefAction`), and no AI code calls these purchase commands.

Options:
- **A.** City upgrades while gold spending: a new economy action, cheapest level first, nearest-to-the-front city first (~0.5 day). Independent of every question below.
- **B.** Site shopping while gold spending: an army with nothing urgent detours to a site within reach and buys there: mercenaries into free slots, training for its units, then items (~2 days).
- **C.** Mage towers too: buy spells the race can't research (~1 day on top of B; the spells then also become mana sinks).

Recommendation: A first, then B with mercenaries and training (gold straight into army strength), items last; C after measuring B.

## Phase 3b-A — done (city upgrades)

Merged 4388ecf6 (feature commit 96f5a205 on `upgrade/ai-city-upgrades`).

- New economy action `WorldAiPlanner.UpgradeCityAction` (priority 0, fixed score MaxScore/2): while gold spending, `WorldUpgradeCityCommand` on the owned city with the lowest level first. Registered for every profile and for designed AIs (it is part of the spending rule, not a designer block); it does nothing unless gold is at or above the threshold, so Classic never upgrades.
- Test: `CustomMapTests.CityUpgradeTest` (two Human level-1 villages, 9999 gold, turn 1: Classic leaves both at 1, Classic+ brings both to 2). Mutation check: with the action switched off, the Classic+ case fails. Suite 5446.
- Classic unchanged: S099 Classic CSV identical apart from timings.
- Measured, War of the Ring, 70 %, 60 turns, seed 1: **no gold lost at the cap for any race** (phase 3: 5,142; Classic: 21,329). The Dwarves now hover around 6,100-7,900 from turn 41 and end at 8,883 on turn 60. Mana losses unchanged (Human 10 %, Elf 27 %, Heretic 8 %, Dwarf and Undead 0 %).
- Consequence for answer E: site shopping (3b-B) is not needed on this map for 60 turns; revisit if a longer run or another map shows gold at the cap again.
- No real-client run (no client change); moves to phase 4.

## Phase 2b — foreign mana (spec, not built)

Problem: level 3-4 spells need two or three mana types (phase 1), each race earns only its own, so its research stops at level 2 and its mana piles up.

What the engine already offers:
- War of the Ring has 247 resource nodes (`SCrys.json`): 115 gold mines and 132 mana sources (28 infernal, 26 life, 16 death, 27 rune, 35 grove).
- Rod planters (`RodAction`) already target every resource node that is unowned or on hostile land and has no rod next to it, all with the same interest (`CreateRodInterest`); the nearest wins. They also remove hostile rods. Quota: 1 rod planter (+1 while gold spending since phase 3).

Options:
- **A.** Rod targets weighted by need: mana sources of a type the race's next unaffordable research costs get extra interest (x2), gold mines and already-owned types stay as they are (~0.5-1 day).
- **B.** A plus one more rod planter while such a mana type is missing (~0.5 day on top).
- **C.** Armies also go for enemy land with such sources (conquest targets): a much bigger AI change, not proposed now.

Recommendation: A + B, measured with the War of the Ring bench (research levels reached, mana lost).

## Open questions

- ~~**A.** Classic too? Recommendation: no. Classic stays upstream's AI; Classic+ is "Classic with our fixes" and gets the spending rule, so picking Classic+ gives the original AI that spends.~~ Answered: recommendation taken.
- ~~**B.** Threshold: 80 % of the cap (8000)? Recommendation: 80 %, editable per designed AI.~~ Answered: recommendation taken.
- ~~**C.** When nothing useful can be cast (no enemy in sight, nobody wounded): accept the loss, or cast anything castable? Recommendation: useful casts only; a summon next to its own armies counts as useful.~~ Answered: recommendation taken.
- ~~**D.** Gold sinks beyond units, buildings and leaders (merchant items, mage tower spells)? Recommendation: later, only if the bench still shows gold at the cap after phase 3.~~ Answered: recommendation taken.
- ~~**E.** (3b) Start with city upgrades alone and measure before site shopping? Recommendation: yes, A is small and may already be enough on most maps.~~ Answered: recommendation taken.
- ~~**F.** (3b) Site shopping detour: how far may an army walk to a site? Recommendation: only sites it reaches this turn or next, and only armies on a Normal order (not healing, reinforcing or on a scenario order).~~ Answered: recommendation taken.
- ~~**G.** (2b) Need-weighted rods only in spending mode, or always for our personalities? Recommendation: always, because a race needs the second mana before it is rich, and Classic stays untouched either way; measured against Classic on the bench.~~ Answered: recommendation taken.
- ~~**H.** (2b) Which "need" counts: the mana types of the cheapest level 3 spell the race could research, or all types its level 3-4 spells use? Recommendation: the cheapest level 3 spell's types, so the AI unlocks one spell at a time.~~ Answered: recommendation taken.
