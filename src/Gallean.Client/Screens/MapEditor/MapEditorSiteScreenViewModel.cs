using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Gallean.Engine;

namespace Gallean.Client;

// Sets up one site: name, greeting, look and what it sells (items with stock at a merchant, spells at a mage tower,
// units with their level at a mercenary camp; a trainer sells experience and has nothing to set up).
internal sealed partial class MapEditorSiteScreenViewModel : AbstractScreenViewModel
{
    public const int MaxNameLength = 40;
    public const int MaxGreetingLength = 200;
    public const int MaxStock = 99;
    public const int MaxUnitLevel = CustomMapUnit.MaxLevel;

    private readonly GameModel editorGame;
    private readonly MapEditorLibrary library;
    private readonly Func<int, string, string, CustomMapMerchantItem[], WorldSpellDefinitionId[], CustomMapMercenary[], string?> onConfirm;
    private readonly Action onDelete;

    public MapEditorSiteScreenViewModel(
        GameViewModel game,
        GameModel editorGame,
        MapEditorLibrary library,
        CustomMapSite site,
        Func<int, string, string, CustomMapMerchantItem[], WorldSpellDefinitionId[], CustomMapMercenary[], string?> onConfirm,
        Action onDelete)
    {
        Game = game;
        this.editorGame = editorGame;
        this.library = library;
        this.onConfirm = onConfirm;
        this.onDelete = onDelete;
        Type = site.Type;
        Title = Tr("{0} at {1}, {2}", MapEditorLabels.SiteType(site.Type), site.TopLeft.X, site.TopLeft.Y);
        Looks = MapEditorLabels.Looks(CustomMapSite.GetVariantCount(site.Type));
        Name = site.Name;
        Greeting = site.Greeting;
        SelectedLook = Looks.FirstOrDefault(option => option.Value == site.Variant) ?? Looks[0];

        foreach (CustomMapMerchantItem item in site.Items)
        {
            AddItemRow(item.Item, item.Count);
        }

        foreach (WorldSpellDefinitionId spell in site.Spells)
        {
            AddSpellRow(spell);
        }

        foreach (CustomMapMercenary unit in site.Units)
        {
            AddUnitRow(unit.Unit, unit.Level);
        }

        UpdateCount();
    }

    public GameViewModel Game { get; }
    public CustomMapSiteType Type { get; }
    public string Title { get; }
    public IReadOnlyList<MapEditorOption<int>> Looks { get; }
    public ObservableCollection<MapEditorStockRowViewModel> Goods { get; } = [];
    public int NameLength => MaxNameLength;
    public int GreetingLength => MaxGreetingLength;
    public bool HasGoods => Type != CustomMapSiteType.Trainer;
    public bool IsTrainer => Type == CustomMapSiteType.Trainer;
    public AssetAnimationKey PreviewKey => new AssetAnimationKey.Site(ToAssetType(Type), SelectedLook.Value);

    public string GoodsTitle => Type switch
    {
        CustomMapSiteType.Merchant => Tr("Items for sale"),
        CustomMapSiteType.MageTower => Tr("Spells for sale"),
        _ => Tr("Units for hire")
    };

    public string AddLabel => Type switch
    {
        CustomMapSiteType.Merchant => Tr("Add item"),
        CustomMapSiteType.MageTower => Tr("Add spell"),
        _ => Tr("Add unit")
    };

    public override bool BlurBackdrop => true;

    [ObservableProperty]
    public partial string Name { get; set; }

    [ObservableProperty]
    public partial string Greeting { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreviewKey))]
    public partial MapEditorOption<int> SelectedLook { get; set; }

    [ObservableProperty]
    public partial string CountText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AddCommand))]
    public partial bool CanAdd { get; private set; }

    protected override bool TryClose()
    {
        CloseSelf();

        return true;
    }

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        switch (Type)
        {
            case CustomMapSiteType.Merchant:
                Game.OpenScreen(new MapEditorListPickerViewModel(Game, Tr("Choose item"), library.CreateItemOptions(editorGame), value =>
                {
                    ItemDefinitionId item = (ItemDefinitionId)value;

                    if (Goods.FirstOrDefault(row => row.Value.Equals(item)) is { } existing)
                    {
                        existing.Amount = Math.Min(MaxStock, (existing.Amount ?? 0) + 1);
                    }
                    else
                    {
                        AddItemRow(item, 1);
                    }

                    UpdateCount();
                }));
                break;

            case CustomMapSiteType.MageTower:
                Game.OpenScreen(new MapEditorListPickerViewModel(Game, Tr("Choose spell"), library.CreateSpellOptions(editorGame), value =>
                {
                    WorldSpellDefinitionId spell = (WorldSpellDefinitionId)value;

                    if (!Goods.Any(row => row.Value.Equals(spell)))
                    {
                        AddSpellRow(spell);
                    }

                    UpdateCount();
                }));
                break;

            case CustomMapSiteType.Mercenaries:
                Game.OpenScreen(new MapEditorUnitPickerViewModel(
                    Game,
                    editorGame,
                    library.Units,
                    pickLeader: false,
                    WorldRace.Neutral,
                    definition => definition.IsWaterOnly ? Tr("Sea creatures cannot be hired") : null,
                    definition =>
                    {
                        AddUnitRow(definition.Id, definition.Level);
                        UpdateCount();
                    }));
                break;
        }
    }

    [RelayCommand]
    private void Confirm()
    {
        CustomMapMerchantItem[] items = Type == CustomMapSiteType.Merchant
            ? [.. Goods.Select(row => new CustomMapMerchantItem((ItemDefinitionId)row.Value, row.Amount ?? 1))]
            : [];
        WorldSpellDefinitionId[] spells = Type == CustomMapSiteType.MageTower ? [.. Goods.Select(row => (WorldSpellDefinitionId)row.Value)] : [];
        CustomMapMercenary[] units = Type == CustomMapSiteType.Mercenaries
            ? [.. Goods.Select(row => new CustomMapMercenary((UnitDefinitionId)row.Value, row.Amount ?? 1))]
            : [];

        if (onConfirm(SelectedLook.Value, Name.Trim(), Greeting.Trim(), items, spells, units) is { } error)
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

    private void AddItemRow(ItemDefinitionId item, int count)
    {
        Goods.Add(new MapEditorStockRowViewModel(
            item,
            MapEditorLibrary.GetItemIcon(editorGame, item),
            MapEditorLibrary.GetItemName(editorGame, item),
            count,
            minAmount: 1,
            MaxStock,
            amount => Tr("× {0}", amount),
            () => ItemDetailsViewModel.Create(item, null, WorldResourceSet.Empty, editorGame.Definitions, editorGame.Texts),
            RemoveRow,
            UpdateCount));
    }

    private void AddSpellRow(WorldSpellDefinitionId spell)
    {
        Goods.Add(new MapEditorStockRowViewModel(
            spell,
            MapEditorLibrary.GetSpellIcon(editorGame, spell),
            MapEditorLibrary.GetSpellName(editorGame, spell),
            amount: null,
            minAmount: 0,
            maxAmount: 0,
            _ => string.Empty,
            () => SpellDetailsViewModel.Create(spell, editorGame.Definitions, editorGame.Texts),
            RemoveRow,
            UpdateCount));
    }

    private void AddUnitRow(UnitDefinitionId unit, int level)
    {
        int baseLevel = editorGame.Definitions.TryGetUnit(unit)?.Level ?? 1;

        Goods.Add(new MapEditorStockRowViewModel(
            unit,
            new IconViewModel.Unit(new AssetSpriteKey.UnitPortrait(unit)),
            MapEditorLibrary.GetUnitName(editorGame, unit),
            Math.Max(level, baseLevel),
            baseLevel,
            MaxUnitLevel,
            amount => Tr("Level {0}", amount),
            () => CreateUnitDetails(unit, level),
            RemoveRow,
            UpdateCount));
    }

    private object? CreateUnitDetails(UnitDefinitionId unit, int level)
    {
        if (editorGame.Definitions.TryGetUnit(unit) is not { } definition)
        {
            return null;
        }

        WorldUnit preview = new(UnitDetailsViewModel.PreviewUnitId, unit, definition.MaxHitpoints, Experience: 0, Math.Max(level, definition.Level), [], null, false);

        return UnitDetailsViewModel.CreateFromWorld(preview, null, editorGame.Definitions, editorGame.Texts, editorGame.Stats);
    }

    private void RemoveRow(MapEditorStockRowViewModel row)
    {
        Goods.Remove(row);
        UpdateCount();
    }

    private void UpdateCount()
    {
        CountText = Tr("{0} / {1}", Goods.Count, CustomMapSite.MaxGoods);
        CanAdd = HasGoods && Goods.Count < CustomMapSite.MaxGoods;
    }

    private static AssetAnimationKey.SiteType ToAssetType(CustomMapSiteType type)
    {
        return type switch
        {
            CustomMapSiteType.Merchant => AssetAnimationKey.SiteType.Merchant,
            CustomMapSiteType.MageTower => AssetAnimationKey.SiteType.Mage,
            CustomMapSiteType.Mercenaries => AssetAnimationKey.SiteType.Mercenary,
            _ => AssetAnimationKey.SiteType.Trainer
        };
    }
}
