# Auto explore (armies that uncover the map on their own)

Status: proposal, open questions F-H below (A-E answered). Nothing built yet.

Gallean Upgrade only. Human players only; the AI is not touched (it has its own explore order, see below).

## Decisions

- **The user's wish.** Like Civilization V: a button on an army sets it to **auto explore**. From then on it walks by itself to uncover unknown land, every turn, until the player takes control back.
- **Most useful land first.** If a road runs nearby (and leads into the unknown) the army follows the road: roads give the most distance per move point and usually lead to towns, sites and treasure. Without an interesting road it goes to the nearest unknown area that it can uncover the most of.
- **"No more land to explore".** When every tile the army could ever reach is already uncovered (for example the rest is walled off by mountains that are already visible on all sides), the game says so once and the button is disabled for that army.
- **When it walks (question A: A1).** Right away when Explore is pressed, with the move points the army has left, and then on every End turn together with the move orders (user: "right away and on every end turn").
- **Roads (question B: B1).** Only roads that lead into the unknown get a score bonus: the seen end of a road that continues into the fog. Roads ending at known places get none. The bonus is a tunable constant, starting at 10 tiles' worth, to be tuned on the 1024 War of the Ring map.
- **What stops an explorer (question C: C1).** The same as move orders (a battle in reach asks the move-order prompt, bags are picked up, sites ask on arrival), plus: an enemy army newly seen within 3 tiles (tunable, start value 3) stops the explorer for the rest of the turn and puts it in the end-of-turn list of armies that need the player. It keeps exploring next turn unless the player changes it.
- **Danger (question D: D3).** Targets within the safety distance of a known hostile army (not one standing still in a city) are skipped. The distance is one tunable setting shared with the C1 sighting rule (start value 3), so explorers stay out of the zone where they would stop anyway. If the only hidden land lies inside such zones, the result is NoTargetNow (try again next turn), never "no more land". Interception on the path stays covered by the path's battle check.
- **Key and button (question E: E1).** Hotkey **L** ("look around") plus a compass button on the selected army's card next to Continue and Stop. L or the button again, X or Stop end exploring. Greyed with a tooltip when there is no more land. (I is taken by the cities list in `docs/cities-overview.md`.)
- **Built on move orders.** Auto explore reuses the move-order machinery (`docs/move-orders.md`): the walk on End turn, the battle prompt, bag pick-ups, the bar look. An exploring army is a special kind of order whose destination is re-picked every time it is reached.

## The problem today

On 512 and 1024 maps most of the map stays hidden for dozens of turns. Scouting means picking a fog edge by hand for every scout every turn. Move orders help only when you already know where to go.

## What the game already does (code read)

- **Fog per player.** `WorldPlayer.FogOfWar` is a bitmap (1 = seen). `FogOfWarCalculator.Update` reveals a square of `StackStats.VisionRadius` (`Globals` min 1, max 8) around every own army and around own cities, plus own-land vision on big maps (`OwnLandVisionCalculator`). Seen tiles never go dark again except by the fog spell.
- **Movement costs** (`WorldMovementCalculator.GetMovementCost`): plain land 2, forest 4 (2 with forest ability), road 1 (2 with the Plain ability), water 6 for land armies (2 for swimmers), mountains impassable. So a road halves the cost of walking for most armies.
- **Paths never cross fog** and never cross bag tiles (`WorldPlan`, see `references/world-screen-movement.md`). A path target must be a seen tile.
- **The AI already explores.** `WorldStackOrder.Explore` -> `WorldAiPlanner.MoveAction.PlanExploreMove`: for every seen tile on the map it counts hidden neighbours (8 around), skips tiles whose path ends in battle, scores `hidden * 10 - path cost` and walks toward the best one, remembering the target. It knows nothing about roads, scans the whole map with one path query per frontier tile (heavy on 1024) and never says "nothing left".
- **Move orders (ours).** Stored destination per army, walks on End turn in bar order, prompt before any battle, bags picked up on the way, arrivals with move points left keep the turn open (decision K). Client only, not saved.
- **Keys:** bound are Tab/N, T, M, X, O, Space, Enter, E, C, B, U/J, P, 1-5, W/A/S/D. Free: F, G, H, I, K, L, Q, R, V, Y, Z.

## What the player would see

1. Select an army. Next to Continue and Stop on its card there is an **Explore** button (compass icon), hotkey **L** (decision E).
2. Click it: the army walks at once with the move points it has left, toward the best unknown area, and keeps exploring every turn when you press End turn. Its card gets the order look (soft green move points) plus a compass mark instead of the turns count.
3. When it reaches a target it picks the next one by itself; the path drawn when you select it is the current target.
4. Anything you would want to decide stops it, like a move order: a battle in reach asks the move-order prompt (Attack / Plan another path / Cancel order), a site asks on arrival.
5. Giving the army a normal path, pressing X / Stop, or Explore again ends auto explore.
6. When nothing reachable is left: a message "<Leader>'s army has no more land to explore." and the army becomes a normal army; its Explore button is greyed with the tooltip "No more land this army can reach." It becomes active again if the map changes (mountains removed by an event or rod, a new region revealed by a spell or another army).

## The algorithm (proposal)

All in one engine calculator, `AutoExploreCalculator` (new file, Engine/World), pure function of state + army, so it is unit-testable and cheap to call from the client.

**Step 1: what can this army reach at all (optimistic flood fill).** Flood fill from the army over tiles that are passable for its movement profile (`IsPassable`), counting **hidden tiles as passable** (we don't know what is there). Seen mountains and the map edge stop it; for a land army water is passable (cost 6), for a ship only deep water. Result: the set of hidden tiles the army might still reach. Empty set = **no more land to explore** for this army. That is exactly the user's "surrounded by mountains on all sides and you can see them" case, and it is per army: a ship and a land army on the same coast get different answers.

**Step 2: candidate targets (frontier).** Seen, passable tiles adjacent to a hidden tile and inside the flood fill, that the army can stand on (no army, city or bag on them) and whose path does not end in a battle (same check as the AI, `PathTraversalCalculator` encounter). One Dijkstra from the army over seen tiles (the existing `WorldPathMap` from `Pathfinding.GetPathMap`) gives every candidate's cost at once, instead of the AI's one query per tile.

**Step 3: score.** For each candidate:

`score = revealed * W_reveal + roadBonus - cost * W_cost`

- `revealed` = how many hidden tiles standing there would uncover (the real vision square of this army, not just the 8 neighbours).
- `roadBonus` (question B): the candidate is a road tile, or the road the army stands on continues into the fog through it. A road leading into the unknown beats open land of similar cost; roads are cheaper anyway, so cost already pulls the army along them, the bonus makes it keep following a road instead of turning off into a forest that uncovers two tiles more.
- `cost` = path cost in move points (turns matter more than tiles: a target reachable this turn is preferred).
- Ties: keep going in the same direction as the last target (no zig-zag), then lower tile index (deterministic for tests).

**Step 4: commit.** The best candidate becomes the army's destination (`stackDestinations` + the order flag). It is re-picked only when reached, when it stops being a frontier (another army or a spell revealed the land behind it) or when its path gets blocked. That avoids the army changing its mind every step.

**Interest points on the way** (question C): a candidate next to a seen but unvisited object (bag, ruin, site) could get a bonus, but walking into a ruin means a battle, so the default is: bags yes (picked up like move orders), ruins and guarded things no (shown in the end-of-turn alert instead).

**Cost.** One flood fill (bitmap, linear in the reachable area) + one path map per pick. On the 1024 map that is a few ms per pick, and picks happen only on arrival, not per step. To be measured with movebench before merging (target: under 20 ms per pick on 1024).

## Technical approach

- Engine: `AutoExploreCalculator.FindTarget(state, stackId, context, lastTarget)` -> `Target(position) | NothingLeft | NoTargetNow` (NoTargetNow = hidden land is reachable but every frontier is blocked right now, e.g. an army stands in a pass: skip this turn, try again next turn, don't say "no more land"). Plus `HasReachableHiddenLand(state, stackId)` for the button state. A read-only engine command wraps both so the client stays a dispatcher.
- Client: `WorldScreenViewModel.AutoExplore.cs` partial next to `MoveOrders.cs` / `MoveOrderWalk.cs`: a set `exploringStacks`; `ToggleAutoExploreCommand`; in `WalkMoveOrdersAsync`, an exploring army first asks for a target, walks to it, and while it has move points left asks again (up to a cap per turn, like the 4 moves after bag pick-ups) so it does not waste points (user rule from move orders: never waste move points silently). Exploring armies do not trigger decision K (arriving with points left) since they use them themselves.
- Card: Explore button in the card template in `WorldScreenView.axaml` next to Continue/Stop, `IconVisual` compass, greyed with tooltip when `HasReachableHiddenLand` is false. Strings in all 7 catalogs.
- Not saved, like orders (question F).

## Testing

- Engine unit tests on small hand-made maps: road vs open land (follows the road), forest vs plain, mountain ring fully seen -> NothingLeft, mountain ring with one gap -> Target through the gap, hidden pocket behind water for a land army (reachable) vs a ship (not), frontier blocked by an own army -> NoTargetNow, enemy on the only path -> no battle target.
- Client tests: toggle, the walk on End turn, re-pick on arrival, Stop, "no more land" message and the disabled button, battle prompt reuse.
- Visual test: card with the Explore button, greyed state, Russian.
- Real client: 1024 War of the Ring, a Pegasus Knight on auto explore for 10 turns: count uncovered tiles per turn against walking by hand; and a measured pick time on 1024.

## Phases

1. **Engine calculator + tests** (flood fill, frontier, score, NothingLeft/NoTargetNow, a perf bench on 1024). ~1 day. No screen; the visible part is phase 2.
2. **Client: Explore button, walks on End turn and right away, card look, "no more land" message, disabled button.** ~1.5 days. Real-client check on 1024.
3. (Optional, question G) **Explore all**: one key that sets every idle scout (leaders of the Explorer category) to explore, or "explore with all armies that have nothing to do". ~0.5 day.

## Open questions

- ~~**A. When does an exploring army walk?**~~ Answered: A1 (see Decisions).
- ~~**B. How strongly to prefer roads?**~~ Answered: B1, bonus starts at 10 tiles (see Decisions).
- ~~**C. What stops an explorer?**~~ Answered: C1, sighting distance starts at 3 (see Decisions).
- ~~**D. Should it avoid danger?**~~ Answered: D3, shared distance with C1, starts at 3 (see Decisions).
- ~~**E. Hotkey and button.**~~ Answered: E1, L + compass button (see Decisions).
- **F. Saving.** F1 (recommended, consistent with move orders): not saved, loading a save forgets it. F2: save it (engine/save-format change, then move orders should be saved too).
- **G. Explore all.** G1: phase 3 as above. G2 (recommended): skip for now; one button per army first.
- **H. "No more land" scope.** H1 (recommended): per army (its movement profile decides; a ship and a land army differ), the button re-enables when the map changes. H2: per player: once one army finds nothing, the button is disabled for all (wrong for ships vs land armies, simpler message).
