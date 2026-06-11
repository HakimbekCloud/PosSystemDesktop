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

    // ── Dialog UI state (added for the redesigned Add Product modal) ──────────
    // These properties exist purely to back the modal form fields shown in the
    // new design. They are *additive*: the existing SaveAsync flow keeps working
    // unchanged. Backend integration for min-stock / warehouse / active flag
    // can wire into the CreateProductRequest later without touching the modal.
    [ObservableProperty] private string         _minStockInput     = "";
    [ObservableProperty] private WarehouseDto?  _selectedWarehouse;
    [ObservableProperty] private bool           _isActive          = true;

    public ObservableCollection<MeasurementDto> Measurements  { get; } = [];
    public ObservableCollection<PriceList>      PriceLists    { get; } = [];
    public ObservableCollection<ProductType>    ProductTypes  { get; } = [];
    public ObservableCollection<WarehouseDto>   Warehouses    { get; } = [];

    // ── Derived: profit + margin shown in the dialog's strip ──────────────────
    // Recomputed via partial-method hooks when Cost or Price input changes.
    public decimal Profit
    {
        get
        {
            decimal.TryParse(CostInput,  out var c);
            decimal.TryParse(PriceInput, out var p);
            return p - c;
        }
    }

    public decimal Margin
    {
        get
        {
            decimal.TryParse(CostInput,  out var c);
            decimal.TryParse(PriceInput, out var p);
            return p > 0 ? Math.Round((p - c) / p * 100m, 1) : 0m;
        }
    }

    public string ProfitFormatted => (Profit >= 0 ? "+" : "") + Profit.ToString("N0");
    public string MarginFormatted => Margin.ToString("0.0") + "%";
    public bool   IsProfitable    => Profit > 0;

    public bool   HasMarginData
    {
        get
        {
            decimal.TryParse(CostInput,  out var c);
            decimal.TryParse(PriceInput, out var p);
            return c > 0 && p > 0;
        }
    }

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

            // Warehouses fetched live — required by backend whenever opening stock > 0.
            var warehouses = await _api.GetWarehousesAsync();
            Warehouses.Clear();
            foreach (var w in warehouses.Where(w => w.Active))
                Warehouses.Add(w);

            if (SelectedWarehouse is null || !Warehouses.Contains(SelectedWarehouse))
                SelectedWarehouse = Warehouses.FirstOrDefault();
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
        MinStockInput       = "";
        IsActive            = true;
        SelectedMeasurement = Measurements.FirstOrDefault();
        SelectedPriceList   = PriceLists.FirstOrDefault();
        SelectedProductType = ProductTypes.FirstOrDefault();
        SelectedWarehouse   = Warehouses.FirstOrDefault();
    }

    // ── Partial-method hooks: keep derived margin properties live ─────────────
    partial void OnCostInputChanged(string value)  => RefreshMargin();
    partial void OnPriceInputChanged(string value) => RefreshMargin();

    private void RefreshMargin()
    {
        OnPropertyChanged(nameof(Profit));
        OnPropertyChanged(nameof(Margin));
        OnPropertyChanged(nameof(ProfitFormatted));
        OnPropertyChanged(nameof(MarginFormatted));
        OnPropertyChanged(nameof(IsProfitable));
        OnPropertyChanged(nameof(HasMarginData));
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

        // Backend requires at least one price (ProductCreateDTO.isPricePayloadValid).
        decimal? price = decimal.TryParse(PriceInput, out var p) && p > 0 ? p : null;
        if (!price.HasValue)
        { ErrorText = "Sotuv narxi kiritilishi shart"; return; }

        decimal? cost  = decimal.TryParse(CostInput,  out var c) && c > 0 ? c : null;
        decimal? stock = decimal.TryParse(StockInput, out var s) && s > 0 ? s : null;

        // Backend requires openingWarehouseUuid when stock > 0
        // (ProductServiceImpl.validateOpeningStockPayload).
        if (stock.HasValue && SelectedWarehouse is null)
        { ErrorText = "Boshlang'ich stok kiritilgan — omborni tanlang"; return; }

        ErrorText = "";
        IsBusy    = true;
        try
        {
            List<CreateProductPriceRequest>? prices = null;
            if (SelectedPriceList is not null)
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
                Name                 = NameInput.Trim(),
                MeasurementUuid      = SelectedMeasurement.Uuid,
                Type                 = SelectedProductType?.Id ?? 0,
                Price                = prices is null ? price : null,
                Cost                 = cost,
                Stock                = stock,
                OpeningWarehouseUuid = stock.HasValue ? SelectedWarehouse?.Uuid : null,
                Barcode              = BarcodeInput.Trim(),
                IsPos                = true,
                Prices               = prices
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
