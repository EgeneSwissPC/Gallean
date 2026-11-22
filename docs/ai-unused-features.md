# AI: the parts of the game it never uses

Status: spec, nothing built yet. Open questions A-F at the end.

## Decisions

- **User priority (2026-11-22):** this comes before lookahead, objectives and maps carrying AIs: "the whole parts of the game go unused ... real players have a huge advantage over them".
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

1. A: summon upkeep awareness (~0.5 day). Independent of every question except D.
2. B: "not for the AI" spells (~0.5-1 day).
3. C: area spells (~1 day).
4. D: site visits: mercenaries, trainers, mage towers, potions (~3-4 days).
5. E: equipment, then equipment at merchants (~2 days).

## Open questions

- **A.** All our AIs, Classic untouched, without a designer block at first? Recommendation: yes; designer blocks ("visit sites", "use area spells") can follow once the behaviour is measured.
- **B.** Site visits always, or only while rich? Recommendation: always when the army can afford something useful there, with the same detour limit as overflow spending (sites reached this turn or next, only armies on a Normal order), and further away while gold spending.
- **C.** Merchants: which items? Recommendation: only items the AI can use: potions now, equipment after phase 5; never quest items or items it can't equip.
- **D.** Summon upkeep limit? Recommendation: running upkeep at most 50 % of that mana's income per turn, up to 100 % in spending mode.
- **E.** Fog, unfog, remove rod and change terrain spells (single, area and "not for the AI")? Recommendation: later, they need new goals (hide armies, scout, break enemy land), not just targeting.
- **F.** Order? Recommendation: the phases above: the three spell phases are small and use what the AI already has; site visits are the big step; equipment last, together with buying equipment.
