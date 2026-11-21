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

Gallean Upgrade (`docs/battle-ai-designer.md`, phase 1). Fights the same battle many times, headless: army A with its
battle AI against army B with its own. Each run builds the battle the way the game does when one army attacks another in
the open (`WorldCommandHelpers.CreateStackBattleState` on a tile without a city, owned by two players of the host map) and
resolves it like an AI-vs-AI battle in the game (`BattleAutoResolveCommand` with both AI IDs: the attacker retreats after
round 15, hard stop at round 45). The loop lives in the engine (`AiBattleBench`), for the battle sandbox later.

```
DOTNET_ROOT=~/.dotnet ./AiTournament --battle ../../../examples/armies/empire.json ../../../examples/armies/undead.json --runs 20 --swap --parallel 8
DOTNET_ROOT=~/.dotnet ./AiTournament --battle empire undead --armies ../../../examples/armies --ai-a ruthless --ai-b cautious
DOTNET_ROOT=~/.dotnet ./AiTournament --export-armies S099SC0000 /tmp/armies
```

- `<army A> <army B>`: preset files, or preset names in `--armies <dir>`. A preset is a JSON file (`BattleArmyStore`):
  `{ "format": 1, "name": "...", "units": [ { "unit": "g000uu0019", "level": 1, "line": 0|1, "flank": 0-2 } ] }`
  (line 0 = front; a large unit sits on its front slot and takes the back slot behind it). Unit IDs: `Globals/Gunits.json`.
  The army must follow the game's rules (one leader at most, leadership not exceeded, no guardians, level not below the
  unit's own); `"allowAnyArmy": true` skips the rules for stress tests. Leader items are not supported yet.
- `--ai-a`, `--ai-b` (classic): battle AIs (`--list`). `--runs n` (20), `--seed n` (1; run k uses seed + k, same seed =
  same battle), `--swap` (army A attacks in even runs and defends in odd runs; default: A always attacks),
  `--parallel n`, `--map id` (host map for players and world modifiers, `S099SC0000`), `--csv file` (one row per run),
  `--verbose` (one line per run).
- `--export-armies <map id> <dir>`: writes every army that starts on a built-in map as a preset.

Per side it prints wins (attacking / defending), runs where the side fled, units alive, hit points left, and per unit how
often it survived. Measured: 20 runs of 4 vs 4 units take ~0.1 s.
