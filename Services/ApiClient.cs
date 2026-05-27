using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using PosSystem.Core.DTOs;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly SettingsRepository _settings;
    private readonly GlobalSettingsRepository _globalSettings;
    private const string DefaultBaseUrl = "https://shefpos.uz";

    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        // System.Text.Json's built-in DateTime reader rejects ISO-8601 with
        // more than 7 fractional-second digits — Jackson on the backend can
        // emit 9 (nanoseconds from java.time.Instant / LocalDateTime). The
        // converters below normalize all timestamps to UTC DateTime and tolerate
        // the extra precision, so product / customer / shift DTOs deserialize
        // regardless of which timestamp shape the backend produces.
        Converters =
        {
            new UtcDateTimeJsonConverter(),
            new NullableUtcDateTimeJsonConverter(),
        }
    };

    public ApiClient(HttpClient http, SettingsRepository settings, GlobalSettingsRepository globalSettings)
    {
        _http = http;
        _settings = settings;
        _globalSettings = globalSettings;
        ApplyBaseUrl();
        ApplyTenantHeader();
        ApplyAuthToken();
    }

    public void ApplyBaseUrl()
    {
        var url = _globalSettings.Get("api_base_url") ?? _settings.Get("api_base_url");
        if (string.IsNullOrWhiteSpace(url))
            url = DefaultBaseUrl;

        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri)) return;
        if (_http.BaseAddress == uri) return;
        _http.BaseAddress = uri;
    }

    public void ApplyTenantHeader()
    {
        _http.DefaultRequestHeaders.Remove("X-Tenant-ID");
        var tenant = _settings.Get("tenant_subdomain");
        if (!string.IsNullOrEmpty(tenant))
            _http.DefaultRequestHeaders.Add("X-Tenant-ID", tenant);
    }

    public void ApplyAuthToken()
    {
        var token = _settings.GetDecrypted("auth_token");
        _http.DefaultRequestHeaders.Authorization = string.IsNullOrEmpty(token)
            ? null
            : new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/v1/auth/login", new { username, password });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Measurements ──────────────────────────────────────────────────────────

    public async Task<List<MeasurementDto>> GetMeasurementsAsync()
    {
        var resp = await GetWithRefreshAsync("api/measurements?page=0&size=200");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<MeasurementDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    // ── Products ──────────────────────────────────────────────────────────────

    // updatedAfter: ISO-8601 UTC timestamp. When non-null, server returns only rows
    // with updatedAt > updatedAfter AND includes soft-deleted/disabled rows
    // (tombstones) so the client can mirror remote state. is_pos filter is dropped
    // in incremental mode for the same reason.
    public async Task<List<ProductDto>> GetProductsAsync(string? updatedAfter = null)
    {
        var all = new List<ProductDto>();
        int page = 0;
        int totalPages;

        string baseQuery = string.IsNullOrEmpty(updatedAfter)
            ? "api/products?is_pos=true"
            : $"api/products?updatedAfter={Uri.EscapeDataString(updatedAfter)}";

        do
        {
            var resp = await GetWithRefreshAsync($"{baseQuery}&page={page}&size=200");
            await EnsureSuccessAsync(resp);
            var result = await resp.Content.ReadFromJsonAsync<PageResponse<ProductDto>>(JsonOptions);
            if (result is null) break;
            all.AddRange(result.Content);
            totalPages = result.TotalPages;
            page++;
        }
        while (page < totalPages && page < 10);

        return all;
    }

    public async Task<ProductDto> CreateProductAsync(CreateProductRequest request)
    {
        var resp = await PostWithRefreshAsync("api/products", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ProductDto>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Customers ─────────────────────────────────────────────────────────────

    public async Task<List<CustomerDto>> GetCustomersAsync(string? updatedAfter = null)
    {
        var all = new List<CustomerDto>();
        int page = 0;
        int totalPages;

        string baseQuery = string.IsNullOrEmpty(updatedAfter)
            ? "api/customers?"
            : $"api/customers?updatedAfter={Uri.EscapeDataString(updatedAfter)}&";

        do
        {
            var resp = await GetWithRefreshAsync($"{baseQuery}page={page}&size=200");
            await EnsureSuccessAsync(resp);
            var result = await resp.Content.ReadFromJsonAsync<PageResponse<CustomerDto>>(JsonOptions);
            if (result is null) break;
            all.AddRange(result.Content);
            totalPages = result.TotalPages;
            page++;
        }
        while (page < totalPages && page < 10);

        return all;
    }

    public async Task<CustomerDto> CreateCustomerAsync(CreateCustomerRequest request)
    {
        var resp = await PostWithRefreshAsync("api/customers", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    public async Task<CustomerDto> UpdateCustomerAsync(string uuid, UpdateCustomerRequest request)
    {
        var resp = await PutWithRefreshAsync($"api/customers/{uuid}", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Reference data ────────────────────────────────────────────────────────

    public async Task<List<BranchDto>> GetBranchesAsync()
    {
        var resp = await GetWithRefreshAsync("api/branches?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<BranchDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<CashboxDto>> GetCashboxesAsync()
    {
        var resp = await GetWithRefreshAsync("api/cashboxes?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<CashboxDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<PriceListDto>> GetPriceListsAsync()
    {
        var resp = await GetWithRefreshAsync("api/price-lists?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<PriceListDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<ProductTypeDto>> GetProductTypesAsync()
    {
        var resp = await GetWithRefreshAsync("api/product-types?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<ProductTypeDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<WarehouseDto>> GetWarehousesAsync()
    {
        var resp = await GetWithRefreshAsync("api/warehouses?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<WarehouseDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    // ── POS Shift (Phase G.1) ─────────────────────────────────────────────────

    // GET /api/pos/shifts/current?cashboxUuid={uuid}
    // Backend returns 404 NO_OPEN_SHIFT when no OPEN shift exists for the
    // cashbox+tenant — that's an expected state for an idle drawer, so we
    // surface it to the caller as `null` instead of propagating it as an
    // HttpRequestException. Every other non-success status is still thrown
    // through EnsureSuccessAsync so the UI can show the parsed message.
    public async Task<PosShiftResponse?> GetCurrentShiftAsync(string cashboxUuid)
    {
        if (string.IsNullOrWhiteSpace(cashboxUuid))
            throw new ArgumentException("cashboxUuid is required", nameof(cashboxUuid));

        var resp = await GetWithRefreshAsync(
            $"api/pos/shifts/current?cashboxUuid={Uri.EscapeDataString(cashboxUuid)}");

        if (resp.StatusCode == HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PosShiftResponse>(JsonOptions);
    }

    public async Task<PosShiftResponse> OpenShiftAsync(OpenShiftRequest request)
    {
        var resp = await PostWithRefreshAsync("api/pos/shifts/open", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PosShiftResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    public async Task<PosShiftResponse> CloseShiftAsync(string shiftUuid, CloseShiftRequest request)
    {
        if (string.IsNullOrWhiteSpace(shiftUuid))
            throw new ArgumentException("shiftUuid is required", nameof(shiftUuid));

        var resp = await PostWithRefreshAsync($"api/pos/shifts/{shiftUuid}/close", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PosShiftResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    public async Task<PosShiftReportResponse> GetShiftReportAsync(string shiftUuid)
    {
        if (string.IsNullOrWhiteSpace(shiftUuid))
            throw new ArgumentException("shiftUuid is required", nameof(shiftUuid));

        var resp = await GetWithRefreshAsync($"api/pos/shifts/{shiftUuid}/report");
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<PosShiftReportResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Debt payment ──────────────────────────────────────────────────────────

    public async Task<CustomerDto> PayDebtAsync(DebtPaymentRequest request)
    {
        var resp = await PostWithRefreshAsync("api/debt/pay", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Sale sync ─────────────────────────────────────────────────────────────

    public async Task<string> SyncSaleAsync(
        Sale sale, string branchUuid, string cashboxUuid, long priceListId, long currencyId)
    {
        var paymentType = sale.PaymentType.ToUpperInvariant() switch
        {
            "CARD"               => "CARD",
            "BANK" or "TRANSFER" => "TRANSFER",
            "MIXED"              => "MIXED",
            _                    => "CASH"
        };

        // Only include items that have a server UUID, valid price and quantity.
        // API validation: quantity >= 0.001, price >= 0.01
        var validItems = sale.Items
            .Where(i => !string.IsNullOrEmpty(i.ProductRemoteUuid)
                        && i.Price    >= 0.01m
                        && i.Quantity >= 0.001m)
            .Select(i => new CreateOrderItemRequest
            {
                ProductUuid   = i.ProductRemoteUuid,
                Quantity      = i.Quantity,
                Price         = i.Price,
                DiscountPrice = Math.Max(0, i.Discount)
            }).ToList();

        if (validItems.Count == 0)
            throw new InvalidOperationException("Serverga yuborish uchun yaroqli mahsulotlar yo'q");

        // API invariant: sum(transactions.amount) must EXACTLY equal
        //                sum(item.price * item.quantity - item.discountPrice)
        var apiTotal = validItems.Sum(i => i.Price * i.Quantity - i.DiscountPrice);
        if (apiTotal <= 0)
            throw new InvalidOperationException("Zakaz summasi noldan katta bo'lishi kerak");

        var hasCustomer = !string.IsNullOrEmpty(sale.CustomerRemoteUuid);
        var transactions = BuildSaleTransactions(sale, apiTotal, hasCustomer, cashboxUuid, currencyId);

        var order = new CreateOrderRequest
        {
            BranchUuid   = branchUuid,
            CustomerUuid = hasCustomer ? sale.CustomerRemoteUuid : null,
            CurrencyId   = currencyId,
            PaymentType  = paymentType,
            IsPos        = true,
            DealType     = 0,
            DeliveryType = "SELF",
            PriceListId  = priceListId,
            Comment      = string.IsNullOrEmpty(sale.Note) ? null : sale.Note,
            Items        = validItems,
            Transactions = transactions
        };

        // Idempotency-Key uses the stable client-generated LocalId (GUID set at checkout).
        // Backend ignores it today; when it opts in, retried POSTs after a dropped response
        // will de-duplicate instead of creating ghost orders.
        var response = await PostWithRefreshAsync(
            "api/orders", order,
            extraHeaders: ("Idempotency-Key", sale.LocalId));
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        return result?.Uuid ?? "";
    }

    // Mixed-payment serialization.
    //
    // The cashier may split a sale across cash, card, bank and debt. Backend
    // distinguishes the methods by the routing cashbox (Cashbox.type), not by a
    // per-transaction enum, so each non-zero row is sent as its own transaction
    // pointing at the cashbox of the matching type. Card/Bank/Debt are recorded
    // as exact amounts; cash absorbs the change overpayment so the API invariant
    // sum(transactions.amount) == apiTotal holds without surprising the backend.
    //
    // Legacy sales created before the breakdown columns were added carry zero in
    // all four rows; in that case we fall back to the original single paid+debt
    // split using sale.PaidAmount.
    private List<CreateTransactionRequest> BuildSaleTransactions(
        Core.Entities.Sale sale, decimal apiTotal, bool hasCustomer,
        string defaultCashboxUuid, long currencyId)
    {
        var totalBreakdown = sale.CashAmount + sale.CardAmount + sale.BankAmount + sale.DebtAmount;
        var legacy = totalBreakdown <= 0m;

        decimal cashPortion, cardPortion, bankPortion, debtPortion;

        if (legacy)
        {
            // Pre-Phase-4 sale: only PaidAmount + customer-debt is known. The
            // default cashbox is the only legitimate use of fallback routing.
            var isFullyPaid = sale.PaidAmount >= sale.TotalAmount;
            cashPortion = isFullyPaid || !hasCustomer
                ? apiTotal
                : Math.Clamp(sale.PaidAmount, 0m, apiTotal);
            cardPortion = 0m;
            bankPortion = 0m;
            debtPortion = apiTotal - cashPortion;
        }
        else
        {
            // Breakdown-aware sale: exact preservation. Card/Bank/Debt may not be
            // silently clamped — that would mis-attribute money between methods.
            var nonCashSum = sale.CardAmount + sale.BankAmount + sale.DebtAmount;
            if (nonCashSum > apiTotal)
                throw new InvalidOperationException(
                    "Karta, bank va qarz yig'indisi sotuv summasidan oshib ketdi.");

            cardPortion = sale.CardAmount;
            bankPortion = sale.BankAmount;
            debtPortion = sale.DebtAmount;
            cashPortion = apiTotal - nonCashSum;

            if (sale.CashAmount + 0.0001m < cashPortion)
                // Cashier tendered less cash than the order actually needs.
                throw new InvalidOperationException("Naqd to'lov yetmaydi.");

            // Debt without a customer cannot be booked on the server — refuse to
            // silently fold debt into cash (that would silently lose receivables).
            if (debtPortion > 0 && !hasCustomer)
                throw new InvalidOperationException(
                    "Qarzga savdo qilish uchun mijoz tanlanishi kerak.");
        }

        // Required cashbox routing. Defaults only allowed for the cash row in
        // legacy mode; CARD and BANK require their explicit type-mapped UUIDs.
        var cashUuid = _settings.Get("cashbox_uuid_cash");
        if (string.IsNullOrEmpty(cashUuid)) cashUuid = defaultCashboxUuid;
        var cardUuid = _settings.Get("cashbox_uuid_card") ?? "";
        var bankUuid = _settings.Get("cashbox_uuid_bank") ?? "";

        if (cashPortion > 0 && string.IsNullOrEmpty(cashUuid))
            throw new InvalidOperationException("Naqd to'lov uchun CASH turidagi kassa sozlanmagan.");
        if (cardPortion > 0 && string.IsNullOrEmpty(cardUuid))
            throw new InvalidOperationException("Karta to'lovi uchun CARD turidagi kassa sozlanmagan.");
        if (bankPortion > 0 && string.IsNullOrEmpty(bankUuid))
            throw new InvalidOperationException("Bank/transfer to'lovi uchun BANK turidagi kassa sozlanmagan.");
        if (debtPortion > 0 && string.IsNullOrEmpty(defaultCashboxUuid))
            throw new InvalidOperationException("Qarz to'lovi uchun kassa sozlanmagan.");

        var txs = new List<CreateTransactionRequest>(4);
        if (cashPortion > 0)
            txs.Add(new CreateTransactionRequest { CashboxUuid = cashUuid, Amount = cashPortion, CurrencyId = currencyId, IsDebt = false, IsCashback = false });
        if (cardPortion > 0)
            txs.Add(new CreateTransactionRequest { CashboxUuid = cardUuid, Amount = cardPortion, CurrencyId = currencyId, IsDebt = false, IsCashback = false });
        if (bankPortion > 0)
            txs.Add(new CreateTransactionRequest { CashboxUuid = bankUuid, Amount = bankPortion, CurrencyId = currencyId, IsDebt = false, IsCashback = false });
        if (debtPortion > 0)
            txs.Add(new CreateTransactionRequest { CashboxUuid = defaultCashboxUuid, Amount = debtPortion, CurrencyId = currencyId, IsDebt = true, IsCashback = false });

        return txs;
    }

    // ── Token refresh ─────────────────────────────────────────────────────────

    private async Task<bool> TryRefreshTokenAsync()
    {
        await _refreshSemaphore.WaitAsync();
        try
        {
            var refreshToken = _settings.GetDecrypted("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                WeakReferenceMessenger.Default.Send(new SessionExpiredMessage());
                return false;
            }

            var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/refresh");
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", refreshToken);

            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Refresh token itself is expired or revoked — force re-login.
                WeakReferenceMessenger.Default.Send(new SessionExpiredMessage());
                return false;
            }

            if (!resp.IsSuccessStatusCode)
                return false; // Network/server error — don't force re-login

            var result = await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (result is null) return false;

            _settings.SetEncrypted("auth_token", result.AccessToken);
            _settings.SetEncrypted("refresh_token", result.RefreshToken);
            ApplyAuthToken();
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            _refreshSemaphore.Release();
        }
    }

    // ── Private HTTP helpers ──────────────────────────────────────────────────

    private async Task<HttpResponseMessage> GetWithRefreshAsync(string url)
    {
        var resp = await _http.GetAsync(url);
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.GetAsync(url);
        }
        return resp;
    }

    private async Task<HttpResponseMessage> PostWithRefreshAsync<T>(
        string url, T body, (string Name, string Value)? extraHeaders = null)
    {
        var resp = await _http.SendAsync(BuildPost(url, body, extraHeaders));
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.SendAsync(BuildPost(url, body, extraHeaders));
        }
        return resp;
    }

    private async Task<HttpResponseMessage> PutWithRefreshAsync<T>(string url, T body)
    {
        var resp = await _http.SendAsync(BuildPut(url, body));
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.SendAsync(BuildPut(url, body));
        }
        return resp;
    }

    private static HttpRequestMessage BuildPost<T>(
        string url, T body, (string Name, string Value)? extraHeaders = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        if (extraHeaders is { Name: { Length: > 0 } name, Value: { Length: > 0 } value })
            req.Headers.TryAddWithoutValidation(name, value);
        return req;
    }

    private static HttpRequestMessage BuildPut<T>(string url, T body) =>
        new(HttpMethod.Put, url) { Content = JsonContent.Create(body) };

    // ── Error handling ────────────────────────────────────────────────────────

    private static async Task EnsureSuccessAsync(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;

        string body = "";
        try { body = await response.Content.ReadAsStringAsync(); } catch { }

        var message = ParseErrorMessage(body, response.StatusCode);
        throw new HttpRequestException(message, null, response.StatusCode);
    }

    private static string ParseErrorMessage(string body, HttpStatusCode status)
    {
        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var doc  = JsonDocument.Parse(body);
                var root = doc.RootElement;
                foreach (var field in new[] { "message", "error", "detail", "title" })
                {
                    if (root.TryGetProperty(field, out var prop)
                        && prop.ValueKind == JsonValueKind.String
                        && prop.GetString() is { Length: > 0 } val)
                        return val;
                }
            }
            catch { }
        }

        return status switch
        {
            HttpStatusCode.Unauthorized        => "Sessiya muddati tugagan, qayta kiring",
            HttpStatusCode.Forbidden           => "Bu amalni bajarish uchun ruxsat yo'q",
            HttpStatusCode.NotFound            => "Ma'lumot topilmadi",
            HttpStatusCode.BadRequest          => "Noto'g'ri so'rov ma'lumotlari",
            HttpStatusCode.UnprocessableEntity => "Ma'lumotlar noto'g'ri formatda",
            HttpStatusCode.InternalServerError => "Server ichki xatosi yuz berdi",
            HttpStatusCode.ServiceUnavailable  => "Server hozircha mavjud emas",
            HttpStatusCode.GatewayTimeout or
            HttpStatusCode.RequestTimeout      => "Server javob bermadi (timeout)",
            _ => $"So'rov xatosi ({(int)status})"
        };
    }
}
