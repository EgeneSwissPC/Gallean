# Battle AI designer

gallean-upgrade only. Spec for the battle AI designer, the separate project split out of the AI designer
(`docs/ai-remaining-work.md` section 2, user decision 2026-11-21). Nothing is built yet.

## Decisions

- Separate project from the map AI designer (user, 2026-11-21). Its own document type, files and IDs
  (`DesignedBattleAi`, `AIs/battle/<name>.json`, `custom-battle:<name>`).
- Classic stays untouched: a designed battle AI is a profile on the Classic planner (extra knobs whose defaults are
  Classic's behaviour), never an edit of the Classic code path. A missing `custom-battle:` ID falls back to Classic.
- Phase 1 (the battle bench) comes first: it doesn't depend on any open question, and every later phase needs it to
  prove "Classic copy = Classic" and to tune designs.

## 1. The problem today

- Battle AIs: Classic, Cautious (flees at 40 % power share) and Ruthless (never flees, never defends). They are fixed
  `BattleAiProfile` knob sets (`RetreatPowerShare`, `CanRetreat`, `CanDefend`) registered in `AiPersonalities`.
- A player can't build their own battle behaviour (focus the healer, protect the leader, finish the wounded).
- There is no way to measure a battle AI on its own: `tools/AiTournament` plays whole games, where battles are few and
  mixed with world decisions.

## 2. How the Classic battle AI decides (`src/Gallean.Engine/Battle/Ai`, 772 lines)

- `BattleAiPlanner.DecideAction(state)`: for the current unit every action makes a plan with a score; the highest
  wins. Actions: Retreat, Heal item (only with items allowed), Attack, Defend (score 0 = fallback), Skip (priority 1).
- Attack scoring (`AttackAction.Scorer`): per target of each attack, utility considerations by attack type (damage
  and drain: Weakness, Lethal, Unwarded, Undefended; control: Power, Unwarded, Controllable; heal: Wounded; cure:
  Afflicted; revive: Fallen; protection: Protectable; boost: Boostable). Targets from `BattleAiTargetProvider`.
- Retreat: leaders of non-neutral sides with a living ally, when the side holds less than 25 % of the power.
- Auto-resolve (`BattleEngine.Rules.AutoResolveStep`): the planner without items; attacker retreats after round 15,
  defender defends after 30, hard stop at 45 (90 with "Refuse to flee").
- Where an AI brain plays: `BattleAiActionCommand.BrainId`, `BattleAutoResolveCommand.AttackerAiId/DefenderAiId`,
  AI-vs-AI battles in `GameSession`. The player's Auto-play / Instant resolve / Heal and defend / Refuse to flee and
  the world AI's battle predictions (`WorldAiEstimator`) stay Classic.

## 3. What the user would see

- A battle AI editor (mode: question BA) listing the player's battle AIs: New (from any battle AI), Copy, Delete, Edit.
- The designer: palette | cards | help, like the map designer. A card (or stage, question BB) holds:
  - **Actions** the AI may use, in priority order: attack, heal ally, use item, defend, flee, skip. Leaving one out
    means never.
  - **Target preferences**: weakest first, can-kill-now first, healers first, casters first, the leader first, the
    hardest hitter first, the most wounded ally first (heals).
  - **Rules**: flee below N % power share, heal allies below N % hit points, drink potions below N %.
  - **Behaviour blocks**: protect the leader, focus fire, finish the wounded.
- A Test panel: pick two armies, fight N battles each way round, see wins, survivors, rounds, retreats.
- The designed AIs appear in Settings → Battle AI and in the AI players panel.

## 4. Technical approach

- **Engine profile**: `BattleAiProfile` grows the knobs (target-preference multipliers applied inside
  `AttackAction.Scorer` on top of Classic's score, action filters, thresholds). Defaults = Classic, guarded by
  `AiPersonalitiesTests` and a "designed Classic copy = Classic" test (same commands for a battle, same bench results).
- **Designed AI**: `Ai/Designer/Battle/` — `DesignedBattleAi`, `DesignedBattleStage`, store (Utf8JsonWriter /
  JsonDocument, AOT-safe), `DesignedBattleAiBrain` (finds the stage per decision and builds the profile, same pattern
  as `DesignedWorldAiBrain`), registry `AiBrainRegistry.SetDesignedBattleAis`.
- **Bench**: `Ai/Tournament/AiBattleBench` — two armies, N seeds, contender A vs B with sides swapped every run,
  results per contender: wins, losses, draws (round limit), retreats, average rounds, survivors and their remaining
  hit points. Deterministic per seed. Used by `tools/AiTournament --battle` and later by the Test panel.
- **Client**: `Screens/AiEditor/` gains the battle list and designer (reuse the map designer's block parts and
  drop-finder); drop-downs list designed battle AIs.

## 5. Testing

- Engine: Classic copy = Classic; each condition; each target preference changes the chosen target in a built battle
  (with a positive control); each behaviour block with a control; file round trip; missing ID → Classic.
- Bench: same seed = same result; Classic vs Classic is symmetric over swapped sides; Ruthless never retreats.
- Client: view-model tests, visual tests en + ru (1366x768, clipping review), AOT build with a designed battle AI on
  the AI side in a real battle.
- After every phase: S099 Classic tournament CSV unchanged.

## 6. Phases

1. **Battle bench** (~1 day): `AiBattleBench` in the engine, `tools/AiTournament --battle` with armies from a
   scenario's stacks (two stack IDs) or a JSON list of unit IDs and levels; first numbers for Classic, Cautious,
   Ruthless. No open question blocks it.
2. **Engine knobs + designed battle AI** (~2 days): profile knobs, document, store, brain, registry (after BB, BC).
3. **Editor mode and list** (~1 day, after BA).
4. **Designer screen** (~2–3 days).
5. **Test panel** (~1 day, after BE).
6. Optional: designed battle AI for the player's own Auto-play / Instant resolve (after BD).

## 7. Open questions

- **BA. Mode**: (a) a "Battle AIs" tab in the existing AI editor — one place, shares the list code; (b) its own
  main-menu mode — fully separate as the user leaned towards, one more menu entry. Recommended (b), matching
  "separate project".
- **BB. Stages**: (a) one card with rules at first, stages later; (b) stages from the start with battle conditions
  (from round N, power share below/above N %, leader below N % HP, fewer than N units, enemy has a healer/mage/
  paralyser, attacker/defender, neutral/player). Recommended (b) with only round, power share and leader HP as the
  first conditions: the stage engine already exists for map AIs.
- **BC. First preferences and behaviours**: recommended weakest first, can-kill-now first, healers first, the leader
  first, and the "focus fire" and "protect the leader" blocks.
- **BD. Player's own Auto-play / Instant resolve**: (a) AI sides only, as today; (b) a setting "My auto battles use".
  Recommended (a) now, (b) as phase 6.
- **BE. Test armies**: (a) scenario armies; (b) armies from a saved game; (c) a small army editor. Recommended (a)
  in the bench, (c) for the Test panel (players want to try "my army vs undead").
- **BF. Size**: ~7–8 days in total, confirmed per phase.
