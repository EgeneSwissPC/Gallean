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

## Phases

1. Rods in the engine and writer: `CustomMapRod`, rules, paint, `SRod.json`, `FromState`, tests (about 1 day).
2. Rod tool in the client: tool, preview, hover line, Shift+click, visual test, real client run (about half a day).
3. Economy panel: `CustomMapEconomy`, panel, live update, highlight (about 1 day).
4. Starting bank per race in the panel (gold + all five manas), "Same for all" (about half a day).
5. Land spread preview: turn bar, cached spread, numbers per turn, benchmark (about 1 day).

Each phase is its own `upgrade/` branch and roadmap update.

## Open questions

1. ~~Starting bank: only gold and own mana, or all five manas?~~ Decided: gold and all five manas, other manas 0 by
   default.
2. ~~"After N turns" estimate?~~ Decided: yes, the turn bar above.
3. ~~Hotkey?~~ Decided: yes, E.
4. Spread preview: should village growth turns (`GrowthTurn`, villages that level up over time) be simulated? The
   editor writes none today, so not planned.
