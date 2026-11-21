using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// One owner in a side's owner row: the race's lord portrait (like the diplomacy screen) or, for the neutrals, a
// neutral leader's face.
internal sealed partial class BattleSandboxOwnerViewModel : ObservableObject
{
    public BattleSandboxOwnerViewModel(BattleSandboxSideViewModel side, WorldRace race, AssetSpriteKey portraitKey)
    {
        Race = race;
        Name = MapEditorLabels.Owner(race);
        PortraitKey = portraitKey;
        SelectCommand = new RelayCommand(() => side.Owner = race);
    }

    public WorldRace Race { get; }
    public string Name { get; }
    public AssetSpriteKey PortraitKey { get; }
    public IRelayCommand SelectCommand { get; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}

// Gallean Upgrade (battle sandbox, docs/battle-ai-designer.md phase 3): one side of the sandbox: its army grid,
// owner, battle AI, "Anything goes" and the army presets.
internal sealed partial class BattleSandboxSideViewModel : ObservableObject
{
    private readonly BattleSandboxScreenViewModel screen;

    public BattleSandboxSideViewModel(BattleSandboxScreenViewModel screen, string title, MapEditorUnitGroup group, WorldRace owner, IReadOnlyList<MapEditorOption<string>> ais)
    {
        this.screen = screen;
        Title = title;
        Group = group;
        Ais = ais;
        Owners = [.. BattleSandboxScreenViewModel.OwnerRaces.Select(race => new BattleSandboxOwnerViewModel(this, race, BattleSandboxScreenViewModel.GetOwnerPortrait(race)))];
        Owner = owner;
        SelectedAi = ais.FirstOrDefault();

        foreach (BattleSandboxOwnerViewModel item in Owners)
        {
            item.IsSelected = item.Race == owner;
        }
    }

    public string Title { get; }
    public MapEditorUnitGroup Group { get; }
    public UnitGridViewModel UnitGrid => Group.UnitGrid;
    public IReadOnlyList<BattleSandboxOwnerViewModel> Owners { get; }
    public IReadOnlyList<MapEditorOption<string>> Ais { get; }
    public ObservableCollection<string> Presets => screen.Presets;

    public string OwnerName => MapEditorLabels.Owner(Owner);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OwnerName))]
    public partial WorldRace Owner { get; set; }

    [ObservableProperty]
    public partial MapEditorOption<string>? SelectedAi { get; set; }

    [ObservableProperty]
    public partial bool AllowsAnything { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SavePresetCommand))]
    public partial string PresetName { get; set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPresetCommand), nameof(DeletePresetCommand))]
    public partial string? SelectedPreset { get; set; }

    public string AiId => SelectedAi?.Value ?? AiBrainRegistry.ClassicId;

    partial void OnOwnerChanged(WorldRace value)
    {
        foreach (BattleSandboxOwnerViewModel owner in Owners)
        {
            owner.IsSelected = owner.Race == value;
        }

        screen.OnSideChanged();
    }

    partial void OnAllowsAnythingChanged(bool value)
    {
        Group.AllowsAnything = value;
        screen.OnSideChanged();
    }

    private bool CanSavePreset() => !string.IsNullOrWhiteSpace(PresetName);

    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    private void SavePreset()
    {
        screen.SavePreset(this, PresetName.Trim());
    }

    private bool HasSelectedPreset() => SelectedPreset is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void LoadPreset()
    {
        if (SelectedPreset is { } name)
        {
            screen.LoadPreset(this, name);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void DeletePreset()
    {
        if (SelectedPreset is { } name)
        {
            screen.DeletePreset(name);
        }
    }

    [RelayCommand]
    private void ChooseLeader()
    {
        screen.ChooseLeader(Group);
    }
}
