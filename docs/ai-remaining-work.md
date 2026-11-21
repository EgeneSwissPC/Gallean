# AI work: what is done and what remains

gallean-upgrade only. Handoff document, written so the next session can start from here with a fresh context.
Specs it summarises: `docs/ai-switching.md` (switchable AI, phases 1–5 done) and `docs/ai-designer.md` (AI designer,
phases 1–5 done). The world AI diagram is `docs/ai/GalleanAI.png` (Classic only; not updated for any of this).

User decision (2026-11-21): **the battle AI designer is a separate project**, very different from the map AI designer.
It is no longer "phase 6 of the AI designer"; it gets its own spec (section 2 below is the starting point for it).

---

## 1. Where things stand

### What a player can do today
- **Settings → Game**: Map AI and Battle AI drop-downs (global, not saved in games).
- **AI players panel** (race screen button, game menu): give single computer players, neutrals included, their own
  map and battle AI. Saved with the game (`WorldPlayer.AiOverride`). Changing the global AI asks whether to switch
  players that have their own.
- **Built-in AIs**: map: Classic, Classic+, Aggressive, Builder, Expansionist; battle: Classic, Cautious, Ruthless.
- **AI editor** (main menu, own mode): list of the player's map AIs; New (start from any AI), Copy, Delete, Edit.
- **Map AI designer**: stage cards (When = OR of AND condition sets; economy and army priority rows; rules and
  tuning; new behaviour blocks), drag and drop, undo/redo, problems, Save; **Test** panel (quick AI-only games of
  the on-screen AI against another, with the share of turns per stage).
- **Tournament tool** `tools/AiTournament` (headless AI-only games, `--ais`, `--new-ai`, CSV, `--parallel`).

### Code map
| Area | Files |
| --- | --- |
| Brain registry, IDs, fallback | `src/Gallean.Engine/Ai/AiBrainRegistry.cs`, `AiBrainInfo.cs`, `AiSelection.cs`, `AiPersonalities.cs` |
| World AI brains | `World/Ai/Brains/` (`ClassicWorldAiBrain`, `ProfileWorldAiBrain`), `World/Ai/WorldAiProfile.cs`, `WorldAiContext.Profile` |
| Battle AI brains | `Battle/Ai/Brains/` (`ClassicBattleAiBrain`, `ProfileBattleAiBrain`), `Battle/Ai/BattleAiProfile.cs` |
| Resolution (own AI → settings → Classic) | `GameSession.Ai.cs` (`GetWorldAiId`, `GetBattleAiOverride`, `GetBattleAiId`), `BattleSession.AttackerOwnerId/DefenderOwnerId` |
| Designed map AIs | `Ai/Designer/` (`DesignedWorldAi`, `DesignedWorldStage`, `DesignedWorldRules`, `DesignedStageCondition`, `WorldAiActionKind`, `DesignedStageEvaluator`, `DesignedWorldAiBrain`, `DesignedWorldAiStore`) |
| New behaviours | `World/Ai/WorldAiPlanner.Behaviours.cs` (applied after Classic's choice in `WorldAiPlanner.DecideStackAction`; gold reserve in `DecideEconomyAction`) |
| Tournament | `Ai/Tournament/AiTournamentRunner.cs` (shared by the tool and the Test panel) |
| Client | `Screens/AiPlayers/`, `Screens/AiEditor/` (`AiEditorMenu*`, `AiDesigner*`, `DesignedAiLibrary`), `Screens/Settings/AiBrainFormatter.cs`, `Screens/Battle/BattleScreenViewModel.AiBrains.cs`, `Game/GameViewModel.AiEditor.cs` |
| Files on disk | `~/.local/share/GalleanUpgrade/AIs/map/<name>.json` (`AppDirectories.MapAis`), ID `custom:<name>` |

### Rules every change keeps
- **Classic must not change.** Proof after any AI change: `tools/AiTournament` S099, 8 games × 50 turns,
  `--parallel 8 --csv`, compare `cut -d, -f1-5,8-13` with the previous CSV (identical). Guard tests:
  `AiPersonalitiesTests.ClassicProfileKeepsOriginalValuesTest`, `DesignedWorldAiTests.ClassicCopyPlaysLikeClassicTest`.
- New knobs default to "off"/the original constant; a personality or design is a profile, never an edit of the Classic code path.
- A missing or unknown AI ID never crashes: built-in unknown → Classic, missing `custom:` → Classic+.
- Fixtures that read or change the designed AIs in the static registry are `[NonParallelizable]`.
- Client JSON: `Utf8JsonWriter`/`JsonDocument` only (the client is native AOT).
- Translations: `add_translations.py` overwrites existing keys; after running it, `git diff ru.json | grep '^-'` must show no removed lines.
- Per phase: feature commit → full non-visual suite (**5409** after battle sandbox phase 5: Run N) → visual tests of the touched
  screens (en + ru, reviewed for clipping at 1366x768) → AOT publish + real-client click-through → merge `--no-ff` →
  `Docs:` commit with a "Phase N — done" section.
- `gallean-upgrade` is checked out in the worktree `~/work/Gallean-spellcast` (another session's); topic branches are
  built in `~/work/Gallean` and merged in that worktree when it is clean and idle.

---

## 2. Battle AI designer (separate project, phase 1 battle bench done)

Spec: `docs/battle-ai-designer.md` (phases, open questions BA–BF with recommendations). Phase 1 (battle bench, `tools/AiTournament --battle`) and 1b (leader items and upgrades, city/capital defence, garrisons, neutral sides) are done; sandbox questions answered, phase 2 (generated host map, difficulty, margin of error) done; phases 3 (setup screen) , 4 (items, upgrades) and 5 (Run N) done; next: phase 6 Watch battle (old questions SA–SF).

### Why it is different from the map AI designer
- **Time scale**: decisions are per unit per battle turn, a battle lasts a few rounds; there are no turns of the game,
  no economy, no memory between battles. "Stages" would be about the state of one battle (round, who is winning, who
  is hurt), not about the game.
- **What it chooses**: an action for one unit (attack which target with which attack, heal whom, defend, flee, use an
  item, skip), not priorities between economy and armies.
- **How to measure it**: whole AI-only games say little about battle AI (few battles, mixed with everything else).
  It needs its own bench: the same battle (two given armies) fought many times with different seeds, reporting
  wins, survivors, rounds, retreats.
- **Where it plays**: every battle where an AI side fights (`BattleAiActionCommand` with `BrainId` per side), auto-
  resolve for AI sides (`BattleAutoResolveCommand.AttackerAiId/DefenderAiId`), AI-vs-AI battles (`GameSession`).
  The player's own Auto-play / Instant resolve / Heal and defend / Refuse to flee stay Classic. The world AI's battle
  predictions (`WorldAiEstimator`) stay Classic.

### How the Classic battle AI decides today (`src/Gallean.Engine/Battle/Ai`, ~730 lines)
- `BattleAiPlanner.DecideAction(state)`: for the current unit, every action makes a plan with a score; the highest
  score wins (all actions have priority 3 except Skip, priority 1).
  - **Attack** (`AttackAction` + `AttackAction.Scorer`): for each possible target of each attack, a utility score
    from considerations that depend on the attack type: damage/drain → Weakness, Lethal, Unwarded, Undefended;
    poison/frost/blister/reduce-* → Weakness, Unwarded; paralyze/petrify/fear → Power, Unwarded, Controllable
    (+ Transformable for transform); heal → Wounded; cure → Afflicted; revive → Fallen; protection → Protectable;
    increase damage → Boostable; summon → none. Targets come from `BattleAiTargetProvider` (fixed-random attack info).
  - **Retreat** (leaders only, not neutral sides, needs a living ally): score high when the side holds less than
    25 % of the power (`BattleAiProfile.RetreatPowerShare`, the curve's XShift).
  - **Heal item** (only when items are allowed): potion on a wounded ally.
  - **Defend**: score 0, so it is the fallback when nothing scores.
  - **Skip**: priority 1, last resort.
- Auto-resolve (`BattleEngine.Rules.AutoResolveStep`) uses the planner without items; after round 15 the attacker
  retreats, after round 30 the defender defends, hard stop at 45 (90 with "refuse to flee").
- Existing personality knobs (`BattleAiProfile`): retreat power share, can retreat, can defend (Cautious = 40 % share;
  Ruthless = no retreat, no defend).

### Proposal (starting point for its own spec)
1. **Mode**: open question BA (a second tab "Battle AIs" in the AI editor list, or its own main-menu mode).
2. **Document**: `DesignedBattleAi` (ID `custom-battle:<name>`, name, stages); files in `AIs/battle/`.
3. **Stage conditions** (per decision, from the battle state): from round N; our power share below/above N %;
   our leader below N % hit points; we have fewer than N units standing; the enemy has a healer / a mage / a
   paralyser; we are the attacker / the defender; fighting a neutral / a player.
4. **Per-stage content**:
   - **Action rows** (priorities like the map designer's rows): attack, heal ally, use item, defend, flee, skip.
     Leaving one out = never in that stage (e.g. no flee).
   - **Target preference rows** (new): weakest first, lethal (can kill this turn) first, healers first, casters
     first, the leader first, the hardest hitter first, the most wounded ally first (heals). Implemented as extra
     considerations/multipliers in `AttackAction.Scorer`, so Classic's own scoring stays the base.
   - **Rules**: flee below N % power share (the existing knob), heal allies below N % hit points, use potions below
     N %.
   - **New behaviour blocks**: protect the leader (units that can protect or heal prefer the leader; the leader
     defends when hurt), focus fire (keep attacking the same target until it falls), finish the wounded.
5. **Engine**: `BattleAiProfile` grows the knobs above (defaults = Classic); `DesignedBattleAiBrain` finds the
   stage and builds the profile per decision (same pattern as `DesignedWorldAiBrain`); registry
   `SetDesignedBattleAis`; missing `custom-battle:` → Classic.
6. **Bench**: `AiBattleBench` in the engine: two armies (from a scenario's stacks, a custom map, or a JSON list of
   unit IDs and levels), N seeds, contender A vs B on both sides (swap attacker/defender), report wins, average
   survivors and their hit points, rounds, retreats. Used by a `tools/AiTournament --battle` mode and by the
   battle designer's Test panel.
7. **Client**: battle designer screen reusing the map designer's layout and drag and drop (palette | stage cards |
   help + Test); Battle AI drop-downs and the AI players panel list designed battle AIs.

### Testing (same gates as the map designer)
- Engine: a design copied from Classic plays exactly like Classic (same commands for a battle, same bench results);
  each condition; each target preference changes the chosen target in a built battle (with a control); each
  behaviour block with a control; file round trip; missing ID → Classic.
- Bench: same seed = same result; swapping sides is part of every run.
- Client: view-model tests, visual tests en + ru, real-client battle with a designed battle AI on the AI side.

### Open questions for its spec
- **BA.** Mode: a tab in the AI editor, or a separate main-menu mode? (User leaned towards "totally separate".)
- **BB.** Are stages useful in battles, or is one card with rules enough at first?
- **BC.** Which target preferences and behaviours first?
- **BD.** Should a designed battle AI be allowed for the player's own Auto-play / Instant resolve too (a setting),
  or only for AI sides as today?
- **BE.** Bench armies: from saved games, scenario armies, or a small army editor in the battle designer?
- **BF.** Size estimate: engine + bench ~3 days, screen ~3 days, Test panel ~1 day (to confirm after BA–BE).

---

## 3. Maps carry designed AIs (`docs/ai-designer.md` phase 7, ~1.5 days)

Replaces `docs/ai-switching.md` phase 7 (per-race AI in the map editor), which is not built.
- **Editor**: an AI block in the economy panel: a race picker (every race of the map + Neutrals), Map AI and Battle AI
  drop-downs ("Settings choice" first, then built-in and designed AIs).
- **Map file**: a new per-scenario table (planned `SAi.json`, `Db.ScenarioAi`: race, world AI ID, battle AI ID) plus a
  **copy of every designed AI the map uses** (so the map works on another player's computer); registered like
  `SRules.json` (`DbJsonSerializerContext`; `DatabaseTests` lists it as empty for original scenarios).
- **Game**: a computer player's `AiOverride` = race-screen choice if set there, else the map's; the race screen's AI
  players panel opens with the map's choices filled in (`GameScenario` needs them). Designed AIs from the map are
  registered for that game; a local AI with the same ID keeps priority, or the map's copy gets a map-scoped ID
  (decide in the spec).
- **MapFromPlan**: an `ai` section in plans (race → map/battle AI) (open question M, recommended yes).
- Tests: editor round trip, loader (map choice reaches the game, race screen wins, human race ignores it, neutrals
  get it), map carrying a designed AI plays on a machine without that file (temp AIs folder), visual test of the AI
  block (en + ru), real client: set an AI per race in the editor, Save and play, open the AI players panel.
- Open questions recorded in `ai-switching.md`: L (mark map-set choices on the race screen; recommended no), M.

---

## 4. Lookahead AI (`docs/ai-switching.md` phase 6, waiting for answers)

- Options: **A** re-rank Classic's top moves by judging the resulting position (~1 week); **B** plan the whole own
  turn (~2–3 weeks); **C** B + the enemy's reply (~4–6 weeks).
- Unanswered questions: **H** which option first (recommended A, as its own AI "Lookahead"); **I** time budget per AI
  turn (recommended 1 s, falls back to Classic+'s move); **J** own battle AI or pair with existing (recommended pair);
  **K** same fog as Classic (recommended yes).
- **X** (`ai-designer.md`): make it a "think ahead" block in the map designer instead of a separate AI (recommended).
- Needs a position evaluator (cities and capitals held, army power and exposure, income, threatened cities) and a
  time budget measured on S099 and the 1024 War of the Ring map.

---

## 5. Improvements parked by the user

### "Gather before attacking" (A–E; user: "think about those later")
How it works today (`WorldAiPlanner.ApplyBehaviours`), for every decision about one fighting army (rod planters,
thieves, summons and illusions are ignored and don't count):
1. Classic picks the army's best action as usual.
2. The block counts the player's fighting armies within 4 tiles of this army, itself included (Chebyshev distance:
   a 9×9 square, diagonals count as one step).
3. Count ≥ N: nothing changes; attacks still need the usual power advantage.
4. Count < N: an attack (enemy armies, cities, capitals, ruins, neutrals) is vetoed; every other action (heal, items,
   fill up, enter own city, rods, bags) is left alone.
5. If the army is then idle (attack vetoed, or Classic only had a plain move/explore), it walks towards the nearest
   other fighting army more than 4 tiles away, with Classic's `FindMoveTowardCell` (never walks into a battle). No
   such friend → Classic's move or nothing.
- No fixed rally point (two armies walk towards each other); once gathered each attacks on its own (no merging, no
  group attack); being attacked is unaffected. Home guards ("Keep armies home") are exempt; "Rush the enemy capital"
  only applies to gathered armies.
- Weakness: with fewer than N fighting armies in total nobody attacks, even next to its own capital (the main reason
  it tested weak: S100 0 wins / 2.0 cities vs Classic 5 / 3.0).
- **A.** Never wait for more armies than the player has. **B.** Let attacks near the AI's own cities through.
  **C.** Only hold back attacks on strong targets (enemy armies, cities). **D.** A fixed rally point (capital or
  strongest army). **E.** Make the 4-tile radius a number in the block. Recommended: A + B.
- The same self-blocking applies to "Keep armies home" with few armies (all stay home); a similar fix
  ("keep N home only while the player has more than N armies") is worth deciding together.

### Open questions in `ai-designer.md`
- **S.** More stage conditions (army count below N, enemy owns more cities, an ally asks for help, my capital under
  attack). Recommended: none until the first five are in use.
- **W.** Sharing designed AIs: export/import a file (like leader export), and maps carrying them (section 3).
- **Y.** Default Test map: today the first in the list (5th Column, where nobody takes a city). Recommended: remember
  the last map used, in the AI's file (an optional field).

### Known limits recorded in the phase notes
- Designer: stages and rows reorder with ↑/↓ buttons, not by dragging their headers; no keyboard way to move blocks.
- Test panel: both sides use the Classic battle AI; the default map shows little (Y).
- Behaviours: home and gather radii fixed at 4 tiles; "fighting army" counts weak leaders too.
- Personalities: Aggressive and Builder are weaker than Classic on the tested maps; Classic+, Cautious, Ruthless
  are the stronger picks (`ai-switching.md` phase 5 numbers).
- The world AI diagram (`docs/ai/`) shows only Classic; updating it for brains, profiles, stages and behaviours is
  not done.

---

## 6. Suggested order

1. Spec for the battle AI designer (answer BA–BF), then build it in phases.
2. Maps carry designed AIs (section 3).
3. Gather/keep-home fixes (section 5), each checked with the tournament.
4. Lookahead once H–K are answered.
5. Small items: Y (remember the Test map), W (export/import), diagram update.
