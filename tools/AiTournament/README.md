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

Per game it prints the outcome (`win` when one playable race is left or a scenario Win event fires, `draw` at the turn
limit) and per player: cities at the start and the end, the turn of its first city or capital capture, and the AI time
per turn (average and max, battles included). The summary per contender adds wins, draws, *leads* (draws it ended with
strictly the most cities), eliminations, average winning turn and first capture.

Measured with Classic only (8 vCPU host, Release): `S099SC0000` (4 races), 30 turns, 4 games: 10 s one at a time, 7 s
with `--parallel 4`, all draws, first capture on turn 22 on average, 47 ms AI per turn (max 404 ms).
