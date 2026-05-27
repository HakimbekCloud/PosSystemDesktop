using System.Net.Http;
using System.Net.NetworkInformation;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public enum SyncStatus { Idle, Syncing, Success, Error }

// Outcome of the per-tenant bootstrap sync that runs immediately after login,
// before navigating to POS. The login screen uses this to decide whether the
// cashier is allowed into POS, and whether the experience is fresh or cached.
public enum BootstrapOutcome { Success, FailedButCached, FailedNoCache }

public class SyncService
{
    private readonly ApiClient            _api;
    private readonly ProductRepository   _products;
    private readonly CustomerRepository  _customers;
    private readonly SaleRepository      _sales;
    private readonly ConnectivityService _connectivity;
    private readonly SettingsRepository  _settings;
    private readonly GlobalSettingsRepository _globalSettings;
    private readonly PriceListRepository   _priceLists;
    private readonly ProductTypeRepository _productTypes;
    private readonly System.Timers.Timer   _timer;
    private readonly SemaphoreSlim         _syncGate = new(1, 1);
    private NetworkAvailabilityChangedEventHandler? _networkHandler;
    private CancellationTokenSource? _connectivityProbeCts;

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
        GlobalSettingsRepository globalSettings,
        PriceListRepository priceLists,
        ProductTypeRepository productTypes)
    {
        _api          = api;
        _products     = products;
        _customers    = customers;
        _sales        = sales;
        _connectivity = connectivity;
        _settings     = settings;
        _globalSettings = globalSettings;
        _priceLists   = priceLists;
        _productTypes = productTypes;

        _timer = new System.Timers.Timer(300_000); // every 5 min
        _timer.Elapsed += async (_, _) => await TrySyncAsync();
        _timer.AutoReset = true;
    }

    public bool IsBackgroundRunning => _timer.Enabled;

    public void StartBackgroundSync()
    {
        _timer.Start();

        // Listen for adapter-up transitions and trigger an immediate sync
        // when the API becomes reachable again. Subscription happens here so
        // we don't fire syncs while the user is still on the login screen.
        if (_networkHandler is null)
        {
            _networkHandler = OnNetworkAvailabilityChanged;
            NetworkChange.NetworkAvailabilityChanged += _networkHandler;
        }
    }

    public void StopBackgroundSync()
    {
        _timer.Stop();
        if (_networkHandler is not null)
        {
            NetworkChange.NetworkAvailabilityChanged -= _networkHandler;
            _networkHandler = null;
        }
        _connectivityProbeCts?.Cancel();
        _connectivityProbeCts = null;
    }

    private bool _isPaused;

    // Drains background sync and holds the sync gate so no further work can
    // start until Resume is called. Intended for use by TenantScopeService
    // around a path-provider switch. Pair every PauseAsync with exactly one
    // Resume — typically via try/finally on the caller.
    public async Task PauseAsync()
    {
        if (_isPaused) return;
        StopBackgroundSync();
        await _syncGate.WaitAsync();
        _isPaused = true;
    }

    public void Resume(bool restartBackground)
    {
        if (!_isPaused) return;
        _isPaused = false;
        _syncGate.Release();
        if (restartBackground) StartBackgroundSync();
    }

    // Fires on a thread-pool thread when the OS reports an adapter state change.
    // We only act on "up" events. Each event cancels any pending probe and
    // schedules a fresh one ~3s later so a flickering connection coalesces into
    // a single probe + sync attempt instead of a burst of duplicate requests.
    private void OnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (!e.IsAvailable) return;

        _connectivityProbeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _connectivityProbeCts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                // Settle window: lets DHCP / DNS / VPN finish handshakes before
                // we issue the reachability probe. Any new event resets this.
                await Task.Delay(TimeSpan.FromSeconds(3), cts.Token);

                var baseUrl = _globalSettings.Get("api_base_url") ?? _settings.Get("api_base_url") ?? "";
                if (string.IsNullOrEmpty(baseUrl)) return;
                if (!await _connectivity.IsApiReachableAsync(baseUrl)) return;

                // Reuses _syncGate via TrySyncAsync — guaranteed no overlap with
                // the 5-min timer or a manual sync that might be in flight.
                // Pending-sale retries still honor NextRetryAt because the push
                // loop uses GetReadyToPushForTenant(tenant, now).
                await TrySyncAsync();
            }
            catch (OperationCanceledException) { /* superseded by a newer event */ }
            catch { /* swallow — next event or timer tick will retry */ }
        });
    }

    public Task InitialSyncAsync() => SyncAllAsync();

    public async Task TrySyncAsync()
    {
        if (!_connectivity.IsOnline) return;

        // Drop overlapping background ticks instead of queueing them.
        if (!await _syncGate.WaitAsync(0)) return;
        try { await RunSyncAsync(); }
        finally { _syncGate.Release(); }
    }

    public async Task SyncAllAsync()
    {
        if (!_connectivity.IsOnline) { SetStatus(SyncStatus.Idle); return; }

        // Manual / initial sync waits for any in-flight background sync to finish.
        await _syncGate.WaitAsync();
        try { await RunSyncAsync(); }
        finally { _syncGate.Release(); }
    }

    // Operator-initiated: clears IsPoison + backoff bookkeeping on every stuck
    // sale for the current tenant, then triggers an immediate sync attempt.
    // Bound to the toolbar "Hammasini qayta urinish" button.
    //
    // The sync runs unconditionally even when the reset touched zero rows —
    // the operator clicked a "retry now" action and deserves immediate
    // feedback in the toolbar status pill regardless of whether anything was
    // technically stuck. Without that, a click that resets zero rows produces
    // no UI change and looks broken.
    //
    // Returns the number of sales whose retry-bookkeeping was reset.
    public async Task<int> RequeuePoisonSalesAsync()
    {
        var tenant = _settings.Get("tenant_subdomain") ?? "";
        if (string.IsNullOrEmpty(tenant)) return 0;
        var count = _sales.RequeueAllPoisonForTenant(tenant);
        await SyncAllAsync();
        return count;
    }

    // ── Bootstrap (post-login gate) ──────────────────────────────────────────
    //
    // A tenant is considered usable offline only if a previous bootstrap for
    // that tenant succeeded AND the local cache still has products.
    public bool HasUsableCacheFor(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain)) return false;
        if (string.IsNullOrEmpty(_settings.Get($"bootstrap_completed_at:{tenantSubdomain}"))) return false;
        return _products.GetAll().Count > 0;
    }

    // Runs once right after login, before POS is opened. Verifies the API is
    // actually reachable (not just that the network adapter is up), pulls all
    // reference + catalog data into local SQLite, and records a per-tenant
    // bootstrap marker so future offline logins can be allowed.
    public async Task<BootstrapOutcome> BootstrapAsync(string tenantSubdomain)
    {
        if (string.IsNullOrEmpty(tenantSubdomain))
            return BootstrapOutcome.FailedNoCache;

        var baseUrl = _globalSettings.Get("api_base_url")
                      ?? _settings.Get("api_base_url")
                      ?? "https://shefpos.uz";

        // Cheap negative signal first; then real /health probe.
        var reachable = _connectivity.IsOnline
                        && await _connectivity.IsApiReachableAsync(baseUrl);

        if (!reachable)
            return HasUsableCacheFor(tenantSubdomain)
                ? BootstrapOutcome.FailedButCached
                : BootstrapOutcome.FailedNoCache;

        await SyncAllAsync();

        if (Status == SyncStatus.Success)
        {
            _settings.Set(
                $"bootstrap_completed_at:{tenantSubdomain}",
                DateTime.UtcNow.ToString("O"));
            return BootstrapOutcome.Success;
        }

        return HasUsableCacheFor(tenantSubdomain)
            ? BootstrapOutcome.FailedButCached
            : BootstrapOutcome.FailedNoCache;
    }

    private async Task RunSyncAsync()
    {
        // No auth token → almost certainly an offline-only session (cache login).
        // Hitting the API would return 401 → force-logout, which would bounce a
        // cashier out of an offline shift unnecessarily. Stay silent until they
        // manually re-login when internet is back.
        if (string.IsNullOrEmpty(_settings.GetDecrypted("auth_token")))
        {
            SetStatus(SyncStatus.Idle);
            return;
        }

        LastError   = "";
        LastErrors  = [];
        SetStatus(SyncStatus.Syncing);

        var errors = new List<string>();

        try { await SyncReferenceDataAsync(); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { errors.Add("Sessiya muddati tugagan"); }
        catch (Exception ex) { errors.Add($"Ma'lumotnoma: {ex.Message}"); }

        // Sales first so the backend stock is already decremented when we fetch products
        try { await SyncPendingSalesAsync(); }
        catch (Exception ex) { errors.Add($"Zakazlar: {ex.Message}"); }

        try { await SyncProductsAsync(); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { errors.Add("Sessiya muddati tugagan"); }
        catch (Exception ex) { errors.Add($"Mahsulotlar: {ex.Message}"); }

        try { await SyncCustomersAsync(); }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
        { errors.Add("Sessiya muddati tugagan"); }
        catch (Exception ex) { errors.Add($"Mijozlar: {ex.Message}"); }

        LastSyncAt = DateTime.Now;
        _settings.Set("last_sync_at", LastSyncAt.Value.ToString("O"));
        PendingSalesCount = _sales.GetPendingCountForTenant(_settings.Get("tenant_subdomain") ?? "");

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

    // ── Reference data (branch / cashbox / price list / currency) ──────────────

    private async Task SyncReferenceDataAsync()
    {
        if (string.IsNullOrEmpty(_settings.Get("default_branch_uuid")))
        {
            var branches = await _api.GetBranchesAsync();
            if (branches.Count > 0)
                _settings.Set("default_branch_uuid", branches[0].Uuid);
        }

        // Always refetch cashboxes so per-type UUIDs (CASH/CARD/BANK) stay in sync.
        // Mixed-payment serialization routes each transaction to the cashbox of its
        // matching type; without these mappings the breakdown can't survive the wire.
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

            // Per-type cashbox UUIDs. Pick the first cashbox of each type;
            // empty when no cashbox of that type exists (falls back to default).
            string PickUuid(string type) =>
                cashboxes.FirstOrDefault(c =>
                    string.Equals(c.Type, type, StringComparison.OrdinalIgnoreCase))?.Uuid ?? "";

            _settings.Set("cashbox_uuid_cash", PickUuid("CASH"));
            _settings.Set("cashbox_uuid_card", PickUuid("CARD"));
            _settings.Set("cashbox_uuid_bank", PickUuid("BANK"));
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
        var tenant = _settings.Get("tenant_subdomain") ?? "";
        if (string.IsNullOrEmpty(tenant)) return; // tenant guard mirrors push path

        var since = _settings.Get($"last_product_sync_at:{tenant}");
        var dtos = await _api.GetProductsAsync(since);

        // Stock-reconcile marker: any sale this tenant has already pushed
        // (Synced=true, SyncedAt < now) is reflected in the backend stock
        // returned (or unchanged because the backend already had the post-sale
        // figure). The POS overlay drops those sales after this marker advances,
        // closing the "synced-but-not-yet-pulled" rebound window.
        var reconcileAt = DateTime.UtcNow;

        if (dtos.Count == 0)
        {
            _settings.Set($"last_stock_reconcile_at:{tenant}", reconcileAt.ToString("O"));
            return;
        }

        // Update priceListId + currencyId from product prices — definitive values for orders.
        var firstPrice = dtos.SelectMany(p => p.Prices).FirstOrDefault();
        if (firstPrice is not null)
        {
            _settings.Set("default_price_list_id", firstPrice.PriceListId.ToString());
            _settings.Set("default_currency_id",   firstPrice.CashCurrency.ToString());
        }

        // Resolve the active price list so we pick the right CashPrice per product.
        long.TryParse(_settings.Get("default_price_list_id"), out var activePriceListId);

        DateTime maxUpdatedAt = ParseWatermark(since);

        _products.UpsertRange(dtos.Select(dto =>
        {
            var matchedPrice = activePriceListId > 0
                ? dto.Prices.FirstOrDefault(p => p.PriceListId == activePriceListId)
                : null;
            var resolvedPrice = matchedPrice?.CashPrice
                ?? (dto.Price > 0 ? dto.Price : dto.Prices.FirstOrDefault()?.CashPrice ?? 0);

            var dtoUpdated = dto.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow;
            if (dtoUpdated > maxUpdatedAt) maxUpdatedAt = dtoUpdated;

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
                UpdatedAt    = dtoUpdated
            };
        }));

        // Advance the per-tenant watermark only after a successful page-set ingest.
        // (Exceptions from UpsertRange would propagate before this line and the
        // next sync would retry from the unchanged watermark.)
        _settings.Set($"last_product_sync_at:{tenant}", maxUpdatedAt.ToString("O"));
        _settings.Set($"last_stock_reconcile_at:{tenant}", reconcileAt.ToString("O"));
    }

    // ── Customers ─────────────────────────────────────────────────────────────

    private async Task SyncCustomersAsync()
    {
        var tenant = _settings.Get("tenant_subdomain") ?? "";
        if (string.IsNullOrEmpty(tenant)) return;

        var since = _settings.Get($"last_customer_sync_at:{tenant}");
        var dtos = await _api.GetCustomersAsync(since);
        if (dtos.Count == 0) return;

        DateTime maxUpdatedAt = ParseWatermark(since);

        _customers.UpsertRange(dtos.Select(dto =>
        {
            var dtoUpdated = dto.UpdatedAt?.ToUniversalTime() ?? DateTime.UtcNow;
            if (dtoUpdated > maxUpdatedAt) maxUpdatedAt = dtoUpdated;

            return new Customer
            {
                RemoteUuid = dto.Uuid,
                Name       = dto.Name,
                Phone      = dto.Phone   ?? "",
                Address    = dto.Address ?? "",
                Balance    = dto.TotalDebt,
                IsActive   = dto.Active,
                UpdatedAt  = dtoUpdated
            };
        }));

        _settings.Set($"last_customer_sync_at:{tenant}", maxUpdatedAt.ToString("O"));
    }

    // RoundtripKind cannot be combined with AssumeUniversal / AssumeLocal /
    // AdjustToUniversal — DateTime.TryParse throws ArgumentException at runtime.
    // AssumeUniversal + AdjustToUniversal gives the same end-result (Kind=Utc
    // regardless of whether the input string has a Z / offset) without that
    // illegal pairing. ToUniversalTime() afterwards becomes a no-op for Utc.
    private static DateTime ParseWatermark(string? iso) =>
        DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal |
            System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var dt) ? dt : DateTime.MinValue.ToUniversalTime();

    // ── Pending sales ─────────────────────────────────────────────────────────

    private async Task SyncPendingSalesAsync()
    {
        var tenant      = _settings.Get("tenant_subdomain") ?? "";
        var branchUuid  = _settings.Get("default_branch_uuid");
        var cashboxUuid = _settings.Get("default_cashbox_uuid");

        // Tenant guard: refuse to push anything if we don't know the current tenant,
        // or if the row's TenantSubdomain doesn't match. This is what prevents a
        // Tenant-A offline sale from leaking to Tenant B after a re-login.
        if (string.IsNullOrEmpty(tenant))
        {
            PendingSalesCount = _sales.GetPendingCount();
            return;
        }

        // Use the price list repository as the source of truth — it is always populated
        // from the real backend data and never contains a stale cached integer.
        var activePriceList = _priceLists.GetAll().FirstOrDefault(p => p.Active)
                              ?? _priceLists.GetAll().FirstOrDefault();

        if (activePriceList is null ||
            string.IsNullOrEmpty(branchUuid) || string.IsNullOrEmpty(cashboxUuid))
        {
            PendingSalesCount = _sales.GetPendingCountForTenant(tenant);
            return;
        }

        var priceListId = activePriceList.Id;
        var currencyId  = activePriceList.CurrencyId;

        var saleErrors = new List<string>();
        var now = DateTime.UtcNow;

        foreach (var sale in _sales.GetReadyToPushForTenant(tenant, now))
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
            catch (Exception ex)
            {
                // Cashier-readable identifier so the operator can correlate the
                // error string with the row in the failed-sale list.
                var saleTag = $"#{sale.LocalId[..Math.Min(8, sale.LocalId.Length)]} {sale.TotalAmount:N0}";

                if (IsPermanentFailure(ex))
                {
                    _sales.MarkPoison(sale.LocalId, ex.Message);
                    saleErrors.Add($"{saleTag} [Bloklandi] {ex.Message}");
                }
                else
                {
                    var attempt = sale.RetryCount + 1;
                    if (attempt > BackoffSeconds.Length)
                    {
                        _sales.MarkPoison(sale.LocalId, ex.Message);
                        saleErrors.Add($"{saleTag} [Bloklandi #{attempt}] {ex.Message}");
                    }
                    else
                    {
                        var delay = TimeSpan.FromSeconds(BackoffSeconds[attempt - 1]);
                        _sales.MarkRetryFailure(sale.LocalId, attempt, now.Add(delay), ex.Message);
                        saleErrors.Add($"{saleTag} [Qayta urinish #{attempt} ~{delay.TotalMinutes:F0}m] {ex.Message}");
                    }
                }
            }
        }

        PendingSalesCount = _sales.GetPendingCountForTenant(tenant);

        if (saleErrors.Count > 0)
            throw new InvalidOperationException(
                $"{saleErrors.Count} ta zakaz yuborilmadi: {string.Join("; ", saleErrors.Take(2))}");
    }

    // Retry schedule, aligned with the 5-minute background timer so each entry
    // corresponds to at least one tick of natural sync activity. Past the last
    // bucket, the sale is quarantined as poison.
    private static readonly int[] BackoffSeconds =
    {
         5 * 60,   // 1st retry: ~5  min
        15 * 60,   // 2nd retry: ~15 min
        30 * 60,   // 3rd retry: ~30 min
        60 * 60,   // 4th retry:  1 h
       120 * 60,   // 5th retry:  2 h
       240 * 60    // 6th retry:  4 h  → next failure marks poison
    };

    // Permanent failures: misconfigured local payment data or server-rejected
    // request shape. These will keep failing identically until something changes
    // outside this app (operator fixes config or admin updates backend state),
    // so we quarantine immediately instead of burning retries.
    private static bool IsPermanentFailure(Exception ex)
    {
        if (ex is InvalidOperationException) return true; // BuildSaleTransactions validators

        if (ex is HttpRequestException http && http.StatusCode is System.Net.HttpStatusCode status)
        {
            int code = (int)status;
            if (code < 400 || code >= 500) return false;       // 5xx → transient
            if (status == System.Net.HttpStatusCode.Unauthorized)    return false; // token refresh path
            if (status == System.Net.HttpStatusCode.RequestTimeout)  return false; // 408 → transient
            if (status == System.Net.HttpStatusCode.TooManyRequests) return false; // 429 → transient
            return true; // 400, 404, 409, 422, …
        }

        return false;
    }

    private void SetStatus(SyncStatus status)
    {
        Status = status;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }
}
