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
3. ~~**Pick on the map, "used by", safe deletes, red broken blocks** (~1 day).~~ done, see "Phase 3 — done".
4. ~~**Recipes** (~1 day).~~ done, see "Phase 4 — done".
5. ~~**Army templates + army effects** (~2 days):~~ done, see "Phase 5 — done". create/move/remove/owner/order/battle, template editor on the army
   screen.
6. ~~**Remaining kinds + variables** (~2 days).~~ done, see "Phase 6 — done".
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

## Phase 3 — done

Feature commits `a77f8cff`, `e9f93748`, merged as `cd9498b4` (branch `upgrade/map-editor-event-safety`).

What was built:
- Engine: `CustomMapEventTarget` (location, village, capital, ruin, army, event), `CustomMap.GetEventsUsing(target)`
  and `GetEventTargetsAt(tile)`.
- **Pick on the map**: every zone, city, ruin and army drop-down has an "On map" button. The event screen steps
  aside, the events list too, every valid target glows gold (`MapEditorPlacementPreview.Highlight`), the hover line
  says "Pick a zone: click one of the gold squares · Esc cancels"; a click on a target brings the screen back with
  it chosen, a click elsewhere beeps and keeps picking (nothing is placed), Esc or the right button cancels.
- **Used by**: the hover line ends with "Used by events: Gate, Northgate" for the capital, village, ruin, army and
  zones under the cursor, with any tool.
- **Delete warnings** (decision "warn once, then allow"): deleting a zone (Shift+click, drag-press, list ×), a
  village or ruin (their screens), an army (its screen) or an event that other events switch asks once, naming the
  events: "Delete anyway" / Cancel. Unused objects go at once, as before.
- **Left-out events**: the list's red dot now also marks events that switch a left-out event (they would point at
  nothing in the game), and the event screen says "it switches an event that is left out". Saving names the
  left-out events in the status line and in an alert (the status line can be out of view at 1366x768).
- Removing a race: the editor has no way to remove a race from a map yet, so there is nothing to warn about; the
  engine side (races stored by race, renumbered at save) is covered by `EventPlayerNumbersTest`.

Tests: `CustomMapEventTests.EventsUsingTest` (engine, every target kind); `MapEditorScreenViewModelTests.EventSafetyTest`
(hover lines, pick: highlight = all zone tiles, miss keeps picking, hit returns the zone, Esc cancels; unused zone
deleted at once; used zone asks and Cancel keeps it; village deleted from its screen after the prompt turns its
event and the event switching it red; deleting a switched event asks; save status and alert; undo restores);
`VisualTests.MapEditorEventsTest` steps 7 (pick mode) and 8 (delete warning). Suite 5222 (+2); all 15 map editor
visual tests pass.

Real client (AOT build, `mapgen` map): "On map" on the Pass event's zone block showed the gold zones; a real click on
"Location 1" brought the screen back with it chosen; Save event; Shift+click on that zone with the Locations tool
showed the warning naming "Pass"; Delete anyway; the Pass row got its red dot; Save wrote only the Welcome event to
`SEvent.json` while `EditorEvents.json` kept Pass. The two follow-ups in `e9f93748` (list hidden while picking, save
alert) were checked by the tests only.

Known limits:
- Removing a village's stationed army from the village screen (clearing the party) doesn't warn; the event turns red
  as usual.
- The reveal block's race choice and a "pick" for events (switch block) are not there: events are picked from the
  drop-down.

## Phase 4 — done

Feature commits `ea0bf51f` (engine), `ce85eecb`, `ecacad2d`, merged as `ed8a952f` (branch `upgrade/map-editor-event-recipes`).

Decision taken while building: **recipes live in the events list**, not in the block palette. A recipe can make
several events (one win per attacking race, plus conquest events), which a palette block inside one event can't.
The list has a recipe box (starts on the first recipe), "Add", and a line saying what the recipe does; Add makes the
events as one undo step and opens the main one in the block editor.

The recipes (targets start at the first race, the map's first village / zone / ruin; "On map" changes them):
- **Capture a city to win**: owns [city] → message to all, [race] wins.
- **Reach a zone to win**: an army enters [zone] → message, win. The game's AI takes no goal from zone conditions,
  so the line says "The computer gets no goal from zones".
- **Survive N turns** (plain): on turn 30 → message, win. No AI goal.
- **Hold out against attackers**: the defender wins on turn 30; one event per other race "takes the [defender]
  capital" → that race wins. The capital becomes the attackers' AI goal (checked with the game's `WorldAiScenario`).
- **Lose if a city falls**: for the other races, owns [city] → message, the players lose (for maps where the player
  defends with the first race).
- **Warning when entering a zone**: message from a scout + reveal the zone. The planned "Ambush" needs "create an
  army", which comes with army templates (phase 5).
- **Message when looting a ruin**: for every race, loots [ruin] → message to the looter.

**Conquest wins**: after a recipe, every playable race that an event ending in win or lose concerns (the game's
`HasWinEvent` rule) and that has no conquest event gets "[race] conquers all capitals": one set of "owns [capital]"
blocks for every other race → [race] wins. Engine: `GetRacesNeedingConquestWin`, `AddConquestWin`, `IsConquestWin`.
"Hold out for N turns in a zone" needs variables (phase 6).

Message texts and event names carry no turn numbers (changing the number would leave them wrong).

Tests: `CustomMapEventTests.ConquestWinTest` (the game ends for the Empire through its conquest event once it holds
the elven capital; without the event the game goes on — the check that the test sees the conquest event and not
something else), `HoldOutGivesTheAiAGoalTest` (attackers' objective targets = the defender's capital; a zone
condition adds none); `MapEditorScreenViewModelTests.EventRecipesTest` (all 7 recipes, one conquest event per race,
every event saveable, undo, the game loads them); `VisualTests.MapEditorEventsTest` steps 9-10. Suite 5225 (+3);
all 15 map editor visual tests pass.

Real client (AOT build, `mapgen` map): picked "Survive N turns" in the recipe box, Add opened "Empire survives 30
turns"; typed 2 into the turn box, Save event, Save and play; turn 1 showed the welcome message, End turn, turn 2
showed "Empire has held out for 30 turns!" and then the Victory screen. The saved map had the recipe event and
"Empire conquers all capitals". The text and name were then changed to carry no number (`ecacad2d`, checked by the
tests); the box starting on the first recipe was found in the same run (it looked empty).

Known limits:
- A recipe always starts with the first race; pick another race in the hat afterwards (conquest events are only
  added when the recipe is added).
- Removing a recipe's main event leaves its conquest events; delete them in the list if no longer wanted.

## Phase 5 — done

Feature commits `a9a919c4` (engine), `dbaccc60` (editor), merged as `6fdf1a8c` (branch `upgrade/map-editor-event-armies`).

What was built:
- Engine (`CustomMapTemplates.cs`): `CustomMapArmyTemplate` (name, owner race or Neutral, six army slots, the order a
  created army gets), `CustomMapArmyOrder` (normal, stand, guard, roam, berserk, explore, go to a zone, defend a zone,
  defend a city, attack an army), `CustomMapArmyRef` (a placed army, every army from a template, or the army that
  fired the event). Effects: create an army from a template at a zone, remove, move to a zone (also the triggering
  army), move next to the triggering army, change owner (a race or the player who fired it), change the order, battle
  (the army attacks the triggering army; the game moves it next to it first). Templates are checked like armies (one
  leader, leadership) and their order target must exist; an event using a broken template is left out, as is the
  template itself.
- Writer: `SStkTemp` rows (`TM` + id, units and positions laid out like an army's unit group, leader, owner and
  subrace by player slot, order and target), effect rows with `ID_STACK` or `ID_STKTEMP`, new owner as the race's
  subrace (`SR` + slot), "the army that fired it" for moves as `BOOLVALUE`. Templates are also kept in the editor's
  `EditorEvents.json`.
- Editor: "Armies" button in the events list opens the **army templates** screen: a row per template with the name
  (edited in the row), owner, order and its target, a details line ("Peasant · 3 units" or what is wrong, red dot),
  Units (the army screen, reused with its own title and "Save template") and ×. New template opens its army screen at
  once. Deleting a template events use asks first.
- **Army blocks** (red "Armies" group under Then): create [template] at [zone]; [army] attacks the army that fired
  it; move [army] to [zone]; move [army] next to the army that fired it; remove [army]; [army] goes over to [race /
  the player who fired it]; order [army] to [stand…] / to go to [zone] / to defend [zone] / to defend [city] / to
  attack [army]. Army drop-downs list placed armies and "every [template] army" (and "the army that fired it" for
  moves); "On map" picks a placed army.
- **Ambush recipe**: when an army of the first race enters the zone, an army from the first template (a neutral
  "Ambushers" template is made when the map has none) appears there and attacks it, after a scout's message.

Tests: `CustomMapEventTests.TemplateSaveAndLoadTest` (template row through the game's loader slot by slot, every army
effect compared with the expected `ScenarioEventEffect`s, editor copy round trip, a template without a leader left
out with its event), `AmbushPlayTest` (entering North Gate creates the raiders inside South Gate with the template's
order), `AmbushBattleTest` (create + battle: the raiders move next to the army and a battle starts);
`MapEditorScreenViewModelTests.EventArmiesTest` (ambush recipe, templates screen rows, owner/order/target, army
blocks saved as expected, delete warning and undo, the game loads both templates); `VisualTests.MapEditorEventsTest`
steps 11-12. Suite 5229 (+4); all 15 map editor visual tests pass.

Real client (AOT build, `mapgen` map): added the ambush recipe from the list (it made "Ambushers"), Save event, Save
and play; selected the capital army and moved it one tile into the Northern Pass: the scout's message "An ambush!
They were waiting for us in Northern Pass!" popped up, and Continue opened the battle against three peasants.

Known limits:
- The made-up "Ambushers" are the first neutral leader and soldier in the data (peasants); change their units on the
  templates screen.
- "Destroys an army" and "an army exists" conditions still point at placed armies only, not templates.
- Deleting a zone or army a template's order points at doesn't warn (only events are checked); the template turns
  red.

## Phase 6 — done

Feature commits `a8a31bbe` (engine), `36674347` (editor), merged as `1fe6e210` (branch `upgrade/map-editor-event-kinds`).

All 17 condition kinds and all 24 effect kinds of the game can now be built (the game's "on turn N" is the editor's
own block on top). New in this phase:
- Conditions: owns an item; an army carries an item; relations between two races (war / neutral / peace); two races
  are allies; owns at least N % of the land; visits a site; a certain army is in a zone; a certain army is in a city;
  an item is brought to a zone; a variable is in a range (optionally "and" / "or" a second range, hidden until
  chosen). "Destroys an army" and "an army exists" now also take "every [template] army".
- Effects: change a variable (+, -, ×, ÷, =); make two races allies (or allies for good); set relations; change the
  land in a zone (a square of N tiles into a race's or neutral land); remove mountains in a zone; remove / change a
  landmark; give / destroy an item (destroy: carried by the army that fired it, or everywhere); teach a spell; cast a
  spell on the army that fired it or on a zone (cast by nobody or a race). The reveal block now has a race choice.
- **Variables screen** ("Variables" in the events list, next to "Armies"): name and starting value edited in the row,
  how many events use each, delete (asks first when used). Saved as `SVars` rows and in the editor copy.
- "On map" picking also for sites and landmarks; "used by" and delete warnings know sites, landmarks, templates and
  variables.

Findings from the game's code (in the block help and here):
- **"Set relations" only does something when it ends an alliance**: the engine's Diplomacy effect only acts on allies
  set to neutral or war (it breaks the alliance); otherwise relations stay. Kept faithful (no engine change).
- The game saves relations as numbers; the editor writes 0 / 49 / 100 like the original maps (war below 34, peace
  above 66).

Tests: `CustomMapEventTests.RemainingKindsSaveAndLoadTest` (12 conditions incl. template references and 12 effects
through the game's loader, `SVars` rows, editor copy round trip), `VariablePlayTest` (a counter +1 each turn wins at
3 on turn 3, in the game engine); `MapEditorScreenViewModelTests.EventKindsTest` (every block kind is in a palette
exactly once, an event with all 46 block kinds survives blocks → save → blocks unchanged, variables screen, delete
warning); `VisualTests.MapEditorEventsTest` steps 13-14. Suite 5232 (+3); all 15 map editor visual tests pass.

Real client (AOT build, `mapgen` map with a "Turns counted" variable, +1 every Empire turn, and a message at 2):
turn 2 showed "The counter has reached two."; no errors in the log.

Known limits:
- Reveal for several chosen races at once (the race choice picks one or "the event races").
- Spell and item names come from the game's lists; there is no check that a spell suits "cast on a zone".

## Open questions

1. ~~Recipes in phase 4 — or before the full screen as a quick win?~~ Decided: phase 4, after the full screen.
2. ~~Popup texts in one language, or per language?~~ Decided: default language (English) plus optional translations, fallback to the default.
3. ~~Deleting an object used by events: block saving, or drop the block with a warning?~~ Decided: confirm once, blocks turn red, saving skips broken events with a note.
4. ~~Removing a race: re-map events automatically or warn?~~ Decided: same as deleting an object (confirm once, red blocks, skipped on save, restored when the race returns).
5. ~~A tree view of event chains — later?~~ Decided: a separate chain screen, phase 7.
6. ~~Should "Survive N turns" also make the AI target something?~~ Decided: two recipes, plain and "Hold out against attackers".
7. ~~OR + "only once": hidden variable or siblings switch each other off?~~ Decided: siblings switch each other off.

All open questions are answered; new ones get added here as the phases go.
