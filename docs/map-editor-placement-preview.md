# Gallean Upgrade – map editor: placement preview under the cursor

Feature description, 2026-11-16, on `gallean-upgrade` at `5231c29`. Not built yet. Part of the map editor roadmap
(`docs/map-editor-roadmap.md`).

## The problem today

In the editor the cursor only lights up the one tile under the mouse, whatever tool is picked. You find out what a
click does only after you click:

- **Size and position are a guess.** Capitals are 5x5, villages 4x4, ruins and sites 3x3, landmarks 1x1 to 5x5. The
  object is centred on the clicked tile, and near the map edge it is pushed back inside the map, so it doesn't land
  where you'd expect.
- **"Blocked" comes as an error message after the click** ("It cannot overlap a capital, village, ruin, site or
  landmark, or stand on a mountain."). The message doesn't say which tile is the problem.
- **Side effects are hidden.** A village turns the land under it into its owner's land, ruins and sites turn water
  into land, most objects clear forests. A Clear or Water brush that touches one tile of a mountain removes the whole
  mountain.
- **Brushes show no size.** You only see the brush area (radius 0 to 8) after you paint.
- **Clicking an existing object does something else** (opens its screen, or removes it for resources and landmarks),
  and nothing tells you which it will be.

## What you would see

While the mouse is over the map, the tiles the click would affect get a coloured overlay, and the hover line at the
bottom left says what will happen.

| Colour | Meaning |
| --- | --- |
| Green | The object fits here; these tiles are what it will cover. |
| Red | This tile blocks the placement. The other tiles stay green, so you see exactly what is in the way. |
| Amber | It fits, but this tile will change: a forest cleared, water turned into land, land claimed by an owner, a mountain removed. |
| Outline glow on an object | A click opens this object's screen (or removes it, see below). Uses the game's own highlight, like when you point at a city in the game. |

Hover line examples:
- `Village (4x4): fits · claims 16 tiles for the Empire`
- `Village (4x4): blocked by a mountain at 23, 41`
- `Ruin (3x3): fits · clears 3 forest tiles, turns 2 water tiles into land`
- `Cemetery (3x3): click removes it`
- `Mercenary camp Guther's Camp: click opens it`

The overlay follows the mouse tile by tile. It disappears when the mouse leaves the map, while the right-button info
popup is open, and while you drag the map with the middle button.

## Per tool

The rules below are the editor's current placement rules (`CustomMap`); the preview only shows them, it adds no new
ones.

**Brushes (Land, Water, Forest, Mountains, Clear)**
- Shows the brush disk at the current size (`CustomMap.GetBrushArea`, radius 0–8), so moving the size slider shows the
  new size at once.
- Tiles the brush won't touch are red: capitals, villages, ruins and sites for every brush; armies and resources for
  Water; resources, bags and landmarks for Forest; anything that can't hold a mountain for Mountains.
- Water and Clear: a mountain that would be removed is amber on all its tiles, including the ones outside the disk.
- Mountains: shows the mountain blocks the brush would create (1x1, 2x2, 3x3 or 5x5 squares, as the brush places
  them), not the disk.
- While painting (button held) the preview keeps showing the disk, so you see where the stroke goes.

**Capital (5x5)**
- On free land: the 5x5 square where the capital will stand, after the push back inside the map.
- Red tiles: other capitals, villages, ruins, sites, landmarks, armies, resources and bags.
- Amber: forests and mountains it removes, land that becomes the race's land.
- If the race already has a capital, the current one gets a faint outline too: placing again moves it, with its
  garrison and stationed army.
- Over an existing capital: glow, "click opens it".

**Army (1 tile)**
- Free land: green tile.
- Red: water, mountain, structure, resource, bag, landmark.
- The entrance tile of a village or capital: green when the chosen owner owns it ("will be stationed in Ford"), red
  when the owner doesn't match.
- Over an army, village or capital: glow, "click opens it".

**Village (4x4), Ruin (3x3), Site (3x3)**
- Footprint after the push back inside the map; red tiles for structures, landmarks, armies, resources and bags.
- Village: amber for the land it claims for its owner. Ruin and site: amber for water that becomes land.
- Over an existing one: glow, "click opens it".

**Resources (1 tile)**
- Red: water, mountain, structure, landmark, army or bag on the tile.
- Over a resource of the chosen type: glow, "click removes it". Of another type: "click changes it to Gold mine".
- The hover line names who earns from it (the land owner), the same as the right-click info.

**Treasure (1 tile)**
- Green on land or water; red on mountains, structures, landmarks, armies, resources and other bags.
- Over a bag: glow, "click opens it".

**Landmark (size from the palette)**
- Footprint of the landmark chosen in the palette; changing the palette selection updates the preview at once.
- Red: structures, other landmarks, mountains, armies, resources, bags. Amber: forests it clears.
- Over a landmark: glow, "click removes it".

## How it would work (technical)

### Engine: placement checks without side effects

Today the checks live inside the `Place*` methods, which change the map, and `PlaceCapital` only returns true/false.
The preview needs the same answer without changing anything, and per tile.

- Add `CustomMap.Preview*` methods (or one `PreviewPlacement`), e.g.
  `CustomMapPlacementPreview PreviewVillage(WorldPosition topLeft)`, returning:
  - the footprint (after clamping),
  - per tile: `Ok`, `Changes` (forest/water/land/mountain) or `Blocked` with the reason
    (`OutsideMap`, `Structure`, `Landmark`, `Mountain`, `Water`, `Army`, `Resource`, `Bag`, `WrongOwner`),
  - the overall result, which is the same `CustomMapPlacementError` the matching `Place*` returns.
- The `Place*` methods call the same check code first, so preview and click can never disagree.
- `PlaceCapital` gets a reason (today it returns `bool`), which also gives a better error message than the current
  "A capital cannot overlap another capital or an army.".
- Brush preview: `PreviewPaint(center, radius, brush)` returns the tiles that would change, the protected tiles and
  the mountains that would be removed or created. `Paint` already has this logic; it is split so the decision part
  can run alone.
- These are cheap (a footprint is at most 25 tiles, a brush at most 225), so they run on every hover-tile change.

### Client: view model

- `MapEditorScreenViewModel` builds a `MapEditorPlacementPreview` (tiles with state, an optional object to glow, the
  hover line text) in `UpdateHover`, and rebuilds it when the tool, the brush size, the palette selection, the owner,
  or the map changes. Nothing is recomputed while the hover tile stays the same.
- The hover line (`HoverText`) gets the preview's text in front of the tile description it shows today.

### Client: drawing

Two ways, the first one preferred:

1. **Editor-only overlay in `MapEditorVisual` (preferred).** After the `WorldRenderOperation`, draw the tiles as
   semi-transparent diamonds with an outline, using the camera's view matrix and `WorldTransform` (tile corners). No
   game renderer files change, which keeps upstream merges easy and keeps "editor changes don't touch the game". The
   overlay is drawn over the sprites (no depth sorting); with transparency and outlines that reads fine for a preview.
   The glow on an existing object can reuse the game's `Highlight` (sprite outline) through the render request,
   or be drawn as a diamond outline of its footprint if that turns out to need renderer changes.
2. **Through the world renderer.** Add an optional list of coloured tiles to `WorldRenderRequest`/`WorldRenderState`
   and draw it like the reach overlay (`ReachOverlay`, which today has one colour and only appears for a selected
   army). This sorts correctly behind tall sprites, but edits several upstream render files.

Colours go into `Theme` next to the existing cursor colours (`HoverCursorInnerColor` etc.), so they match the game's
palette instead of plain traffic-light colours.

### Optional extra (later): ghost sprite

Draw the object itself, semi-transparent, at the preview position: the capital, village, ruin, site or landmark
sprite. This shows what it will look like, not just where it goes. It needs the asset sprites in the overlay and the
right anchor per object type, so it's a separate, bigger step. The coloured tiles are useful on their own without it.

## Settings

- On by default. A checkbox "Placement preview" in the editor panel, or a key (e.g. `P`), to switch it off when you
  just want to look at the map.
- No game settings change; this lives only in the editor.

## Testing

- Engine: for many positions on a sample map with every object type, the preview result equals what the matching
  `Place*` call returns on a copy of the map (same error, same footprint). This catches the two drifting apart.
- Engine: per-tile reasons on hand-made cases (mountain in a corner, a bag inside the footprint, map edge clamping,
  the entrance of a village owned by someone else, a capital moved onto its own old place).
- View model: hover text and tile states per tool; the preview updates when the brush size or palette selection
  changes; it clears when the mouse leaves the map.
- Visual test: screenshots of a green, a red and an amber case, plus a brush, for the contact sheet.
- Real build, headless: hover with `xdotool mousemove` and take screenshots, as for the earlier editor features.

## Phases and size

1. **Footprint and brush outline**, green/red for the whole object (no per-tile reasons yet), hover line "fits /
   blocked". Small: mostly view model and the overlay drawing.
2. **Per-tile reasons and amber changes**, the engine `Preview*` methods, and `Place*` refactored on top of them.
   Medium: touches every placement rule, and needs the agreement tests.
3. **Glow on existing objects** ("click opens / removes").
4. **Ghost sprite** (optional, later).

Phases 1–3 together are a moderate task, about the size of the landmark feature.

## Open questions

1. Colours: green/red/amber, or the game's own gold/purple cursor tones?
2. Should the preview also show the land a village claims (amber), or is that too much colour?
3. On by default with a switch, or always on?
4. Ghost sprites: wanted at all, or are coloured tiles enough?
