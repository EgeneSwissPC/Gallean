# Battle AI designer

gallean-upgrade only. Spec for the battle AI designer, the separate project split out of the AI designer
(`docs/ai-remaining-work.md` section 2, user decision 2026-11-21). Nothing is built yet.

## Decisions

- Separate project from the map AI designer (user, 2026-11-21). Its own document type, files and IDs
  (`DesignedBattleAi`, `AIs/battle/<name>.json`, `custom-battle:<name>`).
- Classic stays untouched: a designed battle AI is a profile on the Classic planner (extra knobs whose defaults are
  Classic's behaviour), never an edit of the Classic code path. A missing `custom-battle:` ID falls back to Classic.
- Phase 1 decision: the bench builds no hidden world of its own. It loads a host map (default `S099SC0000`), makes
  two copies of one of its armies with the preset's units, owned by two of its players (both set to computer
  players), on a tile without a city, and lets `WorldCommandHelpers.CreateStackBattleState` build the battle like
  the game does. The sandbox (phases 2–3) reuses `AiBattleBench.CreateBattle`.
- Phase 1 (the battle bench) comes first: it doesn't depend on any open question, and every later phase needs it to
  prove "Classic copy = Classic" and to tune designs.
- **Battle sandbox** (user, 2026-11-21): a visual mode where the player builds two armies from any units at any
  level, in any combination the game allows, saves them as presets, picks an AI per side, watches the battle on
  the real battle screen, and then presses "Run 10 / 20 / 30 battles" to get the outcomes from the headless bench.
  It comes right after the bench (phase 2) and before the designer, so the designer's Test panel becomes "the
  sandbox with the designed AI selected". See section 3a.
- SC2 (user, 2026-11-21): battlefield panel + owner picker with race banners; hidden generated host map with
  every race.
- SC (user, 2026-11-21): reuse the original game's screens as closely as possible (army tab equipment slots,
  level-up popup for upgrades), so players already know the interface. This is the rule for every sandbox screen.
- SB (user, 2026-11-21): game rules by default, "Anything goes" checkbox per army.
- SA (user, 2026-11-21): the sandbox is its own main-menu entry "Battle sandbox", below "AI editor".

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

## 3a. Battle sandbox (visual mode)

### What the user would see
- **Entry**: a main-menu entry "Battle sandbox" below "AI editor" (decided, SA). The menu must still fit at
  1366x768; tighten the spacing if it doesn't. The designer's Test button opens the same screen later.
- **Setup screen**: two army panels side by side (Attacker | Defender). Each is the game's 2x3 army grid: click a
  slot to pick any unit (search, race filter), set its level with the level bar, drag to move, large units take a
  whole row like in the game. A leadership line ("4 / 6"); when the army breaks a game rule, a red line says which
  and Watch/Run stay disabled, unless the army's "Anything goes" checkbox is ticked (decided, SB). With the box
  ticked, the shared map-editor army grid skips its "Not enough leadership" refusal (a flag; the map editor itself
  is unchanged). The checkbox covers garrisons too.
  Under each army: the **AI** drop-down (every built-in battle AI, later designed ones too) and the preset row:
  **Save preset**, **Load preset**, **Delete**. Between the panels: **Swap sides**, **Watch battle**, and
  **Run 10 / 20 / 30** (question SD).
- **Watch battle**: opens the real battle screen with both sides played by their AIs at battle speed (pause,
  speed and Instant resolve still work). Afterwards it returns to the setup screen with both armies unchanged.
- **Run N**: N headless battles with the attacker/defender swapped every other run (or fixed sides, question SE),
  a progress bar and Cancel. Then a results panel per side: wins, losses, draws (round limit), retreats, average
  rounds, units surviving and their average hit points, and per unit how often it survived. "Watch seed 7" replays
  one particular run visually (same seed = same battle).
- **Presets**: saved as files under `~/.local/share/GalleanUpgrade/Battles/armies/<name>.json` (units, levels,
  positions, leader items), listed alphabetically with search. They also feed the headless bench
  (`tools/AiTournament --battle --armies <preset> <preset>`), so a battle tried on screen can be run 1000 times
  from the command line.

### What the engine already offers
- **Army building**: the map editor's army screen (`MapEditorArmyScreenViewModel` on top of the shared
  `MapEditorUnitsScreenViewModel`: grid, `MapEditorUnitPickerViewModel`, `MapEditorUnitLevelBar`, leadership via
  `CustomMapStack.GetLeadership`/`GetOccupiedLeadership`). The sandbox reuses it instead of building a new picker
  (UI rule: copy the game's own screens).
- **Battles**: the real battle screen plays any `BattleSession`; AI sides take `BattleAiActionCommand.BrainId`,
  auto-resolve takes `BattleAutoResolveCommand.AttackerAiId/DefenderAiId`.
- **Gap**: a battle always starts from a world (two stacks next to each other). The sandbox builds a tiny hidden
  world in memory (the custom map code already makes one from `CustomMapStack`s): two players, two stacks, one
  tile apart, then starts the battle. The bench (phase 1) does the same without a screen.
- **Gap**: the battle screen assumes one side is the human. "Watch" needs a mode where both sides are AI and the
  screen only shows them (the existing Auto-play loop `PlayAiTurnAsync` does most of it; it needs the chosen brain
  per side instead of Classic).

### Technical approach
- Engine: `Ai/Tournament/BattleSandbox` — builds the world from two army descriptions (unit ID, level, slot, items),
  validates them with the game's rules (leadership, large units, one leader per army), returns a `GameSession`
  at the start of the battle. `AiBattleBench` (phase 1) calls it N times with seeds.
- Store: `BattleArmyPresetStore` (Utf8JsonWriter / JsonDocument, AOT-safe).
- Client: `Screens/BattleSandbox/` (setup screen view model + view, results panel), battle screen flag
  "spectator" (both sides AI, brain per side, back to the sandbox at the end).

### Questions for the sandbox
- ~~**SA. Entry**~~: answered — (a) its own main-menu entry "Battle sandbox", below AI editor (user, 2026-11-21).
- ~~**SB. Rules**~~: answered — the game's rules by default plus an "anything goes" checkbox per army (saved in
  the preset as `allowAnyArmy`); a broken rule shows a red line in the army panel and disables Watch/Run until fixed
  or ticked (user, 2026-11-21).
- ~~**SC/SF in the engine**~~: the user asked for leader items, city defence and neutral sides before phase 2; they
  are in the bench (phase 1b). SC/SF remain open only for how the sandbox screen shows them.
- ~~**SC. Leader items and upgrades on screen**~~: answered — copy the game's own screens: the army tab's row of 7
  equipment slots under each army (click a slot: searchable list of items that fit it; items the leader can't use
  yet marked red), and an Upgrades button opening a list like the level-up popup (tick abilities up to the leader's
  limit). Valuables and special items (40 of the 336) are left out. Garrisons hide slots and upgrades; "anything
  goes" allows red items (user, 2026-11-21).
- ~~**SC2. Where the defender stands and who owns the armies**~~: answered — (a) copy the game's screens: a
  battlefield panel between the armies (Open / City / Capital; city level picker with the city picture from the
  game's city screen; "as garrison" checkbox) and an owner picker above each army with the race banners of the race
  select screen (Empire, Undead, Legions, Clans, Elves, Neutrals). The sandbox builds a small hidden host map with
  all five races and the neutrals (map editor code), because no built-in map has all of them (best: five of six;
  S099 has no Elves). Capital greys out for a neutral defender (user, 2026-11-21). Engine work: an
  `AiBattleBench` constructor from a generated `CustomMap` (~0.5 day), also usable by `tools/AiTournament`.
- **SD. Run counts**: fixed buttons 10 / 20 / 30, or a number box (1–1000) with those three as quick picks.
  Recommended the number box plus quick picks.
- **SE. Sides in Run N**: (a) swap attacker/defender every run (fair AI comparison); (b) keep sides as set up
  (answers "does this army win as the attacker?"). Recommended a checkbox, default (b), because you set the
  sides on purpose.
- **SF. Neutral armies**: allow "defender is a neutral army" (neutrals never flee)? Recommended yes, one checkbox.

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

1. ~~**Battle bench** (~1 day)~~: done, see "Phase 1 — done" below.
2. **Battle sandbox, setup screen + presets** (~2 days, after SA–SC): main-menu entry, two army panels reusing the
   map editor's army grid and unit picker, AI per side, save/load presets.
3. **Battle sandbox, Watch + Run N** (~2 days, after SD–SF): spectator battle screen (both sides AI), Run N with
   progress/Cancel, results panel, replay one seed.
4. **Engine knobs + designed battle AI** (~2 days): profile knobs, document, store, brain, registry (after BB, BC).
5. **Battle AI editor mode and list** (~1 day, after BA).
6. **Designer screen** (~2–3 days); its Test button opens the sandbox with the designed AI selected (replaces the
   separate Test panel).
7. Optional: designed battle AI for the player's own Auto-play / Instant resolve (after BD).

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
- ~~**BE. Test armies**~~: answered by the user — any units at any level, saved as presets (battle sandbox, 3a).
- **BF. Size**: ~11–12 days in total with the sandbox, confirmed per phase.

## Phase 1 — done

Merged in 20314881 (feature) and 48744c98 (fix found while measuring).

### What was built
- Engine `src/Gallean.Engine/Ai/Tournament/`:
  - `BattleArmy` / `BattleArmyUnit`: an army preset (unit type, level, line/flank) plus `AllowAnyArmy` (skip the
    game's army rules for stress tests).
  - `BattleArmyStore`: one JSON file per army (Utf8JsonWriter / JsonDocument, AOT-safe; broken files skipped,
    unknown properties ignored).
  - `AiBattleBench`: `Validate` (unknown unit, bad or taken slot, large units, level below the unit's own, more
    than one leader, leadership exceeded, guardians — the game's own `WorldStackBuilder` rules), `Run` (N runs,
    seed + k per run, optional side swap, parallel), `RunOne`, `CreateBattle` (for the sandbox), `ExportArmies`
    (every army of the host map as presets). A run = `BattleStartCommand` + `BattleAutoResolveCommand` with
    both AI IDs, exactly like an AI-vs-AI battle in `GameSession`. Results per run and side: winner, rounds, per
    unit survived / fled / hit points left.
- `tools/AiTournament --battle <army A> <army B> [--ai-a] [--ai-b] [--runs] [--seed] [--swap] [--parallel]
  [--map] [--armies dir] [--csv] [--verbose]` and `--export-armies <map> <dir>`; README section; example armies
  in `tools/AiTournament/examples/armies/` (empire, undead, orcs, deathlords).

### Tests
- `AiBattleBenchTests` (10): same seed = same result (also with `--parallel`), other seeds differ; swap alternates
  the attacker; a bench run equals a hand-dispatched auto-resolve of the same battle; the battle AI reaches the
  battle (Classic flees from a far stronger army, Ruthless never does); both sides get the same world modifiers;
  levels raise hit points; validation of every rule plus "anything goes"; store round trip; broken files and
  unknown properties; every exported S099 army is valid.
- Mutation check: passing no attacker AI ID makes `BattleAiReachesTheBattleTest` fail.
- Full non-visual suite: 5380 passed (5370 + 10), none failed. No client change, so no visual or AOT run; the
  real-client check moves to phase 2. Classic AI code untouched, so no S099 tournament rerun.

### Findings
- First measurement showed the attacker winning ~92 % of mirror battles. Cause: the host map's players were
  human, and only computer players get the difficulty's accuracy/experience bonus, so the sides differed. Fixed
  by making both bench players computer players (48744c98); mirror battles are now ~50/50 (Classic vs Classic,
  undead vs undead, 200 runs swapped: 94 / 106 wins).
- First numbers (200 runs, sides swapped, same undead army on both sides, against Classic): Ruthless 98 wins
  (noise-level difference), Cautious 81 wins and fled in 66 runs, because it flees from even battles.
- Empire (Pegasus Knight, 2 Squires, Acolyte) vs Undead (Death Knight, 2 Fighters, Initiate), all level 1:
  Undead wins 198 of 200.
- Speed: 1000 battles of 4 vs 4 in ~1 s with `--parallel 8`.

### Known limits
- ~~No leader items, no city defence, no neutral side yet~~: added in phase 1b (below).
- "Fled" = a unit of the side was retreating or retreated at the end; a fled leader counts as alive.
- Battles use the game's auto-resolve rules (attacker retreats after round 15, stop at 45). The Watch battle in
  the sandbox plays step by step and may differ after round 15.

## Phase 1b — done (leader items, city defence, neutral sides)

User request (2026-11-21): add these to the bench before phase 2. Merged in 5f0e5cb1 (feature 99492ceb).

### Decisions taken
- **Leader items need leader upgrades.** The game only lets a leader carry a banner, artifact, orb, etc. with the
  matching ability (Banner Bearer, Artifact Lore, ...), and leaders get abilities from level-up upgrades. So presets
  gained both `equipment` (slot + item) and `leaderUpgrades` (Gleaupg modifier IDs), checked with the game's own
  rules (`LeaderEquipmentCalculator.CanPlace` / `GetRequiredAbility`, at most min(level, MaxLeaderAbilities) - 1
  upgrades, minimum levels). "Anything goes" skips these rules too, not only the army rules.
- **City defence = the real place.** The defender stands in a copy of the host map's first city with the chosen
  level (1–5) or in its own race's capital; `FortificationCalculator.UpdateUnitModifiers` gives it the protection
  exactly like in the game, and `CreateStackBattleState` adds the fortification armor and takes away the
  defender's right to flee.
- **Garrison mode** added (`--garrison`): the defender fights as the place's garrison (`CreateGarrisonBattleState`):
  no leader, no leadership limit, guardians allowed (capital guardians such as Myzrael have leadership but are not
  treated as leaders). The capital's guardian is not added automatically; put it in the garrison preset.
- **Neutral side** = the host map's neutral player owns the army (`--neutral-a/-b`, or `--race-a/-b` with any race of
  the host map); neutral leaders never flee (Classic's retreat rule); neutrals have no capital (refused).
- The battle doesn't depend on the seed, so `Run` builds it once per way round (city state copy included).

### Tests
- `AiBattleBenchTests` now 20 (+10): items reach the battle (banner on every unit, artifact on the leader) and win
  more (control: same army without items); item attacks and battle items reach the battle; upgrades are leader
  modifiers; item/upgrade rules (missing ability, wrong slot, two items in a slot, unknown item even with
  "anything goes", too many upgrades, repeated, unknown upgrade, garrison with items); city/capital protection
  (armor 0 in the open, > 0 in a level 1 city, more in level 5, > 0 in a capital; defender can't flee); a city
  changes a mirror battle (defender 30–70 % in the open, 20+ points more in a level 5 city); garrison rules;
  neutral leaders never flee (control: a race's leader does); neutrals have no capital; store round trip with
  items.
- Mutation checks: a city that ignores the chosen level fails `CityGivesTheDefenderItsProtectionTest`; an army
  that loses its equipment on the stack fails `ItemAttacksAndBattleItemsReachTheBattleTest` (it survived the
  first test set, so that test was added).
- Full non-visual suite: 5390 passed (5380 + 10), none failed. Still no client change.

### First numbers (200 runs each, Classic both sides)
- Undead vs the same undead army as defender: open 104/96, level 1 city: defender wins 146, level 5 city: 184,
  Undead capital: 200 (the attacker fled in 28).
- 4 undead attacking a level 3 city garrison of 6 level-2 Empire units: the garrison wins all 200.
- Empire with a level 3 Pegasus Knight vs Undead: 18 wins plain, 54 with Banner of Striking + Runestone (and the
  two upgrades they need).
- Empire vs a far stronger undead army: as a race the leader flees in all 50 runs; as neutrals nobody flees and
  the army dies.

### Known limits
- Battle items (potions, orbs, talismans in Battle1/2) are on the battle state, but the bench resolves battles with
  the game's auto-resolve, which never uses items (also in the game). The sandbox's Watch battle will use them.
- Only the host map's races can own armies; S099 has Empire, Undead, Legions, Clans and neutrals (no Elves). Use
  `--map` with a map that has Elves for Elven-owned armies (units of any race can be in any army regardless).
- The city copy keeps the host city's race look (`WorldCityDefinitionId.Race`); protection comes from the level.
