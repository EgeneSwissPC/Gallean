# Switchable AI (world AI and battle AI)

gallean-upgrade only. Status: phases 1-4 done (interfaces, registry, global Map AI / Battle AI settings, own AI per computer player, AI tournament tool; Classic only).

## Decisions

- **Two separate switches.** The world-map AI and the battle AI are chosen independently (a "Builder" world AI can fight with any battle AI).
- **The global switch lives in Settings and is NOT part of a save.** It's a client setting (`GameSettings`), like the language. Switching it changes the AI in every game, including a running save, starting with the next AI action.
- **Per-player overrides are rare and opt-in.** A game can give one specific AI player its own world AI and/or battle AI. Only those overrides go into the save. Every AI player without an override follows the global switch.
- **Resolution order:** override for that player if it exists → global setting → Classic.
- **Switching mid-game is allowed** at any time (Settings during a game, or the per-player panel).
- **When the global switch changes and the current game has overrides,** the game asks once: "N AI players in this game have their own AI. Switch them too?" with the choices *Only players without their own AI* (default) and *Everyone (removes their overrides)*. Nothing is overwritten silently.
- **Upstream:** no. gallean-upgrade only.
- **Map creators** can give a race its own AI in the map editor; it becomes that player's override in games started from the map (later phase, after 3).
- **Neutral armies** use the global AIs unless given their own: since 62bb88f3 the user wants neutrals listed in the AI players panel too (see below).
- **The AI's name** is shown only in the per-player panel, not on the world screen.
- **Battle AI applies to AI-controlled sides only.** The player's own side (Auto-play, Instant resolve, Heal and defend, Refuse to flee) keeps Classic; in AI-vs-AI battles both sides use the setting. (Phase 2.)
- **Settings layout:** the two drop-downs sit right of Battle speed / Movement speed, labelled "Map AI" (shorter than "World AI") and "Battle AI"; the panel grew from 520 to 660 px wide because it has no free height. Descriptions are tooltips. (Phase 2.)
- **Classic is stored as "no choice"** (null, not written to settings.json), and an unknown ID in the file shows and plays as Classic. (Phase 2.)
- **One "AI players" panel for both places** (race screen button between Back and Next; game-menu button under Settings). Rows are computer players with Map AI / Battle AI drop-downs whose first entry is "Settings choice"; a player with an own AI has a bold name. The panel applies on Close. (Phase 3.)
- ~~Neutral players aren't listed~~ -> **neutral players are listed** (user correction after phase 3, 62bb88f3). Race screen: one "Neutrals" row whose choice applies to every neutral player of the map (`ScenarioConfiguration.AiOverrides[WorldRace.Neutral]`), so the button is always enabled. In game: one row per neutral player; a map with one neutral player shows "Neutrals" (the original maps name it after its lord, "Neutral Male"), maps with neutral kinds show each kind's name. **The panel is disabled during battles** (the game menu button greys out). (Phase 3.)
- **The settings question is asked per part:** changing Map AI only asks about players with an own Map AI, Battle AI only about own Battle AIs. "Switch everyone" clears only that part. Closing the question keeps the own AIs. Not asked outside a game or during a battle. (Phase 3.)
- **The AI estimator's battle simulations stay Classic** (how the world AI predicts a fight); only real battles use the chosen battle AI. (Phase 2.)

## The problem today

- World AI: `src/Gallean.Engine/World/Ai/` (~6,100 lines, a utility AI: every action gets `WorldAiActionScore(Priority, Value)` and the best one wins). Its only entry point is `WorldAiActionCommand`, which always does `new WorldAiController(context).DecideAction(state, memory)`.
- Battle AI: `src/Gallean.Engine/Battle/Ai/` (~700 lines). Entry point is `BattleAiActionCommand` → `new BattleAiPlanner(context, canUseItems, canRetreat).DecideAction(state)`. The battle modes (auto-play, Heal and defend, Refuse to flee) use the same planner.
- Each AI player has its own `WorldAiMemory` in `state.AiMemories` (orders, targets).
- There's exactly one AI of each kind, and nothing to choose.

## What the user sees

- **Settings → Game:** two drop-downs, "World AI" and "Battle AI", each with a one-line description under the selected entry. The settings panel is already at full height at 1366x768, so these need a compact row (e.g. both on one row) or a sub-page. Check `VisualTests.SettingsTest`/`SettingsRussianTest`.
- **New game screen:** next to every AI player, a small "AI: default" marker. Clicking it opens per-player World AI / Battle AI pickers with a "Use global setting" entry (the default).
- **During a game:** the same per-player panel is reachable from the in-game menu (list of AI players, each with its override or "global"). Overrides are highlighted, so you can see that a player is special.
- **Map editor:** a map creator can give a race its own AI, which becomes that player's override in every game started from the map.

## Technical approach

1. **Interfaces:** `IWorldAiBrain { string Id; WorldAiActionPlan DecideAction(WorldState, WorldAiMemory) }` and `IBattleAiBrain` (same shape as `BattleAiPlanner.DecideAction`). The current code becomes the `classic` brains unchanged, in new files plus a small edit at the two entry points.
2. **Registry:** `AiBrainRegistry` with ID, display name and description. Unknown IDs (an old save, a removed AI) fall back to Classic, with a log line.
3. **Global setting:** `GameSettings.WorldAi` / `BattleAi` (record params with defaults, like `ShowPathOnHover`; no format bump). The client passes them into the engine context, so the engine doesn't read client settings itself.
4. **Overrides in the save:** `WorldPlayer.AiOverride` (init prop, nullable: world ID, battle ID). Old saves have none, so they follow the global setting. Changing an override is an engine command (`WorldSetAiOverrideCommand`), so it is saved and undo-free like the other commands.
5. **Battle side:** a battle against an AI player resolves that player's battle brain (override → global). Neutral armies use the global battle AI.
6. **Memory when switching:** all personality brains share `WorldAiMemory`, so switching keeps the orders. A brain with its own memory format starts with an empty one on a switch (reset, not a crash).
7. **Determinism:** brains may only use the engine random provider, so headless runs and tests stay reproducible.

## First brains (proposal)

World AI, all built on the Classic engine with different weights/rules (about a day each):
- **Classic:** today's AI, unchanged (default).
- **Classic+:** Classic plus the custom-map idle fix (don't save for a leader while the capital entrance is occupied) and awareness of summon upkeep.
- **Aggressive:** lower attack-power threshold, more units than buildings, attacks earlier.
- **Builder:** buildings, rods and spells first, attacks only with a clear advantage.
- **Expansionist:** explore and rod orders get more weight, grabs land and villages.

Battle AI:
- **Classic:** today's planner.
- **Cautious:** heals/defends more, retreats earlier.
- **Ruthless:** focuses the weakest killable target, never retreats.

Later, only if the tournament shows it's worth it: a different algorithm (e.g. looking a few moves ahead). Weeks of work, and the AI turn is already the slow part on a 1024 map, so it needs a time budget per turn.

## Testing

- Engine: the brain resolution order (override → global → Classic), unknown-ID fallback, switching mid-game keeps the memory, old saves load with no overrides.
- Client: the global switch while a game is running takes effect at the next AI action; the confirm prompt appears only when overrides exist; both choices do what they say.
- Visual: Settings (en + ru) still fits; the per-player panel.
- **Tournament bench:** a headless AI-vs-AI runner (based on `CustomMapTurnBenchTests`) plays the same map N times per brain pair, then reports wins, turn of first capture, and AI time per turn. This shows whether a personality is really different and not slower.
- AOT build: the settings file round-trips (no reflection JSON).

## Phases

1. ~~Interfaces + registry + Classic wired through (no behaviour change; full suite identical). ~0.5 day.~~ Done.
2. ~~Global settings (both drop-downs) + engine context plumbing + mid-game switch. ~1 day.~~ Done.
3. ~~Per-player overrides: save field, command, new-game and in-game panels, confirm prompt. ~1.5 days.~~ Done.
4. ~~Tournament bench. ~1 day.~~ Done (`tools/AiTournament`).
5. Personalities: Classic+, Aggressive, Builder, Expansionist; battle Cautious, Ruthless. ~1 day each, each measured on the bench.
6. (Optional) Lookahead AI.
7. Map editor: per-race AI saved in the map (after phase 3).

## Phase 1 — done

Commit ed7bf4e3, merged in 0072b613.

- `Engine/Ai/AiBrainInfo.cs` (ID, name, description) and `Engine/Ai/AiBrainRegistry.cs` (public `WorldBrains`/`BattleBrains` lists for the later pickers; internal `ResolveWorld`/`ResolveBattle`, unknown/empty/null ID -> Classic).
- `IWorldAiBrain` (`World/Ai/Brains/`) and `IBattleAiBrain` (`Battle/Ai/Brains/`); `ClassicWorldAiBrain` wraps `WorldAiController`, `ClassicBattleAiBrain` wraps `BattleAiPlanner`. The AI code itself is untouched.
- Entry points: `WorldAiActionCommand` and `BattleAiActionCommand` now go through the registry (with `null` until phase 2 passes the setting). `TestWorldAiRunner` and AI tests still call `WorldAiController` directly, which is the same code.
- Tests: `AiBrainRegistryTests` (5: Classic first, unique IDs, fallback for null/empty/unknown). Full non-visual suite 5303 passed (5298 + 5), every existing AI test unchanged, so no behaviour change.
- No real-client run: nothing visible changed. Phase 2 (the settings) gets the AOT click-through.


## Open questions

- ~~E. Map editor per-race AI~~ -> yes, later phase (see Decisions).
- ~~F. Neutral armies' battle AI~~ -> global.
- ~~G. Show the AI's name~~ -> only in the per-player panel.

## Phase 2 — done

Commit b3f8dbfa, merged in a4b512e9.

- Engine: `AiSelection(WorldAi, BattleAi)` and `GameSession.Ai` (not saved). `WorldAiActionCommand.BrainId`, `BattleAiActionCommand.BrainId`, `BattleAutoResolveCommand.AttackerAiId/DefenderAiId` (carried on `BattleAutoResolveStepEvent`; the auto-resolve rule picks per unit side, keeping its cached Classic planner when the side is Classic). `GameSession.ExecuteAiAction` passes `Ai` to both. Test hook `AiBrainRegistry.RegisterForTests` (never listed).
- Instead of the planned engine-context plumbing, the client sets `Session.Ai` from the settings before every AI action (`GameViewModel.ExecuteAiTurnsAsync`), and the battle screen reads `Game.Settings.BattleAi` per command (`BattleScreenViewModel.AiBrains.cs`). That gives the mid-game switch with no change to `IEngineContext`/`EngineContext`.
- Settings: `GameSettings.WorldAi/BattleAi` (nullable, `JsonIgnore WhenWritingNull`, no format bump). Settings screen: two drop-downs + tooltips, `AiBrainFormatter`/`AiBrainNameConverter`, 5 new strings in all 7 catalogs (ru: ИИ карты, ИИ боя, Классический).
- Tests: `AiSelectionTests` (5: session uses the chosen world AI and a switch mid-turn applies to the next action; unknown ID plays exactly like Classic; battle AI action uses the chosen brain; auto-resolve uses each side's brain; an unset side keeps Classic), `GameSettingsStoreTests.LoadAiChoiceTest`, `SettingsScreenViewModelTests.AiChoiceTest`. Suite 5310 on the branch, 5313 after merging into gallean-upgrade (with the spell-cast branch's tests). Visual `SettingsTest`/`SettingsRussianTest` reviewed: everything fits in both languages.
- Real client (AOT, Debian 12 image, 5th Column): settings.json with `"worldAi": "removed-ai"` showed Classic in both drop-downs; the drop-down opens with its one entry; changing the sound rewrote the file without the AI keys; started the quest, the AI turn ran and attacked, a battle resolved with Instant resolve and another ran 4 rounds of Auto-play against the AI with no errors in the log.
- Known limits: with only Classic registered, the drop-downs have one entry until phase 5. A test brain proves the plumbing; the real client can't show a different AI playing yet.

## Phase 3 — done

Commits c18a184d + 6a1250ac, merged in 437d920e.

- Engine: `WorldAiOverride(WorldAi, BattleAi)` (Contracts) and `WorldPlayer.AiOverride` (init prop, in Equals/hash, saved; old saves load as null). `WorldSetAiOverrideCommand` -> `WorldAiOverrideChangedEvent` (any time, not only on that player's turn; empty override = null; same value = no event; human/unknown player -> new error `AiPlayerRequired`). `ScenarioConfiguration.AiOverrides` (race -> override) applied by `ScenarioLoader` to computer players. `BattleSession.AttackerOwnerId/DefenderOwnerId`; `GameSession.GetWorldAiId(player)`, `GetBattleAiOverride(side)`, `GetBattleAiId(side)` = own AI, else settings. The battle screen uses `GetBattleAiOverride ?? Settings.BattleAi` for AI sides.
- Client: `Screens/AiPlayers/` (`AiPlayersScreenViewModel` + view, `AiPlayerRowViewModel`, `AiPlayersPanel` for in-game rows/commands, `AiChoiceConverter`, `AiOwnAiWeightConverter`). Race screen: third button "AI players" (enabled when a race is Computer; races switched back to Human drop their own AI). Game menu: "AI players" under Settings. Settings: `AskAboutOwnAis` confirm prompt. 10 new strings in all 7 catalogs (ru "Игроки ИИ" so the race-screen button fits).
- Tests: `AiOverrideTests` (6: set/clear/no-op, human refused, own world AI beats settings, own battle AI follows the battle side in a real AI attack, no battle -> settings, save round trip without `isEmpty`), `AiPlayersTests` (2: race screen -> configuration -> started game has the override; game menu panel -> command -> reopen shows it -> settings question: Battle AI change asks nothing, Map AI change asks, close keeps, confirm clears), visual `AiPlayersTest` (race screen + panel) and `GameMenuTest` reviewed. Suite 5321.
- Real client (AOT, 5th Column, Undead human vs Elven computer): race screen AI players -> Elven Battle AI = Classic -> started; the game menu panel showed it (bold name); set Map AI = Classic too, saved: the save has `"aiOverride":{"worldAi":"classic","battleAi":"classic"}` for the Elves only; ended the turn, the Elves attacked twice, one battle played out with Auto-play to victory; no errors in the log. Found there: the save also wrote `"isEmpty":false`, fixed in 6a1250ac with `[JsonIgnore]` (checked by the round-trip test, not by a second real-client run).
- Not checked in the real client: the settings question (needs a second AI to switch to; covered by `AiPlayersTests`) and loading the save back (covered by the round-trip test).

### Follow-up: neutral players in the panel

Commit 62bb88f3, merged in 53fd837f. `AiPlayersPanel.CreateRows` lists every AI player; `RaceSelectScreenViewModel` adds a "Neutrals" row (always enabled). `AiPlayersTests` now check the race-screen Neutrals row reaches every neutral player of the started game and that the in-game panel lists the neutral player as "Neutrals"; visual `AiPlayersTest` reviewed. Suite 5321 (same tests, more assertions). Not run in the real client.

## Phase 4 — done

Commit bbfaedd3, merged in dd14eead.

- Built as a console tool `tools/AiTournament` (like MapFromPlan, not in the solution) instead of a test-project bench: it runs with `dotnet run`/the built binary, takes built-in scenarios or custom maps, and needs no internals. Usage and options in its `README.md`.
- Engine: `GameSession.AllowAiOnly` -> `WorldAiActionCommand.AllowAiOnly` skips the "no human left" stop (`NoPlayersRemaining`). The game never sets it. Test `AiSelectionTests.AiOnlyGameNeedsOptInTest` (an all-AI session throws without it, plays with it).
- Tool: each playable race gets its seat's contender as `WorldAiOverride` (the panel's mechanism); neutrals keep Classic; seats rotate across contenders; game k uses seed + k; the game goes through `GameSession.ExecuteAiAction`, so AI-vs-AI battles auto-resolve with each side's battle AI exactly like in the client. Ends on a scenario end, one playable race left, or the turn limit (draw). Reports per player: cities start/end, first capture turn, AI ms per turn (avg/max); per contender: wins, draws, leads (draws ended with strictly the most cities), eliminations, average win turn, first capture; optional CSV; `--parallel n`; `--verbose` per-turn lines.
- Checks: same seed gives the same game (per-turn lines hashed twice, seeds 1 and 2 differ); `--parallel 4` gives the same CSV as one at a time (S099, 4 games); custom map `S600SC0000` (5 races, 320) runs; an unknown contender exits with the `--list` hint. Suite 5322.
- Classic baseline: built-in scenarios with 2+ playable races mostly end as draws at 40 turns (scan of S095-S104: 3 "scenario ended" by events, 7 draws). S099 (4 races), 8 games x 50 turns, `--parallel 8`: 11 s, 32 seats, 0 wins, 0 eliminations, first capture on turn 27 on average, 3.4 cities at the end, 51 ms AI per turn (max 429 ms). So with Classic nobody gets eliminated in 50 turns; leads and cities are the useful measure until a personality is aggressive enough to win.
- Known limits: built-in quests use their default difficulty (no option yet); draws count only cities, not armies or gold.
