using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Common;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (battle sandbox, docs/battle-ai-designer.md phase 3): the setup screen. Two armies side by side
// built with the map editor's army grid (any unit of any race at any level), an owner and a battle AI per army,
// the battlefield between them (open field, a city of level 1-5 or the defender's capital, optionally as the
// garrison) and the difficulty, plus army presets. The battle itself is built by the engine's AiBattleBench on a
// generated host map with every race, so what this screen shows is exactly what Watch and Run (phases 5 and 6)
// will fight.
internal sealed partial class BattleSandboxScreenViewModel : MapEditorUnitsScreenViewModel
{
    public static readonly IReadOnlyList<WorldRace> OwnerRaces =
        [WorldRace.Human, WorldRace.Undead, WorldRace.Heretic, WorldRace.Dwarf, WorldRace.Elf, WorldRace.Neutral];

    // Where the army presets live (tests point it at a temporary folder).
    public static string ArmiesDirectory { get; set; } = Gallean.Configuration.AppDirectories.BattleArmies;

    // A neutral leader's face stands for the neutrals in the owner row (they have no lord).
    private static readonly UnitDefinitionId NeutralFaceUnit = new("g000uu5132");

    private readonly MusicQueue musicQueue = MusicQueue.Create([new AssetSoundKey.MenuMusic()], isLooping: true);
    private readonly AiBattleBench bench;
    private readonly BattleArmyStore store;
    private bool isReady;

    public BattleSandboxScreenViewModel(GameViewModel game, GameModel editorGame, IReadOnlyList<UnitDefinition> units, AiBattleBench bench, BattleArmyStore store)
        : base(game, editorGame, units)
    {
        this.bench = bench;
        this.store = store;
        Background = MainMenuBackgroundViewModel.Create();
        Places = [new(BattleBenchPlace.Open, Tr("Open field")), new(BattleBenchPlace.City, Tr("City")), new(BattleBenchPlace.Capital, Tr("Capital"))];
        Difficulties = [.. Enum.GetValues<WorldDifficulty>().Select(difficulty => new MapEditorOption<WorldDifficulty>(difficulty, WorldDifficultyFormatter.Format(difficulty)))];
        SelectedDifficulty = Difficulties.First(option => option.Value == WorldDifficulty.Average);

        IReadOnlyList<MapEditorOption<string>> ais = [.. AiBrainRegistry.BattleBrains.Select(info => new MapEditorOption<string>(info.Id, AiBrainFormatter.FormatName(info.Id)))];
        Attacker = new BattleSandboxSideViewModel(this, Tr("Attacker"), AddGroup(MapEditorUnitGroupKind.Army, null, () => GridPosition.Count, () => Attacker?.Owner ?? WorldRace.Human), WorldRace.Human, ais);
        Defender = new BattleSandboxSideViewModel(this, Tr("Defender"), AddGroup(MapEditorUnitGroupKind.Army, null, () => GridPosition.Count, () => Defender?.Owner ?? WorldRace.Undead), WorldRace.Undead, ais);

        RefreshPresets();
        isReady = true;
        Refresh();
    }

    public MainMenuBackgroundViewModel Background { get; }
    public BattleSandboxSideViewModel Attacker { get; }
    public BattleSandboxSideViewModel Defender { get; }
    public IReadOnlyList<MapEditorOption<BattleBenchPlace>> Places { get; }
    public IReadOnlyList<MapEditorOption<WorldDifficulty>> Difficulties { get; }
    public ObservableCollection<string> Presets { get; } = [];
    public string PresetsDirectory => store.Directory;

    public override bool PaintsOwnBackground => true;
    public override bool BlurBackdrop => false;
    public override ScreenMusic Music => ScreenMusic.Tracks(musicQueue);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsOpenField), nameof(IsCity), nameof(IsCapital), nameof(HasPlace), nameof(PlaceImageKey), nameof(PlaceTitle))]
    public partial BattleBenchPlace Place { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaceImageKey), nameof(PlaceTitle))]
    [NotifyCanExecuteChangedFor(nameof(CityLevelDownCommand), nameof(CityLevelUpCommand))]
    public partial int CityLevel { get; set; } = 1;

    [ObservableProperty]
    public partial bool AsGarrison { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<WorldDifficulty> SelectedDifficulty { get; set; }

    public bool IsOpenField => Place == BattleBenchPlace.Open;
    public bool IsCity => Place == BattleBenchPlace.City;
    public bool IsCapital => Place == BattleBenchPlace.Capital;
    public bool HasPlace => Place != BattleBenchPlace.Open;

    // The game's own city and capital pictures (city screen); none for the open field.
    public AssetSpriteKey? PlaceImageKey => Place switch
    {
        BattleBenchPlace.City => new AssetSpriteKey.CityImage(CityLevel),
        BattleBenchPlace.Capital when Defender.Owner != WorldRace.Neutral => new AssetSpriteKey.CapitalImage(Defender.Owner),
        _ => null
    };

    public string PlaceTitle => Place switch
    {
        BattleBenchPlace.City => Tr("City, level {0}", CityLevel),
        BattleBenchPlace.Capital => Tr("Capital of the defender"),
        _ => Tr("Open field")
    };

    public BattleBenchSetup Setup => new(Place, CityLevel, AsGarrison && HasPlace) { Difficulty = SelectedDifficulty.Value };

    public static AssetSpriteKey GetOwnerPortrait(WorldRace race)
    {
        return race == WorldRace.Neutral
            ? new AssetSpriteKey.UnitFace(NeutralFaceUnit)
            : new AssetSpriteKey.LordFace(race, 0, IsLarge: false);
    }

    protected override bool TryClose()
    {
        Game.ShowMainMenuCommand.Execute(null);

        return true;
    }

    [RelayCommand]
    private void SetPlace(MapEditorOption<BattleBenchPlace> option)
    {
        Place = option.Value;
    }

    private bool CanCityLevelDown() => CityLevel > 1;

    [RelayCommand(CanExecute = nameof(CanCityLevelDown))]
    private void CityLevelDown()
    {
        CityLevel--;
    }

    private bool CanCityLevelUp() => CityLevel < 5;

    [RelayCommand(CanExecute = nameof(CanCityLevelUp))]
    private void CityLevelUp()
    {
        CityLevel++;
    }

    // Swaps everything between the sides: units, owner, AI, "Anything goes" and the preset name.
    [RelayCommand]
    private void SwapSides()
    {
        isReady = false;

        (Attacker.Group.Slots, Defender.Group.Slots) = (Defender.Group.Slots, Attacker.Group.Slots);
        (Attacker.Owner, Defender.Owner) = (Defender.Owner, Attacker.Owner);
        (Attacker.SelectedAi, Defender.SelectedAi) = (Defender.SelectedAi, Attacker.SelectedAi);
        (Attacker.AllowsAnything, Defender.AllowsAnything) = (Defender.AllowsAnything, Attacker.AllowsAnything);
        (Attacker.PresetName, Defender.PresetName) = (Defender.PresetName, Attacker.PresetName);

        isReady = true;
        OnSideChanged();
    }

    partial void OnPlaceChanged(BattleBenchPlace value) => OnSideChanged();

    partial void OnAsGarrisonChanged(bool value) => OnSideChanged();

    partial void OnSelectedDifficultyChanged(MapEditorOption<WorldDifficulty> value) => OnSideChanged();

    // Called when an owner, the place, the garrison box or "Anything goes" changes.
    public void OnSideChanged()
    {
        if (!isReady)
        {
            return;
        }

        UpdateDefenderKind();
        OnPropertyChanged(nameof(PlaceImageKey));
        Refresh();
    }

    // The defender is an army, a city's garrison or a capital's garrison; a capital garrison keeps its race's
    // guardian (like in the game), other groups lose any guardian put there by an earlier choice.
    private void UpdateDefenderKind()
    {
        MapEditorUnitGroup group = Defender.Group;
        MapEditorUnitGroupKind kind = !Setup.DefenderIsGarrison
            ? MapEditorUnitGroupKind.Army
            : Place == BattleBenchPlace.Capital && Defender.Owner != WorldRace.Neutral ? MapEditorUnitGroupKind.CapitalGarrison : MapEditorUnitGroupKind.Garrison;
        UnitDefinitionId? guardian = kind == MapEditorUnitGroupKind.CapitalGarrison ? bench.GetGuardian(Defender.Owner) : null;
        CustomMapUnit?[] slots = [.. group.Slots];

        for (int index = 0; index < slots.Length; index++)
        {
            if (slots[index] is { IsGuardian: true } unit && unit.TypeId != guardian)
            {
                slots[index] = null;
            }
        }

        if (guardian is { } guardianId && !slots.Any(unit => unit?.TypeId == guardianId) && EditorGame.Definitions.TryGetUnit(guardianId) is { } definition)
        {
            foreach (GridPosition position in (GridPosition[])[GridPosition.BackCenter, .. GridPosition.All])
            {
                if (CustomMapStack.TryPlaceUnit(slots, position, CustomMapUnit.From(definition)) is { } placed)
                {
                    slots = placed;
                    break;
                }
            }
        }

        group.Kind = kind;
        group.Slots = slots;
    }

    protected override string? Validate()
    {
        if (Place == BattleBenchPlace.Capital && Defender.Owner == WorldRace.Neutral)
        {
            return Tr("Neutrals have no capital: choose another owner for the defender.");
        }

        if (Attacker.Group.IsEmpty || Defender.Group.IsEmpty)
        {
            return Tr("Both sides need at least one unit.");
        }

        // Anything the screen's own checks don't cover (the engine has the final word).
        return bench.Validate(ToSide(Attacker), ToSide(Defender), Setup, swapSides: false) is { } problem
            ? Tr("This battle can't be set up: {0}", problem)
            : null;
    }

    // Says which side a grid problem belongs to (the base screen reports the first problem without its side).
    protected override void OnRefreshed()
    {
        // A neutral defender in a capital is the setup's fault, not the army's: say that first.
        if (Place == BattleBenchPlace.Capital && Defender?.Owner == WorldRace.Neutral)
        {
            Problem = Tr("Neutrals have no capital: choose another owner for the defender.");
            IsValid = false;
            return;
        }

        foreach (BattleSandboxSideViewModel side in new[] { Attacker, Defender })
        {
            if (side?.Group.Problem is { } problem && Problem == problem)
            {
                Problem = Tr("{0}: {1}", side.Title, problem);
                break;
            }
        }
    }

    // The side as the bench sees it: its army (units by slot; a large unit stands on its front slot), AI and owner.
    public BattleBenchSide ToSide(BattleSandboxSideViewModel side)
    {
        return new BattleBenchSide(ToArmy(side), side.AiId) { Race = side.Owner };
    }

    public BattleArmy ToArmy(BattleSandboxSideViewModel side)
    {
        List<BattleArmyUnit> units = [];

        foreach (GridPosition position in GridPosition.All)
        {
            if (side.Group.Slots[position.ToIndex()] is { } unit)
            {
                units.Add(new BattleArmyUnit(unit.TypeId, unit.Level, position));
            }
        }

        string name = string.IsNullOrWhiteSpace(side.PresetName) ? side.Title : side.PresetName.Trim();

        return new BattleArmy(name, [.. units]) { AllowAnyArmy = side.AllowsAnything };
    }

    public void SavePreset(BattleSandboxSideViewModel side, string name)
    {
        try
        {
            store.Save(ToArmy(side) with { Name = name });
            RefreshPresets(name);
            side.SelectedPreset = name;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Game.ShowError(Tr("Failed to save the army: {0}", ex.Message));
        }
    }

    public void LoadPreset(BattleSandboxSideViewModel side, string name)
    {
        BattleArmy? army = store.LoadAll().FirstOrDefault(item => item.Name == name);

        if (army is null)
        {
            Game.ShowError(Tr("The army {0} can't be read.", name));
            RefreshPresets();
            return;
        }

        CustomMapUnit?[] slots = new CustomMapUnit?[GridPosition.Count];
        int skipped = 0;

        foreach (BattleArmyUnit unit in army.Units)
        {
            if (EditorGame.Definitions.TryGetUnit(unit.TypeId) is not { } definition ||
                CustomMapStack.TryPlaceUnit(slots, unit.Position, CustomMapUnit.From(definition, unit.Level, EditorGame.Definitions)) is not { } placed)
            {
                skipped++;
                continue;
            }

            slots = placed;
        }

        isReady = false;
        side.AllowsAnything = army.AllowAnyArmy;
        side.Group.Slots = slots;
        side.PresetName = army.Name;
        isReady = true;
        OnSideChanged();

        if (skipped > 0)
        {
            Game.ShowError(skipped == 1 ? Tr("1 unit of the army could not be placed.") : Tr("{0} units of the army could not be placed.", skipped));
        }
    }

    public void DeletePreset(string name)
    {
        store.Delete(name);
        RefreshPresets();
    }

    private void RefreshPresets(string? keep = null)
    {
        string? attacker = Attacker.SelectedPreset;
        string? defender = Defender.SelectedPreset;

        Presets.Clear();

        foreach (string name in store.LoadAll().Select(army => army.Name).Order(StringComparer.CurrentCultureIgnoreCase))
        {
            Presets.Add(name);
        }

        Attacker.SelectedPreset = Presets.Contains(attacker ?? keep ?? "") ? attacker ?? keep : null;
        Defender.SelectedPreset = Presets.Contains(defender ?? "") ? defender : null;
    }
}
