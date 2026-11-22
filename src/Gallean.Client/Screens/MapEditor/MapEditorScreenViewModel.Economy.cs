using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// The economy panel (E): per race the resources on its land and its income per turn, at Average difficulty, from the
// game's own income code (CustomMapEconomy). It follows every change of the map; selecting a race outlines its
// resources and rods on the map.
internal sealed partial class MapEditorScreenViewModel
{
    private WorldState? economyState;
    private CustomMap? economyMap;
    private bool updatingEconomy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLandmarkPalette))]
    public partial bool IsEconomyOpen { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorEconomyRaceViewModel> EconomyRaces { get; private set; } = [];

    // Resources on land no race owns.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasUnclaimed))]
    public partial IReadOnlyList<MapEditorEconomyRowViewModel> UnclaimedResources { get; private set; } = [];

    [ObservableProperty]
    public partial MapEditorEconomyRaceViewModel? SelectedEconomyRace { get; set; }

    public bool HasUnclaimed => UnclaimedResources.Count > 0;

    // The towns column of the income table, shown when the map has town income.
    [ObservableProperty]
    public partial bool ShowTownsColumn { get; private set; }

    // The palette shares the left of the map with the economy panel.
    public bool ShowLandmarkPalette => IsLandmarkTool && !IsEconomyOpen && !IsLocationsOpen && !IsEventsOpen;

    public CustomMapEconomy? Economy { get; private set; }

    // The starting bank editor under the race blocks: one race at a time, gold and all five manas.
    public IReadOnlyList<MapEditorOption<WorldRace>> BankRaces => CapitalRaces;

    [ObservableProperty]
    public partial MapEditorOption<WorldRace>? SelectedBankRace { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorBankFieldViewModel> BankFields { get; private set; } = [];

    private (WorldRace Race, WorldResourceType Type)? bankEditKey;
    private int bankEditUndoCount = -1;

    partial void OnSelectedBankRaceChanged(MapEditorOption<WorldRace>? value) => ShowBank();

    // Mage towers: how many percent more another race's spells cost there (a map rule; diplomacy keeps list prices).
    [ObservableProperty]
    public partial string SpellMarkupText { get; set; } = "0";

    private bool showingSpellMarkup;
    private int spellMarkupUndoCount = -1;

    // Neutrals: one side per kind (map rule).
    [ObservableProperty]
    public partial bool NeutralKinds { get; set; }

    partial void OnNeutralKindsChanged(bool value)
    {
        if (showingSpellMarkup || map.NeutralKinds == value)
        {
            return;
        }

        undoSteps.Push(map.Clone());
        TrimUndo();
        map.NeutralKinds = value;
        CanUndo = true;
        IsDirty = true;
    }

    private void ShowSpellMarkup()
    {
        showingSpellMarkup = true;

        try
        {
            SpellMarkupText = map.ForeignSpellMarkup.ToString(System.Globalization.CultureInfo.InvariantCulture);
            NeutralKinds = map.NeutralKinds;
            ShowTownIncome();
        }
        finally
        {
            showingSpellMarkup = false;
        }
    }

    // Towns (map rule "town income", docs/town-income.md): on/off and the gold per turn for town levels 1-5. Turning it
    // on fills in the last table used in this session, or the default one.
    [ObservableProperty]
    public partial bool TownIncomeOn { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorTownIncomeFieldViewModel> TownIncomeFields { get; private set; } = [];

    private IReadOnlyList<int> lastTownIncome = CustomMap.DefaultTownIncome;
    private int townIncomeEditLevel;
    private int townIncomeUndoCount = -1;

    private void ShowTownIncome()
    {
        TownIncomeOn = !map.TownIncome.IsDefaultOrEmpty;
        IReadOnlyList<int> shown = TownIncomeOn ? map.TownIncome : lastTownIncome;
        TownIncomeFields = [.. shown.Select((gold, index) => new MapEditorTownIncomeFieldViewModel(index + 1, gold, amount => SetTownIncomeLevel(index + 1, amount)))];
    }

    partial void OnTownIncomeOnChanged(bool value)
    {
        if (showingSpellMarkup || value == !map.TownIncome.IsDefaultOrEmpty)
        {
            return;
        }

        CustomMap before = map.Clone();

        if (!value)
        {
            lastTownIncome = [.. map.TownIncome];
        }

        if (map.SetTownIncome(value ? lastTownIncome : []))
        {
            undoSteps.Push(before);
            TrimUndo();
            townIncomeUndoCount = -1;
            CanUndo = true;
            IsDirty = true;
            RefreshEconomy();
        }
    }

    // One level's box changed: typing into the same box keeps one undo step. Ignored while the rule is off.
    private void SetTownIncomeLevel(int level, int amount)
    {
        if (map.TownIncome.IsDefaultOrEmpty)
        {
            return;
        }

        CustomMap before = map.Clone();

        if (!map.SetTownIncome(map.TownIncome.SetItem(level - 1, amount)))
        {
            return;
        }

        if (townIncomeEditLevel != level || townIncomeUndoCount != undoSteps.Count)
        {
            undoSteps.Push(before);
            TrimUndo();
        }

        townIncomeEditLevel = level;
        townIncomeUndoCount = undoSteps.Count;
        CanUndo = true;
        IsDirty = true;
        RefreshEconomy();
    }

    // An empty box counts as 0; anything that isn't a number is ignored until it is one. Typing keeps one undo step.
    partial void OnSpellMarkupTextChanged(string value)
    {
        if (showingSpellMarkup)
        {
            return;
        }

        string trimmed = value.Trim();
        int percent = 0;

        if (trimmed.Length > 0 && !int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out percent))
        {
            return;
        }

        CustomMap before = map.Clone();

        if (!map.SetForeignSpellMarkup(percent))
        {
            return;
        }

        if (spellMarkupUndoCount != undoSteps.Count)
        {
            undoSteps.Push(before);
            TrimUndo();
        }

        spellMarkupUndoCount = undoSteps.Count;
        CanUndo = true;
        IsDirty = true;
    }

    // Fills the bank fields from the map (after a race change, undo or open).
    private void ShowBank()
    {
        WorldRace race = (SelectedBankRace ?? BankRaces[0]).Value;
        WorldResourceSet bank = map.Players.First(player => player.Race == race).StartingBank;
        WorldResourceType own = CustomMapPlayer.GetOwnMana(race);
        WorldResourceType[] order =
        [
            WorldResourceType.Gold,
            own,
            .. new[] { WorldResourceType.LifeMana, WorldResourceType.InfernalMana, WorldResourceType.DeathMana, WorldResourceType.RuneMana, WorldResourceType.GroveMana }.Where(type => type != own)
        ];

        BankFields = [.. order.Select(type => new MapEditorBankFieldViewModel(type, CustomMapPlayer.Get(bank, type), type == own, amount => SetBankAmount(race, type, amount)))];
        ShowSpellMarkup();
    }

    // One field changed: typing into the same field keeps one undo step.
    private void SetBankAmount(WorldRace race, WorldResourceType type, int amount)
    {
        CustomMapPlayer player = map.Players.First(candidate => candidate.Race == race);
        WorldResourceSet bank = CustomMapPlayer.With(player.StartingBank, type, amount);

        if (bank == player.StartingBank)
        {
            return;
        }

        CustomMap before = map.Clone();

        if (!map.SetStartingBank(race, bank))
        {
            return;
        }

        if (bankEditKey != (race, type) || bankEditUndoCount != undoSteps.Count)
        {
            undoSteps.Push(before);
            TrimUndo();
        }

        bankEditKey = (race, type);
        bankEditUndoCount = undoSteps.Count;
        CanUndo = true;
        IsDirty = true;
        RefreshEconomy();
    }

    [RelayCommand]
    private void CopyBankToAll()
    {
        WorldRace race = (SelectedBankRace ?? BankRaces[0]).Value;
        CustomMap before = map.Clone();

        if (map.CopyStartingBankToAll(race))
        {
            undoSteps.Push(before);
            TrimUndo();
            bankEditKey = null;
            CanUndo = true;
            IsDirty = true;
            RefreshEconomy();
        }
    }

    // The race blocks show each starting bank: recompute even when the shown state didn't change.
    private void RefreshEconomy()
    {
        economyState = null;
        RefreshPreview();
    }

    [RelayCommand]
    private void ToggleEconomy()
    {
        IsEconomyOpen = !IsEconomyOpen;
    }

    partial void OnIsEconomyOpenChanged(bool value)
    {
        if (!value)
        {
            SelectedEconomyRace = null;
            SpreadTurns = 0;
        }
        else
        {
            IsLocationsOpen = false;
            IsEventsOpen = false;
            SelectedBankRace ??= BankRaces[0];
            ShowBank();
        }

        RefreshPreview();
    }

    partial void OnSelectedEconomyRaceChanged(MapEditorEconomyRaceViewModel? value)
    {
        if (!updatingEconomy)
        {
            RefreshPreview();
        }
    }

    // Recomputes the numbers when the shown state changed (terrain strokes, objects, rods, undo, preview rebuilds).
    private void UpdateEconomy()
    {
        if (!IsEconomyOpen || (ReferenceEquals(ShownState, economyState) && ReferenceEquals(map, economyMap)))
        {
            return;
        }

        bool mapReplaced = !ReferenceEquals(map, economyMap);
        economyMap = map;

        economyState = ShownState;
        Economy = CustomMapEconomy.Calculate(ShownState, editorGame.Session.EngineContext, map.TownIncome);
        WorldRace? selected = SelectedEconomyRace?.Race;
        updatingEconomy = true;

        try
        {
            ShowTownsColumn = !map.TownIncome.IsDefaultOrEmpty;
            EconomyRaces = [.. Economy.Races.Select(race => new MapEditorEconomyRaceViewModel(race, map.Players.FirstOrDefault(player => player.Race == race.Race)?.StartingBank ?? WorldResourceSet.Empty, ShowTownsColumn))];
            UnclaimedResources = [.. Economy.Unclaimed.Select(entry => new MapEditorEconomyRowViewModel(new CustomMapEconomyRow(entry.Type, entry.Count, 0, 0)))];
            SelectedEconomyRace = EconomyRaces.FirstOrDefault(race => race.Race == selected);
        }
        finally
        {
            updatingEconomy = false;
        }

        // Undo swaps the whole map: show its banks.
        if (mapReplaced)
        {
            ShowBank();
        }
    }

    // The tiles outlined for the selected race: resources on its land and its rods.
    private IReadOnlyList<WorldPosition> GetEconomyHighlight()
    {
        if (!IsEconomyOpen || SelectedEconomyRace is not { } selected)
        {
            return [];
        }

        // Land as shown: during the spread preview, the land of that turn.
        return
        [
            .. Economy?.GetRace(selected.Race)?.ResourcePositions ?? [],
            .. map.Rods.Where(rod => rod.Owner == selected.Race).Select(rod => rod.Position)
        ];
    }
}

// One race's block in the economy panel, laid out like the game's "Income per turn" table.
internal sealed class MapEditorEconomyRaceViewModel(CustomMapEconomyRace race, WorldResourceSet bank, bool showTowns = false)
{
    public WorldRace Race => race.Race;

    // "Start: 500 Gold · 1500 Life mana" (amounts above zero).
    public string StartText
    {
        get
        {
            string[] parts =
            [
                .. new[] { WorldResourceType.Gold, WorldResourceType.LifeMana, WorldResourceType.InfernalMana, WorldResourceType.DeathMana, WorldResourceType.RuneMana, WorldResourceType.GroveMana }
                    .Where(type => CustomMapPlayer.Get(bank, type) > 0)
                    .Select(type => $"{CustomMapPlayer.Get(bank, type)} {new WorldIncomeRowViewModel(type, 0, 0, 0).Name}")
            ];

            return parts.Length > 0 ? Tr("Start: {0}", string.Join(" · ", parts)) : Tr("Start: nothing");
        }
    }

    public string Title => MapEditorLabels.Race(race.Race);
    public string Summary => race.Rods == 1
        ? Tr("Land: {0} tiles ({1}%) · 1 rod", race.LandTiles, race.LandPercent)
        : Tr("Land: {0} tiles ({1}%) · {2} rods", race.LandTiles, race.LandPercent, race.Rods);
    public IReadOnlyList<MapEditorEconomyRowViewModel> Rows { get; } = [.. race.Rows.Select(row => new MapEditorEconomyRowViewModel(row, showTowns))];
}

// A resource type: the game's income row (icon, name, capital, sources, total) plus how many resources of it.
internal sealed class MapEditorEconomyRowViewModel(CustomMapEconomyRow row, bool showTowns = false)
{
    public WorldIncomeRowViewModel Income { get; } = new(row.Type, row.CapitalIncome, row.ResourcesIncome, row.Total) { TownsIncome = row.TownsIncome, ShowTowns = showTowns };
    public int Count => row.Resources;
    public string CountText => row.Resources.ToString();
}

// One level of the town income table: "Level N" and a box that takes 0-9999 gold.
internal sealed partial class MapEditorTownIncomeFieldViewModel : ObservableObject
{
    private readonly Action<int>? commit;

    public MapEditorTownIncomeFieldViewModel(int level, int gold, Action<int> commit)
    {
        Level = level;
        Text = gold.ToString(System.Globalization.CultureInfo.InvariantCulture);
        this.commit = commit;
    }

    public int Level { get; }
    public string Label => Tr("Level {0}", Level);

    [ObservableProperty]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value)
    {
        // An empty box counts as 0; anything else that isn't a number is ignored until it is one.
        if (commit is null)
        {
            return;
        }

        string trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            commit(0);
        }
        else if (int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int gold))
        {
            commit(Math.Min(gold, CustomMap.MaxTownIncome));
        }
    }
}

// One amount of the starting bank editor: the resource's icon and name, and a box that takes 0-9999.
internal sealed partial class MapEditorBankFieldViewModel : ObservableObject
{
    private readonly Action<int> commit;
    private readonly WorldIncomeRowViewModel label;

    public MapEditorBankFieldViewModel(WorldResourceType type, int amount, bool isOwnMana, Action<int> commit)
    {
        this.commit = commit;
        label = new WorldIncomeRowViewModel(type, 0, 0, 0);
        Type = type;
        IsOwnMana = isOwnMana;
        Text = amount.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public WorldResourceType Type { get; }
    public bool IsOwnMana { get; }
    public string Name => label.Name;
    public IconMoniker Icon => label.Icon;

    [ObservableProperty]
    public partial string Text { get; set; }

    partial void OnTextChanged(string value)
    {
        // An empty box counts as 0; anything else that isn't a number is ignored until it is one.
        if (commit is null)
        {
            return;
        }

        string trimmed = value.Trim();

        if (trimmed.Length == 0)
        {
            commit(0);
        }
        else if (int.TryParse(trimmed, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int amount))
        {
            commit(Math.Min(amount, CustomMapPlayer.MaxStartingAmount));
        }
    }
}
