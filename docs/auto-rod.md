# Auto rod planter (rod planters that find rod jobs on their own)

Status: all open questions answered (A1, B3, C1, D1). Phase 1 (engine) done; phase 2 (client) next.

Gallean Upgrade only. Human players only; the AI keeps its own `RodAction`. Builds on auto explore (`docs/auto-explore.md`).

## Decisions

- **The user's wish.** Like Explore, but for a rod planter: if rod jobs are known (seen on the map), it walks to the best one and does it; if none are known, it explores until it finds one.
- **What counts as a rod job (question A: A1).** The same targets as the AI's `RodAction`: (1) free plant spots next to a resource node that is on neutral or hostile land and has no rod next to it yet (needed mana counts double), and (2) hostile rods to pull up. Only seen tiles count (the AI sees through fog; the player's planter must not). User: "same as the current AI".
- **Plant by itself or ask (question B: B3).** Removing a hostile rod (free) happens automatically on arrival and is logged in the end-of-turn list. Planting (150 gold) asks on arrival: Plant / Skip this spot / Stop. Skip remembers the spot for this auto-rod run so it isn't picked again; Stop ends auto rod. During End-turn walks the planter waits at the spot and goes into the end-of-turn list ("<leader> can plant a rod near <resource>"), like a move-order arrival that needs the player.
- **Not enough gold (question C: C1).** Below `RodCost` it keeps exploring and pulling up hostile rods and skips plant jobs until the gold is there again; it says once "Not enough gold to plant rods." (end-of-turn list), and again only after gold has been enough in between.
- **Key and button (question D: D1).** Hotkey **R** (rod) plus a button on the card next to Explore with the game's plant-rod icon (`Icons.PlantRod`, already used by the action wheel); shown only for leaders with the Rod ability. R or the button again, X or Stop end it. Auto rod and Explore are separate modes: starting one ends the other.

## What the game already does (code read)

- Planting (`WorldCreateRodCommand`): leader with the Rod ability, alive, not in a city, `RodCost` 150 gold, `RodPlantRange` 1, no rod within `RodSpacingRange` 2, not water/mountain/structure/army/bag, not on ally land. Uses up the army's move points.
- Removing (`WorldRemoveRodCommand`): rod leader, move points left, within `RodRemoveRange`, rod not allied. Uses up the move points.
- AI (`WorldAiPlanner.RodAction`): enumerates the targets above, scores interest (attitude `CreateRodInterest` / `RemoveRodInterest`) against path cost within 10 turns (30 for needed mana), skips paths ending in a battle.
- Auto explore (`AutoExploreCalculator` + `WorldScreenViewModel.AutoExplore.cs`): picks fog edges from seen tiles only, walks as a self-repicking move order, stops on sightings and battles, not saved.

## The proposal

1. A **Rod** button (plant-rod icon, hotkey R) on a rod planter's card next to Explore (only for leaders with the Rod ability).
2. Each pick: list rod jobs on seen tiles (A1); if one is reachable and outside the explore safety distance, walk to the best (interest minus path cost, the AI's scoring). On arrival: pull up a hostile rod automatically; ask before planting (B3).
3. No job: take one auto explore leg, then check again after every leg, so it turns toward a mine as soon as one is uncovered.
4. Gold below 150: plant jobs skipped, explore and removals go on (C1). Stops: nothing to explore and no jobs left, plus Explore's sighting and battle stops.
5. Not saved, like Explore.

## Technical approach

- Engine: `AutoRodCalculator.FindJob(state, stack, context, settings)` -> `Plant(pos, standAt) | Remove(pos, standAt) | Explore(target) | NothingLeft` (plant jobs left out when the player can't afford `RodCost`), reusing the RodAction target list (filtered by the player's fog) and `AutoExploreCalculator`. Read-only command for the client.
- Client: `WorldScreenViewModel.AutoRod.cs` next to `AutoExplore.cs`, same walk loop; on arrival it sends the plant/remove command.

## Phases

1. ~~Engine calculator + tests (~1 day).~~ Done, see "Phase 1 — done".
2. Client: button, hotkey, walks on End turn and right away, card look, messages (~1 day). Real-client check.

## Open questions

- ~~**A. What counts as a rod job?**~~ Answered: A1 (see Decisions).
- ~~**B. Plant by itself or ask first?**~~ Answered: B3 (see Decisions).
- ~~**C. Not enough gold to plant?**~~ Answered: C1 (see Decisions).
- ~~**D. Hotkey and button.**~~ Answered: D1 (see Decisions).

## Phase 1 — done

Engine only; nothing on screen yet.

**What was built**

- `World/AutoRodCalculator.cs`: `FindJob(state, stack, context, skippedSpots, previousExploreTarget, settings, exploreSettings)` returns `AutoRodPick(Job, ShortOfGold)`. Job is `Plant(Target, StandAt, PathCost, Resource, IsNeededMana)`, `Remove(Target, StandAt, PathCost)`, `Explore(AutoExploreResult.Target)`, `NothingLeft`, `NoTargetNow` or `NotRodPlanter`.
  1. Jobs (A1), only on seen tiles: hostile rods (seen tile) and plant spots around seen resource nodes on neutral or hostile land with no rod around them; spots the player skipped this run are left out (B3); plant jobs only when the player can pay `RodCost` (C1, `ShortOfGold` tells the client to say so once).
  2. Score like `RodAction`: interest x share of the move budget left (budget = max move points x 10 turns, 30 for a needed mana). Interests are the original GAI values, plant 120 (needed mana x2, max 200), remove 105 (`AutoRodSettings`). "Needed mana" = the AI's rule (mana short for the cheapest level-3 spell the lord can research), computed for the human player.
  3. Stand tiles: within range of the target (1), not the target, seen, free to stand on, not a city or site, outside explore's danger zone (D3 of auto explore, shared `SafetyDistance`); the planter's own tile counts with cost 0 unless it stands in a city. Best first, the path must have no battle or thief encounter (bags are fine).
  4. No job: falls back to `AutoExploreCalculator.FindTarget` (Explore / NothingLeft / NoTargetNow).
- `World/Commands/WorldGetAutoRodJobCommand.cs`: read-only command for the client.
- `WorldCreateRodCommand.GetSpotError`: the spot rules (spacing, terrain, free tile, not ally land) moved into one helper used by both the command and the calculator, so a spot auto rod picks is always one the real command accepts. The command's checks and their order are unchanged.
- `AutoExploreCalculator.BuildDangerZone` is now internal (shared).

**Tests** (`AutoRodCalculatorTests`, 15): walks to a seen resource / plants without moving when in range / the picked spot is accepted by the real command; resource or hostile rod in the fog is unknown -> explores; removes a seen hostile rod; short of gold still removes rods / skips plant jobs and explores (ShortOfGold set); skipped spots are not picked again (all skipped -> explores); resource with a rod next to it and own rods are no job; everything seen and no job -> NothingLeft; job next to a known enemy army skipped; leader without the Rod ability -> NotRodPlanter; a removal next door beats a plant 3-4 turns away.

**Known limits**

- Like `RodAction`, distance only scales the interest down: a plant spot one turn away still beats a removal next door.
- Fog tiles never go dark again, so a hostile rod planted later on an old seen tile counts as known, and a removed one stops being a job at once (the state is read directly, like the explore calculator does for armies and bags).
