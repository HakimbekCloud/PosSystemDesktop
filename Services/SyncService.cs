using System.Net.Http;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public enum SyncStatus { Idle, Syncing, Success, Error }

public class SyncService
{
    private readonly ApiClient            _api;
    private readonly ProductRepository   _products;
    private readonly CustomerRepository  _customers;
    private readonly SaleRepository      _sales;
    private readonly ConnectivityService _connectivity;
    private readonly SettingsRepository  _settings;
    private readonly System.Timers.Timer _timer;
    private bool _isSyncing;

    public SyncStatus Status           { get; private set; } = SyncStatus.Idle;
    public DateTime?  LastSyncAt       { get; private set; }
    public int        PendingSalesCount { get; private set; }
    public string     LastError        { get; private set; } = "";

    public event EventHandler? StatusChanged;

    public SyncService(
        ApiClient api,
        ProductRepository products,
        CustomerRepository customers,
        SaleRepository sales,
        ConnectivityService connectivity,
        SettingsRepository settings)
    {
        _api          = api;
        _products     = products;
        _customers    = customers;
        _sales        = sales;
        _connectivity = connectivity;
        _settings     = settings;

        _timer = new System.Timers.Timer(300_000); // every 5 min
        _timer.Elapsed += async (_, _) => await TrySyncAsync();
        _timer.AutoReset = true;
    }

    public void StartBackgroundSync() => _timer.Start();
    public void StopBackgroundSync()  => _timer.Stop();

    public async Task InitialSyncAsync()
    {
        if (!_connectivity.IsOnline) return;
        await SyncAllAsync();
    }

    public async Task TrySyncAsync()
    {
        if (_isSyncing || !_connectivity.IsOnline) return;
        await SyncAllAsync();
    }

    public async Task SyncAllAsync()
    {
        if (!_connectivity.IsOnline) { SetStatus(SyncStatus.Idle); return; }

        _isSyncing = true;
        LastError  = "";
        SetStatus(SyncStatus.Syncing);

        var errors = new List<string>();

        try
        {
            try { await SyncReferenceDataAsync(); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { errors.Add("Sessiya muddati tugagan"); }
            catch (Exception ex) { errors.Add($"Ma'lumotnoma: {ex.Message}"); }

            try { await SyncProductsAsync(); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { errors.Add("Sessiya muddati tugagan"); }
            catch (Exception ex) { errors.Add($"Mahsulotlar: {ex.Message}"); }

            try { await SyncCustomersAsync(); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { errors.Add("Sessiya muddati tugagan"); }
            catch (Exception ex) { errors.Add($"Mijozlar: {ex.Message}"); }

            try { await SyncPendingSalesAsync(); }
            catch (Exception ex) { errors.Add($"Zakazlar: {ex.Message}"); }

            LastSyncAt = DateTime.Now;
            _settings.Set("last_sync_at", LastSyncAt.Value.ToString("O"));
            PendingSalesCount = _sales.GetPendingCount();

            if (errors.Count > 0)
            {
                LastError = string.Join(" | ", errors);
                SetStatus(SyncStatus.Error);
            }
            else
            {
                SetStatus(SyncStatus.Success);
            }
        }
        finally
        {
            _isSyncing = false;
        }
    }

    // ── Reference data (branch / cashbox / price list) ───────────────────────

    private async Task SyncReferenceDataAsync()
    {
        if (string.IsNullOrEmpty(_settings.Get("default_branch_uuid")))
        {
            var branches = await _api.GetBranchesAsync();
            if (branches.Count > 0)
                _settings.Set("default_branch_uuid", branches[0].Uuid);
        }

        if (string.IsNullOrEmpty(_settings.Get("default_cashbox_uuid")))
        {
            var cashboxes = await _api.GetCashboxesAsync();
            if (cashboxes.Count > 0)
                _settings.Set("default_cashbox_uuid", cashboxes[0].Uuid);
        }

        // Price list ID as early fallback; SyncProductsAsync may override with a more specific value.
        if (string.IsNullOrEmpty(_settings.Get("default_price_list_id")))
        {
            var lists = await _api.GetPriceListsAsync();
            var first = lists.FirstOrDefault(l => l.Active) ?? lists.FirstOrDefault();
            if (first is not null)
                _settings.Set("default_price_list_id", first.Id.ToString());
        }
    }

    // ── Products ──────────────────────────────────────────────────────────────

    private async Task SyncProductsAsync()
    {
        var dtos = await _api.GetProductsAsync();
        if (dtos.Count == 0) return;

        // Extract priceListId + currencyId from product prices if not yet configured.
        if (string.IsNullOrEmpty(_settings.Get("default_currency_id")))
        {
            var firstPrice = dtos.SelectMany(p => p.Prices).FirstOrDefault();
            if (firstPrice is not null)
            {
                _settings.Set("default_price_list_id", firstPrice.PriceListId.ToString());
                _settings.Set("default_currency_id",   firstPrice.CashCurrency.ToString());
            }
        }

        _products.UpsertRange(dtos.Select(dto => new Product
        {
            RemoteUuid   = dto.Uuid,
            Name         = dto.Name,
            Code         = "",
            Barcode      = dto.Barcodes.FirstOrDefault()?.Barcode ?? "",
            Price        = dto.Price > 0 ? dto.Price
                           : (dto.Prices.FirstOrDefault()?.CashPrice ?? 0),
            CostPrice    = dto.Cost,
            CategoryName = "",
            Unit         = dto.MeasurementShortName ?? dto.MeasurementName ?? "dona",
            Stock        = dto.Stock,
            IsActive     = dto.IsPos && !dto.IsDelete,
            UpdatedAt    = DateTime.UtcNow
        }));

        // Backend is the source of truth — remove seed/demo products that have no server UUID.
        _products.DeleteLocalOnly();
    }

    // ── Customers ─────────────────────────────────────────────────────────────

    private async Task SyncCustomersAsync()
    {
        var dtos = await _api.GetCustomersAsync();
        if (dtos.Count == 0) return;

        _customers.UpsertRange(dtos.Select(dto => new Customer
        {
            RemoteUuid = dto.Uuid,
            Name       = dto.Name,
            Phone      = dto.Phone   ?? "",
            Address    = dto.Address ?? "",
            Balance    = dto.TotalDebt,
            UpdatedAt  = DateTime.UtcNow
        }));

        _customers.DeleteLocalOnly();
    }

    // ── Pending sales ─────────────────────────────────────────────────────────

    private async Task SyncPendingSalesAsync()
    {
        var branchUuid  = _settings.Get("default_branch_uuid");
        var cashboxUuid = _settings.Get("default_cashbox_uuid");

        if (!long.TryParse(_settings.Get("default_price_list_id"), out var priceListId) ||
            !long.TryParse(_settings.Get("default_currency_id"),   out var currencyId)  ||
            string.IsNullOrEmpty(branchUuid) || string.IsNullOrEmpty(cashboxUuid))
        {
            PendingSalesCount = _sales.GetPendingCount();
            return;
        }

        foreach (var sale in _sales.GetPendingSync())
        {
            // Skip sales that contain only local seed-data products (no server UUID).
            if (!sale.Items.Any(i => !string.IsNullOrEmpty(i.ProductRemoteUuid)))
            {
                _sales.MarkSynced(sale.LocalId, "LOCAL_ONLY");
                continue;
            }

            try
            {
                var serverUuid = await _api.SyncSaleAsync(
                    sale, branchUuid!, cashboxUuid!, priceListId, currencyId);
                _sales.MarkSynced(sale.LocalId, serverUuid);
            }
            catch
            {
                // Keep in queue; will retry on next sync cycle.
            }
        }

        PendingSalesCount = _sales.GetPendingCount();
    }

    private void SetStatus(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
