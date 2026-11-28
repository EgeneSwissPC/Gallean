# Cities overview and city switching

Spec for a gallean-upgrade game feature (not the map editor). Status: **questions answered, ready for phase 1.**

## Decisions

The user's wishes (voice message, 2026-11-28):

- Going through the cities you own is painful today. Add a way to see **all your cities at once** with their
  status: name, level, how much money each one brings, plus other useful information (left to us to propose).
- The entry point sits with the existing buttons in the **top-left corner of the world screen** (Capital,
  Spellbook, Quest).
- Clicking a city in that list **enters the city** (the normal city screen).
- Inside a city screen, a **previous / next ("left / right") control** switches straight to the next city
  without going back to the map.

User answers to the open questions (2026-11-28):

- A: land growth per turn is always shown; on maps with the town income rule the row shows **both** gold per turn and land growth, without the rule only land growth.
- B: I opens Cities; A / D = previous / next city (plus clickable chevrons); Q / E stay for tabs.
- C: capital first, then alphabetical; the sort picked in the list is also the cycle order.
- D: closing a city opened from the list returns to the list.
- E: enemy badge = distance in tiles to the nearest visible non-allied army of a playable race; neutral armies left out. The red threshold is measured in a real game while building.

Decisions taken while writing the spec (change any of them by saying so):

- The list is its own screen opened like Quest and Spellbook: a management screen with one tab, so it gets the
  same tab bar, resource bar, close button, blur and Esc handling as the other management screens.
- The capital is part of the list and of the cycle (first row), since it is the city the player visits most.
- Only the viewing player's own cities are listed (the city screen itself refuses foreign cities today,
  `CityTabViewModel.ResolveFortification`).

## The problem today

- The only ways into a city: walk an army in, click the city on the map (`WorldScreenViewModel.OpenFortification`),
  or press C for the capital (`OpenCapital`). With 8 villages (Empire on The War of the Ring), 16 (Free Men there),
  or dozens on our 512/1024 maps, checking garrisons and upgrades means scrolling the map city by city.
- Nothing shows the state of all cities together: which one is under-garrisoned, which can be upgraded, which
  has an enemy army next to it, which is rioting.
- Inside a city there is no way to the next one: Esc back to the map, find the next city, click it.

## What the game already offers (code read)

- **World screen buttons**: `Screens/World/WorldScreenView.axaml`, StackPanel `Nav` (top-left): Capital (C),
  Spellbook (B), Quest (U, also J). Each is a `pill` button with an `IconVisual` and a `HotkeyLabel` (the label
  highlights the hotkey letter if the text contains it).
- **World hotkeys bound** (`WorldVisual.GetHotkeyCommands`, `GetHotkeyStackIndex`, scroll helper): Tab, N, T, M,
  X, O, Space, Enter, E, C, B, U, J, P, 1-5, W/A/S/D and the arrow keys (scrolling). Free letters include
  F, G, H, I, K, L, Q, R, V, Y, Z.
- **Screen stack**: `GameViewModel.OpenScreen` pushes onto a stack, so a city opened from the list returns to the
  list when closed.
- **Management screen** (`Screens/Management/ManagementScreenViewModel.cs`): `CreateForFortification(id)` builds
  the city screen: tabs City + Visiting army, and for the capital also Buildings, Spellbook, Diplomacy, Spell groups.
  `CreateForQuest` / `CreateForSpellbook` show the pattern for a one-tab screen. Q / E switch tabs
  (`ManagementScreenView.axaml` KeyBindings + the Q / E keycap buttons around the tab strip). The city tab binds
  H (heal) and R (resurrect).
- **City tab** (`Screens/City/CityTabViewModel.cs`, `CityTabView.axaml`) already computes everything one city
  needs: `CityTitle` (name), `CitySummary` ("City level: N" or "Capital"), `GarrisonSummary` (x / y),
  `CityStats` (armor, regeneration, and "Gold per turn" with the next level's value when the town income rule is on),
  `CityImageKey` (`CityImage(level)` / `CapitalImage(race)`, shown clipped to a circle), upgrade label + cost.
  Engine queries behind it: `WorldGetCityInfoCommand` (armor, regeneration, growth/upgrade info, `TownIncome`,
  `NextLevelTownIncome`), `WorldGetGarrisonInfoCommand`, `WorldGetStackInfoCommand`.
- **City data** (`Engine.Contracts/World/WorldCity.cs`): level via `DefinitionId`, `Garrison`, `Inventory`,
  `RiotTurns` (> 0 = rioting, counts down each turn), `GrowthTurn` (turn of the last paid upgrade: one upgrade per
  city per turn, `WorldUpgradeCityCommand`).
- **Money per city**: in the original rules a city pays **no gold itself**. Income = the race's base income
  (paid as "Capital") + 50 per resource node on your land (`PlayerIncomeCalculator`). Cities matter because they
  spread your land every turn (capital 10 tiles, villages 15 / 15 / 20 / 20 / 25 by level, `ExpansionCalculator`),
  and land captures resources; but land is owned by terrain type, not by a city, so a mine can't be credited to
  one city. Only with our **town income** map rule (`docs/town-income.md`; on by default in new editor maps and
  MapFromPlan maps, off in the original scenarios) does each city pay its own gold: 10 / 25 / 50 / 75 / 100 by level
  by default, and your own capital the level-5 value (`TownIncomeCalculator.GetFortificationGold`, nothing while rioting).
- **Upgrade costs**: 150 / 250 / 500 / 750 gold to reach levels 2-5 (`GcityInf.GROWTH`, all races).

## What the player would see

### 1. "Cities" button and screen

- A fourth pill in the top-left row: **Cities** (Fortification icon, hotkey proposed in question B).
- It opens a management screen with one tab, "Cities". Layout copies the game's own look: ornament panels like the
  city tab, round city art (the city tab's circle-clipped `CityImage` / `CapitalImage`), round leader faces like
  the diplomacy screen, text styles from the city tab.
- **Summary line** on top: number of cities, total levels, total town gold per turn (rule on) and total land growth
  per turn, e.g. "7 cities · levels 18 · +235 gold · +130 land per turn".
- **One row per city** (scrolls; capital first, then the order chosen in question C):
  - round city art, **name**, **level** (or "Capital");
  - **land growth** per turn always, plus **gold per turn** when town income is on (the same number as the city
    tab's "Gold per turn" row);
  - **garrison**: occupied / total slots plus small unit faces; wounded units count (red when any are wounded);
  - **visiting army**: the leader's round face and name when an army stands in the city;
  - **upgrade**: next level and cost, greyed when unaffordable, "upgraded this turn" or "max level" otherwise,
    and an **Upgrade** button right in the row (the same `WorldUpgradeCityCommand` as the city tab);
  - **status badges**: rioting (turns left), enemy army near (distance in tiles, question E), items in the city
    inventory;
  - buttons: **Enter** (or click the row) opens the city screen; **Show on map** closes the list and centres the
    camera on the city.
- Sorting by name / level / gold / threat via small pills above the list (the chosen sort is also the cycle order).

### 2. Previous / next city inside the city screen

- Chevron buttons with keycaps on both sides of the city name in the city tab's title panel, like the Q / E keycaps
  around the tab strip: "‹ A   Minas Tirith   D ›" (keys in question B).
- Pressing next replaces the current city screen with the next city's (same position in the screen stack, so
  Esc still goes back to wherever the player came from: the list or the map).
- Keeps the same tab when the next city has it (City, Visiting army); from a capital-only tab (Buildings,
  Spellbook, Diplomacy, Spell groups) it lands on the City tab.
- Disabled while a targeted action (heal / resurrect / dismiss picking) or a drag is in progress, and while a
  text box has focus (the same guard as `ManagementTabViewModel.IsTyping`).
- A counter next to the name: "3 / 7".

## Technical approach

- **Client only.** No engine rule, save or AI change: all numbers come from the existing queries
  (`WorldGetCityInfoCommand`, `WorldGetGarrisonInfoCommand`, `WorldPlan`, `WorldVisibility`). Upstream files touched
  only where hooks are needed (the `Nav` StackPanel, `GetHotkeyCommands`, `ManagementScreenViewModel` factory,
  the city tab title panel), each marked `// Gallean Upgrade`.
- New files: `Screens/Cities/CitiesTabViewModel.cs`, `CitiesTabView.axaml(.cs)`, `CityRowViewModel.cs`,
  `CityCycle.cs` (builds the ordered list of own fortification IDs; shared by the list and the city switcher).
  `ManagementScreenViewModel.CreateForCities`, plus a line in `ViewDataTemplate.cs`.
- City switching: `ManagementScreenViewModel` gets an optional `CityCycle` (set by `CreateForFortification` when
  opened from the list, the map or C) and `SelectPreviousCity` / `SelectNextCity` commands that open the next
  city's screen and close the current one.
- Threat: per city, the nearest non-allied army on a revealed tile (`WorldVisibility`), Chebyshev distance from the
  city footprint; computed only while the list is open and only when the snapshot changes.
- Texts: every new string through `Tr()` / `{local:Text}` with entries in all 7 catalogues (cs/de/es/fr/it/pl/ru).

## Testing

- Client tests (NUnit, `TestGameViewModelBuilder`): list rows for a world with a capital + 3 cities (names, levels,
  garrison counts, town gold on/off, riot badge, upgrade cost / affordability / upgraded-this-turn, visiting leader);
  the Upgrade button upgrades and refreshes the row; Enter opens that city and Esc returns to the list; Show on map
  closes the list and moves the camera; next / previous cycles in list order, wraps around, keeps the tab, lands on
  City from a capital-only tab, is disabled during a targeted action; foreign cities never appear; the hotkey opens
  the list and is ignored while busy.
- Visual test with the real pack at 1366x768 and in Russian: the four-pill row must not touch the resource plate in
  the top centre ("Города" is the Russian label), the list with 20+ rows scrolls, the title panel with chevrons fits.
- Real-client AOT check on the `mapgen` map and The War of the Ring (16 Free Men villages): open the list, upgrade
  from a row, enter a city, cycle through all cities, check the gold numbers equal the city tab and the income tooltip.

## Phases

1. **Cities list** (button + hotkey, screen, rows with all columns above, Upgrade in the row, Enter, Show on map,
   sorting). ~1.5 days.
2. **Previous / next in the city screen** (chevrons + keys, order shared with the list, tab kept, guards).
   ~0.5-1 day. Independent of phase 1's layout; can go first if wanted.

## Open questions

All answered (A1 as "both when the rule is on", B1, C1, D1, E1), see Decisions.
