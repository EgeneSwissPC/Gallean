# Switchable AI (world AI and battle AI)

gallean-upgrade only. Status: spec, nothing built yet.

## Decisions

- **Two separate switches.** The world-map AI and the battle AI are chosen independently (a "Builder" world AI can fight with any battle AI).
- **The global switch lives in Settings and is NOT part of a save.** It's a client setting (`GameSettings`), like the language. Switching it changes the AI in every game, including a running save, starting with the next AI action.
- **Per-player overrides are rare and opt-in.** A game can give one specific AI player its own world AI and/or battle AI. Only those overrides go into the save. Every AI player without an override follows the global switch.
- **Resolution order:** override for that player if it exists → global setting → Classic.
- **Switching mid-game is allowed** at any time (Settings during a game, or the per-player panel).
- **When the global switch changes and the current game has overrides,** the game asks once: "N AI players in this game have their own AI. Switch them too?" with the choices *Only players without their own AI* (default) and *Everyone (removes their overrides)*. Nothing is overwritten silently.
- **Upstream:** no. gallean-upgrade only.

## The problem today

- World AI: `src/Gallean.Engine/World/Ai/` (~6,100 lines, a utility AI: every action gets `WorldAiActionScore(Priority, Value)` and the best one wins). Its only entry point is `WorldAiActionCommand`, which always does `new WorldAiController(context).DecideAction(state, memory)`.
- Battle AI: `src/Gallean.Engine/Battle/Ai/` (~700 lines). Entry point is `BattleAiActionCommand` → `new BattleAiPlanner(context, canUseItems, canRetreat).DecideAction(state)`. The battle modes (auto-play, Heal and defend, Refuse to flee) use the same planner.
- Each AI player has its own `WorldAiMemory` in `state.AiMemories` (orders, targets).
- There's exactly one AI of each kind, and nothing to choose.

## What the user sees

- **Settings → Game:** two drop-downs, "World AI" and "Battle AI", each with a one-line description under the selected entry. The settings panel is already at full height at 1366x768, so these need a compact row (e.g. both on one row) or a sub-page. Check `VisualTests.SettingsTest`/`SettingsRussianTest`.
- **New game screen:** next to every AI player, a small "AI: default" marker. Clicking it opens per-player World AI / Battle AI pickers with a "Use global setting" entry (the default).
- **During a game:** the same per-player panel is reachable from the in-game menu (list of AI players, each with its override or "global"). Overrides are highlighted, so you can see that a player is special.
- **Map editor (open question E):** a map creator can give a race its own AI, which becomes that player's override in every game started from the map.

## Technical approach

1. **Interfaces:** `IWorldAiBrain { string Id; WorldAiActionPlan DecideAction(WorldState, WorldAiMemory) }` and `IBattleAiBrain` (same shape as `BattleAiPlanner.DecideAction`). The current code becomes the `classic` brains unchanged, in new files plus a small edit at the two entry points.
2. **Registry:** `AiBrainRegistry` with ID, display name and description. Unknown IDs (an old save, a removed AI) fall back to Classic, with a log line.
3. **Global setting:** `GameSettings.WorldAi` / `BattleAi` (record params with defaults, like `ShowPathOnHover`; no format bump). The client passes them into the engine context, so the engine doesn't read client settings itself.
4. **Overrides in the save:** `WorldPlayer.AiOverride` (init prop, nullable: world ID, battle ID). Old saves have none, so they follow the global setting. Changing an override is an engine command (`WorldSetAiOverrideCommand`), so it is saved and undo-free like the other commands.
5. **Battle side:** a battle against an AI player resolves that player's battle brain (override → global). Neutral armies use the global battle AI (open question F).
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

1. Interfaces + registry + Classic wired through (no behaviour change; full suite identical). ~0.5 day.
2. Global settings (both drop-downs) + engine context plumbing + mid-game switch. ~1 day.
3. Per-player overrides: save field, command, new-game and in-game panels, confirm prompt. ~1.5 days.
4. Tournament bench. ~1 day.
5. Personalities: Classic+, Aggressive, Builder, Expansionist; battle Cautious, Ruthless. ~1 day each, each measured on the bench.
6. (Optional) Lookahead AI.

## Open questions

- **E.** Can a map creator set a race's AI in the map editor (saved in the map, becoming that player's override)? Recommendation: yes, in a later phase after 3.
- **F.** Neutral armies in battle: global battle AI, or always Classic? Recommendation: global.
- **G.** Should the in-game per-player panel show the AI's name to the human (e.g. in the player list)? Recommendation: only in the panel, to keep the world screen clean.
