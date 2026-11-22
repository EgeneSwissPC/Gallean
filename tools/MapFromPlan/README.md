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
- `marshes`: `{ area, density }` scattered pools. `islands`: `{ terrain, area }` land raised out of seas and lakes after
  the water is painted.
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
  no path, and each gate alone must open one (e.g. Mordor's three ways in). `-nochecks` skips distances and gate
  checks (minutes on a 1024 map).

### Map editor features (rules, banks, zones, templates, events)

- `rules`: `{ spellMarkup: percent, neutralKinds: bool, townIncome: [5 values] | bool, neutralGold: gold, neutralBuying: bool, neutralUnitFactor: n }`: the editor's map rules (mage
  towers charge more for other races' spells; each neutral kind is its own side; gold per turn for town levels 1-5,
  on with the editor's default 10/25/50/75/100 when left out, `false` = off; gold each neutral side starts with, 0 when left out; neutrals buy units, on when left out; units per village level before neutrals upgrade, 3 when left out). `startingBanks`: `{ Race: { Gold, LifeMana, ... } }`.
- `capitals[].garrison` / `capitals[].army`: `{ template, tier }`; the guardian stays in the garrison. Templates with
  `"race": "Human"` pick that race's units. `villages[].army`: an army stationed in the village (`name` to use it in
  events). `armies[]`: `name` (for events), `loot: { chance, items, max }` (a bag next to the army), `bounty: item`,
  `bountyText` (an event gives the item to whoever beats the army). `armyScatter[]` takes `loot` and `spacing` too.
- `rods`: `{ owner, at }` or `{ owner, count, area }`: rods that already claim land.
- `locations`: `{ name, at (point or "@Structure" = in front of its entrance), radius 0-3 }` zones for events.
- `variables`: `{ name, initial }`. `templates`: `{ name: { owner, army, tier, order } }` armies events create.
  Orders: `{ kind (Normal, Stand, Guard, Roam, Berserk, Explore, MoveToZone, DefendZone, DefendCity, SecureCity,
  AttackArmy), zone | city ("@Village" or "capital:Race") | army }`.
- `events`: `{ name, races ("all" or a list), concerns?, once, enabled, chance, when: [conditions] | whenAny:
  [[...], [...]], then: [effects] }`. Conditions are one-key objects: `turn`, `every`, `enterZone`, `ownCity`,
  `enterCity`, `lootRuin`, `destroyArmy`, `armyExists` (+`exists`), `ownItem`, `leaderItem` (+`army`), `relation`
  `[r1, r2, War|Neutral|Peace]`, `alliance`, `land`, `visitSite`, `armyToZone` (+`zone`), `armyToCity` (+`city`),
  `itemToZone` (+`zone`), `variable` (+`min`, `max`). Effects: `message` (+`audience`, `portrait` unit name,
  `left`), `objective`, `win`, `lose`, `switch` (event name, +`on`), `reveal` (+`races`), `giveItem` (+`toCapital`),
  `createArmy` (+`zone`), `removeArmy`, `moveArmy` (+`zone`), `moveBeside`, `changeOwner` (+`to`: race or
  "triggerer"), `order` (+ an order), `battle`, `castSpell`, `castSpellZone` (+`zone`, `caster`), `giveSpell`,
  `ally` (+`permanent`), `setRelation`, `removeMountains`, `removeLandmark` / `changeLandmark` (landmark `id` given
  in `landmarks`), `changeTerrain` (+`terrain`, `size`), `setVariable` (+`op`, `value`), `destroyItem`. Armies in
  events are `"army:Name"`, `"template:Name"` or `"triggerer"`. `conquestWins: true` adds the editor's "capture
  every enemy capital" win for each race (any event with a win would otherwise end elimination wins).
- `neutralLife`: neutrals that live on the map, all as ordinary editor events and templates:
  - a variable (default `Shadow`) grows by `perTurn` at the start of every turn of `growsWith` (Legions), so it stops
    when they fall; `stages: [{ name, at, tierBonus, message, portrait }]` pick the army strength by its range, so
    events that lower it weaken the neutrals again;
  - every neutral village with a `garrison` (unless `"life": false`) gets a zone in front of it, a band per stage
    built from its garrison's template (so always of the village's kind) that gathers there (`spawn: { every,
    chance, order: DefendZone | Roam }`, one at a time, only while the neutrals hold the village), and avengers per
    stage with the "capture a city" order that march when a race holds the village (`retake: { every, chance,
    tierBonus, message }`, one at a time);
  - `patrols: [{ name, army, tier, path: [points or "@Structure"], home }]`: an army walks its waypoints in a loop
    (a variable remembers the leg, so each leg event fires once per arrival) and a new one sets out every few turns
    while the home village is in neutral hands (or always, without `home`); waypoints on water make boat or sea patrols;
  - `spawners: [{ name, at, radius, army, tier, every, chance, order }]`: bands that gather in a zone of their own (a sea
    lane, a haunted isle), one at a time, stronger by stage.
- Sea creatures: templates led by a Mermaid, Sea Serpent or Kraken are placed on open water only (the tile and the 8
  around it water); `armies[].water` / `armyScatter[].water` put any army on water (land leaders sail in boats).

Names of units, items, spells and landmarks are the English names from `Globals/Tglobal.json`.

## Limits

- One player per race (diplomacy, land and victory are keyed by race), so allies of one race share a player.
- Only existing objects: new peoples are stand-ins (hobbits are peasants, Ents are the elves' Ent Minor).
- Capital names stay the editor's "<Race> Capital"; villages, ruins and sites keep their plan names.
- The world AI idles on custom maps (see `docs/ai/notes-custom-map-ai.md`); full capital garrisons and armies help.
- An event that fires stops the army that was moving: conditions that stay true ("the patrol is in zone A") must be
  paired with something that changes when the event fires, or every step of every army of that race is cut short and
  the AI replans after each tile (seconds per turn on a 1024 map).
