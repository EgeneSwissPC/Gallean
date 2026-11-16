using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Fills one treasure bag: its look and the items inside.
internal sealed partial class MapEditorBagScreenViewModel : AbstractScreenViewModel
{
    private readonly GameModel editorGame;
    private readonly MapEditorLibrary library;
    private readonly bool isWater;
    private readonly Func<int, ItemDefinitionId[], string?> onConfirm;
    private readonly Action onDelete;

    public MapEditorBagScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        MapEditorLibrary library,
        CustomMapBag bag,
        bool isWater,
        Func<int, ItemDefinitionId[], string?> onConfirm,
        Action onDelete)
    {
        Game = game;
        this.editorGame = editorGame;
        this.library = library;
        this.isWater = isWater;
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Title = Tr("Treasure at {0}, {1}", bag.Position.X, bag.Position.Y);
        Looks = MapEditorLabels.Looks(isWater ? 4 : CustomMapBag.VariantCount);
        SelectedLook = Looks.FirstOrDefault(option => option.Value == bag.Variant) ?? Looks[bag.Variant % Looks.Count];

        foreach (ItemDefinitionId item in bag.Items)
        {
            AddRow(item);
        }

        UpdateCount();
    }

    public GameViewModel Game { get; }
    public string Title { get; }
    public IReadOnlyList<MapEditorOption<int>> Looks { get; }
    public ObservableCollection<MapEditorStockRowViewModel> Items { get; } = [];
    public AssetSpriteKey PreviewKey => new AssetSpriteKey.Bag(isWater, SelectedLook.Value);

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewKey))]
    public partial MapEditorOption<int> SelectedLook { get; set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddItemCommand))]
    public partial bool CanAddItem { get; private set; }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanAddItem))]
    private void AddItem()
    {
        Game.OpenScreen(new MapEditorListPickerViewModel(Game, Tr("Choose item"), library.CreateItemOptions(editorGame), value =>
        {
            AddRow((ItemDefinitionId)value);
            UpdateCount();
        }));
    }

    [RelayCommand]
    private void Confirm()
    {
        if (onConfirm(SelectedLook.Value, [.. Items.Select(row => (ItemDefinitionId)row.Value)]) is { } error)
        {
            Game.ShowError(error);
            return;
        }

        CloseSelf();
    }

    [RelayCommand]
    private void Delete()
    {
        CloseSelf();
        onDelete();
    }

    private void AddRow(ItemDefinitionId item)
    {
        Items.Add(new MapEditorStockRowViewModel(
            item,
            MapEditorLibrary.GetItemIcon(editorGame, item),
            MapEditorLibrary.GetItemName(editorGame, item),
            amount: null,
            minAmount: 0,
            maxAmount: 0,
            _ => string.Empty,
            () => ItemDetailsViewModel.Create(item, null, WorldResourceSet.Empty, editorGame.Definitions, editorGame.Texts),
            row =>
            {
                Items.Remove(row);
                UpdateCount();
            },
            () => { }));
    }

    private void UpdateCount()
    {
        CountText = Tr("{0} / {1}", Items.Count, CustomMapBag.MaxItems);
        CanAddItem = Items.Count < CustomMapBag.MaxItems;
    }
}
