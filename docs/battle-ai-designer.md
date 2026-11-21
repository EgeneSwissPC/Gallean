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
- Phase 6 decision (2026-11-21): Watch plays the battle with the auto-resolve rules, one unit action at a time
  (`BattleAutoResolveCommand { SingleStep = true, StartRoundNumber }`), not with the player-style AI that uses
  potions and orbs. Reason: then a watched seed is exactly the battle Run fought with that seed (proved by test),
  so "Watch a win / a loss" from the results shows that very battle, and AI-vs-AI battles in the game are
  auto-resolved without items too. Battle items therefore do nothing in Watch either (spec said "battle items
  used"); a "use items" option would need its own rules and break the seed match.
- Phase 3 decisions (2026-11-21): the owner row uses the races' round lord portraits from the diplomacy screen
  (and a neutral leader's face for the neutrals): the game's race select screen has no race banners, so "banners"
  in SC2 became portraits. The difficulty picker (user, before phase 3) sits in the battlefield panel and travels
  in `BattleBenchSetup.Difficulty`. A capital garrison gets the race's guardian automatically (game rule).
- Phase 2 decision: the bench fixes the game difficulty (default Average) instead of taking it from the host map,
  because it sets the computer players' accuracy and experience bonuses (S099 is Very hard, generated maps
  Average). The sandbox screen can offer a difficulty picker later; the tool has `--difficulty`.
- SE (user, 2026-11-21): sides as set up, "Alternate sides" checkbox off by default.
- SD (user, 2026-11-21): default 1000 runs, ±100/±1000 buttons, results with margin of error.
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
  **Run** with a run-count box (default 1000; −1000 / −100 / +100 / +1000 buttons) (decided, SD).
- **Watch battle**: opens the real battle screen with both sides played by their AIs at battle speed (pause,
  speed and Instant resolve still work). Afterwards it returns to the setup screen with both armies unchanged.
- **Run N**: N headless battles with the sides as set up, or alternating when "Alternate sides" is ticked (decided, SE),
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
- ~~**SD. Run counts**~~: answered — (c) with the user's changes: default **1000 runs**; a number box with buttons
  −1000, −100, +100, +1000; the results show each side's win share with its margin of error, e.g. "A wins 62 % ± 6
  (300 runs)" (95 % interval). Range: typing allows 1–10,000; the buttons stop at 100 and 10,000 (my choice, so
  +1000 has room; ~10 s at 10,000 on 8 cores) (user, 2026-11-21).
- ~~**SE. Sides in Run N**~~: answered — (a) keep the sides as set up, plus an "Alternate sides" checkbox, off by
  default; with it on, whichever army defends in a run stands in the city/capital (user, 2026-11-21).
- ~~**SF. Neutral armies**~~: covered by SC2 (Neutrals in each army's owner picker; Capital greys out for a neutral
  defender).

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
Re-split after the answers (2026-11-21); every sandbox phase copies the original game's screens:
2. ~~**Sandbox engine: generated host map**~~ (done, see "Phase 2 — done"; ~0.5 day, no screen): `AiBattleBench` from a small generated `CustomMap`
   with all five races, the neutrals, a city and every capital; `tools/AiTournament --battle` uses it by default
   (`--map` keeps a built-in host). Margin of error in the results (bench + tool). Pure engine: full suite, no
   client check.
3. ~~**Sandbox setup screen**~~ (done, see "Phase 3 — done"; ~2 days): main-menu entry below AI editor; two army panels (map editor army grid and
   unit picker, "Anything goes" checkbox with a red rule line), owner picker with race banners, battlefield panel
   (Open / City + level picture / Capital, "as garrison"), AI per side, Swap sides, presets (save, load, delete).
4. ~~**Leader items and upgrades on screen**~~ (done, see "Phase 4 — done"; ~1.5 days): the army tab's 7 equipment slots with a fitting-items
   picker (red when the leader can't use them), Upgrades list like the level-up popup.
5. ~~**Run N + results**~~ (done, see "Phase 5 — done"; ~1 day): run-count box (default 1000, ±100/±1000), progress/Cancel, results panel with
   margins of error, per-unit survival, "Alternate sides" checkbox.
6. ~~**Watch battle**~~ (done, see "Phase 6 — done"; ~1.5 days): spectator battle screen (both sides AI with their brains, battle items used),
   back to the setup afterwards, "Watch seed N" from the results.
7. **Engine knobs + designed battle AI** (~2 days): profile knobs, document, store, brain, registry (after BB, BC).
8. **Battle AI editor mode and list** (~1 day, after BA).
9. **Designer screen** (~2–3 days); its Test button opens the sandbox with the designed AI selected (replaces the
   separate Test panel).
10. Optional: designed battle AI for the player's own Auto-play / Instant resolve (after BD).

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
- **BF. Size**: sandbox phases 2–6 ~6.5 days; designer phases 7–9 ~5–6 days; confirmed per phase.

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

## Phase 2 — done (sandbox engine)

Merged in 02b9fe2b (feature b4e0bfaa). Pure engine and tool work, nothing visible in the game: proven with the full
suite; the real-client check moves to phase 3.

### What was built
- `AiBattleBench.CreateWithGeneratedHost`: builds a 48x48 custom map with the map editor's code (`CustomMap` +
  `CustomMapStore`, in a temporary folder deleted right after loading): all five races with their capitals, the
  neutral player, a level 1 neutral city and a neutral army to copy. Every race (Elves included) and the neutrals
  can own an army; every race's capital works. `tools/AiTournament --battle` uses it by default; `--map` keeps a
  built-in host.
- Fixed difficulty: constructor parameter (default Average), `Difficulty` property, `--difficulty`.
- Margin of error: `BattleBenchShare` (share with the 95 % Wilson interval half-width, shown as "55 % ± 3"),
  `BattleBenchResult.Wins` / `WinShare`; the tool prints a "Wins:" line.

### Tests
- `AiBattleBenchTests` 24 (+4): the generated host has every race plus neutrals and every capital (control: S099 has
  no Elves), each race's capital and a city give protection, an Elf vs neutral battle validates both ways round; at
  the same difficulty the generated host fights exactly like S099, run for run (open and city); the difficulty is
  fixed and reaches the battle; margin values (500/1000 ± 3.1 %, 15/30 ± 17 %, 0/1000 shown as "0 % ± 1").
- Mutation check: taking the difficulty from the host fails 2 tests.
- Full non-visual suite: 5394 passed (5390 + 4), none failed.

### Findings
- **The difficulty changed results.** Phase 1/1b numbers on S099 ran at S099's own difficulty (Very hard); from now
  on the default is Average. Mirror battles (same undead army, 2000 runs, no side swap): attacker wins 55 % ± 3 at
  Average, Hard and Very hard, 52 % ± 3 at Easy. So there is a small attacker edge in the open; the earlier "about
  50/50" came from runs with swapped sides.
- 1000 runs of 4 vs 4 take 1.5 s with the generated host (8 cores), so 1000 as the screen's default is fine.

### Known limits
- The generated city keeps the neutral city look (`WorldCityDefinitionId.Race` = Neutral); protection comes from
  the level, as before.
- The margin is shown symmetric (± half-width) around the measured share; the true Wilson interval is slightly
  skewed near 0 % and 100 %.

## Phase 3 — done (setup screen)

Merged in d50ef2d6 (feature 984b5f7b, layout fix 07eaa303).

### What was built
- Main menu: "Battle sandbox" below "AI editor" (`GameViewModel.BattleSandbox.cs`; loads the generated host map
  as an editor game for unit lists and leader panels, plus the bench).
- `Screens/BattleSandbox/`: `BattleSandboxScreenViewModel` (derives from the map editor's
  `MapEditorUnitsScreenViewModel`, so unit list, drag and drop, Remove zone and level bar are the map editor's own)
  and `BattleSandboxSideViewModel` per side: owner row (Empire, Undead, Legions, Clans, Elves, Neutrals), party grid
  with leadership, Battle AI drop-down, "Anything goes", "Save as" + Save, "Saved armies" + Load / Delete.
  Battlefield panel: Open field / City / Capital, round city or capital picture from the city screen, city level
  − / +, "Defends as the garrison", difficulty, Swap sides. Problems show in one line under the panels, naming the
  side.
- Shared map editor code: `MapEditorUnitGroup.Kind` settable (defender army ↔ garrison) and `AllowsAnything`
  (skips the group rules and the leader-first step); always off in the map editor.
- Engine: `AiBattleBench.CreateHostMap`, `GetGuardian(race)`, `BattleBenchSetup.Difficulty`.
- Presets in `~/.local/share/GalleanUpgrade/Battles/armies/` (`AppDirectories.BattleArmies`), the same JSON the
  command-line bench reads.
- 28 new UI strings in all 7 catalogs (ru translated), plus the missing "Loading…".

### Tests
- `BattleSandboxTests` (6, client): opens from the main menu with defaults; building armies through the real unit
  list (leader first, levels via the level bar); "Anything goes" allows a 4th soldier for a Pegasus Knight and an
  army without a leader, and switching it off brings the rule back; battlefield (city level, garrison forbids a
  leader, capital garrison gets the guardian and loses it again, neutral capital refused before the garrison's
  own problem, Elves as owners, difficulty in the setup); Swap sides swaps everything; presets save, load on the
  other side, list in a new screen, delete.
- Engine: `AiBattleBenchTests` 24 (difficulty per setup added to an existing test).
- Visual `BattleSandboxTest` (main menu, empty, city, capital, problem, Russian) reviewed at 1366x768: the first
  run showed the defender panel cut off on the right and the side problem text crowding the panel; fixed
  (narrower panels, problems in one line under the panels). `MainMenuTest` passes; the extra entry fits.
- Full non-visual suite: 5400 passed (5394 + 6), none failed.
- Real client (AOT build, Debian 12 image, 1920x1080): main menu → Battle sandbox opens; clicked an empty slot →
  leader list → Pegasus Knight; added an Acolyte; typed "Raiders" in Save as and saved → `Battles/armies/Raiders.json`
  written with both units (AOT-safe JSON confirmed); City and Capital + garrison showed the city/capital picture and
  the guardian in the defender's grid.

### Known limits
- No large leader portrait/stat panel like the map editor's army screen (no room for two at 1366 px); right-click a
  unit for its details.
- A new army's leader lands front center whatever slot was clicked (map editor behaviour).
- In Russian the "Saved armies" drop-down is narrow next to the longer Load/Delete labels (long names are cut).
- Delete removes a preset without asking.
- Watch battle and Run N come in phases 6 and 5; leader items in phase 4.

## Phase 4 — done (leader items and upgrades on screen)

Merged in 589925fe (feature 9f06a52c, layout fix after the real-client check).

### What was built
- Under each army's party grid, the army tab's 7 equipment sockets (banner, tome, 2 battle items, 2 artifacts,
  boots), shown while the army has a leader and isn't a garrison. Clicking a socket opens the map editor's item list
  (`MapEditorListPickerViewModel`) with only the items that fit that slot (the game's `CanPlace`); items the leader
  can't use yet carry a red line "The leader needs <ability>" and can still be picked; "No item" empties a filled
  slot. A filled socket with a problem gets a red frame and a tooltip; the screen's problem line names the side and
  the item, and Watch/Run stay disabled until fixed or "Anything goes" is ticked.
- "Upgrades n / max" button next to "Anything goes" opens `BattleSandboxUpgradesViewModel`/`View`, a copy of the
  game's level-up popup (portrait, title, list with icons and descriptions): several upgrades can be ticked, up to
  min(level, MaxLeaderAbilities) − 1; rows above the leader's level or past the limit are greyed with the reason.
  "Anything goes" lifts both limits; the screen reports too many upgrades when it is switched off again.
- Items and upgrades are saved in presets, swapped with Swap sides, kept when the leader changes, ignored for a
  garrison.
- Engine: `AiBattleBench.CheckItem` (`BattleItemCheck`: fits / missing ability, same rules as `Validate`),
  `LeaderUpgradeChoices`, `GetMaxLeaderUpgrades`.
- Shared map editor code: `MapEditorListPickerItemViewModel.Warning` (red line under a row; null in the map editor).
- 14 new UI strings in all 7 catalogs (ru translated).

### Tests
- `BattleSandboxTests` 9 (+3): banner slot lists banners only and marks Banner of Striking; picking it marks the
  socket and blocks the battle; level 3 + Banner Bearer from the upgrade list make it legal ("Upgrades 1 / 2");
  saved/loaded with the preset; "No item" empties; a level 1 leader gets no upgrades (every row says why);
  "Anything goes" allows any number and switching it off reports it; a garrison has no items.
- `AiBattleBenchTests` 25 (+1): `CheckItem` agrees with `Validate` for 3 items × 7 slots × 2 armies; upgrade choices
  unique and complete.
- Visual `BattleSandboxTest` gained items, item-problem, upgrades and equipped shots (en + ru), reviewed at
  1366x768. Full non-visual suite: 5404 passed (5400 + 4).
- Real client (AOT, Debian 12 image): leader → banner socket → banner list with "The leader needs Banner Bearer" in
  red on every banner → picked Banner of Striking → socket framed red, problem line shown → "Bannermen" saved with
  `"equipment": [{"slot": "Banner", "item": "G000IG1003"}]`. The same run showed the army panel overflowing at the
  bottom once the item row appeared; fixed by a smaller party grid (checked by the visual test only).

### Known limits
- Items are equipped by clicking, not dragged from an inventory (the sandbox has none).
- The upgrade list offers every lord's upgrades (the bench accepts any); in the game a leader only sees its own
  lord's list.
- Battle items (potions, orbs, talismans) do nothing in Run N (auto-resolve never uses items); Watch battle (phase 6)
  will use them.

## Phase 5 — done (Run N and results)

Merged in 9381df4b.

### What was built
- Battlefield panel, under Swap sides: "Battles to run" box (default 1000) with −1000 / −100 / +100 / +1000 (the
  buttons stop at 100 and 10,000; typing allows 1–10,000), "Alternate sides" (off by default) and **Run** (enabled
  only when the setup is valid).
- `BattleSandboxRunViewModel`/`View`: a modal popup in the game's panel style. While running: progress bar, "n of N
  battles", Cancel. The battles run in chunks of 100 on a background task with all cores, so the bar moves and Cancel
  works between chunks; even chunks give exactly the same runs as one big run (seeds and alternating sides line up).
  After Cancel the battles fought so far are shown; cancelled before the first chunk, the popup closes.
- Results: summary (battles, sides alternating or as set up, first seed, average rounds, draws if any) and per side:
  wins in % with the 95 % margin of error, wins as attacker and as defender (when sides alternate), runs where it
  fled, units alive on average, hit points left, and per unit its face, name and "survived n of N".
- Every Run uses a new random first seed (shown in the summary, for replaying in phase 6).
- Engine: `BattleBenchResult.Combine`, `Summarize` (`BattleBenchSideSummary`, `BattleBenchUnitSummary`),
  `AverageRounds`.
- 17 new UI strings in all 7 catalogs (ru translated).

### Tests
- `BattleSandboxTests` 13 (+4): run-count buttons and limits; Run of 250 with alternating sides and a fixed seed
  gives seeds 42..291, 125 attacks each way, 3 unit rows, and exactly the same winners and rounds as one direct
  engine run; Cancel after the first chunk shows a whole number of chunks; Cancel at once returns to the setup.
- `AiBattleBenchTests` 26 (+1): chunked = whole; summary counts add up.
- Visual `BattleSandboxTest` gained results shots (en + ru). The first run showed the battlefield panel's new rows
  overlapping the level bar; fixed (smaller place picture, tighter spacing). Full suite 5409 passed (5404 + 5).
- Real client (AOT): two one-leader armies, Run → 1000 battles in about a second, results popup readable. Fixed after
  it: the army-name line repeated "Attacker"/"Defender" for unnamed armies (now hidden; tests only).

### Known limits
- No seed box to repeat a run; the summary shows the first seed, replay comes with Watch battle (phase 6).
- Results are not saved or exportable (the command-line tool has `--csv`).

## Phase 6 — done (Watch battle)

Merged in 2d5bb2fe (+ 2b0333ff removing a test-run file the merge carried).

### What was built
- **Watch** button next to Run (new random seed, army A attacks), and **Watch a win** / **Watch a loss** in the
  results popup: the first battle of that run army A won / lost, with its seed and sides.
- The real battle screen opens over the sandbox (or the results): both sides are computer-controlled, each with
  its chosen battle AI, playing one unit action at a time with the auto-resolve rules; the battle screen's own
  controls work (speed, Instant resolve finishes the same battle at once, battle log). Continue goes back to where
  Watch was pressed.
- Engine: `BattleAutoResolveCommand.SingleStep` / `StartRoundNumber` (+ `BattleAutoResolveStepEvent.SingleStep`):
  one step of the auto-resolve; `AiBattleBench.CreateWatchSession` (a game session on the host map with the battle
  prepared, a human viewer player so the game screens work, both battle sides AI) and `StartWatch`.
- Client: `BattleScreenViewModel.SandboxWatch` (`BattleSandboxWatch` record: both AI IDs and the start round) used by
  the AI step loop and Instant resolve; `GameViewModel.WatchSandboxBattleAsync`.
- 6 new UI strings in all 7 catalogs (ru translated).

### Tests
- `AiBattleBenchTests.WatchStepsFightTheRunBattleTest` (2 cases: open field with B attacking, level 3 city with A
  attacking; 12 seeds each, Classic vs Ruthless, items on one side): stepping ends with the same winner, rounds and
  hit points as `RunOne`.
- Visual `BattleSandboxWatchTest` (real asset pack): Run 200, Watch a loss → battle screen with both AIs, not the
  player's turn; Instant resolve in the middle of the battle → same winner and rounds as that run; Continue → back
  to the results popup.
- Full non-visual suite: 5411 passed (5409 + 2). `BattleSandboxTest` (13 states) still passes.
- Real client (AOT, Debian 12 image): Watch from the setup played the battle to "Battle over" (units fighting,
  hit points changing), Continue → setup; Run → Watch a win → battle → Continue → results popup; no crash.

### Findings
- The headless visual test host crashed with SIGSEGV (exit 139) when the watch steps ran inside the long
  `BattleSandboxTest` (crash right after later captures, in native rendering). Moving Watch into its own visual
  test removed it; the real AOT client showed no crash in several watch/close cycles. Not root-caused; worth
  remembering if headless tests that open and close battle screens start crashing.

### Known limits
- Battle items are not used (see the decision above).
- The watched battle's world (`GameModel`) isn't disposed after Continue (small leak per watch until the sandbox
  closes).
- No "watch seed N" box; the results' Win/Loss buttons and the random Watch cover it for now.

### Sandbox complete
Phases 2–6 are done. Next on the roadmap: the battle AI designer itself (phases 7–9, open questions BA–BD), whose
Test button will open this sandbox with the designed AI selected.
