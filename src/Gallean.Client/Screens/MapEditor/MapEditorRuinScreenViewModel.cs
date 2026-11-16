using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Sets up one ruin: name, look, the reward (gold and an item) and the guards who must be beaten for it.
internal sealed partial class MapEditorRuinScreenViewModel : MapEditorGarrisonScreenViewModel
{
    public const int MaxNameLength = 40;

    private readonly MapEditorLibrary library;
    private readonly Func<int, string, int, ItemDefinitionId?, CustomMapUnit?[], string?> onConfirm;
    private readonly Action onDelete;
    private readonly bool initialized;

    public MapEditorRuinScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        MapEditorLibrary library,
        CustomMapRuin ruin,
        Func<int, string, int, ItemDefinitionId?, CustomMapUnit?[], string?> onConfirm,
        Action onDelete)
        : base(game, editorGame, library.Units, ruin.Guards)
    {
        this.library = library;
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Title = Tr("Ruin at {0}, {1}", ruin.TopLeft.X, ruin.TopLeft.Y);
        Looks = MapEditorLabels.Looks(CustomMapRuin.VariantCount);
        Name = ruin.Name;
        SelectedLook = Looks.FirstOrDefault(option => option.Value == ruin.Variant) ?? Looks[0];
        GoldText = ruin.Gold.ToString(CultureInfo.InvariantCulture);
        Item = ruin.Item;
        initialized = true;

        Refresh();
    }

    public string Title { get; }
    public IReadOnlyList<MapEditorOption<int>> Looks { get; }
    public int NameLength => MaxNameLength;
    public AssetAnimationKey PreviewKey => new AssetAnimationKey.Ruin(IsLooted: false, SelectedLook.Value);
    public bool HasItem => Item is not null;
    public string ItemName => Item is { } item ? MapEditorLibrary.GetItemName(EditorGame, item) : Tr("No item");
    public AssetSpriteKey? ItemIcon => Item is { } item ? MapEditorLibrary.GetItemIconKey(EditorGame, item) : null;

    protected override int GarrisonCapacity => CustomMapRuin.GuardCapacity;
    protected override WorldRace PickerRace => WorldRace.Neutral;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewKey))]
    public partial MapEditorOption<int> SelectedLook { get; set; }

    [ObservableProperty]
    public partial string GoldText { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasItem), nameof(ItemName), nameof(ItemIcon))]
    public partial ItemDefinitionId? Item { get; private set; }

    partial void OnGoldTextChanged(string value)
    {
        if (initialized)
        {
            Refresh();
        }
    }

    protected override string? Validate()
    {
        return TryParseGold(out _) ? null : Tr("Gold must be a number from 0 to {0}.", CustomMapRuin.MaxGold);
    }

    protected override void OnValidityChanged()
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void ChooseItem()
    {
        Game.OpenScreen(new MapEditorListPickerViewModel(Game, Tr("Choose item"), library.CreateItemOptions(EditorGame), value => Item = (ItemDefinitionId)value));
    }

    [RelayCommand]
    private void ClearItem()
    {
        Item = null;
    }

    [RelayCommand(CanExecute = nameof(IsValid))]
    private void Confirm()
    {
        if (!TryParseGold(out int gold))
        {
            return;
        }

        if (onConfirm(SelectedLook.Value, Name.Trim(), gold, Item, [.. Slots]) is { } error)
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

    private bool TryParseGold(out int gold)
    {
        string text = GoldText.Trim();

        if (text.Length == 0)
        {
            gold = 0;
            return true;
        }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out gold) && gold is >= 0 and <= CustomMapRuin.MaxGold;
    }
}
