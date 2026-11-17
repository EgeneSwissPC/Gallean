# MapFromPlan

Builds a custom map (the map editor's format, `~/.local/share/GalleanUpgrade/Maps`) from a JSON map plan. A plan
describes a land in miles: regions and their terrain, coasts and lakes, rivers with fords, mountain ranges with passes,
hills, forests, roads, capitals, villages, ruins, sites, landmarks, mines, neutral armies and treasure. The tool shapes
it with noise, places every object through the editor's own rules (`CustomMap.Preview*`, then `Place*`), moving it to
the nearest spot where it fits, and saves it with `CustomMapStore` like the editor does. The map opens in the editor for
fine-tuning and plays as a custom quest.

```
. ~/.local/icu/env.sh
dotnet run --project tools/MapFromPlan -- tools/MapFromPlan/plans/middle-earth.json ~/.local/share/GalleanUpgrade/Maps \
    overview.png -id S600SC0000 -zoom 720,-450,900,-700 mordor.png
```

- `overview.png` (optional): north-up picture of the whole map with labels.
- `-id`: replace that custom map; without it the next free ID is used.
- `-zoom westE,northN,eastE,southN file.png`: close-up of a window of the plan (miles) at 12 pixels per unit.

The run prints what was placed, every object it could not place, travel costs between named places and gate checks.

## Coordinates

Points are `[east, north]` in miles from any origin. `center` is the point at the middle of the map and
`milesPerTile` the scale. North is the top of the screen: one tile east is `(x + 1, y - 1)`, one tile north
`(x - 1, y - 1)`, so the map is a diamond holding every point with `|east| + |north| <= size / 2` tiles from the
centre. Points outside it are reported.

## Plan sections

- `name`, `description`, `size` (16-1024), `milesPerTile`, `center`, `seed`, `warpMiles` (how far noise bends borders),
  `baseTerrain`, `races` (one player per race: Human, Elf, Dwarf, Heretic, Undead), `leaderMovement` (for the report).
- Areas are `"polygon": [[e, n], ...]` or `"around": [e, n], "radius": miles`.
- `land`: `{ terrain, area }` in order; later regions win. Terrain decides who earns from mines on it.
- `water`: seas and lakes (areas). `rivers`: `{ path, width (tiles, at least 2.2), fords: [points], fordRadius }`;
  rivers are painted in tile space so armies can't slip across diagonally; fords stay dry.
- `marshes`: `{ area, density }` scattered pools.
- `ranges`: `{ path, width (miles), gaps: [{ at, radius }] }`. A gap cuts the full width of the range. Filled with 5x5,
  3x3 and 1x1 mountains like the editor brush. `hills`: `{ area, density }` scattered 3x3/1x1 peaks.
- `forests`: `{ area, density }`; `wildForest`: `{ threshold, scaleMiles, exceptTerrain }` for clumps elsewhere.
- `capitals`: `{ race, name, at }`. `villages`: `{ name, owner, level, at, garrison: template, tier }`.
  `ruins`: `{ name, at, guards: template, tier, gold, item }`. `sites`: `{ type (Merchant, MageTower, Mercenaries,
  Trainer), name, greeting, at, items: [[name, count]], spells: [names], mercenaries: [[unit, level]] }`.
  Structures keep one free tile around them and in front of the entrance.
- `roads`: `{ path: [point or "@Structure name"] }`: cheapest 4-connected path between waypoints (road sprites only
  join north/east/south/west), around water, mountains, structures and landmarks. Roads are saved as SRoad rows.
- `landmarks`: `{ name, size?, at }` or scattered `{ name, count, area }`; `water: true` allows water tiles.
- `resources`: `{ type, at }`; `resourceScatter`: `{ types, count, area }`.
- `armyTemplates`: `{ leaders, units, large }` by unit name. `armies`: `{ template, tier (1-5), at }`;
  `armyScatter`: `{ templates, tier: [min, max], count, area }`. Tier sets the number of units, their levels
  (soldiers 1 + 2(t-1), leaders 1 + 3(t-1)) and big units from tier 3. Garrisons use the same templates without leaders.
- `bagScatter`: `{ count, area, items }`.
- `labels`: `{ text, at }` region names for the overview. Ranges and forests are labelled unless `"label": false`;
  rivers only with `"label": true`.
- `distances`: `[[from, to]]` structure names; prints movement points (plain 2, forest 4, road 1).
- `gateChecks`: `{ from, to, radiusMiles, gates: [{ name, at, radiusMiles? }] }`: with all gates closed there must be
  no path, and each gate alone must open one (e.g. Mordor's three ways in).

Names of units, items, spells and landmarks are the English names from `Globals/Tglobal.json`.

## Limits

- One player per race (diplomacy, land and victory are keyed by race), so allies of one race share a player.
- Only existing objects: new peoples are stand-ins (hobbits are peasants, Ents are the elves' Ent Minor).
- Capital names stay the editor's "<Race> Capital"; villages, ruins and sites keep their plan names.
- The world AI idles on custom maps (see `docs/ai/notes-custom-map-ai.md`).
