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
  - starting gold and starting own mana as number boxes (0-9999), plus a "Same for all races" button that copies
    the gold and mana amount to every race (each race gets the amount in its own mana)
- A last block "Nobody's land": resources standing on neutral land, by type, so unclaimed mines are easy to spot.
- All income at **Average difficulty** (see "Decisions"). A note under the panel says so ("Easy: +30 % income for
  humans; computer players earn 70-145 % depending on difficulty").
- The numbers update live after every paint, placement, rod or undo.
- Clicking a race's block outlines its resources and rods on the map (gold outline, as the placement preview does for
  objects); clicking again or closing the panel clears it.

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
- Starting bank: `CustomMapPlayer` gets `StartingGold` and `StartingMana` (defaults 500/1500); `GetStartingBank`
  uses them. `FromState` reads them from `state.Players[...].Resources`; the map is saved as Average, so the load
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
- `CustomMapTests.StartingBankSaveAndLoadTest`.
- `MapEditorScreenViewModelTests.RodTest`, `EconomyPanelTest` (live update after paint/undo, "Same for all").
- `VisualTests.MapEditorRodsTest`, `MapEditorEconomyPanelTest` (also at 1366x768).
- Real client run: place rods for two races, save, play the map as a custom quest, check the rods are drawn and spread
  on turn 2, and the in-game income table matches the panel.

## Phases

1. Rods in the engine and writer: `CustomMapRod`, rules, paint, `SRod.json`, `FromState`, tests (about 1 day).
2. Rod tool in the client: tool, preview, hover line, Shift+click, visual test, real client run (about half a day).
3. Economy panel: `CustomMapEconomy`, panel, live update, highlight (about 1 day).
4. Starting gold and mana per race in the panel, "Same for all" (about half a day).

Each phase is its own `upgrade/` branch and roadmap update.

## Open questions

1. Starting bank: only gold and the race's own mana, or all five mana types per race (the scenario row has all six
   amounts)? The plan is gold + own mana for now; the Players screen could add the rest later.
2. Should the panel also show a "after N turns" estimate (running the real spread from capitals, villages and rods),
   since a mine's owner changes as land spreads? Not planned for now.
3. Is a hotkey for the economy panel wanted (and which)? Not planned for now.
