using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Gallean.Client;

// One entry of a bag, merchant, mage tower or mercenary camp: an item, spell or unit, with an amount (merchant
// stock, mercenary level) where that applies. Right-click shows its details.
internal sealed partial class MapEditorStockRowViewModel : ObservableObject, IDetailsSource
{
    private readonly Func<object?> createDetails;
    private readonly Func<int, string> formatAmount;

    public MapEditorStockRowViewModel(
        object value,
        IconViewModel? icon,
        string name,
        int? amount,
        int minAmount,
        int maxAmount,
        Func<int, string> formatAmount,
        Func<object?> createDetails,
        Action<MapEditorStockRowViewModel> onRemove,
        Action onChanged)
    {
        Value = value;
        Icon = icon;
        Name = name;
        Amount = amount;
        MinAmount = minAmount;
        MaxAmount = maxAmount;
        this.formatAmount = formatAmount;
        this.createDetails = createDetails;
        RemoveCommand = new RelayCommand(() => onRemove(this));
        IncreaseCommand = new RelayCommand(() => { Amount = Math.Min(MaxAmount, (Amount ?? MinAmount) + 1); onChanged(); }, () => Amount < MaxAmount);
        DecreaseCommand = new RelayCommand(() => { Amount = Math.Max(MinAmount, (Amount ?? MinAmount) - 1); onChanged(); }, () => Amount > MinAmount);
    }

    public object Value { get; }
    public IconViewModel? Icon { get; }
    public string Name { get; }
    public int MinAmount { get; }
    public int MaxAmount { get; }
    public bool HasAmount => Amount is not null;
    public string AmountText => Amount is { } amount ? formatAmount(amount) : string.Empty;
    public IRelayCommand RemoveCommand { get; }
    public IRelayCommand IncreaseCommand { get; }
    public IRelayCommand DecreaseCommand { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AmountText), nameof(HasAmount))]
    public partial int? Amount { get; set; }

    partial void OnAmountChanged(int? value)
    {
        IncreaseCommand?.NotifyCanExecuteChanged();
        DecreaseCommand?.NotifyCanExecuteChanged();
    }

    object? IDetailsSource.DetailsContent => createDetails();
}
