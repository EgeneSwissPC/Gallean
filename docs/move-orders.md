# Move orders (armies that keep walking on their own)

Status: proposal, open questions below. Nothing built yet.

Gallean Upgrade only. Human players only; the AI is not touched.

## Decisions

- The feature is called **move orders**: an army whose stored path goes further than it can walk this turn has a move order.
- Orders run when the player presses End turn (E), before the turn ends, one army at a time (user's wish).
- Armies with an order look different in the army bar (move points in another colour) and sit at the end of the bar (user's wish).
- An order that would start a battle this turn never fights on its own: End turn stops and asks, army by army, in bar order: attack, or plan a different path (user's wish).
- **Order path display (question A: A3, with the user's refinement).** A selected army with an order shows only its order path; the path does not follow the mouse. The first click anywhere on the map switches that army into the hover path mode (the path follows the mouse as for armies without orders); the next click accepts the new path and replaces the order. Armies without an order keep the one-click hover path. No second path in the renderer is needed.
- **Bar look (question B: B3).** Cards with an order show their move points in soft green (not blue: the selected card already glows light blue) plus the number of turns the army still needs to arrive. User: spec it now so it isn't forgotten.
- **How orders run (question D: D1).** On E, ordered armies walk one by one in bar order, animated, the camera jumping to each like during the AI turn. Space skips the current walk; holding it skips the rest. Then the turn ends. A setting for instant walks (D2) may come later.
- **Walking order (question E: E1).** Bar order, left to right (leader level high first, then ID). Retry pass (recommended add-on, taken unless the user objects): an army blocked by one of the player's own armies tries once more after all others have walked; if still stuck it keeps its order and stays put.
- **Surprises during the walk (question F: F1).** An enemy revealed by the fog or an interception stops the army one tile before the battle; it keeps its order, the others carry on, and after all walks (before the turn ends) each stopped army is asked with the question G prompt.
- **Attack prompt during End turn (question G: G1).** The game's `AttackPromptViewModel` window, camera on the asking army with its path drawn, text naming both armies ("<own leader>'s army: the path leads into battle with <enemy>"). Buttons: **Attack** (accepted for this turn, fights when its walk comes), **Plan another path** (closes, selects the army in re-plan mode, turn stays open; Esc does the same), **Cancel order** (order deleted, army stays, next army is asked).
- **Other encounters on the way (question H, user's own variant).** Bags: the army picks the bag up, the game's own "Treasure" loot popup (`LootScreenViewModel`, Continue button) shows what was found, and Continue resumes the walk. Sites (merchant, mage, trainer, mercenaries) reached as the destination and thief actions: asked this turn, after the walks and before the turn ends, in the same queue as the F1 surprises: the camera goes to the army and the site screen or the thief action window opens as it does for a manual move. Closing it carries on with the queue.
- **Saving (question I: I2).** Orders are not saved: they live in the world screen like today's stored destinations and are gone after loading a save. No engine or save-format change; the feature stays client-only. Phase 4 is dropped.
- **Tab (question C: C1 + C3, with a setting).** By default Tab/N skips armies with orders and End turn lights up once the others are done. A new key **O** cycles only through armies with orders (Shift+O backwards). A Settings checkbox ("Tab skips armies with orders", default on) decides what Tab does: on = C1; off = Tab visits every army as today, ordered ones last since they sit at the end of the bar. O and clicking a card work either way. (Transcript read as "O button" for the review key; tell me if it meant something else.)

## The problem today

On big maps (512, 1024) a player ends up with many armies that spend most turns walking across empty land. Each one needs a click or an M press every turn.

## What the game already does (code read)

- **The client already remembers a destination per army.** `WorldScreenViewModel.stackDestinations` (upstream code) stores the target tile when a path is confirmed. It survives the end of the turn and is dropped only when the army arrives or disappears (`UpdateSelection`, `UpdateSelectedPath`). Continue (M, the chevrons button on the selected card) walks the stored path again (`ContinuePathCommand`).
- **It is not saved.** It is a field of the world screen view model, so loading a save forgets every stored path.
- **Our hover path hides it.** With "Show path under the cursor" on (`WorldScreenViewModel.HoverPath.cs`), `ResolveRenderCursor` replaces the cursor's path with the hover path as soon as the mouse is over a tile, so the stored path disappears while M still works. A single click on a far tile stores the destination (`TryMoveAlongHoverPath` calls `SetSelectedPath`), so every far click already creates what this spec calls an order.
- **Path colours** (`Theme.World.Path`): gold = reachable this turn, grey = later turns, red = attack.
- **Encounters on a path.** `WorldGetPathCommand` returns the path, the remainder and the first encounter (`WorldPathEncounter.Battle / Thief / Bag / Site`). A battle encounter is only reported when the army can reach it this turn (`CanBegin`). An enemy next to the path also counts (interception: `PathTraversalCalculator.FindInterceptingStack`).
- **A manual move re-plans every step.** The client dispatches `WorldStepStackCommand` tile by tile, and each step recomputes the encounter against the real armies. An enemy revealed by the fog during the move can therefore start a battle on the next step.
- **Attack prompt.** When the path's battle target is not the clicked tile, the game shows `AttackPromptViewModel` (portrait, name, attack/cancel) before moving.
- **Army bar.** `UpdateStackItems` sorts cards by leader level (high first), then ID. Tab/N picks the next card with move points left that is not finished (`FindNextAvailableStackItem`). The End turn medallion lights up when no army has move points left or all are finished (`UpdateEndTurnHighlighted`). Finish (Enter) marks an army done for this turn (`WorldFinishedStacks`).
- **Keys in the world view:** Tab/N (Shift for previous), T, M, Space, Enter, Ctrl+Enter, E, C, B, U/J, P, 1-5, W/A/S/D and arrows. X is free.

## What the player would see

1. Give an army a far destination as today (one click with the hover path on, or two clicks). The army walks this turn's part and keeps the rest as an order.
2. Its card moves to the end of the army bar, its move points (e.g. `24/24`) turn soft green and a small turn count shows how many turns are left to arrive (e.g. `3`) (decision B). Tab skips it unless the setting says otherwise; O cycles through ordered armies (decision C).
3. Selecting it shows its order path, and moving the mouse does not redraw it. To give a new order, click anywhere on the map: from then on the path follows the mouse like for any other army, and the next click sets the new path (decision A).
4. Press E. If any order would start a battle this turn, the game stops before moving anyone: the camera goes to the first such army, its path is shown, and an attack prompt in the game's own style asks **Attack** / **Plan another path** (/ **Cancel order**, question G). Plan another path selects the army and leaves the turn open; the next E starts the check again, and armies already accepted this turn are not asked twice.
5. When nobody needs confirming, the ordered armies walk one by one in bar order, the camera following each, Space fast-forwards. Then the turn ends as usual.
6. If an army meets something during its walk that the check couldn't see (an enemy revealed by the fog, an interception), it stops one tile short, keeps its order, and is asked about after the others have walked, before the turn ends (question F).

## Technical approach

Client only (decision I: orders are not saved).

- **Order state.** Keep `stackDestinations` as the source of truth (it already is) and add a read helper `HasMoveOrder(stack)` = destination stored and not reached. Not saved (decision I).
- **Army bar.** `WorldStackItemViewModel.HasMoveOrder`; sort key `(HasMoveOrder, -Level, StackId)` in `UpdateStackItems`; a `Classes.ordered` on the card with a new `WorldStackBarOrderedBrush` for the move-points text (Theme colour + brush in `Colors.axaml`). Turns left: `WorldStackItemViewModel.OrderTurns`, from a `WorldGetPathCommand` dry run per ordered army (every `WorldPathSegment` carries its `Turn`, so the count is the last segment's turn, plus one if turns are numbered from zero; check the numbering before building). Compute only for ordered armies and only when the world state reference changed, not on every bar refresh, and draw it as a small outlined number on the left side of the card (the side that stays visible when cards overlap). `FindNextAvailableStackItem` skips ordered armies when `GameSettings.TabSkipsOrders` is on (new record param after `ShowPathOnHover`, default true, same no-format-bump pattern); `HasAvailableStack` always ignores them, so the End turn medallion lights up when only ordered armies are left. New `SelectNextOrderedStackCommand` / previous (O / Shift+O in `WorldVisual.GetHotkeyCommands`). The checkbox goes into the Settings panel next to "Show path under the cursor"; the panel is at its height limit for 1366x768, so it shares a row or the panel widens (check the Settings visual tests, EN and RU).
- **Order path display (decision A).** `ResolveHoverPathKey` returns null while the selected army has an order and is not in re-plan mode, so the stored path (`cursor.Path`) stays drawn. A view-model flag `replanStackId` is set by the first map click on such an army (the click does nothing else) and cleared on selection change, Esc, a confirmed move or the end of the turn; while it is set the hover path and its one-click move work as today.
- **End turn.** `EndTurnAsync` becomes: collect ordered armies with move points > 0 in bar order -> dry-run `WorldGetPathCommand` for each -> armies with a battle (or thief) encounter this turn that aren't accepted yet go into a confirm queue -> if the queue isn't empty, run the prompts and stop -> otherwise walk each army with a guarded step loop -> confirm queue for armies stopped by surprises -> `WorldEndTurnCommand`.
- **Guarded walk.** Before each `WorldStepStackCommand` of an auto-walk, re-run `WorldGetPathCommand` (0.01-0.2 ms per call on the 512 map, measured for the hover path with movebench `--hover`) and stop if a battle encounter that wasn't accepted appears. Accepted attacks use the existing encounter visitor (same battle flow as a manual attack).
- **Cancel.** X and a Stop button next to Continue on the selected card (question J).
- New UI strings in all 7 catalogs (ru translated).

## Testing

- Client tests (`WorldScreenViewModel`, test world builder): with an order, hovering keeps the order path and a first click only enters re-plan mode, the second click replaces the order; bar order, colour flag and turns left (drops by one after a turn of walking); Tab skips ordered armies with the setting on and visits them last with it off; O cycles only ordered armies; End turn highlight; E walks two ordered armies in bar order and ends the turn; an army blocked by an own army that walks later gets through on the retry pass; an order with an enemy in reach stops End turn and opens the prompt, Attack fights, Plan another path leaves the turn open, the second E doesn't ask again; an enemy revealed during the walk stops the army one tile short; arriving clears the order; X clears the order.
- Visual tests: bar with ordered cards (colour, turns left, position; also in a crowded, overlapping bar), the order path staying drawn with the mouse elsewhere and the hover path after the first click, the attack prompt during End turn.
- Real client (AOT build) on the 1024 War of the Ring map: give 5 armies far orders, press E for several turns (orders are expected to be gone after a load).

## Phases (estimates)

1. **Visible orders** (~1.5 days): bar colour, turns-left number and order, Tab skip + setting, O key, End turn highlight, order path stays put while the mouse moves (first click enters re-plan mode), X / Stop. No automatic walking yet; M still walks.
2. **Orders walk on End turn** (~1-2 days): the guarded walk for orders without any battle this turn; anything unexpected stops the army and blocks End turn with a short message until the player handles it.
3. **Attack confirmations** (~1-2 days): the confirm queue with the attack prompt, accepted-this-turn memory, surprises after the walk, treasure popup, sites and thief actions asked this turn.

## Open questions

~~A. **Order path while the mouse moves.**~~ Answered: A3 with the user's refinement (see Decisions): no hover path for an army with an order; the first map click enters the hover path mode, the next click sets the new path.

~~B. **Bar look.**~~ Answered: B3, soft green move points plus turns left to arrive (see Decisions).

~~C. **Tab and ordered armies.**~~ Answered: Tab skips them by default, O reviews them, a Settings checkbox switches Tab back to visiting all (see Decisions).

~~D. **When orders run.**~~ Answered: D1, one by one, animated, camera follows, Space skips (see Decisions).

~~E. **Walking order.**~~ Answered: E1, bar order, plus a retry pass for armies blocked by own armies (see Decisions).

~~F. **Surprise encounters during the walk.**~~ Answered: F1, stop one tile short, keep the order, ask before the turn ends (see Decisions).

~~G. **Prompt buttons.**~~ Answered: G1, Attack / Plan another path / Cancel order (see Decisions).

~~H. **Other encounters on the way.**~~ Answered: bags picked up with the Treasure popup, then the walk continues; sites and thief actions open this turn, before the turn ends (see Decisions).

~~I. **Save orders?**~~ Answered: I2, not saved (see Decisions).

J. **Cancel and skip.** J1: X or the Stop button cancels the order; a new path replaces it; Finish (Enter) skips the army this turn but keeps its order (recommended). J2: Finish also cancels the order.

K. **An army arrives with move points left during the End turn walk.** K1: the turn still ends (recommended, the player pressed E). K2: End turn stops so the player can use the remaining points.
