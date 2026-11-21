# AI designer: a mode for building your own AIs

gallean-upgrade only. Status: phase 1 done (engine: designed map AIs). Builds on `docs/ai-switching.md` (switchable AI, phases 1-5
done) and the world AI diagram in `docs/ai/`.

## Decisions

- **Its own mode**, like the map editor: a main-menu button "AI editor" opens a list of your AIs and the designer
  screen. It doesn't live in Settings or in the map editor.
- **Stages**: a designed AI is a list of stage cards. Each card has a **When** (conditions) and what the AI does while
  that stage is active. The first card whose When is true plays; the last card has no When (the default stage).
- **Stage conditions to start with**: from turn N · gold above N · cities owned below N · an enemy army near my
  capital · I lost a city (since my last turn). More can be added later (open question S).
- **Blocks can add new behaviour**, not only reorder and tune what Classic does: besides Classic's actions and
  thresholds, the palette has behaviour blocks that the Classic AI doesn't have (first set: open question T).
- **Separate AIs for the map and for battles**: a designed map AI and a designed battle AI are separate documents with
  their own designer, and they are picked separately (Map AI / Battle AI), like today.
- A designed AI shows up wherever an AI can be picked: the Settings drop-downs, the AI players panel (race screen and
  game menu) and the tournament tool.
- **Stage switching: only the first true stage plays** (open question U, taken as recommended because phase 1 needs it; reversible later by letting stages add up).
- **Priority tiers instead of a strict order** (phase 1): the economy and army slots are lists of *tiers*; blocks in one tier share a priority and compare scores, like Classic's attack / thief / rod / enter city / take bag. Without tiers a copy of Classic couldn't play like Classic. The designer screen will show a tier as blocks side by side in one row.
- A save or setting that names a designed AI you don't have plays Classic+ (the designed AI's file is not part of the
  save; see open question W for sharing).

## The problem today

- The AIs are fixed: Classic plus five personalities (`docs/ai-switching.md`, phase 5), which are the Classic code
  with other values (`WorldAiProfile`, `BattleAiProfile`). A player can pick one but can't shape one.
- They also play the same way all game: a personality can't be careful early and all-in late, or switch to defence
  when it loses a city.

## How the Classic AI is built (what the designer exposes)

From `src/Gallean.Engine/World/Ai` (diagram: `docs/ai/GalleanAI.png`). Every AI action, `WorldAiController`:

1. **Orders**: sets each army's order (`WorldAiOrderPlanner`): Heal below 80 % hit points (healed at 90 %),
   Reinforce below 80 % of leadership, Explore for rod and thief leaders, else Normal.
2. **Economy**: takes the best economy action. Priorities: cast spell 4 > research 3 > hire leader 2 > building 1
   (max 1 per turn) > unit 0 (max 1 per turn). Saving up: it may be short by 400 gold for a leader.
3. **Armies**: the best action per army. Priorities: use item 6 > resurrect 5 > heal 4 > auto-fit 3 >
   attack / thief / rod / enter city / take bag 2 > move 1 > change order 0. Equal priorities compare scores.
4. Attacks need a power advantage set by the difficulty (`AiAttackPowerPercent*`).

Battles (`src/Gallean.Engine/Battle/Ai`): per unit, the best of retreat / heal item / attack / defend (all
priority 3, compared by score), else skip. A leader thinks about fleeing when its side holds less than 25 % of the
power.

Everything in this list is already a knob (`WorldAiProfile`, `BattleAiProfile`, action priorities in
`WorldAiPlanner.CreateStackActions/CreateEconomyActions`), or becomes one in phase 1.

## What the user sees

### Main menu
A new button **AI editor** under Map editor. It opens the AI list.

### AI list
Like the map editor's map list: two tabs, **Map AIs** and **Battle AIs**. Rows show the name, the number of stages and
when it was changed. Buttons: New (starts from a copy of Classic, so it plays from the first second), Edit, Duplicate,
Rename, Delete, Back. Built-in AIs (Classic, Classic+, the personalities) are listed greyed out; Duplicate on one of
them starts a designed AI from it.

### Map AI designer
Full screen, three columns, same look and drag-and-drop as the map editor's event screen
(`MapEditorEventScreenView`):

- **Left: block palette**, grouped by colour, groups fold:
  - Blue – stage conditions: from turn N, gold above N, cities owned below N, enemy army near my capital (N tiles),
    I lost a city.
  - Gold – economy blocks: cast spell, research, hire leader, building, unit. Their order in a stage is their priority.
  - Red – army blocks: use item, resurrect, heal, auto-fit, attack, thief, rod, enter city, take bag, move.
  - Green – order rules: heal below N % (healed at N %), reinforce below N %, explore with rod/thief leaders.
  - Purple – tuning: attack advantage N %, save up to N gold for a leader, wait for a free capital entrance.
  - Orange – new behaviour (open question T), e.g. keep N armies home, gather before attacking, rush the nearest
    enemy capital, keep N gold in reserve, prefer leader type X.
- **Middle: stage cards**, top to bottom in the order they're checked. A card: a hat **When** with condition blocks
  joined by "and" (an "or" block starts another group, like the events editor), then three slots: **Economy order**,
  **Army priorities** and **Rules** (order rules, tuning and behaviour blocks). Drag blocks in from the palette, drag
  to reorder (the order is the priority), drag back to the palette or press Delete to remove. Drag a card's header to
  move the whole stage. "+ Stage" adds a card above the default stage. Number fields are boxes in the block, like the
  event blocks.
- **Right: help and test.** The selected block's description and what Classic does there. Under it **Test**: pick a
  map and an opponent (default Classic), number of games and turns, Run; it plays them in the background
  (the tournament runner) and shows the summary table: wins, leads, cities at the end, first capture, AI time per
  turn.
- Top bar: name (edited in place), Save, Undo/Redo, Back (asks to save changes). A red mark on a stage that can never
  be reached (a stage under one with no When) or a block missing a value; Save still works, and the broken part is
  skipped in the game, like broken events.

### Battle AI designer
Same screen, other palette: conditions (round N, our power share below N %, our leader wounded below N %, enemy has a
healer); unit rules: flee below N % power share, never flee, defend when …, target choice (weakest, healer first,
highest damage, the leader); new behaviour (protect the leader, focus one target until it dies). Stages work the same
way.

## Technical approach

### Engine
- **Document**: `DesignedWorldAi` / `DesignedBattleAi` records: name, ID (`custom:<file name>`), list of stages; a
  stage = conditions (OR of AND groups) + economy order + army priorities + rules. Unknown block kinds are skipped
  (newer files in older builds).
- **Brain**: `DesignedWorldAiBrain` (an `IWorldAiBrain`). On every decision it finds the active stage (first true
  When) and builds a `WorldAiProfile` from it: priorities from the slot order, thresholds and tuning from the rules.
  The Classic controller then plays with that profile, so everything Classic knows is reused. Conditions are cheap
  checks on `WorldState` (turn, resources, owned cities, enemy stacks within N tiles of the capital, a lost-city flag
  kept in `WorldAiMemory`).
- **Profile gets complete**: every Classic action's priority becomes a profile field (today only building, unit and
  rod are), and an action can be switched off (a block left out of a stage = never done in that stage). Classic's
  defaults stay the original constants (guarded by `AiPersonalitiesTests`).
- **New behaviour blocks** are new action classes next to Classic's (`WorldAiPlanner.*Action`), each switched on by
  its block with its parameters, each with its own engine tests, and each measured with the tournament before it
  ships.
- **Registry**: `AiBrainRegistry` gets a second source: designed AIs loaded from files at startup and after each save
  in the designer (listed after the built-ins). Missing `custom:` IDs resolve to Classic+.
- **Battle**: `DesignedBattleAiBrain` builds a `BattleAiProfile` per decision; target choice and the new battle
  behaviours are new scorers next to `BattleAiPlanner.AttackAction`.

### Files
- `~/.local/share/GalleanUpgrade/AIs/map/<name>.json` and `.../AIs/battle/<name>.json`, written with
  `Utf8JsonWriter`, read with `JsonDocument` (the client is AOT: no reflection JSON; see the skill notes). A format
  version in each file.
- The tournament tool gets `--ais <dir>` and `--ai custom:<name>`.

### Client
- `GameViewModel.ShowAiEditorCommand`, `AiEditorListScreenViewModel`, `AiDesignerScreenViewModel` (+ views),
  block view models reusing the event editor's drag/drop and field controls (`MapEditorEventScreenView` pattern:
  palette press-and-drag, drop targets, Delete). New strings in all 7 catalogs.
- Test panel: the tournament runner moves from `tools/AiTournament` into a small shared engine class, so the tool and
  the designer use the same code; runs on `Task.Run` with a cancel button.

## Testing

- Engine: document round trip (write, read, same); stage choice (first true When wins, default stage, OR/AND
  groups); each condition on a built state; a designed AI copied from Classic plays exactly like Classic (same
  tournament CSV); a stage with an action left out never does it; each new behaviour block with its own scenario
  test; missing designed AI -> Classic+.
- Client: list screen (new/duplicate/rename/delete on a temp folder), designer view model (add/reorder/remove blocks
  and stages, undo, save), the Settings drop-down and AI players panel list designed AIs; visual tests of the list and
  the designer (en + ru, empty and full stages) reviewed for clipping at 1366x768.
- Real client (AOT): build an AI with two stages in the designer, save, pick it in Settings, play a few turns; run
  Test in the designer.

## Phases (rough sizes)

1. ~~Engine: designed map AI document + files, stage conditions (the five), complete profile (all priorities, switch
   off), `DesignedWorldAiBrain`, registry source, tournament `--ais`. ~2–3 days.~~ Done.
2. Client: AI editor mode, main-menu button, AI list screen. ~1 day.
3. Client: map AI designer screen (palette, stage cards, drag and drop, fields, save/undo, broken-block marks).
   ~4 days.
4. Test panel in the designer (shared tournament runner). ~1.5 days.
5. New behaviour blocks, first set (open question T), each with tests and a tournament check. ~0.5–1 day each.
6. Battle AI designer: engine (document, conditions, rules, target choice) + screen. ~4 days.
7. Maps can carry designed AIs (replaces `ai-switching.md` phase 7: the map editor's AI block can pick a designed AI
   and the map file stores a copy). ~1.5 days.

The lookahead AI (`ai-switching.md` phase 6) can later become a block ("think ahead") in this designer.

## Open questions

- **R.** Main menu: the AI editor button under Map editor. Check the menu still fits at 1366x768
  (else Map editor and AI editor share a row). Recommendation: under Map editor, checked with the main-menu visual
  test.
- **S.** More stage conditions after the first five? Candidates: army count below N, enemy owns more cities than me,
  an ally asks for help, my capital under attack. Recommendation: none until the first five are in use.
- **T.** First set of new behaviour blocks. Candidates: keep N armies home (guard the capital), gather before
  attacking (wait until N armies stand together), rush the nearest enemy capital, keep N gold in reserve, prefer
  leader type X, raid with thieves, patrol own cities. Recommendation: the first four, as one phase.
- ~~**U.** Stage switching~~ -> first true stage only (phase 1). Old text: Stage switching: only the first true stage plays, or can several be active (later cards adding to earlier
  ones)? Recommendation: first true stage only; easier to read.
- **V.** Should the AI see a stage change (e.g. a message "the Undead change their strategy")? Recommendation: no
  message in the game; the designer's Test shows how often each stage was active.
- **W.** Sharing designed AIs: export/import a file (like leader export), and/or a map carries a copy (phase 7).
  Recommendation: both; export/import in phase 2's list screen, maps in phase 7.
- **X.** When Lookahead is decided (`ai-switching.md` questions H–K), does it become a block here instead of a
  separate AI? Recommendation: yes, a "think ahead" rule block with a time budget.

## Phase 1 — done

Commit f9a1bff8, merged in 7bbcb0bb.

- Model (`src/Gallean.Engine/Ai/Designer/`): `DesignedWorldAi` (ID `custom:<file name>`, name, stages), `DesignedWorldStage` (name; When = OR of AND groups, empty = always; `Economy` and `Army` as priority tiers; `DesignedWorldRules`: hurt %, healed %, reinforce %, attack power %, leader savings gold, wait for a free capital, null = Classic's value), `DesignedStageCondition` (FromTurn, GoldAbove, CitiesBelow, EnemyNearCapital(tiles), LostCity), `WorldAiActionKind` (5 economy + 10 army actions; change order always runs at 0). `DesignedWorldAi.FromClassic` = one default stage with Classic's order.
- Engine: `WorldAiProfile.Priorities` (when set, every action's priority comes from it and missing actions are never done; null = Classic's constants, so Classic and the personalities are unchanged). `DesignedStageEvaluator` finds the active stage (first true When; none -> Classic) and turns it into a profile (economy tiers end at 0, army tiers at 1). `DesignedWorldAiBrain` plays it through the Classic controller. "I lost a city": `WorldAiMemory.OwnedCities` + `LostCityTurn` (init props, saved; the brain compares at the first action of a turn). "Enemy near capital" counts hostile armies on explored tiles within N tiles of the capital entrance (same fog as Classic).
- Files: `DesignedWorldAiStore` (one JSON file per AI, `Utf8JsonWriter`/`JsonDocument` for AOT, temp file + move on save, broken files skipped, unknown condition/action kinds skipped). Registry: `AiBrainRegistry.SetDesignedWorldAis` adds them after the built-ins in `WorldBrains`; a missing `custom:` ID resolves to Classic+.
- Tournament: `--ais <dir>`, `--ai custom:<name>`, `--new-ai <dir> <name>` (writes a Classic copy to edit by hand); example `tools/AiTournament/examples/turtle-then-strike.json` (3 stages: take a lost city back / from turn 20 strike / build up).
- Tests: `DesignedWorldAiTests` (12: file round trip, unknown kinds skipped, store save/list/delete with a broken file, Classic stage = Classic's priorities, rules -> profile, Classic copy chooses the same commands and scores as Classic for 5 actions, a left-out action is never done, each condition, first true stage wins / no stage -> null, lost city noticed at turn start, memory survives a save, registry incl. missing -> Classic+). Suite 5341.
- Tournament checks: S099, 8 games x 50 turns: Classic before phase 1, Classic after, and `custom:classic-copy` give the same results (CSV identical apart from the AI name columns). `turtle-then-strike` vs Classic: S100 12 games 3 wins / 2.4 cities (Classic 5 / 2.6); S099 6 games first capture turn 26 (28.6). It plays and differs; it isn't tuned.
- Not done here (by plan): no client yet (phase 2: AI editor mode and list), so designed AIs are only reachable through the tournament tool for now; no real-client run in this phase.
