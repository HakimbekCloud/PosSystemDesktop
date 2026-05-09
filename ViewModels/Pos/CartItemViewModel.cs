using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace PosSystem.ViewModels.Pos;

public partial class CartItemViewModel : ObservableObject
{
    public int    ProductId         { get; init; }
    public string ProductRemoteUuid { get; init; } = "";
    public string ProductName       { get; init; } = "";
    public string ProductCode       { get; init; } = "";
    public string Unit              { get; init; } = "dona";
    public decimal UnitPrice        { get; init; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _quantity = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _lineDiscount;

    public decimal LineTotal => Math.Round(UnitPrice * Quantity - LineDiscount, 2);

    [RelayCommand]
    private void IncreaseQuantity() => Quantity += 1;

    [RelayCommand]
    private void DecreaseQuantity() { if (Quantity > 1) Quantity -= 1; }
}
