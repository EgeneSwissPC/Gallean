# Gallean Upgrade – map editor: locations

Planned 2026-11-17 on branch `gallean-upgrade` at `2ce057c1`. Nothing is built yet. This file tracks the work and
its progress: one "Phase N — done" section is added per phase.

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

## Phases

1. Engine data, writer and loader round trip (small).
2. Locations tool with placement preview and the overlay (medium).
3. Locations list panel (small).

## Open questions

1. ~~Default name, or ask for a name on placing?~~ Decided: default "Location N", editable in a name box on placing.
2. ~~What should the drag feel like?~~ Decided: grab anywhere, selected then smallest first, snap to the nearest
   fitting place on drop (see "Dragging").
3. ~~Where does the list go?~~ Decided: left overlay, alphabetical, search, L, names edited in the row.
