# AiTournament

Gallean Upgrade (`docs/ai-switching.md`, phase 4). Plays AI-only games on one map between AI *contenders*: a world AI
and a battle AI from `AiBrainRegistry` (the ones the Settings drop-downs list). Every playable race gets the contender of
its seat as its own AI (the same `WorldAiOverride` the AI players panel sets); neutrals keep Classic. With several
contenders the seats rotate, so each contender plays every race and start position.

```
. ~/.local/icu/env.sh
dotnet build tools/AiTournament -c Release
cd tools/AiTournament/bin/Release/net11.0
DOTNET_ROOT=~/.dotnet ./AiTournament S099SC0000 --ai classic --ai builder/ruthless --games 4 --turns 50 --parallel 4 --csv out.csv
DOTNET_ROOT=~/.dotnet ./AiTournament S600SC0000 --maps ~/.local/share/GalleanUpgrade/Maps --turns 30
DOTNET_ROOT=~/.dotnet ./AiTournament --list
```

- `<map id>`: a built-in scenario (`S099SC0000`, from the build output's `RiseOfTheElves/Scens`) or, with `--maps <dir>`,
  a custom map from that folder. The map needs at least two playable races. Built-in quests load with their own default
  difficulty and scenario events, so some end early ("scenario ended").
- `--ai world/battle`: a contender; repeat it for more. `--ai builder` alone uses `builder` for battles too when such a
  battle AI exists, else Classic. Default: one `classic/classic` contender (a mirror match, useful as a baseline and for
  timing).
- `--games n` (2): games per rotation; `--turns n` (50): turn limit, after which the game is a draw.
- `--seed n` (1): first random seed; game k uses seed + k. The same seed gives the same game, so a change to an AI can be
  compared game by game.
- `--parallel n`: games at once (independent sessions; same results as one at a time). `--verbose`: one line per turn
  with each player's cities, armies and gold (forces one game at a time).
- `--csv file`: one row per player per game.
- `--ais <dir>`: load designed map AIs (`docs/ai-designer.md`) from that folder; pick one with `--ai custom:<file name>`.
  `--new-ai <dir> <name>` writes `<dir>/<name>.json`, a designed AI that plays exactly like Classic, to edit by hand
  until the AI editor exists. `--ais <dir> --list` lists them with the built-ins. Designed AIs live in
  `~/.local/share/GalleanUpgrade/AIs/map/` once the AI editor exists.

Per game it prints the outcome (`win` when one playable race is left or a scenario Win event fires, `draw` at the turn
limit) and per player: cities at the start and the end, the turn of its first city or capital capture, and the AI time
per turn (average and max, battles included). The summary per contender adds wins, draws, *leads* (draws it ended with
strictly the most cities), eliminations, average winning turn and first capture. For a designed AI it also prints how
many of its turns it spent in each stage (`Stages of custom:...: Strike 44%, Build up 37%, ...`). The game loop lives in
the engine (`AiTournamentRunner`), shared with the AI designer's Test panel.

Measured with Classic only (8 vCPU host, Release): `S099SC0000` (4 races), 30 turns, 4 games: 10 s one at a time, 7 s
with `--parallel 4`, all draws, first capture on turn 22 on average, 47 ms AI per turn (max 404 ms).

## Battle bench (`--battle`)

Gallean Upgrade (`docs/battle-ai-designer.md`, phases 1, 1b and 2). Fights the same battle many times, headless: army A
with its battle AI against army B with its own. Each run builds the battle the way the game does when one army attacks
another (`WorldCommandHelpers.CreateStackBattleState`, or `CreateGarrisonBattleState` for a garrison) with players of a
host map (by default a small generated map with all five races, their capitals, the neutrals and a city) and resolves it like an AI-vs-AI battle in the game (`BattleAutoResolveCommand` with both AI IDs: the attacker retreats after
round 15, hard stop at round 45). The loop lives in the engine (`AiBattleBench`), for the battle sandbox later.

```
DOTNET_ROOT=~/.dotnet ./AiTournament --battle ../../../examples/armies/empire.json ../../../examples/armies/undead.json --runs 20 --swap --parallel 8
DOTNET_ROOT=~/.dotnet ./AiTournament --battle empire undead --armies ../../../examples/armies --ai-a ruthless --ai-b cautious
DOTNET_ROOT=~/.dotnet ./AiTournament --battle undead garrison --armies ../../../examples/armies --defender-in city --city-level 3 --garrison
DOTNET_ROOT=~/.dotnet ./AiTournament --battle empire-geared deathlords --armies ../../../examples/armies --neutral-b
DOTNET_ROOT=~/.dotnet ./AiTournament --export-armies S099SC0000 /tmp/armies
```

- `<army A> <army B>`: preset files, or preset names in `--armies <dir>`. A preset is a JSON file (`BattleArmyStore`):
  `{ "format": 1, "name": "...", "units": [ { "unit": "g000uu0019", "level": 1, "line": 0|1, "flank": 0-2 } ] }`
  (line 0 = front; a large unit sits on its front slot and takes the back slot behind it). Unit IDs: `Globals/Gunits.json`.
  Optional: `"equipment": [ { "slot": "Banner", "item": "G000IG1003" } ]` (slots Banner, Tome, Battle1, Battle2,
  Artifact1, Artifact2, Boots; item IDs from `Globals/GItem.json`) and `"leaderUpgrades": [ "g000um4003" ]` (leader
  level-up upgrades, unit modifier IDs from `Globals/GleaUpg.json`, e.g. Banner Bearer g000um4003, Artifact Lore
  g000um4001). The army must follow the game's rules: one leader at most, leadership not exceeded, no guardians, level
  not below the unit's own, items in fitting slots and allowed by the leader's abilities (a banner needs Banner Bearer),
  at most min(level, 4) - 1 upgrades, each at its minimum level. `"allowAnyArmy": true` skips the rules for stress tests
  (unknown units and items are still refused).
- `--ai-a`, `--ai-b` (classic): battle AIs (`--list`). `--runs n` (20), `--seed n` (1; run k uses seed + k, same seed =
  same battle), `--swap` (army A attacks in even runs and defends in odd runs; default: A always attacks),
  `--parallel n`, `--map id` (a built-in host map instead of the generated one; only its races can own armies),
  `--difficulty easy|average|hard|veryhard` (average; it sets the computer players' accuracy and experience bonuses and is
  fixed, not taken from the host map), `--csv file` (one row per run), `--verbose` (one line per run).
- `--race-a`, `--race-b` (empire, undead, legions, clans, elves, neutral): which player of the host map owns the army
  (default: the map's first two playable races). `--neutral-a` / `--neutral-b` = neutral: neutral leaders never flee.
- `--defender-in open|city|capital` (open), `--city-level n` (1-5): the defender stands in a city of that level (a copy
  of the host map's first city, owned by the defender) or in its race's capital, with the place's protection (armor) and
  without the right to flee, like in the game. `--garrison`: the defender fights as the place's garrison instead of an
  army in it: no leader, no leadership limit, guardians allowed (add the capital's guardian yourself, e.g. Myzrael
  g000uu3001). Neutrals have no capital.
- `--export-armies <map id> <dir>`: writes every army that starts on a built-in map as a preset (with its leader's
  items and upgrades).

It prints each side's win share with its 95 % margin of error (Wilson interval half-width, e.g. `A 55 % ± 3`; at 30
runs the margin is about ± 17 points, at 1000 about ± 3). Per side it prints wins (attacking / defending), runs where the side fled, units alive, hit points left, and per unit how
often it survived. Measured: 20 runs of 4 vs 4 units take ~0.1 s.
