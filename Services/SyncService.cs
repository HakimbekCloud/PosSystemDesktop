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
    private readonly PriceListRepository   _priceLists;
    private readonly ProductTypeRepository _productTypes;
    private readonly System.Timers.Timer   _timer;

    // Bug C3: a single gate around SyncAllAsync itself serialises every entry
    // point (timer, manual, initial, post-product-save). WaitAsync(0) means an
    // overlapping caller returns immediately instead of queueing a duplicate run.
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    public SyncStatus    Status           { get; private set; } = SyncStatus.Idle;
    public DateTime?     LastSyncAt       { get; private set; }
    public int           PendingSalesCount { get; private set; }
    public string        LastError        { get; private set; } = "";
    public List<string>  LastErrors       { get; private set; } = [];

    public event EventHandler? StatusChanged;

    public SyncService(
        ApiClient api,
        ProductRepository products,
        CustomerRepository customers,
        SaleRepository sales,
        ConnectivityService connectivity,
        SettingsRepository settings,
        PriceListRepository priceLists,
        ProductTypeRepository productTypes)
    {
        _api          = api;
        _products     = products;
        _customers    = customers;
        _sales        = sales;
        _connectivity = connectivity;
        _settings     = settings;
        _priceLists   = priceLists;
        _productTypes = productTypes;

        _timer = new System.Timers.Timer(300_000); // every 5 min
        // Bug H4: the timer fires on a pool thread, so any escaping exception is
        // unobserved and crashes the WPF process mid-sale. The whole handler body
        // must be wrapped — never let anything propagate out of an async void.
        _timer.Elapsed += async (_, _) =>
        {
            try { await TrySyncAsync(); }
            catch (Exception ex)
            {
                LastError = ex.Message;
                SetStatus(SyncStatus.Error);
            }
        };
        _timer.AutoReset = true;
    }

    public void StartBackgroundSync() => _timer.Start();
    public void StopBackgroundSync()  => _timer.Stop();

    // Manual + initial syncs force a retry of every pending sale (bypass backoff).
    public async Task InitialSyncAsync()
    {
        if (!_connectivity.IsOnline) return;
        await SyncAllAsync(force: true);
    }

    // Background/timer-driven attempt: honours the per-sale backoff window.
    public async Task TrySyncAsync()
    {
        if (!_connectivity.IsOnline) return;
        await SyncAllAsync(force: false);
    }

    // force == true  → manual/initial sync: retry every pending sale immediately.
    // force == false → timer-driven sync: permanently-rejected sales obey backoff.
    public async Task SyncAllAsync(bool force = true)
    {
        if (!_connectivity.IsOnline) { SetStatus(SyncStatus.Idle); return; }

        // Bug C3: only one sync may run at a time. An overlapping caller returns
        // immediately rather than queueing a duplicate run that could double-submit.
        if (!await _syncLock.WaitAsync(0)) return;

        // Everything below is inside try/finally so the semaphore is always
        // released and (Bug H4) no exception escapes — even from the epilogue
        // statements (settings write, pending-count read) outside the inner trys.
        try
        {
            LastError   = "";
            LastErrors  = [];
            SetStatus(SyncStatus.Syncing);

            var errors = new List<string>();

            try { await SyncReferenceDataAsync(); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { errors.Add("Sessiya muddati tugagan"); }
            catch (Exception ex) { errors.Add($"Ma'lumotnoma: {ex.Message}"); }

            // Sales first so the backend stock is already decremented when we fetch products
            try { await SyncPendingSalesAsync(force, errors); }
            catch (Exception ex) { errors.Add($"Zakazlar: {ex.Message}"); }

            try { await SyncProductsAsync(); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { errors.Add("Sessiya muddati tugagan"); }
            catch (Exception ex) { errors.Add($"Mahsulotlar: {ex.Message}"); }

            try { await SyncCustomersAsync(); }
            catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            { errors.Add("Sessiya muddati tugagan"); }
            catch (Exception ex) { errors.Add($"Mijozlar: {ex.Message}"); }

            try
            {
                LastSyncAt = DateTime.Now;
                _settings.Set("last_sync_at", LastSyncAt.Value.ToString("O"));
                PendingSalesCount = _sales.GetPendingCount();
            }
            catch (Exception ex) { errors.Add($"Holat: {ex.Message}"); }

            LastErrors = errors;
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
        catch (Exception ex)
        {
            // Last-resort guard: nothing may escape into a timer/pool context.
            LastError = ex.Message;
            SetStatus(SyncStatus.Error);
        }
        finally
        {
            _syncLock.Release();
        }
    }

    // ── Reference data (branch / cashbox / price list / currency) ──────────────

    private async Task SyncReferenceDataAsync()
    {
        if (string.IsNullOrEmpty(_settings.Get("default_branch_uuid")))
        {
            var branches = await _api.GetBranchesAsync();
            if (branches.Count > 0)
                _settings.Set("default_branch_uuid", branches[0].Uuid);
        }

        // Fetch cashboxes if UUID or currency ID is still unknown.
        if (string.IsNullOrEmpty(_settings.Get("default_cashbox_uuid")) ||
            string.IsNullOrEmpty(_settings.Get("default_currency_id")))
        {
            var cashboxes = await _api.GetCashboxesAsync();
            if (cashboxes.Count > 0)
            {
                if (string.IsNullOrEmpty(_settings.Get("default_cashbox_uuid")))
                    _settings.Set("default_cashbox_uuid", cashboxes[0].Uuid);

                // Derive currency ID from cashbox currency code.
                // Backend mapping: UZS=1, USD=2 (from OpenAPI investigation).
                if (string.IsNullOrEmpty(_settings.Get("default_currency_id")))
                {
                    var code = cashboxes[0].CurrencyCode?.ToUpperInvariant();
                    var id   = code == "USD" ? "2" : "1";   // default UZS=1
                    _settings.Set("default_currency_id", id);
                }
            }
        }

        // Always fetch and cache price lists so the Add Product form can work offline.
        var lists = await _api.GetPriceListsAsync();
        if (lists.Count > 0)
        {
            _priceLists.UpsertRange(lists.Select(l => new PriceList
            {
                Id         = l.Id,
                Name       = l.Name,
                Currency   = l.Currency ?? "",
                CurrencyId = l.Currency?.ToUpperInvariant() == "USD" ? 2L : 1L,
                Active     = l.Active
            }));

            // Always keep default_price_list_id in sync with the first active price list.
            var firstActive = lists.FirstOrDefault(l => l.Active) ?? lists.FirstOrDefault();
            if (firstActive is not null)
                _settings.Set("default_price_list_id", firstActive.Id.ToString());
        }

        // Always fetch and cache product types so the Add Product form can work offline.
        var types = await _api.GetProductTypesAsync();
        if (types.Count > 0)
            _productTypes.UpsertRange(types.Select(t => new ProductType
            {
                Id     = t.Id,
                Name   = t.Name,
                Active = t.Active
            }));
    }

    // ── Products ──────────────────────────────────────────────────────────────

    private async Task SyncProductsAsync()
    {
        var dtos = await _api.GetProductsAsync();
        if (dtos.Count == 0) return;

        // Always update priceListId + currencyId from product prices — these are the
        // definitive values that must be sent with every order.
        var firstPrice = dtos.SelectMany(p => p.Prices).FirstOrDefault();
        if (firstPrice is not null)
        {
            _settings.Set("default_price_list_id", firstPrice.PriceListId.ToString());
            _settings.Set("default_currency_id",   firstPrice.CashCurrency.ToString());
        }

        // Resolve the active price list so we pick the right CashPrice per product.
        long.TryParse(_settings.Get("default_price_list_id"), out var activePriceListId);

        _products.UpsertRange(dtos.Select(dto =>
        {
            // Prefer the price from the matching price list; fall back to dto.Price, then first price.
            var matchedPrice = activePriceListId > 0
                ? dto.Prices.FirstOrDefault(p => p.PriceListId == activePriceListId)
                : null;
            var resolvedPrice = matchedPrice?.CashPrice
                ?? (dto.Price > 0 ? dto.Price : dto.Prices.FirstOrDefault()?.CashPrice ?? 0);

            return new Product
            {
                RemoteUuid   = dto.Uuid,
                Name         = dto.Name,
                Code         = "",
                Barcode      = dto.Barcodes.FirstOrDefault()?.Barcode ?? "",
                Price        = resolvedPrice,
                CostPrice    = dto.Cost,
                CategoryName = "",
                Unit         = dto.MeasurementShortName ?? dto.MeasurementName ?? "dona",
                Stock        = dto.Stock,
                IsActive     = dto.IsPos && !dto.IsDelete,
                UpdatedAt    = dto.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow
            };
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
            UpdatedAt  = dto.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow
        }));

        _customers.DeleteLocalOnly();
    }

    // ── Pending sales ─────────────────────────────────────────────────────────

    // force == true  → manual/initial sync: ignore backoff, retry every sale.
    // force == false → timer sync: previously-failed sales honour the backoff
    //                  window so a stuck sale doesn't hit the server every cycle.
    // Per-sale failures are recorded on the Sale row (attempts/error/timestamp);
    // a summary of permanently-failing sales is appended to `errors` for the UI.
    private async Task SyncPendingSalesAsync(bool force, List<string> errors)
    {
        var branchUuid  = _settings.Get("default_branch_uuid");
        var cashboxUuid = _settings.Get("default_cashbox_uuid");

        // Use the price list repository as the source of truth — it is always populated
        // from the real backend data and never contains a stale cached integer.
        var activePriceList = _priceLists.GetAll().FirstOrDefault(p => p.Active)
                              ?? _priceLists.GetAll().FirstOrDefault();

        if (activePriceList is null ||
            string.IsNullOrEmpty(branchUuid) || string.IsNullOrEmpty(cashboxUuid))
        {
            PendingSalesCount = _sales.GetPendingCount();
            return;
        }

        var priceListId = activePriceList.Id;
        var currencyId  = activePriceList.CurrencyId;

        var failedSales = new List<string>();   // per-sale context for the UI
        var permanentFailures = 0;               // count of server-rejected sales

        foreach (var sale in _sales.GetPendingSync())
        {
            // Sales that contain only local seed-data products (no server UUID) can
            // never reach the backend — record a clear reason and leave them pending.
            if (!sale.Items.Any(i => !string.IsNullOrEmpty(i.ProductRemoteUuid)))
            {
                // Record the reason once; don't inflate SyncAttempts every cycle.
                if (string.IsNullOrEmpty(sale.LastSyncError))
                    _sales.RecordSyncFailure(sale.LocalId,
                        "Mahsulot serverda mavjud emas (lokal savdo)");
                continue;
            }

            // Backoff BEFORE sending (timer syncs only): a sale that already failed
            // waits out its window so it isn't re-POSTed to the server every cycle.
            if (!force && sale.SyncAttempts > 0 && IsWithinBackoff(sale))
            {
                if (!string.IsNullOrEmpty(sale.LastSyncError))
                    failedSales.Add(SaleErrorLine(sale.LocalId, sale.LastSyncError));
                continue;
            }

            try
            {
                var serverUuid = await _api.SyncSaleAsync(
                    sale, branchUuid!, cashboxUuid!, priceListId, currencyId);
                _sales.MarkSynced(sale.LocalId, serverUuid);
            }
            catch (Exception ex)
            {
                // NEVER mark synced, NEVER delete — record the attempt and move on.
                _sales.RecordSyncFailure(sale.LocalId, ex.Message);
                failedSales.Add(SaleErrorLine(sale.LocalId, ex.Message));
                if (IsPermanentFailure(ex)) permanentFailures++;
            }
        }

        PendingSalesCount = _sales.GetPendingCount();

        if (failedSales.Count > 0)
        {
            // Surface actionable per-sale context plus a permanent-failure count.
            if (permanentFailures > 0)
                errors.Add($"{permanentFailures} ta savdo sinxronlanmadi");
            errors.AddRange(failedSales.Take(5));
        }
    }

    // PERMANENT: server rejected the payload (4xx) — except auth/throttle/timeout
    // codes which are transient. TRANSIENT (return false): network errors, 5xx,
    // timeouts, 401, 408, 429, and any HttpRequestException without a StatusCode.
    private static bool IsPermanentFailure(Exception ex)
    {
        if (ex is HttpRequestException http && http.StatusCode is { } code)
        {
            var n = (int)code;
            if (n is 401 or 408 or 429) return false;   // transient
            return n is >= 400 and < 500;                // other 4xx = permanent
        }
        return false;   // network/timeout/unknown → transient, always retry
    }

    // Skip a previously-failed sale on timer syncs while it is still inside its
    // backoff window: min(SyncAttempts * 5 min, 60 min) since the last attempt.
    // Manual syncs (force) bypass this entirely, so no sale is ever stranded.
    private static bool IsWithinBackoff(Sale sale)
    {
        if (sale.LastSyncAttemptAt is not { } last) return false;
        var window = TimeSpan.FromMinutes(Math.Min(sale.SyncAttempts * 5, 60));
        return DateTime.UtcNow - last.ToUniversalTime() < window;
    }

    private static string SaleErrorLine(string localId, string message)
    {
        var shortId = localId.Length >= 8 ? localId[..8] : localId;
        return $"#{shortId}: {message}";
    }

    private void SetStatus(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
