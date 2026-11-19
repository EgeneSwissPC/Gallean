# Gallean Upgrade – map editor: events (Scratch-style blocks)

Spec written 2026-11-19 on `gallean-upgrade` at `3c64f1c3`. Nothing is built yet. Numbers come from
`~/work/gallean-tools/survey_events.py` over the 111 bundled scenarios.

## Decisions

- **Scratch-style block editor.** Events are built from coloured blocks snapped together: a "When" hat holds
  condition blocks, a "Then" block holds effect blocks. Each block reads as a sentence with pickers. Aimed at players
  who just want to design their own level, not at scripters.
- **Editor-only.** No engine or AI change. The editor writes the game's normal event tables (`SEvent`, `SEvCond`,
  `SEvEffec`, `SEvUnfog`, Tscen texts, `SStkTemp`, `SVars`), so maps stay upstream-compatible and play with the
  game's own event code.
- **The editor hides the engine's limits where it can:** OR between conditions is allowed in the blocks and saved as
  several game events; "on turn N" is saved as "every N turns" + "only once".
- **Recipes** (ready-made filled scripts) replace the separate "victory settings" task: they are the first blocks in
  the palette.
- **Order: the full block editor first, recipes after it (phase 4)**, built from real blocks so every recipe can be
  opened and changed. No temporary victory panel.
- **Texts: a default language plus optional translations.** Every text the player reads (message, objective) has one
  box in the default language (English). An "Add translation" button adds a box for another of the game's 7
  translations (ru, de, fr, it, pl, cs, es); each can be removed again. A language without a translation falls back
  to the default text. The writer fills the base Tscen and each `Languages/<lang>/Scens` copy.
- **Deleting a used object: warn once, then allow.** Deleting (any tool, Delete key, list) an object that events use
  asks once: "Northgate is used by 2 events: Capture Northgate, Village falls. Delete anyway?" (Enter = yes, Esc =
  no). Yes deletes it; the blocks stay, drawn red with an empty slot, and the event gets a red mark in the list.
  Saving works and leaves broken events out of the file, with a note "2 events skipped because of missing targets".
  Fixing the block brings the event back into the next save. Undo restores the object and its references together.
- **Removing a race works exactly like deleting an object** (one rule for every reference, no special logic). Events
  store races by name; the writer turns them into player IDs (PL + slot) at save time, so renumbering is automatic.
  Removing a race that events name asks once; blocks naming it turn red and are skipped on save; a race set that
  also has other races just drops it. Putting the race back (placing its capital) turns those blocks normal again.
- **Event chains get a separate chain screen, later (phase 7).** Each event is a box, arrows for "switches on" /
  "switches off", events that are never switched on (and start off) in a warning colour, click a box to open its
  script. The block screen gets no chain strip.
- **Two survival recipes:** "Survive N turns" (plain: the chosen race wins on turn N; no AI goal) and "Hold out
  against attackers" (the attacking races win if they capture the defender's capital before turn N, the defender
  wins on turn N; the capital becomes the AI's goal from its objective turn). Every win recipe also adds a win event
  for capturing every enemy capital, so conquering everyone still ends the game early (a race with a win event no
  longer wins by elimination).
- **OR + "only once": the copies switch each other off.** An OR event is saved as one game event per condition group
  with the same effects; with "only once" each copy gets a hidden effect (before any Win effect) "switch off the other
  copies" (the game's normal EnableEvent effect, no variables). "Switch [X] on/off" blocks elsewhere switch all
  copies of X.
- **The editor keeps its own copy of the events** (`EditorEvents.json` in the map folder; taken in phase 1). The
  game only reads the event tables; the editor reopens events from its copy, so "or" groups, "turn N" blocks,
  translations and broken events come back exactly as built. Folding the game's rows back (the earlier plan) could
  not bring back broken events, which the tables leave out. Event names are editor-only, so they have no
  translations.

## How events work in the game (engine facts)

- An event (`ScenarioEventDefinition`, loaded by `ScenarioLoader.LoadEvents`) has a name (Tscen `NAME_TXT`, editor
  only), conditions (all must hold; no OR, NOT or nesting, except inside the variable condition), effects (run in
  order), two race sets and four settings:
  - **Triggered by** (`VER*` flags, `VerifiedRaces`): whose turn or army checks the event.
  - **Concerns** (`HUMAN`..`ELF` flags, `AffectedRaces`): which races the conditions talk about ("an Empire army
    enters", "the Empire owns the city"); also the audience of "affected races" popups. The sets differ in 1,050 of
    4,598 original events.
  - **Enabled** at start, **Occurs once**, **Chance** % (rolled at every check), **Order** (checked by Order, then ID).
  Events with no condition are skipped by the loader.
- **When events are checked:** start of every player's turn, after each army step, ruin looted, site visited, army
  destroyed, city captured, item gained, army created. Never on turn 0.
- **Cascade** (`ScenarioEventCalculator.CascadeEvents`): the first enabled event whose conditions hold fires, then the
  check repeats over the rest, so an event can switch on another (or change a variable) and that one fires in the same
  moment. Campaigns chain quests this way (596 events start switched off). "Occurs once" switches an event off after
  it fires. A battle effect ends the cascade.
- "Every N turns" = turn number divisible by N, at the start of the triggering player's turn. There is no exact-turn
  condition.
- Zone conditions ignore armies inside a village or an uncaptured capital.
- **A win event disables "last one standing wins"** for its race (`PlayerDefeatedEndScenarioRule.HasWinEvent`: an
  event whose last effect is Win).
- **The AI reads win events** (`WorldAiScenario`): from turn 60/50/40/30 (Easy..Very hard) its armies go after the
  conditions of its win events, avoid the zones of lose events and guard armies named in win conditions.
- Custom maps today save an empty `SEvent.json`; the game ends only by elimination.

### Use in the original maps (rows; maps using it)

Conditions: every N turns 1,458 (111); army enters a zone 1,323 (109); owns a city 1,066 (99); army exists / doesn't
387 (65); loots a ruin 279 (59); destroys an army 275 (77); a given army reaches a zone 140 (42); enters a city
85 (21); carries an item into a zone 73 (27); owns an item 68 (21); variable in range 52 (9); % of land 50 (12); a given
army reaches a city 20 (7); diplomacy level 20 (6); visits a site 9 (6); leader carries an item 4 (1); alliance 1 (1).

Effects: popup 5,281 (111; 93 % with a portrait); switch event 1,489 (95); create army from template 1,262 (98); change
order 582 (78); cast spell on the triggering army 490 (63); win/lose 276 (110; 221 win, 55 lose); move army to zone 270
(56); change owner 189 (48); remove army 166 (48); reveal zone 139 (50); give item 139 (44); cast spell on zone 116 (29);
remove landmark 95 (22); remove mountains 77 (28); change terrain 75 (11); change variable 71 (9); battle 69 (35);
change objective 61 (35); move beside triggerer 52 (30); diplomacy 38 (13); destroy item 34 (17); alliance 30 (11); give
spell 10 (7); change landmark 5 (4).

Per event: 90 % have one condition, half have one effect, at most 10 of each. Median 33 events per map, max 209.
Army templates: 950 in 100 maps. Variables: 47 in 11 maps.

## What the user sees

### Events list (V)
Left overlay like the locations list: sorted by name, search, count, rename in the row, enabled checkbox, duplicate,
delete, "New event". Double-click (or Enter) opens the event screen. Hotkey V (free; E is the economy panel).

### Event screen
Full screen like the army screen, three columns:

- **Left – block palette**, grouped by colour; groups fold, only the most used blocks show at first (~10):
  - Recipes (top): Capture a city to win · Hold a zone to win · Survive N turns · Hold out against attackers · Lose if a city falls · Ambush when
    entering a zone · Treasure message when looting a ruin.
  - Blue – time: on turn N, every N turns.
  - Green – zones: an army enters [zone], [army] reaches [zone], an army carries [item] into [zone].
  - Gold – places: owns [city], enters [city], loots [ruin], visits [site], owns N % of the land.
  - Red – armies: destroys [army], [army] exists / is gone, [army] reaches [city].
  - Purple – items and spells: owns [item], [leader] carries [item].
  - Effect groups in the same colours: show message, win, lose, switch [event] on/off, change objective · create
    [army] at [zone], move/remove/change owner/order of [army], battle · reveal/change terrain/remove mountains in
    [zone], remove/change [landmark] · give/destroy [item], give/cast [spell] · diplomacy, alliance · variables.
- **Middle – the script:** hat **When** [for Empire ▾]; condition blocks under it joined by "and"; an "or" block
  starts another condition group; **Then** with effect blocks stacked inside. Drag from the palette to add, drag to
  reorder, drag back to the palette (or Delete) to remove. Fields are drop-downs, number boxes and a 🎯 button that
  jumps to the map to pick a zone, city, army, ruin, site or landmark (only valid targets light up; click picks, Esc
  cancels).
- **Right – settings in plain words:** Only once, Starts switched on, Chance %. *Advanced*: the "concerns" race set
  (defaults to the When race; 77 % of original events use the same set for both), order.
- **Text fields** show the default-language box and an "Add translation" button (language drop-down, a small
  "+ ru" style tag per added language, × removes it). The popup preview has a language switch to check each version.
- **Message block** opens a preview copied from the game's event popup (text, portrait picker, left/right side,
  audience: the triggering player / everyone / the races concerned).
- Shape rules: condition blocks only fit under When, effect blocks only inside Then. A block with an empty or broken
  field is outlined red and its event shows a red mark in the list.

### Map side
- The hover line on any object used by events says "used by 2 events"; deleting it asks once (see Decisions).
- While picking, the map shows only valid targets.

## Technical approach

- **Engine (editor-only model)** `src/Gallean.Engine/MapEditor/CustomMapEvents.cs`: `CustomMapEvent(Id, Name, Race,
  ConcernsRaces?, Once, Enabled, Chance, Order, ImmutableArray<ImmutableArray<CustomMapCondition>> Groups,
  ImmutableArray<CustomMapEffect> Effects)`; condition/effect records point at editor objects by editor ID (location,
  stack, village, capital race, ruin, site, landmark, template, event) and races, not raw scenario IDs.
- **Writer:** one game event per OR group (same effects; with "only once" each copy switches the other copies off,
  inserted before a Win effect so the engine's "last effect is Win" checks still hold); Tscen `NAME_TXT`, `POPUP_TXT` (SEQ_NUM = effect NUM),
  `OBJECT_TXT`; player IDs from the current race slots, so removing a race re-maps events instead of breaking them.
- **Texts:** `CustomMapText(Default, ImmutableDictionary<string, string> Translations)` for every event text; the
  writer puts the default into the base Tscen and translation-or-default into each language's Tscen.
- **Reader:** `CustomMapStore.Open` reads `EditorEvents.json` (`CustomMapEventsFile`) and passes the events to
  `FromState`.
- **Client:** `MapEditorEventsListViewModel` (overlay), `MapEditorEventScreenViewModel` + view (palette, script,
  settings), block view models per kind with a shared field set (zone/object picker, number, drop-down, text);
  drag-drop through the game's `DragDropBehavior`; map picking through a pick mode in `MapEditorScreenViewModel`.

## Testing

- `CustomMapTests.EventSaveAndLoadTest`: every block kind written, loaded with the game's `ScenarioLoader`, compared
  with the expected `ScenarioEventDefinition`s, and read back into the editor unchanged (OR groups included).
- Engine play tests: entering a zone shows a popup and creates an army; capturing a city wins; "on turn 5" fires once
  on turn 5; a win recipe still ends the game for the race that wiped out the rest (recipe adds the elimination win).
- `MapEditorScreenViewModelTests`: list, drag a block in/out/reorder, pickers, red broken blocks, delete-used-object
  prompt.
- Visual tests: list, event screen idle and with a recipe, message preview, map pick mode.
- Real client on the `mapgen` map: enter a zone → message + army; capture → victory screen.

## Phases

1. ~~**Engine model + save/load, starter set** (~1.5 days):~~ done, see "Phase 1 — done". every N turns, on turn N, enters zone, owns/enters city,
   loots ruin, destroys army, army exists; message, win, lose, switch event, reveal zone, give item, change objective;
   OR groups.
2. ~~**Block event screen + events list** (~2.5 days):~~ done, see "Phase 2 — done". palette, script with drag-drop, settings, message preview.
3. **Pick on the map, "used by", safe deletes, red broken blocks** (~1 day).
4. **Recipes** (~1 day).
5. **Army templates + army effects** (~2 days): create/move/remove/owner/order/battle, template editor on the army
   screen.
6. **Remaining kinds + variables** (~2 days).
7. **Chain screen** (~1.5 days): events as boxes with switch-on/off arrows, unreachable events marked, click opens
   the script.

## Phase 1 — done

Feature commit `e844625a`, merged as `defbc255` (branch `upgrade/map-editor-events`).

What was built:
- `CustomMapEvents.cs`: `CustomMapEvent` (name, triggering races, optional concerned races, once, enabled, chance,
  order, condition groups = "or", effects), `CustomMapText` (default + translations for cs/de/es/fr/it/pl/ru,
  fallback to the default), `CustomMapCity` (village or a race's capital). Conditions: turn N / every N turns, army
  enters a zone, owns a city, enters a city, loots a ruin, destroys an army, army exists / is gone. Effects: message
  (audience, unit portrait, side), win, lose, switch an event on/off, reveal a zone, give an item (army or capital),
  change the objective. `CustomMap.AddEvent/UpdateEvent/RemoveEvent/GetEvent`, `GetEventProblems` (no race on the
  map, no condition, missing target, a second objective change) and `GetSaveableEvents` (also drops events that
  switch a dropped event, repeated until stable).
- Writer: `SEvent`/`SEvCond`/`SEvEffec`/`SEvUnfog` + Tscen `NAME_TXT`/`POPUP_TXT`/`OBJECT_TXT`. One game event per
  "or" group (`EV` + running number); with "only once" each copy first switches the others off; a "turn N" group's
  copy occurs once; win/lose saved last; races become player IDs at save time. Translations go to
  `<maps>/Languages/<code>/<map id>/Tscen.json` (removed when a language has none); `GameLoader` reads them for
  custom maps (small client change; the game laid language texts over scenario texts already).
- `CustomMapEventsFile` (editor copy, JsonNode by hand, AOT-safe); `Open` reads it.

Tests (`CustomMapEventTests`, 4): `EventRulesTest`; `EventSaveAndLoadTest` (every kind through the game's
`ScenarioLoader`, compared with the expected `ScenarioEventDefinition`s; English and Russian texts through
`GameTextProvider`; reopen equals the built events, broken ones included; translation file removed when dropped);
`EventPlayerNumbersTest` (removing the race before another renumbers players, the win follows the race);
`EventPlayTest` (game engine: entering either gate shows the message once in all, turn 3 wins for the Empire).
Mutation check: without the copies switching each other off, `EventPlayTest` fails ("South Gate: only once in
all", 2 popups). Suite 5219 (5215 + 4); the 14 map editor visual tests pass.

Real client (AOT build, `mapgen` map with a "Welcome" turn-1 message and a Northern Pass event): starting the
custom quest shows the game's story popup "Welcome to the custom map events test." with the portrait on turn 1;
no errors in the client log. Entering the pass was not driven in the client (covered by `EventPlayTest`).

Known limits:
- No editor UI yet (phase 2); events exist only through the engine API and saved files.
- The game keeps one reveal audience per event: several reveal blocks in one event reveal for the union of their
  races. One objective change per event (the text is keyed by event).
- Portraits are unit type IDs (as in the original maps); the picker comes with the message block.

## Phase 2 — done

Feature commit `e315bcf3`, merged as `029b070d` (branch `upgrade/map-editor-event-screen`).

What was built:
- **Events list (V)**, over the left of the map like the locations list (they and the economy panel take turns):
  sorted by name, search, count, "New event", per row the races and size ("Empire · 1 condition · 2 effects"), an
  "on at the start" box, a red dot when the event can't be saved yet, Open / Rename (in the row) / Duplicate / ×.
  Every change is one undo step. "Events (V)" and "Economy (E)" share the right panel's top row (the "Map editor"
  title made room).
- **Block editor** (`MapEditorEventScreenViewModel` + view, blocks in `MapEditorEventBlocks.cs`): palette grouped by
  colour (Time blue, Zones green, Places gold, Armies red, Items purple, Messages grey, Game dark), the script with
  the "When, for [races]" hat, condition sets joined by "or" ("+ or" adds a set, "Remove this set"), and "Then"; the
  settings column (Only once, Switched on at the start, Chance %, Advanced: concerned races and order). Blocks read as
  sentences with drop-downs (zones, cities, ruins, armies, events, races), number boxes, an item button (the game's
  item list) and texts with "Add translation" per language.
- **Drag and drop**: a click on a palette block adds it (conditions to the last set); a drag drops it before/after
  the block under the mouse or at the end of a set / of Then; script blocks can be dragged within and between sets;
  dropping a block on the palette removes it (× too). Conditions only go under When, effects only under Then.
- **Red blocks** (taken from phase 3 because the screen needs the feedback): the engine's `GetEventProblems` marks
  blocks with a missing or empty target red, the hat red without a race, and a line under the settings says why the
  event isn't saved yet.
- **Message preview**: the game's own story popup (`ScenarioEventPopupViewModel`) with the block's text and portrait;
  each translation row has its own Preview. Portrait = a unit picked from the game's leader list.

Tests: `MapEditorScreenViewModelTests.EventScreenTest` (list exclusivity, new event, palette click and drop, "or",
wrong-list drops refused, red empty message, translation, move, settings, preview in both languages, the saved event
compared field by field, rename/on-off/duplicate/search/delete/undo, a removed zone turns the row red and the saved
map keeps the event in the editor copy but not in the game tables); `VisualTests.MapEditorEventsTest` (6 shots:
list, new event, blocks, red block, preview, list after). Suite 5220 (+1); all 15 map editor visual tests pass.

Real client (AOT build, `mapgen` map): V opens the list with the map's two events; Open shows the "Pass" event; a real
mouse drag of "loots a ruin" from the palette onto the condition set shows the ghost under the cursor and drops the
block; dragging a block onto the palette removes it; Save event + Save wrote `SEvCond` with the new `LootingRuin`
row and `EditorEvents.json`; no errors in the log.

Known limits:
- No insertion marker while dragging (the drop lands before/after the block under the mouse).
- The reveal block always reveals to the event's races (a race choice comes with the remaining kinds).
- Picking targets on the map, "used by" in the hover line and the delete confirmations are still phase 3.

## Open questions

1. ~~Recipes in phase 4 — or before the full screen as a quick win?~~ Decided: phase 4, after the full screen.
2. ~~Popup texts in one language, or per language?~~ Decided: default language (English) plus optional translations, fallback to the default.
3. ~~Deleting an object used by events: block saving, or drop the block with a warning?~~ Decided: confirm once, blocks turn red, saving skips broken events with a note.
4. ~~Removing a race: re-map events automatically or warn?~~ Decided: same as deleting an object (confirm once, red blocks, skipped on save, restored when the race returns).
5. ~~A tree view of event chains — later?~~ Decided: a separate chain screen, phase 7.
6. ~~Should "Survive N turns" also make the AI target something?~~ Decided: two recipes, plain and "Hold out against attackers".
7. ~~OR + "only once": hidden variable or siblings switch each other off?~~ Decided: siblings switch each other off.

All open questions are answered; new ones get added here as the phases go.
