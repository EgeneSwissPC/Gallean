# Gallean Upgrade – map editor: locations

Planned 2026-11-17 on branch `gallean-upgrade` at `2ce057c1`. This file tracks the work and its progress: one
"Phase N — done" section is added per phase. All three phases are done: 1 (`8a6ffe2d`), 2 (`8b9ccde8`), 3
(`b31aa076`).

## The problem today

Locations are named zones on the map. The game uses them for event conditions and effects and for the "move to
location" / "defend location" army orders, but the editor can't place them and `CustomMapWriter` writes no
`SLoc.json`. Every custom map has no locations, so nothing that needs one (events, those orders) can be added later.

## How it works in the game (checked in the code)

- `SLoc.json` rows: `LOC_ID` (`S095LO0000`), `POS_X`, `POS_Y`, `RADIUS`. The name is the Tscen text with the same ID.
  The loader builds `ScenarioLocationDefinition(Id, NameTextId, Position, Radius)`.
- The area is a square around the centre (`ScenarioLocationCalculator.EnumerateTiles`): radius 0 = 1 tile, 1 = 3x3,
  2 = 5x5, 3 = 7x7.
- Every bundled scenario has them: 109 `SLoc` files, 2,404 locations, up to 102 in one map; radii used 0–3.
- The game never draws them.
- What uses them:
  - army orders `WorldStackOrder.MoveToLocation` / `DefendLocation` (`ScenarioLoader` ~line 2353; the AI walks into
    the square and stays, `WorldAiPlanner.MoveAction.cs`)
  - event conditions: entering a zone, army to location, item to location
  - event effects: move army, unfog, change terrain, remove mountain, cast a spell on the map
  - AI victory zones (`WorldAiScenario.GetDefeatZones`)
- On their own locations change nothing in a game, so adding them can't change game behaviour.
- **Overlapping locations are independent.** Events trigger, not locations: after a move `ScenarioEventCalculator`
  checks every event, and the entering-zone check only asks whether the army is inside that event's square
  (`ConditionChecker.EnteringZone.cs`). An army on a tile covered by two zones fires both zones' events in the same
  move; nothing ranks them by size. Orders also only look at their own square. The editor's "selected, then
  smallest" rule is only for picking a location with the mouse. The overlay draws every square, and the hover line
  lists every location under the cursor, so an overlap is never hidden.

## Decisions

- **Editor only.** Locations are not shown in the game, not even in dev tools. Maybe later (afterthought).
- **Radius 0–3 only**, like the engine and the original maps, so saved maps stay compatible with upstream.
- **Army orders are not part of this task.** They belong to the separate armies task (roadmap "Missing: settings on
  things you can place" → Armies), which will use the locations placed here.
- **Looks like landmarks.** Landmarks have no labels drawn on the map: their name shows in the hover line and in the
  right-click info. Locations get the same, plus their name drawn on the map over the square, in the outlined text
  style the game uses for path cost labels (`WorldRenderer.DrawPathCostLabel`), so the zones can be found at a glance.
- **Default names "Location 1", "Location 2" ...** (the next unused number). Names are edited in one place only: the
  location's row in the list. After placing, its row opens for editing with the name filled in and selected: type a
  new one, or press Enter to keep it. Double-click a row to rename later.
- **List on the left over the map** (like the landmark palette and the economy panel, which it hides while open),
  sorted alphabetically by default, with a search box. Hotkey **L** (checked free: the editor uses W/A/S/D, arrows,
  Ctrl+Z, Ctrl+S, P and E in `MapEditorVisual.OnKeyDown`).
- **Ctrl+click places a zone inside another one** (a plain click inside a zone selects it, so without this a small
  trap zone could only be placed before the larger zone around it). Taken during phase 2; say if you want a different
  key.
- **Names sit on the square's top corner tile**, not the centre: zones sharing a centre (warning + trap) would hide
  each other's names (seen in the real client with the mapgen map).
- **Locations can be dragged to move them**: grab anywhere inside the square; where squares overlap, the selected
  location is picked first, then the smallest. Dropping where the square would leave the map snaps it to the nearest
  place that fits (see "Dragging").

## What the user would see

### Locations tool

- A "Locations" tool in the tool list (Russian label must fit ~90 px).
- Hovering shows the square that would be placed (placement preview: green; red where the square leaves the map).
- Click: places a location centred on the tile with the current radius and a default name ("Location 1", ...), then
  opens its list row for editing the name.
- Radius 0–3 in the tool settings, also with + / −. Not the scroll wheel: it zooms the map in the editor.
- Click inside an existing location: selects it; the settings then show its radius, X/Y position and a Delete
  button (the name is edited in the list). Shift+click deletes. Ctrl+Z undoes every change.
- Locations may overlap and may cover water, mountains and objects (the original maps do this); they block nothing.

### Dragging

- Only with the Locations tool on (other tools paint on a drag).
- Hovering outlines the location a press would pick up: the selected one if the cursor is in it, else the smallest
  square under the cursor.
- Press and release on the same tile = click (select, or place on empty ground). Moving to another tile before the
  release = drag. A drag starting on empty ground does nothing.
- The grabbed tile stays under the cursor; the square follows with the placement preview. Where it would leave the
  map, the preview shows amber at the snapped place (centre clamped so the square fits: `radius <= x, y <= size - 1 -
  radius`, the nearest fitting centre) and the hover line says "Northern Pass: 30, 12 -> 34, 20 (snapped to the map
  edge)". Releasing moves it there. A click-to-place near the edge snaps the same way, so placing never fails.
- Esc or right-click during the drag puts it back; Ctrl+Z undoes the whole drag in one step.
- Screen-edge scrolling keeps working during the drag (long moves on 512 maps); X/Y number boxes in the settings for
  exact positions.
- Moving keeps the location's ID and name (later events and orders point at the ID).

### Overlay

- Translucent squares with the name, drawn by the editor only, while the Locations tool is on or a "Show locations"
  toggle is set.
- The selected location gets the gold outline used for objects a click opens.

### Locations list

- Left overlay over the map, like the landmark palette and economy panel; while open it hides them (computed `Show*`
  property notified from every toggle). The right panel shows only ~270 px at 1366x768, too little for up to 102 rows.
- Opens by itself when the Locations tool is picked; a "Locations (L)" button and the L key open/close it with any
  tool.
- Header with the count: "Locations (12)". A search box under it filters by name (case-insensitive, contains).
- Rows sorted alphabetically (culture-aware, so Russian names sort right), quest-list style
  (`ListBox Classes="select-list"`, `Border Classes="row"`, `TextBlock Classes="row-title"`): name, size ("5x5"),
  position. A small delete button per row.
- The selected location is the highlighted row; selecting it on the map scrolls the list to its row.
- Hover a row: its square lights up on the map. Click: camera moves there and selects it. Double-click: edit the name
  in the row (Enter saves, Esc cancels). A new location's row opens in edit mode right after placing; renaming re-sorts
  the list and keeps the row in view.

## Technical approach

- Engine (`src/Gallean.Engine/MapEditor`): new `CustomMapLocations.cs` with `CustomMapLocation(Id, Name, Position,
  Radius)` on `CustomMap`, and `Place/Move/Rename/Resize/RemoveLocation` returning an error instead of throwing, like
  the other `Place*` methods. Tiles via the engine's own `ScenarioLocationCalculator` where possible.
- Writer: `SLoc.json` rows, names as Tscen rows, stable IDs `<prefix>LO0001`... (like landmarks' stable IDs), so later
  events and orders can point at them. Store them in `CustomMapStore` so editing a saved map keeps the IDs.
- Client: tool + settings in `MapEditorScreenViewModel`, preview rules in `MapEditorScreenViewModel.Preview.cs`,
  overlay in `MapEditorVisual` after `WorldRenderOperation` (same place as the placement preview). Editor-only colours
  in an editor file.
- New strings in all 7 translation catalogs (ru translated).

## Testing

- Engine: place/move/resize/rename/remove rules; save and load round trip (IDs, names, positions, radii survive);
  agreement test: the editor's tiles for each radius equal the game's `ScenarioLocationCalculator` after a load.
- Visual tests: tool idle, hover preview, placed and selected location, overlay labels, list panel, Russian labels.
- Real client: save a mapgen map with locations, load it in the game and play a few turns: nothing changes in the game
  (no locations visible), no errors in the log. The order test in the game comes with the armies task.

## Phase 1 — done

Commit `8a6ffe2d`, merged in `657cd245`. Engine only; the editor screen doesn't show locations yet.

- `CustomMapLocations.cs`: `CustomMapLocation(Id, Name, Center, Radius)` (`Side`, `Footprint`, `Contains` = the
  game's Chebyshev distance), kept on `CustomMap.Locations` (constructor and `Clone` carry them).
  - `PlaceLocation(center, radius, name?)`: blank name = `GetNextLocationName()` ("Location N", smallest unused N
    among default names); names are trimmed.
  - `SnapLocationCenter`: centre clamped to `radius..Size-1-radius`, used by place, move and resize, so none of them
    fail. Radius outside 0–3 throws (`MaxLocationRadius`).
  - `MoveLocation`, `ResizeLocation`, `RenameLocation` (blank ignored), `RemoveLocation` by ID; ID and name survive a
    move.
  - `PickLocation(position, selectedId)`: selected first, then smallest radius, then most recent; `GetLocationsAt`
    lists all of them (for the hover line).
- Writer: `SLoc.json` (`LOC_ID` = `<prefix>LO<hex id>`, `CustomMapWriter.GetLocationId`), name as Tscen `NAME_TXT`.
- Reader: `CustomMapStore.FromState(..., locations)` takes `Scenario.Provider.Locations` (locations aren't in the
  world state); `Open` passes them, names from Tscen. Radii above 3 (none in the original maps) are clamped.
- Tests (`CustomMapTests`): `LocationRulesTest` (default names, snapping at edges, overlap picking, move/resize/
  rename/remove, clone), `LocationSaveAndLoadTest` (SLoc rows, a Russian name, the game's provider has the same IDs,
  agreement with `ScenarioLocationCalculator` on every tile of the map for radius 0–3, nothing else in the world
  changes, reopen + save keeps IDs). Mutation check: switching `Contains` to a diamond (Manhattan) distance makes the
  agreement test fail. Full non-visual suite 5213 (was 5211).
- Real client: the `mapgen` map now has "Northern Pass" (12,10 r1), "Pass Warning" (12,10 r3, overlapping) and a
  location placed at 0,0 r2 that snapped to 2,2. AOT build loaded it in the quest list, the game started and
  played 3 turns, log clean apart from the usual missing sound card.
- Known limits: IDs are the highest + 1, so deleting the newest location and placing another reuses its ID (same as
  the other editor objects); this matters only once events point at locations.

## Phase 2 — done

Commit `8b9ccde8`, merged in `4193fb1e`. Client only (the engine part is phase 1).

- `MapEditorScreenViewModel.Locations.cs`: tool `MapEditorTool.Location` ("Locations", 16th, last in the grid).
  - Hover: green square of the current size with the name it will get; near the edge amber at the snapped place
    ("... at 1, 1 (moved inside the map)"). Over a zone: gold outline, "click selects it · Ctrl+click places a zone
    inside · Shift+click removes it".
  - Click on empty ground places (`PlaceLocationAt`, default name from the translated "Location " prefix), selects it
    and opens its row for the name; a click inside a zone selects it (`PickLocation`); Shift+click removes; Ctrl+click
    (in `MapEditorVisual`) places inside a zone.
  - Size: - / 3x3 / + in the tool settings and the + / - keys; with a zone selected they resize it (one undo step).
  - Every change is one undo step and only redraws the overlay (locations aren't in the world state, so no preview
    rebuild); Undo rebuilds the list.
- List (`IsLocationsOpen`, L key, "List (L)" button, opens with the tool): left overlay 340 px, "Locations (N)",
  search (culture-aware, ignores case), rows sorted by name (culture-aware) with size and position and a delete
  button. Click a row: the camera goes there (`TakeCameraTarget`, taken by `MapEditorVisual`); selecting on the map
  scrolls the list to the row. Double-click or a new zone: name edited in the row (Enter keeps, Esc cancels, leaving
  the box keeps; blank keeps the old name). Hovering a row outlines its zone in gold. The selection survives a search
  that hides its row. Economy panel, landmark palette and the list share the left side (opening one closes the others).
- Overlay (`MapEditorVisual.DrawLocations`, `MapEditorPlacementPreview.Locations`): pale blue squares, largest first;
  selected / hovered row gold; names in the outlined style of the game's path labels on each square's top corner tile.
  Drawn while the tool is on or the list is open. The hover line lists every zone on the tile with any tool
  ("Locations here: Northern Pass, Trap").
- Layout at 1366x768: with 16 tools the grid has 6 rows, which pushed other tools' settings out of view; tool rows are
  now 4 px shorter and the key help line lost "E: economy" (the button says "Economy (E)" anyway), so every tool's
  first setting shows again (checked in all 14 map editor screenshots).
- Tests: `MapEditorScreenViewModelTests.LocationToolTest` (hover/snap, place + name, resize limits, overlap picking,
  sorting, search, camera only from the list, hover line, Shift+click, undo, delete button, shared left side),
  `VisualTests.MapEditorLocationsTest` (5 shots: hover, naming, overlay, row hover, hidden). Non-visual suite 5214
  (was 5213); all 14 map editor visual tests pass (9 min).
- Real client (AOT): opened the mapgen map, the tool showed its 3 zones and list; placed a zone, typed "Ford of
  Bruinen" + Enter, Ctrl+S wrote `S600LO0004` (37,16 r1) with that Tscen name; Save and play ran 2 turns, no zones in
  the game, log clean apart from the missing sound card.
- Known limits: labels can overlap where zones' top corners are close; names longer than the panel are cut with an
  ellipsis in the list; the delete button doesn't ask (Ctrl+Z brings it back).

## Phase 3 — done

Commit `b31aa076`, merged in `b80e2f19`. Client only.

- `MapEditorScreenViewModel.LocationDrag.cs`: with the Locations tool the left button is now press / move / release
  (`PressLocation`, `MoveLocationPress`, `ReleaseLocationPress`, hooked into `BeginStroke` / `ContinueStroke` /
  `EndStroke`).
  - Press inside a zone (selected first, then smallest) and move to another tile: drag. The grabbed tile stays under
    the cursor (centre + cursor offset), the zone is selected, the preview shows the drop square (green, amber where
    it was snapped onto the map) with the old place dashed, and the hover line says "Northern Pass: 20, 14 -> 24, 13 ·
    Esc puts it back" (or "(snapped to the map edge)").
  - Release: `MoveLocation` (snapped, ID and name kept, one undo step); nothing happens when the drop is the old place.
  - Press and release on the same tile = the click from phase 2 (select, or place and name). A press on empty ground
    that moves away does nothing. Shift+click still removes at the press; Ctrl+click still places inside a zone.
  - Esc, the right button, or losing the mouse capture puts the zone back (`CancelLocationDrag`).
  - Edge scrolling during a drag (or a paint stroke) now follows the cursor: when the camera moves under a still
    mouse, `MapEditorVisual` feeds the new tile to `ContinueStroke` / `UpdateHover`.
- X/Y boxes under the size row, shown while a zone is selected: Enter or leaving a box moves it there (clamped to
  the map, then snapped); junk text is put back. The boxes follow drags, undo and selection.
- Tests: `MapEditorScreenViewModelTests.LocationDragTest` (off-centre grab, preview and dashed old place, drop,
  snapped drop past the edge, Esc, drag from empty ground, undo, X/Y boxes incl. snapping and junk), `LocationToolTest`
  updated to press + release, `VisualTests.MapEditorLocationsTest` step "3b-dragging". Non-visual suite 5215 (was
  5214); all 14 map editor visual tests pass (9 min 30 s).
- Real client (AOT, mapgen map): dragged Northern Pass with the mouse (mousedown, moves, mouseup); the green drop
  square, dashed old place and hover line showed during the drag, the zone was selected after the drop with X/Y
  filled in, and Ctrl+S saved `S600LO0001` at its new place (23,4).
- Known limits: with a zone selected the tool's help text sits below the X/Y boxes and needs the panel's scroll bar at
  1366x768; the X/Y boxes don't show the snapping until Enter.

## Phases

1. ~~Engine data, writer and loader round trip (small).~~ Done.
2. ~~Locations tool with placement preview, the overlay with labels, and the list with names edited in the row
   (medium to large; the list moved here because names live in it).~~ Done.
3. ~~Dragging with snap-to-fit and the X/Y boxes (medium).~~ Done.

## Open questions

1. ~~Default name, or ask for a name on placing?~~ Decided: default "Location N", editable in a name box on placing.
2. ~~What should the drag feel like?~~ Decided: grab anywhere, selected then smallest first, snap to the nearest
   fitting place on drop (see "Dragging").
3. ~~Where does the list go?~~ Decided: left overlay, alphabetical, search, L, names edited in the row.
