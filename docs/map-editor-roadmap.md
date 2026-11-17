# Gallean Upgrade – map editor: what's missing and next steps

Brainstorm notes, started 2026-11-16 on branch `gallean-upgrade` at `84072f5`; updated 2026-11-16 after landmark
placement (commit `2931c5c`), 2026-11-17 after the placement preview (commit `07c76c6`) and after roads in the engine
plus the MapFromPlan generator (commits `418e463`, `9d96d14`), after the road tool (commit `219ecd5`), and after
planning rods and the economy panel. Each section is meant as a separate task.

## Decisions so far

- **No AI changes for now.** Part of this work should be merged upstream, and changing the AI would get in the way.
  The AI idle problem on custom maps is documented in `docs/ai/notes-custom-map-ai.md`. It stays unfixed for now.
- Victory settings are a good idea, but not priority 1.
- Player settings, landmarks/roads/looks and items are all wanted. The order is still open (see "Open questions").
- **Landmarks block armies, like in the game.** The editor writes normal `SLmark` rows and the game treats them as it
  treats the original maps' landmarks. Decoration-only landmarks would need a change to the game rules (`WorldPlan`),
  which goes against "editor changes must not change the game". Landmarks of the mountain type (49 of them: 42 in
  Terrain, plus two cemeteries, the Unholy Gates and three elven houses) also make their tiles count as mountains, as
  in the original maps.
- **Roads are laid along a path, like an army's path in the game**: one click starts, the road to the cursor is shown
  with the game's road pieces (joined with existing roads, junction pieces where roads meet), the next click lays it.
  No brush size (a road is one tile wide). Roads still clear forests (amber in the preview), as in `SetRoad`.
- **Road INDEX like the original maps**: SRoad `INDEX` uses the original numbering (measured on all 13,227 road tiles of
  the 95 maps), not the neighbour mask. The game ignores it either way. The pack has one look per road piece, so `VAR`
  stays 0 (the original maps use 0 and 1).
- **Placement preview**: green/red/amber, on by default (checkbox and P key to switch it off), no ghost sprites for
  now. Its rules are the editor's placement rules; `Place*` run the same checks, so preview and click can't disagree.
- **Rods paint their first 2 tiles like in the game**: placing a rod turns 2 tiles into its race's land (and flips the
  3x3 of any resource they touch), as the game does on the turn a rod is planted. Removing a rod leaves the land, as
  in the game.
- **No neutral rods** for now: neutral land doesn't spread, so a neutral rod would only block other races. Maybe later.
- **Economy panel at Average difficulty**: income is shown at Average (no difficulty bonus, AI attitude 100 %). It
  also shows the race's base income from its capital, so totals match what the game pays on turn 1.
- **Starting gold and mana per race**, with a "Same for all races" button, in the economy panel.

## Already in the editor

- Terrain brushes: land, water, forest, mountains, clear.
- Capitals:
  - place and move them
  - set the garrison (the race's guardian always stays, 6 places in all)
  - set the stationed army (starts with the race's first leader; for a human player that leader is replaced by the
    one chosen when the game starts)
- Armies:
  - leader and units, with levels from each unit's own level up to 99
- Villages:
  - name, owner and level 1–5
  - garrison, and stationed army, on one screen
- Resources: gold mines and mana sources.
- Ruins: name, look, gold reward, one item reward, guards.
- Treasure bags: on land or water, up to 16 items.
- Sites:
  - merchant (items with stock)
  - mage tower (spells)
  - mercenary camp (units with levels)
  - trainer
- Landmarks (new):
  - all 156 of the game, in a picture palette split by the game's 4 categories (Misc 19, Buildings 32,
    Structures 50, Terrain 55), with a size filter (1x1 to 5x5)
  - on land or water (the game then draws the water picture), half in the water works too
  - click to place centred on the tile, click a landmark to remove it, Ctrl+Z to undo
  - the hover line and the right-click info show the landmark's name
- Right-click info for capitals and villages shows them as their owner sees them (editor only).
- Roads (new): tool "Road"
  - click a tile to start, move the mouse: the road to the cursor takes the cheapest way around water, mountains,
    capitals, villages, ruins, sites and landmarks, follows existing roads, and avoids forests when it can
  - it is drawn with the game's 16 road pieces before it is laid, outlined green (amber where it clears a forest);
    where it meets a road the pieces turn into junctions and crossroads
  - the next click lays it and the road carries on from that tile; the same tile again or Esc stops
  - Shift+click removes a road tile; a click where a road can't go, or can't reach, says why; Ctrl+Z undoes
- Placement preview (new), for every tool:
  - green tiles where the object or brush goes, red where a tile blocks it, amber where a tile visibly changes
    (forest cleared, a whole mountain removed, water turned into land)
  - the hover line says what a click does ("Village (4x4): blocked by a capital at 10, 6", "Cemetery: click removes
    it", "Water brush, size 1: paints 9 tiles · removes 1 mountain")
  - a gold outline (or the game's hover cursor) on an object a click opens or removes; moving a capital outlines its
    current place

## Missing: things you can't place yet

| Object | Notes |
| --- | --- |
| Rods | terraforming rods; planned, see "Task: rods and the economy panel" |
| Locations | named zones (see below) |
| Events | scenario scripting (see below); every custom map is saved with no events |
| Army templates | armies that events create |

## Missing: settings on things you can place

- **Map:**
  - There's no description box, so every map says "A custom map."
  - The briefing is fixed ("Destroy the enemy capitals.").
  - There are no victory conditions. This is also why the AI never gets map goals: it only gets them from victory
    events.
- **Players:**
  - Starting gold and mana are fixed at 500 and 1500 (planned in "Task: rods and the economy panel").
  - There are no starting buildings or spells.
  - You can't choose human or computer per race, or set the difficulty.
  - All races get the same relations, and the lord type is fixed.
- **Armies:**
  - Leaders can't carry items.
  - The order is always normal, and neutral armies always stand still.
  - There are no armies on water and no boats.
  - You can't name leaders or set AI priority.
- **Terrain:** mountain and forest looks are random and can't be picked.
- **Ruins:** gold and one item only, no mana.
- **Villages:** no riot, protection or growth settings (minor).
- **Landmarks:** no custom description (the scenario's `DESC_TXT`; no original map uses it either).

---

## Task: player settings

Ideas:
- per race: human or computer, and starting gold and each mana
- lord type: warrior, mage or guildmaster
- buildings already built and spells already known at the start
- diplomacy between each pair of races
- the map's default difficulty

UI idea: one "Players" screen, opened from a button in the editor.

Technical pointers:
- Written in `CustomMapWriter.BuildRows`:
  - player rows (`Splayer`: bank, lord, IS_HUMAN, ALWAYSAI, buildings, known spells)
  - `StartingGold`/`StartingMana` constants and `GetStartingBank`
  - relations (`SRel`), currently all set to the same value
- `CustomMapPlayer` (in `CustomMap.cs`) would get the new fields. Save/load goes through `CustomMapStore.FromState`.

Starting gold and own mana per race moved to "Task: rods and the economy panel".

## Task: rods and the economy panel

Full description: `docs/map-editor-rods-economy.md`. Summary:
- Rod tool: pick a playable race, click to place (game rules: not on water, mountains, structures, armies or bags; no
  other rod within 2 tiles), the 2 tiles the rod converts and any flipped resources shown amber, Shift+click removes,
  Ctrl+Z undoes. The writer gets `SRod.json`; no original map has rods, so the loader's rod path gets its first test.
- Economy panel: one block per race laid out like the game's "Income per turn" table (resources of each type on the
  race's land, capital income, resources income, total at Average difficulty), land %, rods, plus a block for
  resources on nobody's land. Numbers come from `WorldGetPlayerInfoCommand` on the editor's preview state, so they
  match the game. Clicking a race outlines its resources and rods.
- Starting gold and own mana per race in the panel, "Same for all races".
- Phases: rods in engine and writer (~1 day), rod tool (~0.5 day), economy panel (~1 day), starting bank (~0.5 day).

## Task: items (one shared item editor)

Items can sit in several places in the scenario data:
- a capital's or village's inventory (scenario `SInventr`, keyed by the fortification)
- a treasure bag (done, also `SInventr`)
- a ruin's reward (one item for now; could become several)
- an army leader:
  - 7 equipment slots: banner, tome, 2 battle items, 2 artifacts, boots (fields on the scenario stack row)
  - a carried inventory

Idea: build one item editor component and reuse it for leaders, capitals, villages, bags and ruins.
The editor already has an item picker (`MapEditorListPickerViewModel`) and an item list row
(`MapEditorStockRowViewModel`).

## Task: landmarks — done

What was built (branch `upgrade/map-editor-landmarks`, commit `2931c5c`, merged into `gallean-upgrade`):
- Engine: `CustomMapLandmark` (in `CustomMapObjects.cs`), `CustomMap.PlaceLandmark`/`RemoveLandmark`/`GetLandmarkAt`.
  Rules: inside the map; no overlap with capitals, villages, ruins, sites, other landmarks or mountains; no army,
  resource or bag under it; a forest under it is cleared. Land and water can still be painted under a landmark;
  forests and mountains can't. Nothing else (capital, village, ruin, site, army, resource, bag) can be put on one.
- Writer: `SLmark.json` (IDs `MM` + n, like the original maps) and `SMapEl.json` rows for the tiles of mountain-type
  landmarks (the loader only reads map elements for those). `FromState` reads landmarks back and clears the mountain
  marks the loader put on mountain-type landmark tiles, so a removed landmark doesn't leave invisible mountains.
  `ApplyTerrain` also refreshes landmarks, so they show at once.
- Client: tool "Landmark"; the palette (category and size boxes, pictures from the game's landmark sprites) sits over
  the left side of the map so it has the full height at 1366x768 too. `MapEditorLandmarkOption`, library list from
  `CustomMapStore.ListLandmarks`.
- Tests: `CustomMapTests.LandmarkRulesTest`, `LandmarkSaveAndLoadTest`, landmarks added to `VillagePlayTest` (AI plays
  12 turns around them), `MapEditorScreenViewModelTests.LandmarkTest`, `VisualTests.MapEditorLandmarksTest`.

Known limits / possible follow-ups:
- While the palette is open it covers the map's left edge, so edge scrolling to the left only works with WASD, the
  arrow keys or the middle button there.
- ~~No footprint preview under the cursor yet.~~ Done for every tool: see the placement preview task.

## Task: placement preview under the cursor (all tools) — done

What was built (branch `upgrade/map-editor-placement-preview`, commit `07c76c6`, merged into `gallean-upgrade` as
`f78edb6`): coloured tiles under the mouse for every tool and brush, the hover line saying what a click does, and an
outline on objects a click opens or removes. Engine `CustomMap.Preview*` methods (the `Place*` methods run the same
checks first; `PlaceCapital` now returns an error reason), client `MapEditorPlacementPreview` drawn by
`MapEditorVisual` after the world render. Tests: `CustomMapTests.PreviewAgreesWithPlaceTest` (every tile, every tool,
against `Place*` on a copy), `PreviewPaintAgreesWithPaintTest`, `PreviewTileReasonsTest`,
`MapEditorScreenViewModelTests.PlacementPreviewTest`, `VisualTests.MapEditorPlacementPreviewTest`.

Full description, what differs from the plan and known limits: `docs/map-editor-placement-preview.md` ("As built").

## Task: roads and looks

Done so far (commit `418e463`): roads in `CustomMap` (`SetRoad`: open or forest land, the forest is cleared; never on
water, mountains, structures or landmarks; land brushes keep a road, water and forest brushes replace it) and in the
writer (`SRoad.json`, one row per tile, INDEX = neighbour mask N1 E2 S4 W8 like the original maps; the loader only
reads the position and the renderer joins the 16 pieces itself). Test: `CustomMapTests.RoadTest`.

Road tool — done (branch `upgrade/map-editor-road-tool`, commit `219ecd5`):
- Engine (`CustomMapRoads.cs`): `GetRoadBlock` (the one rule for where a road can go; `SetRoad` uses it),
  `FindRoadPath` (A* over the 4 tile edges, because road pieces only join across edges; steps cost 10 on a road, 20 on
  open land, 30 through forest, plus up to 3 for straying from the straight line, so equal paths come out as an even
  staircase), `PreviewRoad`, `LayRoad`, `ToWorldMap(extraRoads)` (the map shown while a road is planned).
- Client (`MapEditorScreenViewModel.Road.cs`): tool "Road" (14th in the tool list), start tile, the planned road put
  into the shown map so the game renderer draws the real pieces, preview tiles as outlines only
  (`MapEditorPlacementPreview.OutlineOnly`) so the road stays visible, Esc in `MapEditorVisual`, Shift+click.
- Writer: SRoad `INDEX` in the original maps' numbering.
- Tests: `CustomMapTests.RoadPathTest`, `MapEditorScreenViewModelTests.RoadTest`, `VisualTests.MapEditorRoadsTest`.

Known limits:
- A road across the screen (left-right or up-down) is always a zigzag: pieces join only across tile edges, which are
  the screen diagonals.
- A lone road tile shows the crossroads piece (the pack's `road/none` is the same picture).
- No bridges: roads stop at water.

Still to do: the looks.

Ideas:
- Roads painted like a brush; the 16 road pieces join up automatically.
- Pick the mountain look (4 sizes with 11/10/9/8 looks) and the forest look, or keep random.

Technical pointers:
- scenario tables `SRoad` (roads), `SMounts` (mountains)
- sprites in the asset pack (see `~/work/gallean-tools/map_object_catalogue.py` for the inventory)
- the landmark palette (`MapEditorLandmarkOption` + the `palette` ListBox in `MapEditorScreenView.axaml`) can be
  reused for a mountain/forest look picker

## Task: maps from a description (MapFromPlan) — done

What was built (commit `9d96d14`): `tools/MapFromPlan`, a console tool that turns a JSON map plan into a custom map.
The plan describes a land in miles on a north-up map (regions, seas, rivers with fords, ranges with passes, hills,
forests, roads, capitals, villages, ruins, sites, landmarks, mines, neutral army templates by tier, treasure). Objects
go through `CustomMap.Preview*`/`Place*`, so a generated map follows the same rules as a hand-made one, opens in the
editor and plays as a custom quest. The tool prints what it couldn't place, travel costs and gate checks, and writes a
labelled overview PNG. Usage and plan format: `tools/MapFromPlan/README.md`.

First plan: `tools/MapFromPlan/plans/middle-earth.json` (320x320, 7 miles per tile; Empire = Gondor and Rohan, elves,
dwarves, Legions = Mordor with Harad and Rhun, Undead = Dol Guldur, Minas Morgul and Angmar). Checked: Mordor is sealed
except for the Black Gate, Cirith Ungol and the Morgul pass; the Misty Mountains only open at the High Pass, the
Redhorn Pass and the Gap of Rohan; the map loads in the published client and in the editor, and turn 1 to 2 runs.

Known limits:
- One player per race, so allies of one race share a player (Gondor and Rohan are both the Empire).
- Capitals keep the writer's "<Race> Capital" name (there is no name field on `CustomMapPlayer` yet).
- Only existing objects: hobbits are peasants, Ents are the elves' Ent Minor.

## Task (later): victory settings

Ready-made events instead of a full event editor, for example:
- destroy all enemy capitals
- capture a certain city
- survive a number of turns

Side effect: the AI would then get map goals (from turn 60/50/40/30, easy to very hard), without any AI code change.

## Task (later): full events and locations

**Locations** are named zones on the map: a center point and a radius. The original maps use them a lot (60–75 rows
in many scenarios). Events use them ("when an army enters the Northern Pass"), and so do army orders ("move to" or
"defend this location").

**Events** are the scenario's script. Each event has conditions, which races it applies to, whether it happens once,
and effects. The engine already loads all of it:

- **17 condition kinds:**
  - a turn number or frequency
  - entering a zone, entering a city, owning a city
  - destroying an army
  - owning an item, a leader owning an item
  - diplomacy relations, an alliance
  - looting a ruin, transforming land, visiting a site
  - an army reaching a location, an army reaching a city, an item reaching a location
  - an army existing
  - a variable in a range
- **24 effect kinds:**
  - win or lose
  - popup message
  - give an item, give a spell, destroy an item
  - create an army, remove an army, move an army, move beside, change an army's order
  - battle
  - change diplomacy, make an alliance, change owner
  - change terrain, change a landmark, remove a landmark, remove a mountain
  - reveal the map (unfog)
  - cast a spell, cast a spell on the map
  - change a variable
  - switch other events on/off
  - change objective

"Victory settings" are a few ready-made events. "Full events" means a real editor for building any condition and
effect, which would make story maps like the campaigns possible. Same data, very different amount of work.

Note: placed landmarks have stable IDs (`S6xxMM0001`...), so "change a landmark" and "remove a landmark" effects can
point at them once events exist.

---

## Open questions

1. Player settings: which of the ideas above, and is one Players screen right?
2. Items: should the leader's 7 slots work like the in-game equipment screen, or just be a list?
3. ~~Landmarks: should they block armies like in the game, or be decoration only?~~ Decided: they block, like in the
   game (see "Decisions so far"). Say if you want decoration-only after all; that needs a game rule change.
4. ~~Which comes next: player settings, items, or the mountain/forest looks?~~ Next: rods and the economy panel.
   After that, still open: player settings, items, or the mountain/forest looks.
5. Landmarks: is the palette over the left of the map fine, or should it go into the right panel (it only fits there
   with scrolling at 1366x768)? ~~Is a footprint preview under the cursor worth adding?~~ Done, for every tool.
6. Placement preview: should the "Placement preview (P)" checkbox move out of the scrolling panel (next to Undo/Save),
   so it is always visible at 1366x768?
7. Capital names: add a name to `CustomMapPlayer` (a field on the capital screen and in the writer), so generated and
   hand-made maps can have "Minas Tirith" instead of "Empire Capital"?
8. ~~Roads: is the road brush the next editor task?~~ Done as a path tool (click, move, click).
9. Roads: should a road also be allowed to run onto a village's or capital's entrance tile, so it visibly reaches the
   gate? Today structures block roads, as before.
10. Starting bank: only gold and the race's own mana (planned), or all five mana types per race?
11. Economy panel: is an "after N turns" estimate of the land spread wanted, and a hotkey for the panel?
