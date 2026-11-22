# AI overflow spending: never sit at the 9999 cap

Status: spec answered (all four recommendations taken, 2026-11-22); phase 1 done (diagnosis), phase 2 next.

## Decisions

- **User rule (2026-11-22):** every one of our world AIs, however different, must never reach 9999 mana or gold. When an amount gets close to the cap, the AI starts spending, and keeps spending until it is back below.
- It applies to every personality (Classic+, Aggressive, Builder, Expansionist) and to every designed AI (AI designer), as a shared rule on top of their own knobs, not as a separate personality.
- Classic stays identical to upstream (answer A): Classic+ gets the rule, so Classic+ is the original AI that spends.
- Spending starts at 80 % of the cap = 8000 (answer B), editable per designed AI.
- Only useful casts (answer C); a summon next to its own armies counts as useful. Mana that nothing useful can use is still lost.
- Gold sinks are units, buildings and leaders (answer D); merchant items and mage tower spells only if the bench still shows gold at the cap after phase 3.

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
2. Mana spending in the engine for all personalities and designed AIs (~1 day), mana bench + tournament.
3. Gold spending (~1 day), same checks.
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

## Open questions

- ~~**A.** Classic too? Recommendation: no. Classic stays upstream's AI; Classic+ is "Classic with our fixes" and gets the spending rule, so picking Classic+ gives the original AI that spends.~~ Answered: recommendation taken.
- ~~**B.** Threshold: 80 % of the cap (8000)? Recommendation: 80 %, editable per designed AI.~~ Answered: recommendation taken.
- ~~**C.** When nothing useful can be cast (no enemy in sight, nobody wounded): accept the loss, or cast anything castable? Recommendation: useful casts only; a summon next to its own armies counts as useful.~~ Answered: recommendation taken.
- ~~**D.** Gold sinks beyond units, buildings and leaders (merchant items, mage tower spells)? Recommendation: later, only if the bench still shows gold at the cap after phase 3.~~ Answered: recommendation taken.
