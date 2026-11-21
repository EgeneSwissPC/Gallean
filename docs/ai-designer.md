# AI designer: a mode for building your own AIs

gallean-upgrade only. Status: phases 1-5 done; phase 6 moved to its own project; phase 7 remains (see `docs/ai-remaining-work.md`) (engine; AI editor mode and list; the designer screen; the Test panel; the first new behaviour blocks). Builds on `docs/ai-switching.md` (switchable AI, phases 1-5
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
- **Phase 2 list screen** (taken while building, reversible): one list of the player's map AIs (no Battle AIs tab until battle designs exist, phase 6); built-in AIs aren't rows but the **Start from** choices of New (Classic, Classic+, the personalities and the player's own AIs); **Rename** moves to the designer's top bar (phase 3); **Copy** instead of "Duplicate" (the longer label was cut off in the 4-button row); **Edit** is shown but disabled with a tooltip until the designer exists. Export/import (question W) is not in this phase.
- **Designer screen choices (phase 3, reversible)**: the designer is its own full screen (not a popup over the list); a priority row is a horizontal strip of blocks, and a new lowest row is a separate drop strip under the rows; whole rows and whole stages move with ↑/↓ buttons instead of dragging their headers (dragging is for blocks only, so it never competes with text boxes); the last card is always the default stage (no When); a design whose last stage has conditions gets an empty default stage after it when opened; an action appears once per stage (dropping it elsewhere moves it; clicking it in the palette when it's already there only shows a hint); rule and tuning blocks appear once each per stage; numbers are clamped to their range as you type, so there are no "missing value" blocks; hovering a palette block shows its help on the right. Edit and Create both open the designer; Back asks before leaving with unsaved changes.
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
2. ~~Client: AI editor mode, main-menu button, AI list screen. ~1 day.~~ Done.
3. ~~Client: map AI designer screen (palette, stage cards, drag and drop, fields, save/undo, broken-block marks).
   ~4 days.~~ Done.
4. ~~Test panel in the designer (shared tournament runner). ~1.5 days.~~ Done.
5. ~~New behaviour blocks, first set (open question T), each with tests and a tournament check. ~0.5–1 day each.~~ Done (the four recommended ones).
6. ~~Battle AI designer~~ -> moved out: user decision (2026-11-21) that the battle AI designer is a separate project; starting point in `docs/ai-remaining-work.md` section 2.
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
- ~~**T.** First behaviour blocks~~ -> keep N armies home, gather N before attacking, rush the nearest enemy capital, keep N gold in reserve (phase 5, the recommended set).
- ~~**V.** Stage-change message~~ -> no message in the game; the designer's Test shows the share of turns per stage (phase 4, as recommended).
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

## Phase 2 — done

Commit 80f0a303, merged in e6fbd393.

- Engine: `DesignedWorldAi.FromBuiltIn(sourceId, fileName, name)` starts a design from any AI: Classic -> the Classic stage; a personality -> one stage from its profile (`DesignedStageEvaluator.FromProfile`: priorities grouped into tiers, rules only where they differ from Classic); a designed AI -> its stages. `GetEffectivePriorities(profile)`.
- Client: `AppDirectories.MapAis` (`~/.local/share/GalleanUpgrade/AIs/map`); `DesignedAiLibrary` (folder, `Reload()` into the registry, `MakeFileName`: lower-case letters/digits/dashes, unique, "ai" for names with no Latin letters); designed AIs are loaded at app start, so settings and saves that name one find it. `GameViewModel.ShowAiEditorCommand`; main-menu button **AI editor** under Map editor (the menu still fits at 1366x768). `AiEditorMenuScreenViewModel` + view (copy of the map editor's menu layout): list with name and "N stages · date", Back / Delete (confirm) / Copy / Edit (disabled, tooltip), New map AI: Name, Start from, Create AI. `AiBrainFormatter` shows a designed AI's own name and "Designed in the AI editor." 21 new strings in all 7 catalogs.
- Tests: `DesignedWorldAiStartTests` (7: starting from each built-in keeps its priorities and rules; Classic start = Classic stage; starting from a designed AI copies its stages), `AiEditorMenuTests` (7: create from Aggressive -> file, list row, registry, formatter, Settings drop-down, Start from list; copy has the same stages; delete via the prompt -> gone, the ID now plays Classic+; Back -> main menu; 5 file-name cases), visual `AiEditorMenuTest` (list + empty) and `MainMenuTest` reviewed. Fixtures that read or change the designed AIs in the static registry are `[NonParallelizable]`. Suite 5355.
- Real client (AOT, Debian 12 image): main menu -> AI editor -> Name "Raider", Start from Aggressive -> Create AI: `AIs/map/raider.json` written (attack power 80), the row shows "1 stage · 2026-11-21"; Settings -> Map AI lists Raider after the personalities, picked it (settings.json `"worldAi": "custom:raider"`); started 5th Column, ended 3 turns: the Elves (Raider) attacked, the battle resolved to a victory; no errors in the log.
- Found while building: the first label "Duplicate" was cut off ("Duplic…") in the 4-button row; renamed to "Copy" (checked in the visual test). The add-translations script overwrote the existing ru "Duplicate" (used by the map editor's events list); restored to "Копия".

## Phase 3 — done

Commits b713c723 + 5c06a48d, merged in a04f8a02.

- `Screens/AiEditor/AiDesignerBlocks.cs` (palette items with label and help text, colours: conditions blue, economy gold, armies red, rules green, tuning purple; block sentences built from the event editor's `MapEditorBlockText`/`MapEditorBlockNumber`; row, slot and condition-set view models), `AiDesignerStageViewModel` (card: name, When sets, economy and army slots, rules; ↑/↓/× for the stage and ↑/↓ per row), `AiDesignerScreenViewModel` (model <-> view models, insert/move/remove with the rules above, problems, undo/redo by design snapshots, Save -> file + registry reload, Back with a prompt), `AiDesignerScreenView` (layout copied from the event screen: palette | cards | help, drag ghost; code-behind drop finder like the event screen's). The list's Edit (and double-click) opens it; Create opens the new AI in it.
- Problems shown on the right and as a red card border: a non-default stage without conditions (it would always play and hide the stages below), a stage whose economy and army rows are both empty. Saving still works.
- 96 new strings in all 7 catalogs (ru translated; existing catalog words like "Heal", "Resurrect" reuse their old translation); the obsolete "The AI designer comes in the next update." removed.
- Tests: `AiDesignerTests` (7: load = file, remove/undo/redo, moving blocks between rows and into a new lowest row, moving a row, palette clicks incl. the default-stage and duplicate hints, rule numbers, + Stage and "+ or", problems, rename + prompt + save + back to the list with the AI selected, Create opens the designer). Visual `AiDesignerTest` drags with the headless mouse (palette -> condition set, stage block -> palette removes it), checks the hover help and that + Stage scrolls the new card into sight (mutation-checked: without the scroll the test fails); `AiDesignerRussianTest`; `AiEditorMenuTest`. All reviewed: no clipping at 1366x768 in en and ru. Suite 5362.
- Real client (AOT, Debian 12 image): AI editor -> Create "Rusher" from Classic -> the designer opened; dragged "Buy units" from row 5 into row 1 with the real mouse (ghost followed, rows renumbered, `rusher.json` economy starts `[["CastSpell","PurchaseUnit"], ...`); + Stage, clicked "I lost a city" in the palette -> the new stage has `fromTurn` + `lostCity`; Save, Back -> the list shows "2 stages"; Settings -> Map AI lists Rusher, picked it; 5th Column: the Elves attacked on turn 3 and the battle resolved; no errors in the log.
- Found in the real client and fixed (5c06a48d): after + Stage the new card was out of sight (inserted above the last card while the view stayed at the bottom); it now scrolls to the new card. Checked by the visual test, not re-run in the real client.
- Known limits: stages and rows reorder with buttons, not by dragging their headers; no keyboard way to move blocks; the designer's Test panel is phase 4.

## Phase 4 — done

Commit b554bf3d, merged in a1cc1331.

- Engine: the tournament loop moved from `tools/AiTournament` into `Engine/Ai/Tournament/AiTournamentRunner.cs` (`AiContender`, `AiGameResult`, `AiPlayerResult` with `StageTurns`, `AiContenderSummary.Summarize`, `Plan` for the seat rotation, `Play(..., CancellationToken)`). A designed AI's stage is counted once per own turn at its first action, through `DesignedWorldAiBrain.FindStage` (the same lost-city detection the brain uses). The tool now uses it and prints `Stages of custom:...` lines; Classic's S099 CSV is identical before and after the move.
- Client: `GameLoader.ListAiTestMaps` (skirmish quests and custom maps with at least two playable races) and `CreateAiTournament`; `AiDesignerScreenViewModel.Test.cs`: Map, Against (every other AI), Games (2-20, total), Turns (5-100), Run test / Cancel, progress "Game N of M…", results per side ("wins, draws (ahead), times out; cities at the end; first city taken on turn N; ms per turn") and "Time in each stage". It tests the AI **as it is on screen**, saved or not: the registry gets the on-screen design under the AI's ID for the run and is reloaded from the files afterwards. Games run in parallel on up to 4 threads (processors − 1). Leaving the designer cancels a running test.
- Tests: `CustomMapTests.TournamentRunnerCountsStagesTest` (seat rotation and seeds, stage turns counted for the designed AI only, same seed = same game, summary), `AiDesignerTestPanelTests` (2: an unsaved second stage plays in the test and shows in the stage line while the file and registry keep the saved version; cancel), visual `AiDesignerTestPanelTest` reviewed (no clipping). Suite 5365.
- Real client (AOT, Debian 12 image): AI editor -> Create "Tested" -> designer -> Run test with the defaults (5th Column, against Classic, 4 games, 30 turns): finished in under a second, "4 games of up to 30 turns: Tested: 0 wins, 4 draws (0 ahead), 0 times out; 1.0 cities at the end; no city taken; 3 ms per turn."; no errors in the log.
- Known limits: the default map is the first in the list (5th Column, a small quest whose events keep everyone at 1 city, so it shows little); pick a bigger skirmish for meaningful numbers (open question Y). Both sides use the Classic battle AI in the test.

### Open question added
- **Y.** Default Test map: the first in the list, or the biggest skirmish with at least 2 races and 6+ cities, remembered per AI? Recommendation: the last map used, remembered in the AI's file (no format change needed: an optional field).

## Phase 5 — done

Commit 20380e61, merged in 526c1427.

- Engine: `WorldAiProfile.KeepArmiesHome`, `GatherArmies`, `RushEnemyCapital`, `GoldReserve` (0/false = off, so Classic and the personalities are unchanged; the S099 Classic CSV is identical). `WorldAiPlanner.Behaviours.cs` acts after Classic has chosen an army's action:
  - **Keep N armies home**: the N fighting armies nearest the capital entrance (ties by ID) can't take a move ending more than `HomeRadius` = 4 tiles from it, and walk back when they are further.
  - **Gather N before attacking**: an army's attack is vetoed unless N fighting armies (itself included) stand within `GatherRadius` = 4 tiles; an idle or vetoed army walks towards the nearest other one.
  - **Rush the enemy capital**: an army with only a move/explore (or nothing) to do marches on the nearest hostile capital whose entrance the player has explored (the attack itself still needs the wanted advantage, so Classic's Attack action does it).
  - **Keep a gold reserve**: economy actions are planned and checked on a copy of the state with that much gold taken away.
  - "Fighting armies" = not rod planters, thieves, summons or illusions. Marches use Classic's own "move toward" step (`FindMoveTowardCell`, which never walks into a battle).
- File/designer: `DesignedWorldRules` + JSON (`keepArmiesHome`, `gatherArmies`, `rushEnemyCapital`, `goldReserve`); an orange **New behaviour** palette group; the blocks go in the rules area, once each per stage; clicking one that's already there only shows a hint. 18 new strings in all 7 catalogs.
- Tests: `CustomMapTests` `KeepArmiesHomeTest` (without the block the capital leader walks further than 4 tiles by turn 6, with it never), `GatherBeforeAttackingTest` (with one army and "gather 2" no battle is started by that player in 6 turns; the same AI without the block attacks), `RushEnemyCapitalTest` (map explored: closest approach to the elven capital by turn 5 = 13 tiles with the block, 34 without), `GoldReserveTest` (lowest treasury in 6 turns 400 with a 400 reserve, 0 without); `DesignedWorldAiTests` round trip and profile mapping with the new rules; `AiDesignerTests.BehaviourBlocksTest`; visual `AiDesignerRussianTest` shows the group and blocks (no clipping). Suite 5370.
- Tournament (Classic+ with one block each vs Classic; S100 2 races 12 games x 60 turns / S099 4 races 12 games x 60 turns), wins and cities at the end, Classic's in brackets:
  - none (Classic+ itself): S100 4 / 2.8 (2 / 2.2); S099 0 / 3.6 (0 / 3.5).
  - keep 1 home: S100 0 / 1.1 (11 / 3.9); S099 0 / 2.6 (0 / 3.8). Much weaker: early on the AI has one army, so it all stays home (now said in the block's help).
  - gather 2: S100 0 / 2.0 (5 / 3.0); S099 0 / 3.0 (0 / 3.7). Weaker, for the same reason.
  - rush: S100 4 / 2.5 (6 / 2.0), first capture turn 9.6 (13.9); S099 0 / 3.6 (0 / 3.5). Earlier captures, about even.
  - reserve 300: S100 4 / 2.8 (2 / 2.2); S099 0 / 3.7 (0 / 3.7). About even.
  - These are building blocks for stages (e.g. keep armies home only "when an enemy is near my capital", gather only "from turn 20"), not stand-alone improvements.
- Real client (AOT, Debian 12 image): designer -> "Rusher" -> clicked "Rush the enemy capital" and "Keep a gold reserve" in the New behaviour group -> Save (`rushEnemyCapital: true`, `goldReserve: 300` in the file) -> Map AI = Rusher -> 5th Column: 3 turns, the Elves attacked, the battle resolved; no errors in the log.
- Known limits: home and gather radii are fixed at 4 tiles; "fighting army" excludes rod planters and thieves but counts weak leaders too.
