using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gallean.Client;

// One choice in the item or spell list. Right-click shows its details.
internal sealed class MapEditorListPickerItemViewModel : IDetailsSource
{
    private readonly Func<object?> createDetails;

    public MapEditorListPickerItemViewModel(
        object value,
        IconViewModel? icon,
        string name,
        string description,
        string info,
        string group,
        Func<object?> createDetails)
    {
        Value = value;
        Icon = icon;
        Name = name;
        Description = description;
        Info = info;
        Group = group;
        this.createDetails = createDetails;
    }

    public object Value { get; }
    public IconViewModel? Icon { get; }
    public string Name { get; }
    public string Description { get; }
    public string Info { get; }
    public string Group { get; }
    public object? Details => createDetails();
    public IRelayCommand PickCommand { get; internal set; } = null!;

    // Gallean Upgrade (battle sandbox): a reason shown in red under the row; the row stays pickable.
    public string? Warning { get; init; }

    object? IDetailsSource.DetailsContent => Details;
}

// Chooses an item (bags, ruins, merchants) or a spell (mage towers) from every one in the game, with a filter by
// group (item category or spell race).
internal sealed partial class MapEditorListPickerViewModel : AbstractScreenViewModel
{
    private readonly IReadOnlyList<MapEditorListPickerItemViewModel> all;
    private readonly Action<object> onPicked;

    public MapEditorListPickerViewModel(GameViewModel game, string title, IReadOnlyList<MapEditorListPickerItemViewModel> options, Action<object> onPicked)
    {
        Game = game;
        Title = title;
        this.onPicked = onPicked;
        all = options;

        foreach (MapEditorListPickerItemViewModel option in options)
        {
            option.PickCommand = new RelayCommand(() => Pick(option));
        }

        Filters = [Tr("All"), .. options.Select(option => option.Group).Distinct()];
        SelectedFilter = Filters[0];
    }

    public GameViewModel Game { get; }
    public string Title { get; }
    public IReadOnlyList<string> Filters { get; }

    [ObservableProperty]
    public partial string SelectedFilter { get; set; }

    [ObservableProperty]
    public partial IReadOnlyList<MapEditorListPickerItemViewModel> Options { get; private set; } = [];

    public void Pick(MapEditorListPickerItemViewModel option)
    {
        CloseSelf();
        onPicked(option.Value);
    }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    partial void OnSelectedFilterChanged(string value)
    {
        Options = value == Filters[0] ? all : [.. all.Where(option => option.Group == value)];
    }
}
