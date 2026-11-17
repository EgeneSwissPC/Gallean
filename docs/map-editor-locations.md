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
- **Default names "Location 1", "Location 2" ...** (the next unused number). After placing, a small name box opens
  with that name filled in and selected: type a new one, or press Enter / Save to keep it. Renaming later is in the
  tool settings.
- **Locations can be dragged to move them.** How the drag works is being worked out (open question 2).

## What the user would see

### Locations tool

- A "Locations" tool in the tool list (Russian label must fit ~90 px).
- Hovering shows the square that would be placed (placement preview: green; red where the square leaves the map).
- Click: places a location centred on the tile with the current radius and a default name ("Location 1", ...), then
  opens the name box.
- Radius 0–3 in the tool settings, also with + / −. Not the scroll wheel: it zooms the map in the editor.
- Click an existing location's centre: selects it; the settings then show its name (editable), radius, position and
  a Delete button. Shift+click deletes. Ctrl+Z undoes every change.
- Locations may overlap and may cover water, mountains and objects (the original maps do this); they block nothing.

### Overlay

- Translucent squares with the name, drawn by the editor only, while the Locations tool is on or a "Show locations"
  toggle is set.
- The selected location gets the gold outline used for objects a click opens.

### Locations list

- A list of all locations: name, position, radius. Clicking a row moves the camera there and selects it.
- Quest-list style (`ListBox Classes="select-list"`), not a palette list.
- Where it goes (right panel or left overlay, which then hides the landmark palette and economy panel) is decided in
  phase 3 against the 1366x768 screenshot.

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
2. Dragging is wanted: what should the drag feel like (grab anywhere or the centre, overlaps, cancel, edge scroll)?
3. Where does the list go (right panel vs left overlay)? Decided in phase 3.
