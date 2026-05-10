using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;
using PosSystem.Services;

namespace PosSystem.ViewModels.Pos;

public partial class AddProductViewModel : ObservableObject
{
    private readonly ApiClient             _api;
    private readonly PriceListRepository   _priceLists;
    private readonly ProductTypeRepository _productTypes;

    public event EventHandler? ProductSaved;

    [ObservableProperty] private string       _nameInput    = "";
    [ObservableProperty] private string       _priceInput   = "";
    [ObservableProperty] private string       _costInput    = "";
    [ObservableProperty] private string       _stockInput   = "";
    [ObservableProperty] private string       _barcodeInput = "";
    [ObservableProperty] private MeasurementDto?  _selectedMeasurement;
    [ObservableProperty] private PriceList?       _selectedPriceList;
    [ObservableProperty] private ProductType?     _selectedProductType;
    [ObservableProperty] private string       _errorText    = "";
    [ObservableProperty] private bool         _isBusy;

    public ObservableCollection<MeasurementDto> Measurements  { get; } = [];
    public ObservableCollection<PriceList>      PriceLists    { get; } = [];
    public ObservableCollection<ProductType>    ProductTypes  { get; } = [];

    public AddProductViewModel(
        ApiClient api,
        PriceListRepository priceLists,
        ProductTypeRepository productTypes)
    {
        _api          = api;
        _priceLists   = priceLists;
        _productTypes = productTypes;
    }

    public async Task LoadAsync()
    {
        ErrorText = "";
        try
        {
            // Product types from local cache
            var types = _productTypes.GetAll();
            ProductTypes.Clear();
            foreach (var t in types.Where(t => t.Active))
                ProductTypes.Add(t);

            if (SelectedProductType is null || !ProductTypes.Contains(SelectedProductType))
                SelectedProductType = ProductTypes.FirstOrDefault();

            // Price lists from local cache
            var cached = _priceLists.GetAll();
            PriceLists.Clear();
            foreach (var pl in cached.Where(p => p.Active))
                PriceLists.Add(pl);

            if (SelectedPriceList is null || !PriceLists.Contains(SelectedPriceList))
                SelectedPriceList = PriceLists.FirstOrDefault();

            // Measurements fetched live
            var items = await _api.GetMeasurementsAsync();
            Measurements.Clear();
            foreach (var m in items.Where(m => m.Active))
                Measurements.Add(m);

            if (SelectedMeasurement is null || !Measurements.Contains(SelectedMeasurement))
                SelectedMeasurement = Measurements.FirstOrDefault();
        }
        catch (Exception ex)
        {
            ErrorText = $"Ma'lumotlar yuklanmadi: {ex.Message}";
        }
    }

    public void Reset()
    {
        NameInput           = "";
        PriceInput          = "";
        CostInput           = "";
        StockInput          = "";
        BarcodeInput        = "";
        ErrorText           = "";
        SelectedMeasurement = Measurements.FirstOrDefault();
        SelectedPriceList   = PriceLists.FirstOrDefault();
        SelectedProductType = ProductTypes.FirstOrDefault();
    }

    [RelayCommand]
    private void GenerateBarcode()
    {
        var rng    = new Random();
        var digits = new int[12];
        for (int i = 0; i < 12; i++)
            digits[i] = rng.Next(0, 10);

        int sum = 0;
        for (int i = 0; i < 12; i++)
            sum += digits[i] * (i % 2 == 0 ? 1 : 3);
        int check = (10 - sum % 10) % 10;

        BarcodeInput = string.Concat(digits.Select(d => d.ToString())) + check;
    }

    [RelayCommand(CanExecute = nameof(CanSave))]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(NameInput))
        { ErrorText = "Mahsulot nomi kiritilishi shart"; return; }

        if (SelectedMeasurement is null)
        { ErrorText = "O'lchov birligini tanlang"; return; }

        if (string.IsNullOrWhiteSpace(BarcodeInput))
        { ErrorText = "Barkod kiritilishi shart"; return; }

        ErrorText = "";
        IsBusy    = true;
        try
        {
            decimal? price = decimal.TryParse(PriceInput, out var p) && p > 0 ? p : null;
            decimal? cost  = decimal.TryParse(CostInput,  out var c) && c > 0 ? c : null;
            decimal? stock = decimal.TryParse(StockInput, out var s) && s > 0 ? s : null;

            List<CreateProductPriceRequest>? prices = null;
            if (SelectedPriceList is not null && price.HasValue)
            {
                prices =
                [
                    new CreateProductPriceRequest
                    {
                        PriceListId  = SelectedPriceList.Id,
                        CashPrice    = price.Value,
                        CashCurrency = SelectedPriceList.CurrencyId
                    }
                ];
            }

            var request = new CreateProductRequest
            {
                Name            = NameInput.Trim(),
                MeasurementUuid = SelectedMeasurement.Uuid,
                Type            = SelectedProductType?.Id ?? 0,
                Price           = prices is null ? price : null,
                Cost            = cost,
                Stock           = stock,
                Barcode         = BarcodeInput.Trim(),
                IsPos           = true,
                Prices          = prices
            };

            await _api.CreateProductAsync(request);
            ProductSaved?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSave() => !IsBusy;

    partial void OnIsBusyChanged(bool value) => SaveCommand.NotifyCanExecuteChanged();
}
