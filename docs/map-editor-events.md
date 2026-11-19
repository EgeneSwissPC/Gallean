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
  - Recipes (top): Capture a city to win · Hold a zone to win · Survive N turns · Lose if a city falls · Ambush when
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
- **Message block** opens a preview copied from the game's event popup (text, portrait picker, left/right side,
  audience: the triggering player / everyone / the races concerned).
- Shape rules: condition blocks only fit under When, effect blocks only inside Then. A block with an empty or broken
  field is outlined red and its event shows a red mark in the list.

### Map side
- The hover line on any object used by events says "used by 2 events"; deleting it asks first.
- While picking, the map shows only valid targets.

## Technical approach

- **Engine (editor-only model)** `src/Gallean.Engine/MapEditor/CustomMapEvents.cs`: `CustomMapEvent(Id, Name, Race,
  ConcernsRaces?, Once, Enabled, Chance, Order, ImmutableArray<ImmutableArray<CustomMapCondition>> Groups,
  ImmutableArray<CustomMapEffect> Effects)`; condition/effect records point at editor objects by editor ID (location,
  stack, village, capital race, ruin, site, landmark, template, event) and races, not raw scenario IDs.
- **Writer:** one game event per OR group (same effects; "Occurs once" across groups via a shared variable or by
  switching the sibling events off — open question 7); Tscen `NAME_TXT`, `POPUP_TXT` (SEQ_NUM = effect NUM),
  `OBJECT_TXT`; player IDs from the current race slots, so removing a race re-maps events instead of breaking them.
- **Reader:** `CustomMapStore.Open` reads events back (like locations, through an optional `FromState` parameter);
  sibling events written for an OR group carry a marker in the name text so they fold back into one event.
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

1. **Engine model + save/load, starter set** (~1.5 days): every N turns, on turn N, enters zone, owns/enters city,
   loots ruin, destroys army, army exists; message, win, lose, switch event, reveal zone, give item, change objective;
   OR groups.
2. **Block event screen + events list** (~2.5 days): palette, script with drag-drop, settings, message preview.
3. **Pick on the map, "used by", safe deletes, red broken blocks** (~1 day).
4. **Recipes** (~1 day).
5. **Army templates + army effects** (~2 days): create/move/remove/owner/order/battle, template editor on the army
   screen.
6. **Remaining kinds + variables** (~2 days).

## Open questions

1. Recipes in phase 4 — or before the full screen as a quick win?
2. Popup texts in one language, or per language?
3. Deleting an object used by events: block saving, or drop the block with a warning (it stays red until fixed)?
4. Removing a race: re-map events automatically (proposed) or warn?
5. A tree view of event chains (which event switches which) — later?
6. Should "Survive N turns" also make the AI target something, or just win on turn N?
7. OR + "only once": use a hidden variable, or have each sibling switch the others off (no variables needed)?
