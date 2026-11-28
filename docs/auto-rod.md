# Auto rod planter (rod planters that find rod jobs on their own)

Status: proposal. Open questions: C, D. Answered: A1, B3.

Gallean Upgrade only. Human players only; the AI keeps its own `RodAction`. Builds on auto explore (`docs/auto-explore.md`).

## Decisions

- **The user's wish.** Like Explore, but for a rod planter: if rod jobs are known (seen on the map), it walks to the best one and does it; if none are known, it explores until it finds one.
- **What counts as a rod job (question A: A1).** The same targets as the AI's `RodAction`: (1) free plant spots next to a resource node that is on neutral or hostile land and has no rod next to it yet (needed mana counts double), and (2) hostile rods to pull up. Only seen tiles count (the AI sees through fog; the player's planter must not). User: "same as the current AI".
- **Plant by itself or ask (question B: B3).** Removing a hostile rod (free) happens automatically on arrival and is logged in the end-of-turn list. Planting (150 gold) asks on arrival: Plant / Skip this spot / Stop. Skip remembers the spot for this auto-rod run so it isn't picked again; Stop ends auto rod. During End-turn walks the planter waits at the spot and goes into the end-of-turn list ("<leader> can plant a rod near <resource>"), like a move-order arrival that needs the player.

## What the game already does (code read)

- Planting (`WorldCreateRodCommand`): leader with the Rod ability, alive, not in a city, `RodCost` 150 gold, `RodPlantRange` 1, no rod within `RodSpacingRange` 2, not water/mountain/structure/army/bag, not on ally land. Uses up the army's move points.
- Removing (`WorldRemoveRodCommand`): rod leader, move points left, within `RodRemoveRange`, rod not allied. Uses up the move points.
- AI (`WorldAiPlanner.RodAction`): enumerates the targets above, scores interest (attitude `CreateRodInterest` / `RemoveRodInterest`) against path cost within 10 turns (30 for needed mana), skips paths ending in a battle.
- Auto explore (`AutoExploreCalculator` + `WorldScreenViewModel.AutoExplore.cs`): picks fog edges from seen tiles only, walks as a self-repicking move order, stops on sightings and battles, not saved.

## The proposal

1. A **Rod** button on a rod planter's card next to Explore (only for leaders with the Rod ability).
2. Each pick: list rod jobs on seen tiles (A1); if one is reachable and outside the explore safety distance, walk to the best (interest minus path cost, the AI's scoring). On arrival: pull up a hostile rod automatically; ask before planting (B3).
3. No job: take one auto explore leg, then check again after every leg, so it turns toward a mine as soon as one is uncovered.
4. Stops: not enough gold (open question C), nothing to explore and no jobs left, plus Explore's sighting and battle stops.
5. Not saved, like Explore.

## Technical approach

- Engine: `AutoRodCalculator.FindJob(state, stack, context, settings)` -> `Plant(pos, standAt) | Remove(pos, standAt) | Explore(target) | NothingLeft | NoGold`, reusing the RodAction target list (filtered by the player's fog) and `AutoExploreCalculator`. Read-only command for the client.
- Client: `WorldScreenViewModel.AutoRod.cs` next to `AutoExplore.cs`, same walk loop; on arrival it sends the plant/remove command.

## Phases

1. Engine calculator + tests (~1 day).
2. Client: button, hotkey, walks on End turn and right away, card look, messages (~1 day). Real-client check.

## Open questions

- ~~**A. What counts as a rod job?**~~ Answered: A1 (see Decisions).
- ~~**B. Plant by itself or ask first?**~~ Answered: B3 (see Decisions).
- **C. Not enough gold to plant (under 150):** keep exploring and removing, skip plant jobs, or stop?
- **D. Hotkey and button.**
