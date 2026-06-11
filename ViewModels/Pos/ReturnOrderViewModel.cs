using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Services;

namespace PosSystem.ViewModels.Pos;

// Backs the ReturnOrderWindow. One instance per dialog; discarded on close.
//
// On construction: populates Lines from the OrderListDto. Since the list
// endpoint does not return item details, the ViewModel loads a warehouse list
// from the API and falls back gracefully when product details are unavailable.
//
// Submit flow:
//   1. Validate (at least one qty > 0, no qty overflow, warehouseUuid, cashboxUuid)
//   2. Set IsBusy + raise CanExecuteChanged
//   3. POST api/orders/{uuid}/returns
//   4. On success: raise ReturnCompleted event → window closes
//   5. On failure: set ErrorMessage, reset IsBusy → window stays open
public partial class ReturnOrderViewModel : ObservableObject
{
    private readonly ApiClient    _api;
    private readonly OrderListDto _order;

    public event EventHandler<ReturnOrderResponse>? ReturnCompleted;

    // ── Order header (display only) ───────────────────────────────────────────

    public string OrderLabel      { get; }
    public string OrderDateText   { get; }
    public string OrderTotalText  { get; }
    public bool   HasItems        => Lines.Count > 0;
    public bool   NoItems         => Lines.Count == 0;

    // ── Lines ─────────────────────────────────────────────────────────────────

    public ObservableCollection<ReturnLineItem> Lines { get; } = [];

    // ── Inputs ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _reason = "";
    [ObservableProperty] private string _warehouseUuid = "";

    // CashboxUuid is kept for SubmitReturnAsync — it is kept in sync with SelectedCashbox.
    [ObservableProperty] private string _cashboxUuid = "";

    // ── Warehouse picker (loaded from API) ────────────────────────────────────

    public ObservableCollection<WarehouseDto> Warehouses { get; } = [];
    [ObservableProperty] private WarehouseDto? _selectedWarehouse;

    partial void OnSelectedWarehouseChanged(WarehouseDto? value)
    {
        WarehouseUuid = value?.Uuid ?? "";
        SubmitReturnCommand.NotifyCanExecuteChanged();
    }

    // ── Cashbox picker (loaded from API) ─────────────────────────────────────

    public ObservableCollection<CashboxDto> Cashboxes { get; } = [];
    [ObservableProperty] private CashboxDto? _selectedCashbox;

    partial void OnSelectedCashboxChanged(CashboxDto? value)
    {
        CashboxUuid = value?.Uuid ?? "";
        SubmitReturnCommand.NotifyCanExecuteChanged();
    }

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _errorMessage   = "";
    [ObservableProperty] private string _successMessage = "";
    [ObservableProperty] private bool   _isBusy;
    [ObservableProperty] private bool   _isLoading;
    [ObservableProperty] private bool   _hasLoadError;

    partial void OnIsBusyChanged(bool value) =>
        SubmitReturnCommand.NotifyCanExecuteChanged();

    partial void OnIsLoadingChanged(bool value) =>
        SubmitReturnCommand.NotifyCanExecuteChanged();

    partial void OnHasLoadErrorChanged(bool value) =>
        SubmitReturnCommand.NotifyCanExecuteChanged();

    // ── Computed total ────────────────────────────────────────────────────────

    public decimal TotalReturnAmount =>
        Lines.Sum(l => l.LineTotal);

    // Constructor: called from ReturnOrderWindow code-behind.
    public ReturnOrderViewModel(
        ApiClient    api,
        OrderListDto order,
        string       defaultCashboxUuid,
        string       defaultWarehouseUuid)
    {
        _api   = api;
        _order = order;

        // Header
        var date = order.CreatedAt ?? order.CreatedDate ?? DateTime.MinValue;
        OrderLabel     = $"Zakaz #{order.OrderNumber ?? order.Uuid[..8]}";
        OrderDateText  = date == DateTime.MinValue ? "—" : date.ToString("dd.MM.yyyy  HH:mm");
        OrderTotalText = $"{order.TotalAmount ?? 0m:N0} so'm";

        // Pre-fill cashbox
        CashboxUuid = defaultCashboxUuid;

        // Pre-fill warehouse UUID (will be replaced once API loads, if non-empty)
        WarehouseUuid = defaultWarehouseUuid;
    }

    // ── Initialization ────────────────────────────────────────────────────────

    // Called by the window after construction to load warehouses from the API.
    // Lines remain empty (NoItems=true) if the order has no embedded product
    // info — the XAML shows a note in that case and disables submit.
    public async Task InitializeAsync()
    {
        try
        {
            var whs = await _api.GetWarehousesAsync();
            Warehouses.Clear();
            foreach (var w in whs.Where(w => w.Active))
                Warehouses.Add(w);

            // Select the pre-configured warehouse if it's in the list; else pick first.
            var preSelected = Warehouses.FirstOrDefault(w => w.Uuid == WarehouseUuid)
                              ?? Warehouses.FirstOrDefault();
            if (preSelected is not null)
            {
                SelectedWarehouse = preSelected;
                WarehouseUuid     = preSelected.Uuid;
            }
        }
        catch
        {
            // Non-fatal: WarehouseUuid stays as pre-configured value.
            // Submit validation will catch if still blank.
        }
    }

    // Populates lines from an explicit item list (called by code-behind when
    // the caller passes item details that it loaded separately).
    public void SetLines(IEnumerable<ReturnLineItem> lines)
    {
        Lines.Clear();
        foreach (var line in lines)
        {
            line.LineTotalChanged += (_, _) =>
                OnPropertyChanged(nameof(TotalReturnAmount));
            Lines.Add(line);
        }
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(NoItems));
        OnPropertyChanged(nameof(TotalReturnAmount));
        SubmitReturnCommand.NotifyCanExecuteChanged();
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    private bool CanSubmit() =>
        !IsBusy
        && !IsLoading
        && !HasLoadError
        && HasItems
        && Lines.Any(l => l.ReturnQty > 0);

    [RelayCommand(CanExecute = nameof(CanSubmit))]
    private async Task SubmitReturnAsync()
    {
        ErrorMessage = "";

        // Desktop-side validation before hitting the API
        if (string.IsNullOrWhiteSpace(WarehouseUuid))
        {
            ErrorMessage = "Ombor tanlanishi shart.";
            return;
        }
        if (SelectedCashbox is null || string.IsNullOrWhiteSpace(CashboxUuid))
        {
            ErrorMessage = "Kassani tanlang.";
            return;
        }

        var itemsToReturn = Lines.Where(l => l.ReturnQty > 0).ToList();
        if (itemsToReturn.Count == 0)
        {
            ErrorMessage = "Kamida bitta mahsulot qaytarilishi kerak.";
            return;
        }

        foreach (var line in itemsToReturn)
        {
            if (line.ReturnQty < 0)
            {
                ErrorMessage = $"'{line.ProductName}': qaytarish miqdori manfiy bo'lishi mumkin emas.";
                return;
            }
            var maxQty = line.SoldQty - line.AlreadyReturnedQty;
            if (line.ReturnQty > maxQty)
            {
                ErrorMessage = $"'{line.ProductName}': qaytarish miqdori ({line.ReturnQty:N3}) sotilgan miqdordan ({maxQty:N3}) oshib ketdi.";
                return;
            }
        }

        IsBusy = true;
        try
        {
            var request = new ReturnOrderRequest
            {
                WarehouseUuid = WarehouseUuid,
                CashboxUuid   = CashboxUuid,
                Reason        = string.IsNullOrWhiteSpace(Reason) ? null : Reason,
                Items = itemsToReturn.Select(l => new ReturnOrderItemRequest
                {
                    ProductUuid = l.ProductUuid,
                    Quantity    = l.ReturnQty
                }).ToList(),
                IdempotencyKey = Guid.NewGuid().ToString()
            };

            var result = await _api.ReturnOrderAsync(_order.Uuid, request);
            if (result is null)
            {
                ErrorMessage = "Server javob bermadi. Qayta urinib ko'ring.";
                return;
            }

            SuccessMessage = $"Qaytarish muvaffaqiyatli amalga oshirildi. Jami: {result.TotalReturnAmount:N0} so'm";
            ReturnCompleted?.Invoke(this, result);
        }
        catch (System.Net.Http.HttpRequestException ex)
        {
            ErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void Cancel() { /* Window code-behind subscribes and calls Close() */ }
}

// ── Line item ────────────────────────────────────────────────────────────────

public partial class ReturnLineItem : ObservableObject
{
    public event EventHandler? LineTotalChanged;

    public string  ProductUuid         { get; init; } = "";
    public string  ProductName         { get; init; } = "";
    public decimal SoldQty             { get; init; }
    public decimal AlreadyReturnedQty  { get; init; }
    public decimal UnitPrice           { get; init; }

    // MaxQty is a computed property for display in the DataGrid
    public decimal MaxQty => SoldQty - AlreadyReturnedQty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LineTotal))]
    private decimal _returnQty;

    partial void OnReturnQtyChanged(decimal value) =>
        LineTotalChanged?.Invoke(this, EventArgs.Empty);

    public decimal LineTotal => ReturnQty * UnitPrice;
}
