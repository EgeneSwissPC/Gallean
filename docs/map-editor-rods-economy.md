# Gallean Upgrade – map editor: rods and the economy panel

Feature description, written 2026-11-17 on `gallean-upgrade` at `4fc0da0`. Nothing is built yet. Linked from
`map-editor-roadmap.md` ("Task: rods and the economy panel").

## The problem today

- **Rods can't be placed.** The game knows rods (`WorldRod`: ID, owner, position; `SRod.json`), plants them with
  rod-planter leaders, spreads land from them every turn, and draws them (`WorldRenderComposer.Parts.Rods`). The editor
  has no rod tool and the writer writes no `SRod.json`. None of the 112 bundled scenario folders has an `SRod.json` either, so the
  scenario loader has never read one from a map file (`ScenarioLoader.LoadRods` exists but is untested on real data).
- **There's no overview of who owns which resource.** A gold mine or mana source has no owner field: it pays whoever
  owns the land under it (`PlayerIncomeCalculator`, `WorldPlan.GetLandOwner`: the race whose native terrain the tile
  is). The editor already knows this per tile (`CustomMap.GetLandOwner`; with the resource tool the hover line says
  "income goes to Empire"), but only for the tile under the cursor while placing. There's no list of how many mines and mana sources each race has, and
  no total income.
- **Starting gold and mana are fixed.** `CustomMapWriter.StartingGold`/`StartingMana` give every race 500 gold and 1500
  of its own mana (`GetStartingBank`).

## How it works in the game (checked in the code)

Rods:
- Planting (`WorldCreateRodCommand`): not on water or a mountain; not on a structure (capital, village, ruin, site,
  landmark, resource), an army or a bag; no other rod within 2 tiles (`RodSpacingRange` = 2, Chebyshev distance, so a
  5x5 square around every rod is closed to other rods). Forests and roads are allowed. The leader rules (rod-planter
  ability, in range 1, not inside a city, 150 gold) only apply to planting in a game.
- The turn it's planted (`WorldEngine.Events.RodCreated` -> `ExpansionCalculator.ExpandRodTerrain`), the rod turns
  2 tiles (`RodTerrainTilesPerTurn`) into its race's land; every turn after that, 2 more (`ExpandPlayerTerrain`).
  The spread is a breadth-first search from the rod over the 4 tile edges: it walks through tiles that are already the
  race's land and converts the first 2 that aren't. It never converts water or mountains, never enters other rods'
  tiles, resource tiles, tiles of capitals/villages that a playable race owns, or mountain-type landmarks.
- When converted land touches a resource (distance 1), the whole 3x3 around the resource flips with it
  (`TerrainTransformCalculator.EnqueueResourceNode`). That's how a mine changes owner.
- An enemy rod next to a mine protects it: other races' spread can't enter the 3x3 around that mine
  (`ExpansionCalculator.HasBlockingRod`).
- Rods block armies (`WorldPlan.BuildObstacleCells`), so a rod on a road closes the road.
- A defeated race's rods are removed (`WorldEngine.Rules.PlayerDefeatedDestroyRods`). Removing a rod doesn't change
  the land back.

Income per turn (`PlayerIncomeCalculator.GetIncomeInfo`, `WorldGetPlayerInfoCommand`):
- Base income from the race (`Grace.json INCOME`): 100 gold and 25 of the race's own mana for every playable race.
- Plus 50 per resource on the race's land (`ResourceNodeIncome`), of the resource's own type (a death mana source
  on Empire land gives the Empire death mana).
- Scaled by difficulty (`INCOME_E/A/H/V` = +30 / 0 / 0 / 0 %) and, for computer players, by the AI attitude
  (`D_EASY_M/D_AVG_M/...` = 70 / 100 / 130 / 145 % for all 7 attitudes). **At Average both are 100 %**, so the
  Average numbers are the plain sum.
- Starting bank: the scenario's `Splayer` bank, scaled at load by difficulty (`StartingResourcesPercent*`: Easy 300 %,
  Average 100 %, Hard 50 %, Very hard 25 %). Custom maps are saved as Average.
- The game already has an "Income per turn" table in the world screen (`WorldIncomeViewModel`,
  `WorldIncomeRowViewModel`, markup in `WorldScreenView.axaml`): one row per resource type with icon, name, capital
  income, resources income and total.

## What the user would see

### Rod tool

- A new tool "Rod" in the tool list, with the owner picker the army and village tools already use, limited to the
  playable races on the map (no neutral rods, see "Decisions").
- Hover: the placement preview shows the rod tile green, or red with the reason ("Rod: another rod at 12, 8 is too
  close", "Rod: on water", "Rod: blocked by a village at 10, 6"). The 2 tiles the rod will turn into its race's land
  are amber, and so are the 3x3 squares of any resource that flips with them. The hover line says what a click does:
  "Rod (Empire): turns 2 tiles into Empire land · takes 1 gold mine from nobody".
- Click: places the rod and turns the 2 tiles (plus flipped resource squares) into the race's land, exactly as the
  game does on the turn a rod is planted. The race's rod sprite appears at once.
- Shift+click on a rod removes it. The land stays as it is, like in the game; Ctrl+Z undoes a placement completely.
- Right-click info on a rod: "Rod", owner.
- Other tools respect rods: nothing else can be put on a rod tile, roads go around rods (they block armies), the
  water and mountain brushes leave rod tiles alone (like resources). The land and forest brushes can paint under a
  rod.

### Economy panel

- An "Economy" button in the editor opens a panel over the map (same place and full-height trick as the landmark
  palette, so it fits at 1366x768).
- One block per race on the map, laid out like the game's "Income per turn" table so it looks like the game:
  - header: race name, land tiles and % of the map (the game's `GetTransformedPercentage`: tiles that aren't water
    or mountains), number of rods
  - one row per resource type with icon and name: number of resources of that type on the race's land, capital
    income, resources income, total per turn, e.g. "Gold · 3 mines · 100 + 150 = +250"
  - starting bank as number boxes (0-9999): gold and all five mana types. By default gold 500, the race's own mana
    1500 and the other four manas 0; any of them can be set by hand. A "Same for all races" button copies the
    selected race's bank to every race, with own mana mapped to own mana (Empire's life mana amount becomes the
    Undead's death mana amount) and the other manas copied as they are
- A last block "Nobody's land": resources standing on neutral land, by type, so unclaimed mines are easy to spot.
- All income at **Average difficulty** (see "Decisions"). A note under the panel says so ("Easy: +30 % income for
  humans; computer players earn 70-145 % depending on difficulty").
- The numbers update live after every paint, placement, rod or undo.
- Clicking a race's block outlines its resources and rods on the map (gold outline, as the placement preview does for
  objects); clicking again or closing the panel clears it.
- Hotkey **E** opens and closes the panel (E is free in the editor; WASD, arrows, P and Ctrl+Z/S are taken).

### Land spread preview (turns)

- At the top of the panel, a turn bar built like the unit level bar (`MapEditorUnitLevelBar`): `-10` `-` **Turn N**
  `+` `+10`, plus "Back to the map" (turn 0). Turn 0 is the map as it is saved.
- Each step redraws the whole map with the land as it would be after N turns of spread: capitals, villages and rods
  grow every turn (engine defaults: capital 10 tiles, villages 15/15/20/20/25 by level, rods 2), mines flip with
  the land, and the panel's numbers (land %, resources per race, income) are recalculated for turn N. So you can
  click +1, +1, +10 and watch who gets which mine when.
- Assumptions, shown as a note: nobody moves, fights, captures, plants or removes rods; only the spread happens.
- While N > 0 the map is a preview: editing tools are disabled (the hover line says "Turn N preview: go back to the
  map to edit"), and any edit attempt or closing the panel returns to turn 0. The preview never changes the saved map.
- Large maps: each turn runs the real spread once per race; it's computed on demand and cached per turn (turn 20
  reuses turn 10 and adds 10), so +10 on a 512 map should stay well under a second; measured in phase 5.

## Technical approach

Editor-only code, as in the earlier tasks: the game rules aren't touched.

Engine (`src/Gallean.Engine/MapEditor/`):
- `CustomMapRod(int Id, WorldPosition Position, WorldRace Owner)` in `CustomMapObjects.cs`; `CustomMap` gets a
  `rods` list (constructor, `Clone`, `Rods`), `GetRodAt`.
- New `CustomMapRods.cs`: `GetRodBlock` (the one rule, used by `PreviewRod` and `PlaceRod`), `PreviewRod` (tile
  statuses, the 2 converted tiles, the flipped resources), `PlaceRod` (place + paint), `RemoveRod`.
- The paint is an editor port of `ExpansionCalculator.ExpandAround` + `TerrainTransformCalculator.TransformCells`
  on `CustomMap` cells (budget = `GlobalVariables.RodTerrainTilesPerTurn`, read from the defaults, not hard-coded).
  `ExpansionCalculator` needs a `WorldState` and an engine context, which `CustomMap` doesn't have; instead an
  agreement test checks the port against the real thing (see "Testing").
- Existing rules learn about rods: `GetRoadBlock`, `GetBrushBlock` (water/mountain brushes skip rod tiles), and the
  site checks behind every object `Preview*`/`Place*` (a new `CustomMapTileReason.Rod`).
- Writer: `SRod.json` (`ROD_ID` = `MakeId(prefix, "RO", n)`, `OWNER` = the race's player ID, `POS_X`, `POS_Y`).
  The loader takes any ID; whether the original game's rod ID type is `RO` should be checked against a save with a
  rod, it only matters for looks.
- `CustomMapStore.FromState` reads `state.Rods` back (owner via the player's race); `ApplyTerrain` also refreshes
  `Rods`, so a placed rod shows without a full preview rebuild.
- Starting bank: `CustomMapPlayer` gets `StartingBank` (a `WorldResourceSet`: gold + all five manas; default 500
  gold, 1500 own mana, 0 for the rest); `GetStartingBank` writes it as is. `FromState` reads them from `state.Players[...].Resources`; the map is saved as Average, so the load
  scaling is 100 % and the round trip is exact.
- Economy numbers: a new `CustomMapEconomy` record built from the editor's preview `WorldState` with
  `Difficulty = Average`, calling `WorldGetPlayerInfoCommand` per player (the same code the game's income table uses)
  and counting resources per type with `WorldPlan.GetLandOwner`. Using the real calculator means the panel can't
  disagree with the game.

Client (`src/Gallean.Client/Screens/MapEditor/`):
- `MapEditorScreenViewModel.Rod.cs`: the tool, owner picker, Shift+click, hover line, undo.
- `MapEditorScreenViewModel.Economy.cs` + `MapEditorEconomyBlockViewModel` (one per race) reusing
  `WorldIncomeRowViewModel`; the panel markup copied from the world screen's income table.
- Highlight: extra outline tiles in `MapEditorPlacementPreview`.
- Spread preview: `CustomMapSpreadPreview` (engine side) takes the editor's preview `WorldState` and, per turn, calls
  `ExpansionCalculator.ExpandPlayerTerrain` for every player in turn order, the same call the game makes in
  `WorldEngine.Events.TurnStarting`; results cached by turn. The client shows `state with { Map = turnMap }` (like the
  road ghost map) and feeds the same state to the economy numbers. Turn bar view model mirrors the level bar
  (`CanTurnDown`, `TurnUpTenCommand`, ...).
- New strings in all 7 catalogs (ru translated).

## Testing

- `CustomMapTests.RodRulesTest`: water, mountain, each structure kind, army, bag, rod spacing (distance 2 blocked,
  3 allowed), forest and road allowed, neutral owner refused.
- `CustomMapTests.RodPaintAgreesWithGameTest`: on several hand-made layouts (open land, land already partly the
  race's, a mine 2 tiles away, a mine next to an enemy rod, a village and a mountain-type landmark in the way) the
  editor's paint gives the same cells as `ExpansionCalculator.ExpandRodTerrain` on the loaded preview state.
- `CustomMapTests.PreviewAgreesWithPlaceTest` extended with the rod tool (every tile).
- `CustomMapTests.RodSaveAndLoadTest`: write, load through `ScenarioLoader`, `FromState`: same rods, owners, land;
  the first real test of `LoadRods` on a written map.
- `CustomMapTests.RodPlayTest`: the AI plays 12 turns on a map with rods (like `VillagePlayTest`); rods spread 2
  tiles per turn.
- `CustomMapTests.EconomyTest`: counts and income per race against hand-computed numbers (base 100 gold + 25 mana,
  50 per resource), including a resource on neutral land and one flipped by a rod.
- `CustomMapTests.StartingBankSaveAndLoadTest` (all six amounts, non-default other manas).
- `CustomMapTests.SpreadPreviewAgreesWithGameTest`: N turns of the preview give the same map as N turns of the real
  `WorldEngine` turn start with no other actions (armies kept still); `MapEditorScreenViewModelTests.SpreadPreviewTest`
  (turn bar, tools disabled, edit returns to turn 0); spread benchmark on the 512 map.
- `MapEditorScreenViewModelTests.RodTest`, `EconomyPanelTest` (live update after paint/undo, "Same for all").
- `VisualTests.MapEditorRodsTest`, `MapEditorEconomyPanelTest` (also at 1366x768).
- Real client run: place rods for two races, save, play the map as a custom quest, check the rods are drawn and spread
  on turn 2, and the in-game income table matches the panel.

## Phase 1 — done

What was built (branch `upgrade/map-editor-rods`, commit `ca0330f`, merged into `gallean-upgrade` as `367c0bb`):
- Engine `CustomMapRods.cs`: `CustomMapRod(Id, Position, Owner)` (in `CustomMapObjects.cs`), `GetRodBlock` (the one
  rule), `GetRodTooClose`, `PreviewRod`, `PlaceRod`, `RemoveRod`, `GetRodAt`; `RodSpacing` and `RodTilesPerTurn`
  come from `GlobalVariables` defaults (2 and 2). The spread is a port of `ExpansionCalculator.ExpandAround` and
  `TerrainTransformCalculator.TransformCells`.
- New tile reasons: `ClaimsLand` (amber: land a rod takes) and `Rod` (a rod in the way, or one too close). Capitals,
  villages, ruins, sites, landmarks, armies, resources and bags refuse a rod's tile; roads go around rods; water and
  mountain brushes leave them alone.
- `CustomMapLandmark` remembers the mountain type (`Mountain`), set by `PlaceLandmark` and `FromState`, because the
  spread can't enter mountain-type landmarks.
- Writer: `SRod.json`, IDs `RO` + n (`CustomMapWriter.GetRodId`), owner = the race's player (`PL` + slot).
  `FromState` reads rods back (only playable races' rods); `ApplyTerrain` refreshes `Rods`, so a placed rod shows at
  once.
- Client: the hover line names a rod as "a rod" when it blocks something (new string in all 7 catalogs).
- Tests (suite 5199 -> 5204): `CustomMapTests.RodRulesTest`, `RodResourceTest` (3x3 flip, a rod guarding a mine,
  capitals stop the spread, walking over own land), `RodPaintAgreesWithGameTest` (3,996 placements on every free tile
  for both races, terrain compared with the game's `ExpansionCalculator.ExpandRodTerrain` on the loaded map; 159 of
  them took a resource's 3x3 along), `RodSaveAndLoadTest`, `RodPlayTest` (AI, 9 player turns: Empire land near its
  rod 2 -> 16 tiles), plus rods in `PreviewAgreesWithPlaceTest`. Two deliberate bugs in the port (rod-guarded mines,
  neutral villages) were each caught by the agreement test.
- Real client run (AOT build on the Debian 12 image, test map from `mapgen` with an Empire rod next to an infernal
  mine): the editor draws the rod and its right-click info says "Rod, owner Empire"; in the game the rod is drawn and
  the Empire earns +50 infernal and +50 rune mana per turn from turn 1.

Found on the way:
- Mines next to each other flip together: the 3x3 taken along around one resource can touch the next resource,
  whose 3x3 then flips too (the game's `TransformCells` keeps going). In the test map the rod's 2 tiles took the
  infernal mine and, through it, the rune mine 2 tiles further. The preview will show this as amber tiles.
- The loader's rod path works on written maps (no original scenario has rods).

Known limits:
- There is no Rod tool in the editor yet (phase 2); maps get rods only through code (`mapgen`, `MapFromPlan` could
  use `PlaceRod`).
- Whether the original game's rod ID type is `RO` is unchecked; the loader takes any ID.

## Phase 2 — done

What was built (branch `upgrade/map-editor-rod-tool`, commit `97fe196`, merged as `1e3fd51`):
- Tool "Rods" (15th in the tool list) with a "Rod owner" box listing the races on the map
  (`MapEditorScreenViewModel.Rod.cs`).
- Hover: the rod tile green, the land it claims amber, and the line says what a click does: "Rod (Undead Hordes):
  fits · turns 10 tiles into Undead Hordes land · takes Gold mine from nobody". When another rod is too close, both
  rods are red and the line says "another rod at 20, 20 is too close (rods need 2 free tiles between them)"; other
  blocks read like the other tools ("blocked by a capital at 8, 8").
- Click places the rod (terrain-only commit: the rod and its land show at once, no preview rebuild). Clicking a rod,
  or Shift+click on it, removes it; its land stays. Ctrl+Z undoes either.
- The tool list now has 3 columns (5 rows instead of 8), so the settings of every tool stay visible at 1366x768.
  The rod tool is labelled "Rods" (ru "Жезлы") because the game's "Rod" is "Жезл власти" in Russian, too long for a
  column.
- Fix found on the way: the editor drew the game's terrain expansion (the fade-in of new race land) at time zero,
  so after any change of race land (land brush, capital, village, rod) the last changed tiles kept showing the old
  land, and cleared trees stayed visible, until the next change. `MapEditorVisual` now draws expansions as finished,
  so changes show at once (no fade in the editor).
- Tests (suite 5204 -> 5205): `MapEditorScreenViewModelTests.RodTest` (owners, hover states and lines, placing,
  too close, a mine taken, other tools blocked by a rod, removing by click and Shift+click, undo, save and reopen),
  `VisualTests.MapEditorRodsTest` (hover, too close, a mine's 3x3, both placed); all 12 map editor visual tests
  pass with the 3-column tool list.
- Real client run (AOT build, Debian 12 image): Rods tool selected from the new tool grid, hover line "Rod (Empire):
  fits · turns 2 tiles into Empire land", rods planted on a forest and on open land, drawn at once with their land,
  hovering one says "Rod of Empire: click removes it (its land stays)".

## Phase 3 — done

What was built (branch `upgrade/map-editor-economy`, commit `687944d`, merged as `fedcdeb`):
- Engine `CustomMapEconomy.cs`: `CustomMapEconomy.Calculate(state, context)` on the editor's preview state, with
  `PlayerIncomeCalculator` (the code behind the game's "Income per turn" table), at Average difficulty and as a human
  player. Per race: land tiles, land % (`TerrainTransformCalculator.GetTransformedPercentage`, the game's "transform
  land" share), rods, and per resource type the number of resources on its land, capital income, resources income
  and total; plus the resources on nobody's land.
- Client `MapEditorScreenViewModel.Economy.cs`: the "Economy (E)" button above Undo/Save and the E key open a panel
  over the left of the map (the landmark palette steps aside while it is open). One block per race, laid out like
  the game's income table (icon, name, Count, Capital, Sources, Total), then "Unowned land". Rows the race neither
  owns nor earns are left out. The panel is recomputed only while open and only when the shown state changed, so
  hovering costs nothing.
- Selecting a race (it uses the game's quest-list row style) outlines its resources and rods on the map in the
  hover-cursor gold, whatever tool is active; click again to clear. The outline follows changes.
- "Unowned land" includes resources on the land of races that aren't on the map (e.g. Undead land on a map without
  the Undead): nobody earns from those.
- Tests (suite 5205 -> 5207): `CustomMapTests.EconomyTest` (counts by land, 100 + 25 base income, 50 per
  resource, rods, land %, nobody's land incl. a missing race's land, same numbers as `WorldGetPlayerInfoCommand` for a
  human at Average, an Easy map shows the same), `MapEditorScreenViewModelTests.EconomyPanelTest` (live after land
  paint, resources, a rod taking a mine, undo; outline; palette; close), `VisualTests.MapEditorEconomyTest` (E key,
  panel, a selected race's outline); all 13 map editor visual tests pass.
- Real client run (AOT build, Debian 12 image, `mapgen` test map): E opens the panel; Empire Gold 1 · 100 · 50 ·
  +150, Life mana 1 · 25 · 50 · +75, Infernal and Rune mana 1 · 0 · 50 · +50 each (both taken by its rod), Undead
  Gold 0 · 100 · 0 · +100 and Death mana 1 · 25 · 50 · +75, unowned: 1 Grove mana; selecting the Empire outlines its
  4 resources and its rod.

## Phase 4 — done

What was built (branch `upgrade/map-editor-starting-bank`, commits `8ac6ac2` and `97db6a5`, merged as `837acdf`):
- Engine: `CustomMapPlayer.StartingBank` (a `WorldResourceSet`: gold and all five manas; default 500 gold, 1500 of
  the race's own mana, the others 0), `CustomMap.SetStartingBank` (each amount kept within 0 and 9999, the game's
  `MaxResourceAmount`) and `CopyStartingBankToAll`. The writer writes it as the `Splayer` bank (the old fixed
  `StartingGold`/`StartingMana` constants are gone); `FromState` reads it back from the player's resources. Maps saved
  before this read back as the old 500/1500, so nothing changes for them.
- Economy panel: under "Unowned land", a "Starting bank" editor: a race box, the six amounts (gold, the race's own
  mana, then the other four) and "Same for all races"; each race block shows a "Start:" line with its non-zero
  amounts. Typing into one field keeps one undo step; anything that isn't a number is ignored, an empty box is 0,
  more than 9999 is 9999.
- "Same for all races" decision: gold and the other manas are copied as they are, and the source's own mana and the
  target's own mana swap amounts (Empire 1500 life + 250 rune -> Clans 1500 rune + 250 life).
- The game's rule, shown as a note: a human player's bank is scaled by difficulty when the game starts (Easy 300 %,
  Average 100 %, Hard 50 %, Very hard 25 %); computer players get it as set (`ScenarioLoader`, `!isAi`).
- "Economy (E)" moved into the right panel's title row: above Undo/Save it took a row of height, and at 1366x768
  tool settings scrolled out of view.
- Tests (suite 5207 -> 5209): `CustomMapTests.StartingBankTest` (defaults, clamping, undo copies, save as
  `Splayer` bank, load, reopen, a human at Hard gets 50 % and a computer player the full amount, same for all),
  `MapEditorScreenViewModelTests.StartingBankTest` (fields, typing, one undo step per field, same for all, undo,
  save and reopen), `VisualTests.MapEditorEconomyTest` (starting bank shot); all 13 map editor visual tests pass.
- Real client run (AOT build, Debian 12 image): Empire gold set to 1234 and infernal mana to 300 in the panel, Save
  and play: the treasury on turn 1 shows 1384 gold, 1575 life, 50 rune and 350 infernal mana, which is the bank plus
  exactly the turn-1 income the economy panel shows (+150, +75, +50, +50).

## Phase 5 — done

What was built (branch `upgrade/map-editor-spread-preview`, commit `caab9b3`, merged as `a6e6b66`):
- Engine `CustomMapSpreadPreview.cs`: from the editor's preview state, turn by turn, `ExpansionCalculator
  .ExpandPlayerTerrain` for every playable race (the call `WorldEngine.Events.TurnStarting` makes), in the engine's turn
  order with every player a computer player (Empire, Legions, Clans, Undead, Elves; in a game the human player moves
  first). Turns are computed on demand and kept (up to `MaxTurns` = 200); `GetState` is thread-safe.
- Economy panel: a turn bar under the description, built like the unit level bar: `-10` `-` "The map now" /
  "After N turns" `+` `+10`, and "Back to the map" while a later turn is shown. The map view, the minimap, the
  panel's numbers (land, resources per race, income, unowned land) and the selected race's outline all show that
  turn. The map itself is never changed by it.
- While a later turn is shown nothing is edited: the hover line says "After N turns (preview, nothing is edited):
  click goes back to the map", and a click, a stroke or Ctrl+Z goes back to the map first (Ctrl+Z then undoes as
  usual). Closing the panel goes back too. After an edit the preview starts again from the new map.
- Speed: on a 512 map with 5 capitals, 143 villages and 143 rods one turn takes about 0.2 s (+10: 2.2 s; the
  economy numbers for a turn: 9 ms). Steps the preview expects to take longer than 0.15 s run on a background
  thread ("Turn N: computing..."), so the editor stays responsive; earlier turns are kept, so going back is instant.
- Tests (suite 5209 -> 5211): `CustomMapTests.SpreadPreviewAgreesWithGameTest` (8 turns compared with the real engine
  on the rod test map, every player human and only ending turns: same land every turn, 686 tiles changed owner;
  caching and the cap), `MapEditorScreenViewModelTests.SpreadPreviewTest` (+1 is the capital's 10 tiles, +10, -,
  numbers and outline follow, the map is untouched, a click and a stroke go back without editing, the preview
  restarts after an edit, -10, Back to the map, closing the panel), `VisualTests.MapEditorEconomyTest` (after 10 and
  30 turns).
- Real client run (AOT build, Debian 12 image, `mapgen` map): +10 three times shows "After 30 turns" with the Empire
  at 1028 tiles (20 %) and the Undead at 1305 (26 %); by then the Empire's land has also reached the grove mana
  source that was unowned, and the panel shows it (+50 grove mana).

Known limits:
- Villages that level up over time (`GrowthTurn`) aren't simulated; the editor writes none.
- Turn order: the human player moves first in a game, so where two races' land meets, the real split can differ by a
  few tiles from the preview.

## Phases

1. ~~Rods in the engine and writer: `CustomMapRod`, rules, paint, `SRod.json`, `FromState`, tests (about 1 day).~~
   Done, see "Phase 1 — done".
2. ~~Rod tool in the client: tool, preview, hover line, Shift+click, visual test, real client run (about half a
   day).~~ Done, see "Phase 2 — done".
3. ~~Economy panel: `CustomMapEconomy`, panel, live update, highlight (about 1 day).~~ Done, see "Phase 3 — done".
4. ~~Starting bank per race in the panel (gold + all five manas), "Same for all" (about half a day).~~ Done, see
   "Phase 4 — done".
5. ~~Land spread preview: turn bar, cached spread, numbers per turn, benchmark (about 1 day).~~ Done, see
   "Phase 5 — done". All five phases are done.

Each phase is its own `upgrade/` branch and roadmap update.

## Open questions

1. ~~Starting bank: only gold and own mana, or all five manas?~~ Decided: gold and all five manas, other manas 0 by
   default.
2. ~~"After N turns" estimate?~~ Decided: yes, the turn bar above.
3. ~~Hotkey?~~ Decided: yes, E.
4. Spread preview: should village growth turns (`GrowthTurn`, villages that level up over time) be simulated? The
   editor writes none today, so not planned.
