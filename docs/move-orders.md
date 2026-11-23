# Move orders (armies that keep walking on their own)

Status: all three phases merged: 1 (bb2ed500), 2 (54e66f09), 3 (ed787104).

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
- **Cancel and wait (question J: J1).** X or a Stop button next to Continue on the selected card deletes the order (army stays, becomes a normal army). A new path replaces the order. Finish (Enter) = wait this turn: the army does not walk on E this turn but keeps its order for the next turn (covers the wait button the prompt doesn't have).
- **Arrival with moves left (question K: K2).** User: move points are valuable, never waste them silently. After the walks, every army that arrived with move points left joins the end-of-turn queue: the camera goes to it, it is selected, and a short message says it arrived with N moves left. The turn stays open; pressing E again ends it (those armies are not asked again this turn unless they get a new order).
- **When a move becomes an order (decided while building phase 1).** A confirmed move becomes an order only once the army stops short of its stored destination (or at once when it has no move points left to start walking). A move that arrives this turn never flips the card to the order look, so cards don't jump to the end of the bar and back during ordinary moves (an upstream test, `StackItemMovementTest`, checks the cards stay the same objects). With the hover path off, the first click only selects a path as before; it becomes an order after the confirming click and the walk.
- **Settings layout (user correction after phase 1).** Both world-map checkboxes, "Show the army path under the cursor" and "Tab skips armies with orders" under it, sit in the panel footer left of Close (merged cef5bbb9). The panel had no free row; the footer row was half empty. RU label back to "Tab пропускает армии с приказом".
- **Bags on the way (found while building phase 2).** Paths never cross a bag tile, so a walk meets a bag only as its destination: the army picks it up from the next tile (the game's own treasure popup when it holds items) and walks on onto the tile. Decision H's "pick up and continue" is therefore in phase 2 already.
- **Space during the walks (decided while building phase 2).** Space switches on the game's own fast-forward (2x, as during computer turns) for the rest of the walks instead of skipping one walk; the fast-forward ends with the walks.
- **Surprises are rare (found while building phase 3).** The path check counts every army on the map, hidden ones too, and paths never cross the fog, so nearly every battle an order runs into this turn is already known before anyone walks and is asked in the first round. The after-walk question is a safety net (for example when a walk changes the route); a test with the first round switched off shows that net catches the same battle.
- **Cancelled orders (decided while building phase 3).** An army whose order is cancelled in the prompt keeps the turn open once if it still has move points ("stopped with N move points left"), the same rule as arrivals (decision K).
- **Prompt (decided while building phase 3).** A copy of the game's attack prompt (`MoveOrderPromptView`, 640 px wide so the Russian buttons fit) instead of an extra button in the upstream view; it shows the enemy's portrait like the original.
- **The prompt shows what the player knows about the target (user request after phase 3, merged 846837c7).** Instead of the large portrait, the prompt shows the target like the world's right-click info (`WorldStackInfoViewModel` / `WorldFortificationInfoViewModel`, templates copied from `WorldScreenView`): an army's leader with its units when the player may see them (`WorldVisibility.KnowsPlayerContents`: own, neutral, or a race a thief has spied on), otherwise the leader with the game's "A thief must spy on this race to gain information about it."; a city shows its level, owner, garrison and visiting party the same way (or the city thief note). Tests: `PromptShowsEnemyUnitsWhenKnownTest` (spied: 1 unit, not spied: the note), visual `WorldMoveOrderPromptTest` shots 1-4 (army with units, Russian, not spied, capital with garrison). Suite 5495. The game's own attack prompt (when you click) is unchanged.
- **Tab (question C: C1 + C3, with a setting).** By default Tab/N skips armies with orders and End turn lights up once the others are done. A new key **O** cycles only through armies with orders (Shift+O backwards). A Settings checkbox ("Tab skips armies with orders", default on) decides what Tab does: on = C1; off = Tab visits every army as today, ordered ones last since they sit at the end of the bar. O and clicking a card work either way. (Transcript read as "O button" for the review key; tell me if it meant something else.)

- Upstream v0.17.1 merge (2026-11-23): a tile next to a site's entrance counts as the site (upstream 651e4200). An
  order to a site stops on the first such tile and opens the site screen there (`OrderStop.Site` from the path's site
  encounter); `ReachedSiteOpensTest` follows that.

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
6. An army that arrives with move points left stops the turn from ending: the camera selects it so the points can be used, and the next E ends the turn (decision K).
7. If an army meets something during its walk that the check couldn't see (an enemy revealed by the fog, an interception), it stops one tile short, keeps its order, and is asked about after the others have walked, before the turn ends (question F).

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

- Client tests (`WorldScreenViewModel`, test world builder): with an order, hovering keeps the order path and a first click only enters re-plan mode, the second click replaces the order; bar order, colour flag and turns left (drops by one after a turn of walking); Tab skips ordered armies with the setting on and visits them last with it off; O cycles only ordered armies; End turn highlight; E walks two ordered armies in bar order and ends the turn; an army blocked by an own army that walks later gets through on the retry pass; an order with an enemy in reach stops End turn and opens the prompt, Attack fights, Plan another path leaves the turn open, the second E doesn't ask again; an enemy revealed during the walk stops the army one tile short; arriving clears the order; X clears the order; an army arriving with moves left keeps the turn open and is selected, the second E ends the turn; a finished army with an order doesn't walk on E and walks again next turn.
- Visual tests: bar with ordered cards (colour, turns left, position; also in a crowded, overlapping bar), the order path staying drawn with the mouse elsewhere and the hover path after the first click, the attack prompt during End turn.
- Real client (AOT build) on the 1024 War of the Ring map: give 5 armies far orders, press E for several turns (orders are expected to be gone after a load).

## Phases (estimates)

1. ~~**Visible orders** (~1.5 days): bar colour, turns-left number and order, Tab skip + setting, O key, End turn highlight, order path stays put while the mouse moves (first click enters re-plan mode), X / Stop. No automatic walking yet; M still walks.~~ Done, see "Phase 1 — done".
2. ~~**Orders walk on End turn** (~1-2 days): the guarded walk for orders without any battle this turn, one by one in bar order with the camera following and Space skipping, the retry pass, Finish = wait this turn; anything unexpected stops the army and blocks End turn with a short message until the player handles it.~~ Done, see "Phase 2 — done".
3. ~~**End-of-turn queue** (~2-2.5 days): arrivals with moves left (decision K), the confirm queue with the attack prompt, accepted-this-turn memory, surprises after the walk, treasure popup, sites and thief actions asked this turn.~~ Done, see "Phase 3 — done".

## Phase 1 — done

Feature commit a3386812, merged in bb2ed500 (gallean-upgrade).

What was built (client only, `Screens/World/WorldScreenViewModel.MoveOrders.cs` plus small edits):
- An army has a move order when a confirmed move stopped short of its stored destination (`confirmedOrders` next to upstream's `stackDestinations`; still not saved, decision I).
- Army bar: ordered cards sort last (join key `(HasMoveOrder, -Level, StackId)`), move points in soft green (`Theme.World.StackBar.OrderedMovesColor` 150,214,120 + `WorldStackBarOrderedMovesBrush`), and `» N` under them = turns of walking left (from the path segments' `Turn`; recomputed only when the world state changes; kept from the last calculation during other players' turns).
- Tab/N skip ordered armies while the new setting `GameSettings.TabSkipsOrders` (default on) is on; End turn lights up when only ordered armies have move points left (always, independent of the setting). Settings checkbox "Tab skips armies with orders" in the Game section next to Autosave limit (the panel had no free row; the RU label is "Tab пропускает армии в пути" so it fits).
- O / Shift+O cycle through ordered armies; X or the new Stop button (cross icon, shown only for an ordered army, 4th socket above the selected card) cancels the order.
- Hover path on: an ordered army keeps showing its order path while the mouse moves; the first map click enters re-plan mode (hover path, nothing moves), the next click sets the new order. Re-planning keeps the card in the order look while the army walks. Selecting another army ends re-plan mode.

Tests: `WorldMoveOrdersTests` (13: far click creates an order with 2 turns left, short move is no order, ordered cards sort last, order path stays under the mouse, first click re-plans, selection change ends re-plan, hover off keeps two clicks, cancel, arrival ends the order, Tab with the setting on and off, End turn highlight, O cycling), `GameSettingsStoreTests.LoadTabSkipsOrdersTest`; `SaveFormatTest` and `ClientTextIndexTests.SettingsTest` updated for the new setting. Visual: `WorldMoveOrdersBarTest` (S107 with 8 hero copies, the hero ordered to a tile 2 turns away: shots `1-selected`, `2-unselected`), `SettingsTest` / `SettingsRussianTest` checked, `WorldStackBarCompactTest` / `WorldStackBarScrollTest` still pass. Suite 5470 (5456 on gallean-upgrade + 14).

Real client (AOT build, The War of the Ring as Empire): far hover click -> the Pegasus Knight walks this turn's part (to the 24-point mark) and its card moves last with green `0/23` and `» 1`; O selects it from the other army; X turns it back (orange `0/23`, first in the bar); a new order with 0 move points works without an error; next turn Tab skips it, Enter on the other army lights End turn; the first click on the map switches to the hover path without moving, the second moves; the settings checkbox writes `"tabSkipsOrders": false/true` to settings.json.

Findings: none blocking. The 5th Column quest's revealed area is too small to give a multi-turn order on turn 1 (paths don't cross the fog), so the real-client check used the 1024 map.

Known limits: orders still need M (or E in phase 2) to walk; no turns label on the map path itself.

## Phase 2 — done

Feature commit fe717a1a, merged in 54e66f09 (gallean-upgrade, on top of town income phase 2; the 7 translation catalogs conflicted and were rebuilt from gallean-upgrade's plus our strings). The settings layout fix (4080558c) went in first, merged in cef5bbb9.

What was built (client only, `Screens/World/WorldScreenViewModel.MoveOrderWalk.cs` plus small edits):
- `EndTurnAsync` first runs `WalkMoveOrdersAsync`: the ordered cards in bar order; skipped are armies without move points, finished ones (Finish = wait, decision J) and armies already reported this turn.
- Each walk is the game's own step loop with a guard (`ActiveMove.Guarded`): before every step the path is checked again and the walk stops when the next step would start a battle (an army, a city, an interception: the engine resolves an encounter at the end of the approach) or a thief already stands next to its target. Errors stay quiet (no error popup for a blocked army).
- Retry pass: an army whose first step failed for any reason but missing move points walks again after all others.
- A bag pick-up ends the game's move; the walk then goes on (up to 4 moves per army).
- After the walks, armies that need the player keep the turn open: stopped before an encounter, still blocked after the retry, reached a site, arrived with move points left, or stopped by something else (a scenario event) with move points left. One info alert lists them ("An army with an order needs you" + one line each + "End the turn again to continue."), the first is selected and centred. They are not reported again this turn (unless they get a new order), so the next End turn ends the turn.
- While walking, the screen counts as busy (no hover path, no clicks); Space fast-forwards.

Tests: `WorldMoveOrdersTests` +7 (walks over three End turns and arrives, arrival with moves left keeps the turn once, Finish waits a turn, stops at (7,0) before an enemy that intercepts at (8,0), picks up an ordered bag and walks onto its tile, a destination taken by an own army is reported after the retry, Space fast-forwards). Mutation checks: guard switched off -> the enemy test fails (a battle starts); one move per walk -> the bag test fails. Suite 5482 (5475 on gallean-upgrade + 7).

Real client (AOT build, The War of the Ring as Empire): the Pegasus Knight with an order 2 tiles away walked on E and the alert "Pegasus Knight arrived with 19 move points left. End the turn again to continue." kept turn 2 open; an order 10 points away from 0 move points: E ended the turn without moving, on turn 4 E walked it along the path (camera following) and reported 13 points left; the settings show both checkboxes stacked left of Close. The stop before an enemy and the bag pick-up were checked by tests only (no enemy near the start on that map).

Known limits (phase 3): the alert is a short message, not the per-army prompt; armies stopped before a battle aren't asked Attack / Plan another path / Cancel order yet; sites aren't opened; the walk order of armies with the same leader level follows the army ID.

## Phase 3 — done

Feature commit e6d58839, merged in ed787104 (gallean-upgrade, on top of town income phase 4 docs; the 7 translation catalogs conflicted again and were rebuilt).

What was built:
- `Screens/World/AttackPrompt/MoveOrderPromptViewModel` / `MoveOrderPromptView` (registered in `ViewDataTemplate`): title "Confirm the attack", the enemy's portrait (or the unknown-enemy icon), "<army>: the path leads into battle with <enemy>.", buttons Attack / Plan another path / Cancel order; Esc = Plan another path.
- `WalkMoveOrdersAsync` now runs in three rounds: (1) every ordered army whose path reaches a battle this turn is asked in bar order, with the camera on it; (2) the walks: accepted armies walk unguarded and fight when their turn comes (the game's normal battle), the rest stay guarded; (3) in bar order: a guarded army stopped before a battle is asked the same way (Attack walks it now), a thief next to its target opens the game's thief window, an army that reached a site opens the site screen. Then the alert for armies with move points left, including cancelled orders.
- Plan another path ends the End turn at once and puts the army in re-plan mode (the path follows the mouse; the next click sets the new order). Accepted attacks are remembered for the turn; a new order clears them.

Tests: `WorldMoveOrdersTests` 24 (phase 3: planned attack asked first with the right names, Plan another path keeps the turn and enables the hover path, Cancel order keeps the turn with "stopped with 4 move points left", Attack starts the battle at the interception, Esc = Plan another path, reaching a trainer opens its screen and closing it keeps the turn with "reached Trainer"). Visual: `WorldMoveOrderPromptTest` (English and Russian shots with the real pack). Mutation checks: first round off -> the two first-round tests fail, while Attack and Esc still pass through the after-walk question (so that branch is tested); walks always guarded -> the Attack test fails. Suite 5493 on the merged tree (5486 on our branch; gallean-upgrade gained tests meanwhile).

Real client (AOT build): the build starts and plays the custom mapgen map; the 1024 checks from phase 2 still apply. The prompt itself was checked with the visual test only: on the test maps no enemy stood where an order could reach it without the game's own attack prompt opening first (an order needs the army to stop short, and an enemy within this turn's reach is asked by the original prompt when the order is given).

Known limits: thief actions open the thief window without our prompt (it has its own cancel); after a fought battle the army doesn't walk on in the same End turn; a walk order between armies with the same leader level follows the army ID.

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

~~J. **Cancel and skip.**~~ Answered: J1, X / Stop cancels, Finish waits a turn and keeps the order (see Decisions).

~~K. **An army arrives with move points left during the End turn walk.**~~ Answered: K2, End turn stops and selects the army (see Decisions).
