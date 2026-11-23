# Notes: AI leaders stay in the capital on custom maps

Status: investigated, not fixed yet.

## What happens

On a map made in the map editor, the AI's starting leader often waits in its capital for 20 turns or more.
It is a loop between the AI's own rules:

1. A capital starts with a lone leader. The party is 1 of 4 places full, below the 80 % threshold, so the
   order planner gives it the **Reinforce** order. Under Reinforce the army only moves to its target city,
   which is the city it already stands in, and it only attacks enemies it has already explored.
2. Gold goes to buildings first: Construct building (priority 1) always beats Buy unit (priority 0).
3. With less than 500 gold, Hire leader (priority 2) returns a "save up" plan: it is short by at most
   400 gold (`OvercommitLimit`). The best economy plan can't be executed, so `DecideEconomyAction`
   returns nothing for that step. It does not fall back to the next plan, so no unit is bought.
4. That hire could never happen anyway. The waiting leader stands on the capital's entrance, so
   `WorldPurchaseLeaderCommand` would fail with `TargetOccupied`. But it checks gold
   (`InsufficientResources`) first, so the AI keeps saving for it.
5. Units only get bought on the odd turn with 500+ gold and nothing left to build. The party fills very
   slowly and the leader keeps waiting.

A second, separate gap: the AI only gets map goals (objective targets) from scenario events that win the
game. It only uses them from turn 60 / 50 / 40 / 30 (easy to very hard). Custom maps are saved with no
events, so the AI never gets a goal. It only reacts to what it has explored. The original scenarios all
have events (8 to 100+ each).

## Measurements

The test map was a 72 x 72 editor map, Empire vs Undead, both players controlled by the AI, average
difficulty, with the diagnostic harness logging every AI action.

| Setup | Capital leader first leaves |
| --- | --- |
| Lone starting leader (as written today) | Empire turn 24, Undead turn 27 |
| Another map, Undead with lone leader | did not leave within 40 turns |
| Capital army set in the editor (leader + soldier) | turn 1 |
| One-line fix below, lone leader | Empire turn 3, Undead turn 4 |

## Update: fixed upstream (v0.16.5)

Upstream applied fix 1 below as 8b60959d ("Block the purchase leader world AI action when the entrance is occupied").
Since the v0.17.1 merge, Classic's lone capital leader leaves on a fresh custom map too
(`CustomMapTests.CapitalLeaderLeavesWithClassicPlusTest` now expects that for Classic as well).

## Possible fixes

1. **AI fix (one line, tested, not applied):** in `WorldAiPlanner.PurchaseLeaderAction`, only return the
   "save up" plan when the capital entrance is free (`plan.GetStackAt(capital.Entrance) is null`).
   The original campaigns still need checking for side effects.
2. **Map editor:** start new capitals with a fuller army, not a lone leader.
3. **Map editor:** write standard victory events for custom maps (for example "destroy the enemy capitals"),
   so the AI gets goals later in the game.

## How it was measured

With a local diagnostic engine test, not in the repo. It plays a custom map with AI players only and logs gold,
the chosen economy and army actions, the economy evaluations, and every army's position, units and order
for each turn.
