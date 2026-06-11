using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PosSystem.Core.DTOs;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;
using PosSystem.Services;

namespace PosSystem.ViewModels.Ombor;

// Phase I.1 — backs the "+ Yangi kirim" modal opened from the Ombor page.
//
// Calls POST /api/inventory/adjustments with direction=IN. The single most
// common use case today is filling in missing opening stock for products
// created with stock=0 (e.g., the asdafsadf case that blocked retries of
// poisoned sales) — but the form is generic enough to handle any IN
// adjustment.
//
// Server-confirmed only. No offline queueing in this phase: inventory rows
// participate in WAC + idempotency invariants the backend owns, and
// LocalDB-then-replay would risk drift.
public partial class InventoryAdjustmentViewModel : ObservableObject
{
    private readonly ApiClient           _api;
    private readonly ProductRepository   _products;
    private readonly ConnectivityService _connectivity;
    private readonly SettingsRepository  _settings;

    public event EventHandler<InventoryAdjustmentResponse>? AdjustmentSaved;

    // ── Picker state ──────────────────────────────────────────────────────────

    [ObservableProperty] private string _productSearchText = "";
    [ObservableProperty] private bool   _isProductPopupOpen;
    [ObservableProperty] private Product? _selectedProduct;
    [ObservableProperty] private WarehouseDto? _selectedWarehouse;

    public ObservableCollection<Product>     ProductSuggestions { get; } = [];
    public ObservableCollection<WarehouseDto> Warehouses        { get; } = [];

    // ── Inputs ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _quantityInput = "";
    [ObservableProperty] private string _reasonInput   = "Desktop stock incoming";
    [ObservableProperty] private string _commentInput  = "";

    // ── Status ────────────────────────────────────────────────────────────────

    [ObservableProperty] private string _errorMessage   = "";
    [ObservableProperty] private string _successMessage = "";
    [ObservableProperty] private bool   _isBusy;

    public string SelectedProductDisplay => SelectedProduct is null
        ? "Mahsulot tanlanmagan"
        : $"{SelectedProduct.Name}  ·  joriy: {SelectedProduct.Stock:N3} {SelectedProduct.Unit}";

    public InventoryAdjustmentViewModel(
        ApiClient api,
        ProductRepository products,
        ConnectivityService connectivity,
        SettingsRepository settings)
    {
        _api          = api;
        _products     = products;
        _connectivity = connectivity;
        _settings     = settings;
    }

    // ── Public entry points ───────────────────────────────────────────────────

    public async Task LoadAsync()
    {
        ErrorMessage   = "";
        SuccessMessage = "";

        // Warehouses are tenant-scoped and small (≤ a handful in practice);
        // fetched fresh on every modal open so the dropdown reflects current
        // backend state.
        try
        {
            var warehouses = await _api.GetWarehousesAsync();
            Warehouses.Clear();
            foreach (var w in warehouses.Where(w => w.Active))
                Warehouses.Add(w);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Omborlar yuklanmadi: {ex.Message}";
        }

        // Default warehouse — Main Warehouse if exactly one exists, otherwise
        // keep null so cashier must explicitly pick.
        if (SelectedWarehouse is null || !Warehouses.Contains(SelectedWarehouse))
            SelectedWarehouse = Warehouses.Count == 1
                ? Warehouses[0]
                : Warehouses.FirstOrDefault();
    }

    public void Reset()
    {
        ProductSearchText = "";
        IsProductPopupOpen = false;
        SelectedProduct   = null;
        QuantityInput     = "";
        ReasonInput       = "Desktop stock incoming";
        CommentInput      = "";
        ErrorMessage      = "";
        SuccessMessage    = "";
        ProductSuggestions.Clear();
    }

    public async Task<bool> SubmitAsync()
    {
        ErrorMessage   = "";
        SuccessMessage = "";

        // Validation
        if (SelectedProduct is null)
        { ErrorMessage = "Mahsulotni tanlang."; return false; }
        if (string.IsNullOrEmpty(SelectedProduct.RemoteUuid))
        { ErrorMessage = "Bu mahsulot serverda mavjud emas. Avval sinxronlang."; return false; }
        if (SelectedWarehouse is null || string.IsNullOrEmpty(SelectedWarehouse.Uuid))
        { ErrorMessage = "Omborni tanlang."; return false; }
        if (!decimal.TryParse(QuantityInput, out var quantity) || quantity < 0.001m)
        { ErrorMessage = "Miqdor 0.001 dan kichik bo'lmasligi kerak."; return false; }
        if (string.IsNullOrWhiteSpace(ReasonInput))
        { ErrorMessage = "Sabab kiritilishi shart."; return false; }
        if (!_connectivity.IsOnline)
        { ErrorMessage = "Kirim qilish uchun internet/server ulanishi kerak."; return false; }

        try
        {
            IsBusy = true;
            // Idempotency-Key from the (product, warehouse, quantity, timestamp)
            // tuple — re-submits with the same content de-duplicate server-side.
            // Including a UTC timestamp prevents two intentional adjustments of
            // the same shape from being silently collapsed by the user.
            var idemSuffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var idem = $"DESKTOP-IN:{SelectedProduct.RemoteUuid}:{SelectedWarehouse.Uuid}:{quantity}:{idemSuffix}";

            var request = new CreateInventoryAdjustmentRequest
            {
                WarehouseUuid  = SelectedWarehouse.Uuid,
                ProductUuid    = SelectedProduct.RemoteUuid,
                Direction      = "IN",
                Quantity       = quantity,
                Reason         = ReasonInput.Trim(),
                Comment        = string.IsNullOrWhiteSpace(CommentInput) ? null : CommentInput.Trim(),
                IdempotencyKey = idem
            };

            var response = await _api.CreateInventoryAdjustmentAsync(request);

            // Sync local product stock to server-confirmed afterQuantity. Avoids
            // a full product-sync round-trip and avoids touching the watermark.
            if (response.AfterQuantity.HasValue)
                _products.SetStockByRemoteUuid(SelectedProduct.RemoteUuid, response.AfterQuantity.Value);

            SuccessMessage = $"Kirim muvaffaqiyatli qo'shildi. Yangi qoldiq: {response.AfterQuantity:N3} {SelectedProduct.Unit}";
            AdjustmentSaved?.Invoke(this, response);
            return true;
        }
        catch (Exception ex)
        {
            ErrorMessage = MapBackendError(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ── Product picker ────────────────────────────────────────────────────────

    partial void OnProductSearchTextChanged(string value)
    {
        ProductSuggestions.Clear();

        if (string.IsNullOrWhiteSpace(value))
        {
            IsProductPopupOpen = false;
            return;
        }

        // ProductRepository.GetAll already hides demo rows when server-backed
        // products exist (Phase I diagnosis fix), so the picker naturally only
        // suggests real server products.
        var q = value.Trim().ToLowerInvariant();
        var matches = _products.GetAll()
            .Where(p => !string.IsNullOrEmpty(p.RemoteUuid))
            .Where(p => p.Name.ToLowerInvariant().Contains(q)
                        || (p.Barcode ?? "").Contains(q)
                        || (p.Code ?? "").ToLowerInvariant().Contains(q))
            .OrderBy(p => p.Name)
            .Take(20);

        foreach (var p in matches)
            ProductSuggestions.Add(p);

        IsProductPopupOpen = ProductSuggestions.Count > 0;
    }

    [RelayCommand]
    private void SelectProduct(Product product)
    {
        SelectedProduct    = product;
        ProductSearchText  = "";
        IsProductPopupOpen = false;
        OnPropertyChanged(nameof(SelectedProductDisplay));
    }

    [RelayCommand]
    private void ClearProduct()
    {
        SelectedProduct    = null;
        ProductSearchText  = "";
        IsProductPopupOpen = false;
        OnPropertyChanged(nameof(SelectedProductDisplay));
    }

    partial void OnSelectedProductChanged(Product? value) =>
        OnPropertyChanged(nameof(SelectedProductDisplay));

    // ── Error translation ─────────────────────────────────────────────────────

    private static string MapBackendError(Exception ex)
    {
        if (ex is System.Net.Http.HttpRequestException http &&
            http.StatusCode is System.Net.HttpStatusCode status)
        {
            return status switch
            {
                System.Net.HttpStatusCode.Forbidden    => "Sizda omborga kirim qilish huquqi yo'q.",
                System.Net.HttpStatusCode.NotFound     => "Mahsulot yoki ombor topilmadi.",
                System.Net.HttpStatusCode.Conflict     => $"Konflikt: {http.Message}",
                System.Net.HttpStatusCode.BadRequest   => $"Noto'g'ri ma'lumot: {http.Message}",
                System.Net.HttpStatusCode.Unauthorized => "Sessiya muddati tugagan, qayta kiring.",
                _ => http.Message
            };
        }
        return ex.Message;
    }
}
