using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Gallean Upgrade (AI designer): one stage card. The last card is the default stage: it has no When and
// plays when no stage above it matches.
internal sealed partial class AiDesignerStageViewModel : ObservableObject
{
    private readonly AiDesignerScreenViewModel screen;

    public AiDesignerStageViewModel(AiDesignerScreenViewModel screen, string name)
    {
        this.screen = screen;
        name_ = name;
        Economy = new AiDesignerSlotViewModel(this, AiDesignerCategory.Economy);
        Army = new AiDesignerSlotViewModel(this, AiDesignerCategory.Army);
    }

    private string name_;

    public string Name
    {
        get => name_;
        set
        {
            if (SetProperty(ref name_, value))
            {
                screen.OnChanged();
            }
        }
    }

    public ObservableCollection<AiDesignerGroupViewModel> Groups { get; } = [];
    public AiDesignerSlotViewModel Economy { get; }
    public AiDesignerSlotViewModel Army { get; }
    public ObservableCollection<AiDesignerBlockViewModel> Rules { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp), nameof(CanMoveDown), nameof(CanRemove), nameof(ShowWhen))]
    public partial bool IsDefault { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveUp))]
    public partial bool IsFirst { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanMoveDown))]
    public partial bool IsLastBeforeDefault { get; set; }

    [ObservableProperty]
    public partial bool HasProblem { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; }

    [ObservableProperty]
    public partial int Number { get; set; }

    public bool ShowWhen => !IsDefault;
    public bool CanMoveUp => !IsDefault && !IsFirst;
    public bool CanMoveDown => !IsDefault && !IsLastBeforeDefault;
    public bool CanRemove => !IsDefault;
    public bool HasNoRules => Rules.Count == 0;

    public void RefreshState()
    {
        foreach (AiDesignerGroupViewModel group in Groups)
        {
            group.IsFirst = ReferenceEquals(group, Groups[0]);
            group.Refresh();
        }

        Economy.Renumber();
        Army.Renumber();
        OnPropertyChanged(nameof(HasNoRules));
    }

    [RelayCommand]
    private void AddGroup()
    {
        screen.AddGroup(this);
    }

    [RelayCommand]
    private void RemoveGroup(AiDesignerGroupViewModel group)
    {
        screen.RemoveGroup(this, group);
    }

    [RelayCommand]
    private void MoveUp()
    {
        screen.MoveStage(this, -1);
    }

    [RelayCommand]
    private void MoveDown()
    {
        screen.MoveStage(this, +1);
    }

    [RelayCommand]
    private void Remove()
    {
        screen.RemoveStage(this);
    }

    [RelayCommand]
    private void MoveTierUp(AiDesignerTierViewModel tier)
    {
        screen.MoveTier(tier, -1);
    }

    [RelayCommand]
    private void MoveTierDown(AiDesignerTierViewModel tier)
    {
        screen.MoveTier(tier, +1);
    }
}
