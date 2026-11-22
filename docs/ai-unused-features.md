# AI: the parts of the game it never uses

Status: questions A-F answered (2026-11-22); all five phases done; follow-ups listed in "Phase 5 — done".

## Decisions

- **User priority (2026-11-22):** this comes before lookahead, objectives and maps carrying AIs: "the whole parts of the game go unused ... real players have a huge advantage over them".
- Who (answer A): all our AIs (Classic+, Aggressive, Builder, Expansionist, every designed AI); Classic untouched. Designer blocks may follow later.
- Site visits (answer B): whenever the army can afford something useful there and the site is reachable; the richer the player, the further the AI is willing to walk (no fixed this-turn-or-next limit). After phase 4 (user): mercenary camps and trainers get longer limits that grow progressively as gold nears the cap.
- Merchants (answer C): only items the AI can use; more item kinds as the AI learns to use them (equipment after phase 5).
- Summon upkeep (answer D): a tunable profile knob, the share of a mana's income per turn that running summon upkeep may take; default 50 % for our AIs. After the measurement (option B, user): while that mana is at or above the spending line, up to 100 % of its income.
- Fog, unfog, remove rod and change terrain spells (answer E): later, once their goals are designed.
- Order (answer F): phases 1-5 as listed.
- Same rule as every AI change: Classic stays identical to upstream. The new behaviour belongs to our AIs (Classic+, Aggressive, Builder, Expansionist, every designed AI), gated by a profile knob, and is proven with the S099 Classic CSV diff.

## The problem today (from the code)

- **Sites:** armies never go to merchants, mage towers, mercenary camps or trainers. `WorldAiTargetEnumerator` only offers sites to thieves (`ThiefAction`), and no AI code calls `WorldPurchaseItemCommand`, `WorldPurchaseSpellCommand`, `WorldPurchaseMercenaryCommand` or `WorldTrainUnitCommand`. The game data already has AI interests for them that nothing reads for ordinary armies (`GAI.json`: I_MERCHANT 90, I_TOWERS 100, I_TRAINER 100, I_H_MERCS 120 for the first attitude).
- **Items:** on the world map the AI only uses revive and heal items from the army's bag (`UseItemAction`) and never equips anything: no AI code calls `WorldEquipItemCommand`. Banners, artifacts, boots and tomes it picks up stay in the bag.
- **Area spells:** `CastSpellAction` only looks at single-target spells (`AreaRadius` 0) and global ones; spells with a radius are never cast. There are 19 of them (`Gspells.json`): attack 6, lower 3, fog 3, unfog 2, heal 2, change terrain 2, boost 1.
- **Spells marked "not for the AI":** research and casting skip `AI_CAT` None. 36 of 120 spells are marked so: Elf 11, Human 8, Heretic 7, Undead 5, Dwarf 5. By effect: boost 11, lower 10, restore movement 3, summon 3, fog 3, unfog 1, invisibility 1, heal 1, attack 1, remove rod 1, change terrain 1. 30 of the 36 have effects the AI already knows how to cast.
- **Summon upkeep:** on maps 256 and up our summon-upkeep rule charges a summon's casting cost again every turn and dismisses summons it can't pay. The AI summons without counting that, so it can summon more than it can keep.

War of the Ring has 23 sites: 8 merchants, 6 mage towers, 6 mercenary camps, 3 trainers.

## Options

- **A. Summon upkeep awareness (~0.5 day).** Before casting a summon, add its upkeep to the upkeep already running; summon only while the total stays under a share of that mana's income (open question D). In spending mode the share is higher.
- **B. "Not for the AI" spells (~0.5-1 day).** Our AIs research and cast the 30 spells whose effect they already handle (boost, lower, restore movement, summon, heal, attack, invisibility); fog, unfog, remove rod and change terrain stay skipped until the AI has a use for them (question E).
- **C. Area spells (~1 day).** Attack and lower: the target tile that hits the most hostile army strength and no own or allied army. Heal and boost: the tile that covers the most own armies that need it. Fog, unfog and terrain area spells stay skipped (question E).
- **D. Site visits (~3-4 days).** Sites become move targets for ordinary armies of our AIs, scored with the GAI interests above and the travel time; on arrival the army buys:
  - mercenaries into free slots (best unit it can afford and lead),
  - training for its units at trainers (experience for gold),
  - spells at mage towers (gold; the spells then also use the spare mana),
  - items it can use at merchants (today: healing and revive potions; equipment after E).
- **E. Equipment (~2 days).** Leaders equip banners, artifacts, boots and tomes from the bag when the slot and ability allow it (`LeaderEquipmentCalculator`), best item per slot. Prerequisite for buying equipment at merchants.

## Testing

- Engine tests per behaviour with Classic as the control (Classic doesn't do it, our AI does), mutation-checked.
- War of the Ring bench (60 turns): counts of site visits and purchases by type, area and "not for the AI" casts, summons dismissed for upkeep, items equipped; mana and gold lost at the cap.
- Tournament against Classic, because these change strength, not only flavour: S100 (2 races) and S099 (4 races), plus one big-map comparison.
- Real-client check once something is visible (a mercenary joining an AI army, an equipped AI leader in the army's info).

## Phases (proposal)

1. ~~A: summon upkeep awareness (~0.5 day).~~ Done, see "Phase 1 — done".
2. ~~B: "not for the AI" spells (~0.5-1 day).~~ Done, see "Phase 2 — done".
3. ~~C: area spells (~1 day).~~ Done, see "Phase 3 — done".
4. ~~D: site visits: mercenaries, trainers, mage towers, potions (~3-4 days).~~ Done, see "Phase 4 — done".
5. ~~E: equipment, then equipment at merchants (~2 days).~~ Done, see "Phase 5 — done".

## Phase 1 — done (summon upkeep awareness)

Merged 96aa9792 (feature commit 32b1c99d on `upgrade/ai-summon-upkeep`).

- `WorldAiProfile.SummonUpkeepPercent` (0 = Classic, no check); every personality and designed AI gets `WorldAiSpending.DefaultSummonUpkeepPercent` 50.
- `WorldAiSpending.CanAffordSummonUpkeep`: on maps where summon upkeep applies (`LargeMapRules`), the new summon's casting cost plus the upkeep of the player's running summons must stay within the knob's share of that mana's income (`PlayerIncomeCalculator`), per mana the summon costs; a mana at or above the spending line may use up to 100 % (`RichSummonUpkeepPercent`, user option B). `CastSpellAction` drops summons that fail it before choosing.
- Tests: `WorldAiOverflowSpendingTests.SummonUpkeepLimitTest` (income 300 death mana: 2000 mana and nothing running -> summons; 2000 mana and 100 running -> no (50 %); 9000 mana and 100 running -> summons (100 %); 9000 and 250 running -> no; Classic always passes the check; at 9000 the Classic+ brain's first action matches). Mutation checks: check forced off -> the 50 % case fails; rich raise removed -> the 100 % case fails. Suite 5454. S099 Classic CSV identical apart from timings.
- Measured, War of the Ring (new map), 60 turns, seed 1, spending 70 %:
  - no limit (before): 343 summons, 7 summons dismissed for unpaid upkeep, 51,200 mana paid as upkeep, mana lost at the cap 6.9 %;
  - 50 % always: 268 summons, 0 dismissed, 20,500 upkeep, lost 9.5 % (Elf grove loss 1,051 -> 8,901: the upkeep had been a mana sink);
  - 50 %, 100 % when rich (merged): 272 summons, 0 dismissed, 47,900 upkeep, lost 6.8 %.
- No real-client run (no client change).

## Phase 2 — done ("not for the AI" spells)

Merged b91aeafa (feature commit 8973ba9b on `upgrade/ai-all-spells`).

- `WorldAiProfile.UseAllSpells` (false = Classic; true for every personality and designed AI). `ResearchSpellAction` then also takes spells marked `AI_CAT` None, but only of the effect types it already loops over (summon, attack, heal, weaken, restore movement; boost and invisibility only in spending mode, as before). Fog, unfog, remove rod and change terrain have no AI effect type and stay out (answer E). Casting never filtered on `AI_CAT`, so learned spells are cast by the existing code.
- `WorldAiSpending.GetNeededMana` (phase 2b of the overflow spec) counts these spells too, and only spells with a castable effect.
- Test: `WorldAiOverflowSpendingTests.ResearchNotForAiSpellTest` (turn 5, 2000 mana: Classic skips an attack spell marked None, Classic+ researches it). Mutation check: rule removed -> the test fails. Suite 5455. S099 Classic CSV identical apart from timings.
- Measured, War of the Ring (new map), 60 turns, seed 1, compared with phase 1's final run:
  - spells known at turn 60: 45 -> 61 (Elf 7 -> 16, Human 8 -> 11, Heretic 9 -> 12, Dwarf 9 -> 10, Undead 12 -> 12); "not for the AI" spells researched 16, cast 209 times (Elf 114, Human 68, Heretic 21, Dwarf 3, Undead 3);
  - mana lost at the cap 6.8 % -> 5.9 % (Elf 13 % -> 9 %);
  - total casts 877 -> 769 and summons 272 -> 177: the games went differently (the Heretics cast 231 -> 109 and lost 7 % -> 8 %), so read single-race numbers as one sample.
- No real-client run (no client change).

## Phase 3 — done (area spells)

Merged d73cb5fe (feature commit 7cd2eb8d on `upgrade/ai-area-spells`, then gallean-upgrade merged in twice because another session merged town income and move orders meanwhile).

- Engine fact that shaped it: an area spell hits every army in its square, own and allied ones too (`WorldAbstractUnitSpellResolver` hits the whole footprint, with less power per ring), and nothing inside cities; the centre may not be in a city.
- `CastSpellAction.FindAreaCast` (our AIs, `UseAllSpells`; runs after Classic's choices and before overflow spending): attack and weaken spells centred on a visible hostile army (the 30 nearest to own armies) whose square holds no own or allied army; heal on an own damaged army, boost and invisibility on an own army about to fight (outside a city, movement left, an enemy within 7 tiles), each with no hostile army in the square. Score = the ring power of every wanted army in the square; cast when it reaches 2 armies (`AreaMinStacks`), or 1 when the spell's mana is above the spending line. Fog, unfog and terrain area spells stay out (answer E).
- Tests: `AreaAttackOnTwoArmiesTest` (Classic never casts, Classic+ centres on one of two neutrals), `AreaAttackSparesOwnArmiesTest` (an own army in the square -> no cast; one neutral alone -> no cast), `AreaHealOnTwoWoundedArmiesTest`. Mutation checks: own-army check off -> the spare test fails; area casting off -> the other two fail. Suite 5473 after both merges. S099 Classic CSV identical apart from timings (against gallean-upgrade cfe773ef).
- Measured, War of the Ring (new map), 60 turns, seed 1: 37 area casts (Elf 19, Undead 10, Human 8); the Dwarves and Heretics learned no area spell in 60 turns (only 4 area spells were known in total, all races together). Mana lost 6.0 % (phase 2: 5.9 %, within noise).
- No real-client run (no client change).

## Phase 4 — done (site visits)

Merged 8d9c0957 (feature commit 12542e42 on `upgrade/ai-site-visits`; gallean-upgrade merged in twice because another session merged move orders phase 2 and town income phase 3 meanwhile).

- Built as one stack action instead of four steps, because the site types share the walking and only differ in the purchase: `WorldAiPlanner.VisitSiteAction` (priority 2, registered for Classic's list and designed AIs, acts only with the new profile knob `VisitSites`, true for every personality and designed AI).
- Who: armies on a Normal order whose leader is a fighter or a mage (rod planters, explorers and thieves keep their own jobs).
- Where (answer B): explored sites whose entrance it can reach within 2 turns of its full movement, plus 1 turn per 2,000 gold, at most 6; score = the attitude's interest in that site type (`GAI.json` I_MERCHANT/I_TOWERS/I_H_MERCS/I_TRAINER) x the inverse path cost, like rod targets. Only sites where it would buy something right now count.
- What, at the entrance, one purchase per decision: mercenaries = the most expensive unit that fits the army; trainers = the unit that gains the most experience (the engine trains as far as the gold goes, so it is only done when the whole cost fits); mage towers = a spell with an effect the AI can cast; merchants = healing or revive potions while the army carries fewer than 2 (answer C).
- Decision taken without asking (say if it should change): site purchases leave at least 500 gold (`VisitSiteAction.GoldKept`), except while gold spending, so shopping doesn't starve unit and leader purchases.
- Tests: `WorldAiSiteVisitTests` (5: mercenary, trainer and mage tower purchases at the entrance, walking toward a merchant 6 tiles away, the 500-gold rule), Classic as the control in each. Mutation checks: knob gate off -> all 5 fail; gold rule off -> 2 fail. Suite 5489 after both merges. S099 Classic CSV identical apart from timings (against gallean-upgrade b90dbb4e).
- Measured, War of the Ring (new map), 60 turns, seed 1, with and without site visits (other phases on in both):
  - Humans: 9 spells, 23 trainings, 2 potions for 15,965 gold; their mana lost at the cap fell from 7 % to 1 % (more spells to cast). Dwarves: 3 potions. Heretics: 4 spells. Elves and Undead: nothing.
  - No mercenaries were bought: the six mercenary camps lie 55 to 331 tiles from the nearest capital, mostly beyond the 6-turn limit, and armies that are already full can't take more units. Not verified per army.
- No real-client run (no client change).

## Phase 5 — done (equipment; longer walks to mercenaries and trainers)

Merged 2d25bf9c (feature commit d0105a8e on `upgrade/ai-equipment`; gallean-upgrade merged in once more for the other session's move-orders work).

- Equipment: `WorldAiPlanner.EquipItemAction` (priority 6, every order; acts only with the new knob `EquipItems`, true for every personality and designed AI). From the army's bag it equips banners, tomes, artifacts and boots into empty slots, most valuable first (value = the item's price in all resources), if the leader has the ability (`LeaderEquipmentCalculator.GetRequiredAbility`); when no free slot fits and the bag holds a better item, it takes the cheapest worse item off first (the equip command only fills empty slots). Battle slots (potions, orbs, talismans, scrolls, wands) stay untouched.
- Merchants: besides potions, `VisitSiteAction` buys the most valuable piece of equipment the leader can wear that fills an empty slot or beats what is worn, unless a better one already waits in the bag (answer C).
- Longer walks (user, after phase 4): mercenary camps and trainers may be 4 turns away when poor, growing with the square of gold's share of the cap up to 20 turns at 9999 (about 8 at 5,000, 11 at 7,000); other sites keep 2 turns + 1 per 2,000 gold, at most 6.
- Tests: `WorldAiEquipmentTests` (4: equips a banner from the bag, takes cheap boots off when better ones wait, ignores a sword without the weapon ability, buys boots at a merchant but not a sword it can't use and not boots no better than worn ones), `WorldAiSiteVisitTests.MercenaryWalkGrowsWithGoldTest` (a camp 40 tiles away: not at 2,000 gold, yes at 9,000). Mutation checks: equipping off -> 2 equipment tests fail; old limit for camps -> the 9,000 case fails. Suite 5501 after the merge. S099 Classic CSV identical apart from timings (against gallean-upgrade 13724c59).
- Measured, War of the Ring (new map), 60 turns, seed 1:
  - Training rose with the longer limits: Dwarves 10, Heretics 5, Humans 23 (before: Humans 23 only). Heretic mana lost 8 % -> 5 %.
  - Still no mercenaries: at turn 60 only 2 of the 30 fighting armies had room for another unit (the AI keeps them full from its garrisons), and mercenaries only join the visiting army.
  - No item was equipped: the runs with and without equipping were identical. At turn 60 the armies carried 8 equippable items in total, none usable by the leader carrying it (23 leaders had some equipment ability, but not the one their bag's item needs); no merchant sale happened either.
- So this phase is correct (tests) but rarely triggers on this map. Follow-ups, not built: mercenaries that replace the weakest unit of a full army; handing items to the army whose leader can wear them (or to the capital); preferring equipment abilities when leaders level up; battle-slot items (questions BG/BH of the battle AI designer).
- No real-client run (no client change).

## Open questions

- ~~**A.** All our AIs, Classic untouched, without a designer block at first? Recommendation: yes; designer blocks ("visit sites", "use area spells") can follow once the behaviour is measured.~~ Answered, see Decisions.
- ~~**B.** Site visits always, or only while rich? Recommendation: always when the army can afford something useful there, with the same detour limit as overflow spending (sites reached this turn or next, only armies on a Normal order), and further away while gold spending.~~ Answered, see Decisions.
- ~~**C.** Merchants: which items? Recommendation: only items the AI can use: potions now, equipment after phase 5; never quest items or items it can't equip.~~ Answered, see Decisions.
- ~~**D.** Summon upkeep limit? Recommendation: running upkeep at most 50 % of that mana's income per turn, up to 100 % in spending mode.~~ Answered, see Decisions.
- ~~**E.** Fog, unfog, remove rod and change terrain spells (single, area and "not for the AI")? Recommendation: later, they need new goals (hide armies, scout, break enemy land), not just targeting.~~ Answered, see Decisions.
- ~~**F.** Order? Recommendation: the phases above: the three spell phases are small and use what the AI already has; site visits are the big step; equipment last, together with buying equipment.~~ Answered, see Decisions.
