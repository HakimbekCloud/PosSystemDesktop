using System.IO;
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
        // Tenant + auth headers are stamped per-request by TenantAuthHeaderHandler
        // (Bug H1). No DefaultRequestHeaders mutation here.
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

    // Bug H1: tenant + auth headers are stamped per-request by
    // TenantAuthHeaderHandler reading the settings store on each send. The old
    // ApplyTenantHeader()/ApplyAuthToken() no-op stubs (L2) and their call sites
    // have been removed — the handler reflects any settings change on the next
    // request, so there is nothing to "apply".

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        var response = await _http.PostAsJsonAsync("api/v1/auth/login", new { username, password });
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // Asks the backend to revoke both the access token and the refresh token
    // for this session. Called by AuthService.Logout, which captures the tokens
    // SYNCHRONOUSLY before clearing local storage and passes them in — the
    // tokens are NOT read from settings here, because by the time this runs on
    // the thread-pool ClearUserData has already wiped them (C1: the old
    // settings-read lost the token race, so revocation never carried a valid
    // token and was effectively dead code).
    //
    // The explicit Authorization header is honoured end-to-end:
    // TenantAuthHeaderHandler only stamps a Bearer when the request has none,
    // so the captured access token survives even though settings are now empty.
    //
    // Failures are swallowed — a network error during logout must never block
    // the cashier from reaching the login screen.
    public async Task LogoutAsync(string accessToken, string refreshToken)
    {
        if (string.IsNullOrEmpty(accessToken) && string.IsNullOrEmpty(refreshToken))
            return;

        try
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "api/v1/auth/logout");
            if (!string.IsNullOrEmpty(accessToken))
                req.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);
            if (!string.IsNullOrEmpty(refreshToken))
                req.Headers.TryAddWithoutValidation("X-Refresh-Token", refreshToken);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var resp = await _http.SendAsync(req, cts.Token);
            // Backend returns 200 {message: "Logged out successfully"}.
            // Any non-2xx is silently ignored — e.g. token already expired.
        }
        catch
        {
            // Best-effort. Network failure during logout does not block the
            // local session clear that follows.
        }
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

    // ── Inventory adjustment (Phase I.1) ──────────────────────────────────────

    // POST /api/inventory/adjustments. Routes through the existing
    // PostWithRefreshAsync wrapper so 401 → token refresh → retry is automatic;
    // surface 403 / 404 / 409 / 400 via the standard ParseErrorMessage path.
    // Optional Idempotency-Key header makes retried submits safe — backend
    // de-duplicates inside InventoryMovementService.postAdjustment.
    public async Task<InventoryAdjustmentResponse> CreateInventoryAdjustmentAsync(
        CreateInventoryAdjustmentRequest request)
    {
        var idem = request.IdempotencyKey;
        var resp = await PostWithRefreshAsync(
            "api/inventory/adjustments", request,
            extraHeaders: string.IsNullOrEmpty(idem) ? null : ("Idempotency-Key", idem));
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<InventoryAdjustmentResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
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

    // POST /api/pos/shifts/{uuid}/cash-in (Phase 11.3).
    // An idempotency key is generated by the caller (ShiftViewModel) and sent
    // via the `Idempotency-Key` header so that a retried tap after a dropped
    // response does not double-record the movement.
    public async Task<ShiftCashMovementResponse> CashInAsync(
        string shiftUuid, ShiftCashMovementRequest request, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(shiftUuid))
            throw new ArgumentException("shiftUuid is required", nameof(shiftUuid));

        var resp = await PostWithRefreshAsync(
            $"api/pos/shifts/{shiftUuid}/cash-in", request,
            extraHeaders: ("Idempotency-Key", idempotencyKey));
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ShiftCashMovementResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // POST /api/pos/shifts/{uuid}/cash-out (Phase 11.3).
    // Amount must be positive — the backend negates it internally.
    public async Task<ShiftCashMovementResponse> CashOutAsync(
        string shiftUuid, ShiftCashMovementRequest request, string idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(shiftUuid))
            throw new ArgumentException("shiftUuid is required", nameof(shiftUuid));

        var resp = await PostWithRefreshAsync(
            $"api/pos/shifts/{shiftUuid}/cash-out", request,
            extraHeaders: ("Idempotency-Key", idempotencyKey));
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ShiftCashMovementResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Debt payment ──────────────────────────────────────────────────────────

    public async Task<CustomerDto> PayDebtAsync(DebtPaymentRequest request)
    {
        // Backend's DebtPaymentRequest.idempotencyKey is @NotBlank — a missing
        // key produces a 400 that would poison-quarantine any retry. Always
        // guarantee a non-blank value so the backend can deduplicate retries.
        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            request.IdempotencyKey = Guid.NewGuid().ToString();

        var resp = await PostWithRefreshAsync("api/debt/pay", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<CustomerDto>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Order history (GET /api/orders) ──────────────────────────────────────
    //
    // Fetches the server-side order list with optional date-range filtering.
    // `from` / `to` are ISO-8601 date strings (yyyy-MM-dd). Returns up to
    // `size` rows from page 0 — the history window shows one page; pagination
    // buttons can be added later. Returns null on any non-success response so
    // callers can fall back gracefully to local-only mode.
    public async Task<PageResponse<OrderListDto>?> GetOrdersAsync(
        string? from = null,
        string? to   = null,
        int     page = 0,
        int     size = 100)
    {
        var sb = new System.Text.StringBuilder("api/orders?isPos=true");
        sb.Append("&page=").Append(page);
        sb.Append("&size=").Append(size <= 0 ? 100 : size);
        if (!string.IsNullOrWhiteSpace(from))
            sb.Append("&from=").Append(Uri.EscapeDataString(from));
        if (!string.IsNullOrWhiteSpace(to))
            sb.Append("&to=").Append(Uri.EscapeDataString(to));

        var resp = await GetWithRefreshAsync(sb.ToString());
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync<PageResponse<OrderListDto>>(JsonOptions);
    }

    // ── Order detail (GET /api/orders/{uuid}) ────────────────────────────────
    //
    // Fetches a single order with its item lines. Returns null on 404 so the
    // caller can fall back gracefully (no hard crash). Every other non-2xx is
    // thrown via EnsureSuccessAsync — matches the GetCurrentShiftAsync pattern.
    public async Task<OrderDetailDto?> GetOrderByUuidAsync(string orderUuid)
    {
        if (string.IsNullOrWhiteSpace(orderUuid))
            throw new ArgumentException("orderUuid is required", nameof(orderUuid));

        var resp = await GetWithRefreshAsync(
            $"api/orders/{Uri.EscapeDataString(orderUuid)}");

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OrderDetailDto>(JsonOptions);
    }

    // ── Order return (POST /api/orders/{uuid}/returns) ───────────────────────
    //
    // Submits a return/refund request for an existing server order. The backend
    // restores stock to the specified warehouse and handles debt-reduction before
    // cash refund. The idempotencyKey is auto-generated if the caller left it
    // blank so that a retried tap after a dropped response does not create a
    // duplicate return record.
    public async Task<ReturnOrderResponse?> ReturnOrderAsync(
        string orderUuid, ReturnOrderRequest request)
    {
        if (string.IsNullOrWhiteSpace(orderUuid))
            throw new ArgumentException("orderUuid is required", nameof(orderUuid));
        if (request is null) throw new ArgumentNullException(nameof(request));

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
            request.IdempotencyKey = Guid.NewGuid().ToString();

        var resp = await PostWithRefreshAsync(
            $"api/orders/{Uri.EscapeDataString(orderUuid)}/returns", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<ReturnOrderResponse>(JsonOptions);
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

        // Bug M1: every line must be sendable (server UUID, price >= 0.01,
        // quantity >= 0.001). Previously invalid lines were SILENTLY filtered out
        // and apiTotal computed from the survivors — the server would record less
        // than the customer paid, or the UI mixed-payment split (set against the
        // FULL total) would exceed apiTotal and poison the sale with a confusing
        // error. Instead, if ANY line is structurally bad, throw a clear Uzbek
        // error naming the issue. The sale becomes poison with an actionable
        // LastSyncError the cashier can see in the retry UI, rather than a
        // wrong-total order silently reaching the backend. The checkout-time guard
        // (PosViewModel.CheckoutAsync) prevents this for new sales; this catches
        // legacy/partially-written pending sales already on disk.
        var invalid = sale.Items
            .FirstOrDefault(i => string.IsNullOrEmpty(i.ProductRemoteUuid)
                                 || i.Price    < 0.01m
                                 || i.Quantity < 0.001m);
        if (invalid is not null)
            throw new InvalidOperationException(
                $"«{invalid.ProductName}» qatori serverga yuborib bo'lmaydi " +
                "(mahsulot serverga bog'lanmagan yoki narx/miqdor noto'g'ri) — " +
                "savdo yuborilmadi.");

        var validItems = sale.Items
            .Select(i => new CreateOrderItemRequest
            {
                ProductUuid   = i.ProductRemoteUuid,
                Quantity      = i.Quantity,
                Price         = i.Price,
                DiscountPrice = Math.Max(0, i.Discount)
            }).ToList();

        // Keep the existing zero-items throw: a sale with no lines at all is
        // structurally invalid and must poison rather than POST an empty order.
        if (validItems.Count == 0)
            throw new InvalidOperationException("Serverga yuborish uchun yaroqli mahsulotlar yo'q");

        // Bug H2: the cart-wide discount (sale.Discount) is stored separately from
        // per-line item discounts and is NOT otherwise sent to the server. Fold it
        // into the per-line DiscountPrice BEFORE apiTotal is computed so the order
        // total the server records matches what the customer actually paid. The
        // mixed-payment split below is computed against this discounted apiTotal,
        // which is exactly the discounted Total the UI based its split amounts on,
        // so the two stay consistent.
        DistributeCartDiscount(validItems, Math.Max(0, sale.Discount));

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
            Transactions = transactions,
            // Bug H1: carry the shift recorded on the row (set at checkout), NOT a
            // value read from the VM at sync time — by the time a pending sale
            // syncs the shift may already be closed. Legacy pending sales created
            // before this column existed carry null, and the field is omitted from
            // the JSON (JsonIgnoreCondition.WhenWritingNull) so the backend accepts
            // them unchanged.
            ShiftUuid    = string.IsNullOrEmpty(sale.ShiftUuid) ? null : sale.ShiftUuid
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

    // Bug H2: distribute a cart-wide discount across the order lines as added
    // DiscountPrice, using largest-remainder rounding so the cent total is exact
    // and no line is discounted below zero. Mutates the passed items in place.
    private static void DistributeCartDiscount(
        List<CreateOrderItemRequest> items, decimal cartDiscount)
    {
        // Line totals after the existing per-line discount (the distributable base).
        var lineNet = items
            .Select(i => i.Price * i.Quantity - i.DiscountPrice)
            .ToArray();

        var netSum = lineNet.Sum();
        if (netSum <= 0) return; // nothing to distribute against

        // The cart discount can absorb at most the whole net sum (free order).
        var residual = Math.Min(cartDiscount, netSum);
        if (residual <= 0) return;

        // Proportional share per line, rounded to 2dp, clamped to its line net.
        var extra = new decimal[items.Count];
        for (int k = 0; k < items.Count; k++)
        {
            var share = Math.Round(residual * lineNet[k] / netSum, 2, MidpointRounding.AwayFromZero);
            extra[k]  = Math.Clamp(share, 0m, lineNet[k]);
        }

        // Largest-remainder correction: nudge by ±0.01 on the lines with the most
        // remaining capacity until the distributed total equals the residual exactly.
        var distributed = extra.Sum();
        var leftover     = residual - distributed;
        var step         = leftover > 0 ? 0.01m : -0.01m;
        int guard        = 0;
        int maxSteps     = items.Count * 200 + 100; // safety bound, never infinite

        while (Math.Abs(leftover) >= 0.01m && guard++ < maxSteps)
        {
            // Pick the line that can still absorb the step and has the largest
            // remaining capacity (largest-remainder), so big lines soak up leftovers.
            int best = -1;
            decimal bestCapacity = -1m;
            for (int k = 0; k < items.Count; k++)
            {
                var capacity = step > 0
                    ? lineNet[k] - extra[k]   // room to add more discount
                    : extra[k];               // room to remove discount
                if (capacity >= 0.01m && capacity > bestCapacity)
                {
                    bestCapacity = capacity;
                    best = k;
                }
            }
            if (best < 0) break; // no line can absorb the step → clamp residual

            extra[best] += step;
            leftover    -= step;
        }

        // Apply the distributed cart discount on top of the per-line discount.
        for (int k = 0; k < items.Count; k++)
        {
            items[k].DiscountPrice += extra[k];
            // Final guard: never exceed the line total (price*qty).
            var lineTotal = items[k].Price * items[k].Quantity;
            if (items[k].DiscountPrice > lineTotal)
                items[k].DiscountPrice = lineTotal;
        }
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
            // The refreshed token is picked up per-request by
            // TenantAuthHeaderHandler from settings — no header to "apply" (L2).
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

    // ── Operator permission API (Phase 10.19C; display-only) ────────────────
    //
    // These three methods consume the backend skeleton introduced in
    // Phase 10.19B (Ham-Pos/src/main/java/com/example/hampos/security/
    // controller/OperatorPermissionController.java). They use the existing
    // configured HttpClient (auth header + tenant header + refresh-on-401)
    // so no separate unauthenticated client is required. None of these
    // calls executes any maintenance operation; the validate call is a pure
    // permission check.
    //
    // The OperatorPermissionApiClient wrapper translates exceptions from
    // these methods into a fail-closed null result; the methods themselves
    // throw on non-2xx, matching the existing ApiClient idiom.

    public async Task<OperatorIdentityDto?> GetOperatorIdentityAsync(CancellationToken ct = default)
    {
        var resp = await GetWithRefreshAsync("api/v1/operator/identity");
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorIdentityDto>(JsonOptions, ct);
    }

    public async Task<OperatorPermissionsDto?> GetOperatorPermissionsAsync(CancellationToken ct = default)
    {
        var resp = await GetWithRefreshAsync("api/v1/operator/permissions");
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorPermissionsDto>(JsonOptions, ct);
    }

    public async Task<OperatorPermissionValidateResultDto?> ValidateOperatorPermissionAsync(
        OperatorPermissionValidateRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var resp = await PostWithRefreshAsync("api/v1/operator/permissions/validate", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorPermissionValidateResultDto>(JsonOptions, ct);
    }

    // ── Phase 10.19G — Operator audit-intent and evidence-registration ───────
    //
    // Both endpoints are response-only on the backend (no DB persistence yet)
    // and require an authenticated user. They never accept tokens, passwords,
    // confirmation phrases, raw ZIPs, raw DB content, raw backups, or raw
    // logs. The desktop wraps these methods through OperatorAuditEvidenceApi
    // Client so callers can fail closed without crashing.
    public async Task<OperatorAuditIntentResultDto?> RegisterOperatorAuditIntentAsync(
        OperatorAuditIntentRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var resp = await PostWithRefreshAsync("api/v1/operator/audit-intent", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorAuditIntentResultDto>(JsonOptions, ct);
    }

    public async Task<OperatorEvidenceRegisterResultDto?> RegisterOperatorEvidenceAsync(
        OperatorEvidenceRegisterRequestDto request,
        CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        var resp = await PostWithRefreshAsync("api/v1/operator/evidence/register", request);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorEvidenceRegisterResultDto>(JsonOptions, ct);
    }

    // ── Phase 10.22F — Operator evidence bundle upload + finalize ────────────
    //
    // Five methods consuming the Phase 10.22C/D endpoints under
    // /api/v1/operator/evidence/bundles. Each returns an
    // EvidenceBundleApiCallOutcome<T> so the desktop wrapper can branch
    // on a stable backend error code (FEATURE_FLAG_OFF, REDACTION_FAILED,
    // MIME_MISMATCH, MANIFEST_INVALID, …) without throwing.
    //
    // The multipart upload helper streams the file via FileStream so a
    // 25 MiB upload never loads into memory. It NEVER logs the multipart
    // body, the file content, the file's absolute path, or the
    // Authorization header.
    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        CreateEvidenceBundleAsync(
            EvidenceBundleCreateRequestDto request,
            CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post, "api/v1/operator/evidence/bundles", request, ct);
    }

    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleUploadResponseDto>>
        UploadEvidenceBundleFileAsync(
            string bundleUuid,
            string relativePath,
            string absoluteSourcePath,
            bool redacted,
            string? declaredSha256,
            string? contentType,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (string.IsNullOrWhiteSpace(relativePath))
            throw new ArgumentException("relativePath is required", nameof(relativePath));
        if (string.IsNullOrWhiteSpace(absoluteSourcePath))
            throw new ArgumentException("absoluteSourcePath is required", nameof(absoluteSourcePath));

        var url = $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/files";
        var resp = await SendMultipartWithRefreshAsync(
            HttpMethod.Post,
            url,
            buildContent: () => BuildEvidenceBundleUploadContent(
                absoluteSourcePath, relativePath, redacted, declaredSha256, contentType),
            ct: ct).ConfigureAwait(false);
        return await ReadEvidenceBundleOutcomeAsync<EvidenceBundleUploadResponseDto>(resp, ct);
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        FinalizeEvidenceBundleAsync(
            string bundleUuid,
            EvidenceBundleFinalizeRequestDto? request,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        // Backend accepts an empty body; send {} when the operator
        // supplied no notes so the controller's JSON binder doesn't 415.
        var body = request ?? new EvidenceBundleFinalizeRequestDto();
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/finalize",
            body,
            ct);
    }

    // ── Phase 10.22G — reviewer decision + binary download ──────────────────

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        ReviewEvidenceBundleAsync(
            string bundleUuid,
            EvidenceBundleReviewRequestDto request,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (request is null) throw new ArgumentNullException(nameof(request));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/review",
            request,
            ct);
    }

    /// <summary>
    /// Streams the backend's evidence bundle ZIP to a temporary file and
    /// atomically renames it into <paramref name="destinationFolder"/>.
    /// Returns the absolute destination path + the SHA-256 computed
    /// over the downloaded bytes + the byte count.
    ///
    /// Safety:
    ///   • Writes to a `*.part-{guid}.tmp` sibling first; renames on
    ///     success; deletes on failure.
    ///   • Refuses to overwrite an existing file unless
    ///     <paramref name="allowOverwrite"/> is true.
    ///   • Uses HttpCompletionOption.ResponseHeadersRead so the body
    ///     never materialises in memory.
    ///   • Honours the response's Content-Disposition filename if
    ///     present; falls back to `operator-evidence-bundle-{uuid}.zip`.
    ///   • Never logs Authorization headers or body bytes.
    /// </summary>
    public async Task<EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>>
        DownloadEvidenceBundleAsync(
            string bundleUuid,
            string destinationFolder,
            bool allowOverwrite,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (string.IsNullOrWhiteSpace(destinationFolder))
            throw new ArgumentException("destinationFolder is required", nameof(destinationFolder));

        var url = $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/download";

        HttpResponseMessage resp;
        try
        {
            resp = await _http.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, url),
                HttpCompletionOption.ResponseHeadersRead,
                ct).ConfigureAwait(false);
            if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
            {
                resp.Dispose();
                resp = await _http.SendAsync(
                    new HttpRequestMessage(HttpMethod.Get, url),
                    HttpCompletionOption.ResponseHeadersRead,
                    ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { throw; }
        catch
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                "NETWORK_FAILURE", "Backend is unreachable.", httpStatus: 0);
        }

        using var _ = resp;
        if (!resp.IsSuccessStatusCode)
        {
            // Backend returns {"code":"…","message":"…"} for evidence
            // bundle errors. Re-use the same parser as the JSON path.
            string? code = null;
            string? safeMessage = null;
            try
            {
                var err = await resp.Content.ReadFromJsonAsync<EvidenceBundleApiErrorDto>(JsonOptions, ct)
                                    .ConfigureAwait(false);
                code = err?.Code;
                safeMessage = err?.Message;
            }
            catch { /* generic fallback */ }
            if (string.IsNullOrWhiteSpace(code))
                code = "HTTP_" + (int)resp.StatusCode;
            if (string.IsNullOrWhiteSpace(safeMessage))
                safeMessage = ParseErrorMessage("", resp.StatusCode);
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                code, safeMessage, (int)resp.StatusCode);
        }

        var filename = ExtractContentDispositionFilename(resp)
                       ?? $"operator-evidence-bundle-{bundleUuid}.zip";
        filename = SanitizeDownloadFilename(filename);

        Directory.CreateDirectory(destinationFolder);
        var absoluteDestination = Path.Combine(destinationFolder, filename);
        if (File.Exists(absoluteDestination) && !allowOverwrite)
        {
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                "LOCAL_FILE_EXISTS",
                "Destination file already exists. Set allowOverwrite=true to replace.",
                httpStatus: (int)resp.StatusCode);
        }

        var tempPath = absoluteDestination + ".part-" + Guid.NewGuid().ToString("N") + ".tmp";
        long byteCount = 0;
        string sha256Hex;
        try
        {
            using (var fs = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                                           bufferSize: 8 * 1024, useAsync: true))
            using (var sha = System.Security.Cryptography.IncrementalHash.CreateHash(
                                  System.Security.Cryptography.HashAlgorithmName.SHA256))
            using (var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            {
                byte[] buf = new byte[8 * 1024];
                int read;
                while ((read = await src.ReadAsync(buf.AsMemory(0, buf.Length), ct).ConfigureAwait(false)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, read), ct).ConfigureAwait(false);
                    sha.AppendData(buf, 0, read);
                    byteCount += read;
                }
                sha256Hex = ToHex(sha.GetHashAndReset());
            }

            if (File.Exists(absoluteDestination))
            {
                File.Delete(absoluteDestination);
            }
            File.Move(tempPath, absoluteDestination);
        }
        catch (OperationCanceledException)
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
            throw;
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { /* best-effort */ }
            return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Failure(
                "LOCAL_WRITE_FAILURE",
                "Failed to write the downloaded bundle to disk.",
                httpStatus: (int)resp.StatusCode);
        }

        var result = new EvidenceBundleDownloadResultDto
        {
            BundleUuid          = bundleUuid,
            DestinationPath     = absoluteDestination,
            DestinationFilename = filename,
            ByteSize            = byteCount,
            Sha256Hex           = sha256Hex,
        };
        return EvidenceBundleApiCallOutcome<EvidenceBundleDownloadResultDto>.Success(
            result, (int)resp.StatusCode);
    }

    private static string? ExtractContentDispositionFilename(HttpResponseMessage resp)
    {
        var cd = resp.Content.Headers.ContentDisposition;
        if (cd is null) return null;
        // FileNameStar is the RFC 5987 encoded form; FileName is the
        // quoted ASCII form. Both may appear.
        var raw = cd.FileNameStar ?? cd.FileName;
        if (string.IsNullOrWhiteSpace(raw)) return null;
        return raw.Trim('"');
    }

    private static string SanitizeDownloadFilename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "operator-evidence-bundle.zip";
        var sb = new System.Text.StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.') sb.Append(c);
            else sb.Append('-');
        }
        var s = sb.ToString();
        while (s.Contains("--")) s = s.Replace("--", "-");
        s = s.Trim('-');
        if (string.IsNullOrWhiteSpace(s)) return "operator-evidence-bundle.zip";
        if (!s.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) s += ".zip";
        return s;
    }

    private static string ToHex(byte[] bytes)
    {
        var sb = new System.Text.StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        GetEvidenceBundleAsync(string bundleUuid, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Get,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}",
            body: null,
            ct);
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>>
        ListEvidenceBundlesAsync(
            string? evidenceType, string? phase, string? tenantId, string? status,
            int page, int size, CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder("api/v1/operator/evidence/bundles?");
        sb.Append("page=").Append(System.Math.Max(0, page));
        sb.Append("&size=").Append(size <= 0 ? 20 : System.Math.Min(size, 100));
        AppendParam(sb, "evidenceType", evidenceType);
        AppendParam(sb, "phase",        phase);
        AppendParam(sb, "tenantId",     tenantId);
        AppendParam(sb, "status",       status);
        return CallEvidenceBundleJsonAsync<EvidenceBundlePageResponseDto>(
            HttpMethod.Get, sb.ToString(), body: null, ct);
    }

    // ── Phase 10.22H — retention / legal hold / archive / expire ──────────

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        UpdateEvidenceBundleRetentionAsync(
            string bundleUuid,
            EvidenceBundleRetentionRequestDto request,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (request is null) throw new ArgumentNullException(nameof(request));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/retention",
            request,
            ct);
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        UpdateEvidenceBundleLegalHoldAsync(
            string bundleUuid,
            EvidenceBundleLegalHoldRequestDto request,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (request is null) throw new ArgumentNullException(nameof(request));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/legal-hold",
            request,
            ct);
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        ArchiveEvidenceBundleAsync(
            string bundleUuid,
            EvidenceBundleArchiveRequestDto request,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (request is null) throw new ArgumentNullException(nameof(request));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/archive",
            request,
            ct);
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundleResponseDto>>
        ExpireEvidenceBundleAsync(
            string bundleUuid,
            EvidenceBundleExpireRequestDto request,
            CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(bundleUuid))
            throw new ArgumentException("bundleUuid is required", nameof(bundleUuid));
        if (request is null) throw new ArgumentNullException(nameof(request));
        return CallEvidenceBundleJsonAsync<EvidenceBundleResponseDto>(
            HttpMethod.Post,
            $"api/v1/operator/evidence/bundles/{Uri.EscapeDataString(bundleUuid)}/expire",
            request,
            ct);
    }

    public Task<EvidenceBundleApiCallOutcome<EvidenceBundlePageResponseDto>>
        ListEvidenceBundleRetentionCandidatesAsync(
            string? evidenceType, string? status, string? tenantId,
            string? beforeIsoUtc, int page, int size,
            CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder("api/v1/operator/evidence/bundles/retention-candidates?");
        sb.Append("page=").Append(System.Math.Max(0, page));
        sb.Append("&size=").Append(size <= 0 ? 20 : System.Math.Min(size, 100));
        AppendParam(sb, "evidenceType", evidenceType);
        AppendParam(sb, "status",       status);
        AppendParam(sb, "tenantId",     tenantId);
        AppendParam(sb, "before",       beforeIsoUtc);
        return CallEvidenceBundleJsonAsync<EvidenceBundlePageResponseDto>(
            HttpMethod.Get, sb.ToString(), body: null, ct);
    }

    // ── Phase 10.22P — lifecycle scheduler run-history ───────────────────────

    public Task<EvidenceBundleApiCallOutcome<RetentionSweepRunPageResponseDto>>
        ListRetentionSweepRunsAsync(int page = 0, int size = 20, CancellationToken ct = default)
    {
        var url = $"api/v1/operator/evidence/bundles/retention-sweeper/runs" +
                  $"?page={System.Math.Max(0, page)}&size={System.Math.Min(size <= 0 ? 20 : size, 100)}";
        return CallEvidenceBundleJsonAsync<RetentionSweepRunPageResponseDto>(
            HttpMethod.Get, url, body: null, ct);
    }

    public Task<EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>>
        GetRetentionSweepRunAsync(string runUuid, CancellationToken ct = default) =>
        CallEvidenceBundleJsonAsync<RetentionSweepRunResponseDto>(
            HttpMethod.Get,
            $"api/v1/operator/evidence/bundles/retention-sweeper/runs/{Uri.EscapeDataString(runUuid)}",
            body: null, ct);

    public Task<EvidenceBundleApiCallOutcome<RetentionSweepRunResponseDto>>
        RunRetentionSweepOnceAsync(RetentionSweepRunRequestDto request, CancellationToken ct = default) =>
        CallEvidenceBundleJsonAsync<RetentionSweepRunResponseDto>(
            HttpMethod.Post,
            "api/v1/operator/evidence/bundles/retention-sweeper/run-once",
            body: request, ct);

    public Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunPageResponseDto>>
        ListExpirationSweepRunsAsync(int page = 0, int size = 20, CancellationToken ct = default)
    {
        var url = $"api/v1/operator/evidence/bundles/expiration-sweeper/runs" +
                  $"?page={System.Math.Max(0, page)}&size={System.Math.Min(size <= 0 ? 20 : size, 100)}";
        return CallEvidenceBundleJsonAsync<ExpirationSweepRunPageResponseDto>(
            HttpMethod.Get, url, body: null, ct);
    }

    public Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>>
        GetExpirationSweepRunAsync(string runUuid, CancellationToken ct = default) =>
        CallEvidenceBundleJsonAsync<ExpirationSweepRunResponseDto>(
            HttpMethod.Get,
            $"api/v1/operator/evidence/bundles/expiration-sweeper/runs/{Uri.EscapeDataString(runUuid)}",
            body: null, ct);

    public Task<EvidenceBundleApiCallOutcome<ExpirationSweepRunResponseDto>>
        RunExpirationSweepOnceAsync(ExpirationSweepRunRequestDto request, CancellationToken ct = default) =>
        CallEvidenceBundleJsonAsync<ExpirationSweepRunResponseDto>(
            HttpMethod.Post,
            "api/v1/operator/evidence/bundles/expiration-sweeper/run-once",
            body: request, ct);

    // ── Phase 10.22F helpers ─────────────────────────────────────────────────

    private async Task<EvidenceBundleApiCallOutcome<T>>
        CallEvidenceBundleJsonAsync<T>(
            HttpMethod method,
            string url,
            object? body,
            CancellationToken ct)
        where T : class
    {
        HttpResponseMessage resp;
        if (method == HttpMethod.Get)
        {
            resp = await GetWithRefreshAsync(url).ConfigureAwait(false);
        }
        else if (method == HttpMethod.Post)
        {
            resp = await PostWithRefreshAsync(url, body!).ConfigureAwait(false);
        }
        else
        {
            throw new System.InvalidOperationException(
                "Evidence bundle API only uses GET / POST.");
        }
        return await ReadEvidenceBundleOutcomeAsync<T>(resp, ct).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendMultipartWithRefreshAsync(
        HttpMethod method,
        string url,
        System.Func<MultipartFormDataContent> buildContent,
        CancellationToken ct)
    {
        var req = new HttpRequestMessage(method, url) { Content = buildContent() };
        var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct)
                              .ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            // Re-build the multipart content (its streams may be partially
            // consumed) so the retried request sends fresh bytes.
            var retry = new HttpRequestMessage(method, url) { Content = buildContent() };
            resp = await _http.SendAsync(retry, HttpCompletionOption.ResponseHeadersRead, ct)
                              .ConfigureAwait(false);
        }
        return resp;
    }

    private static MultipartFormDataContent BuildEvidenceBundleUploadContent(
        string absoluteSourcePath,
        string relativePath,
        bool redacted,
        string? declaredSha256,
        string? contentType)
    {
        var multipart = new MultipartFormDataContent();
        // Open with FileShare.Read + ResponseHeadersRead streaming so a
        // 25 MiB upload never lands in memory as a byte[].
        var fileStream = new FileStream(
            absoluteSourcePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 8 * 1024, useAsync: true);
        var streamContent = new StreamContent(fileStream);
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            try { streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType); }
            catch { /* ignore malformed operator-supplied content-type */ }
        }
        // The backend reads only the file part's content + the form
        // fields; the multipart filename is provided so the part has
        // a deterministic Content-Disposition. Use the bare last
        // segment of the relative path — never the absolute path.
        var bareName = relativePath;
        var slash = bareName.LastIndexOf('/');
        if (slash >= 0) bareName = bareName[(slash + 1)..];
        multipart.Add(streamContent, "file", bareName);
        multipart.Add(new StringContent(relativePath), "relativePath");
        multipart.Add(new StringContent(redacted ? "true" : "false"), "redacted");
        if (!string.IsNullOrWhiteSpace(declaredSha256))
        {
            multipart.Add(new StringContent(declaredSha256), "declaredSha256");
        }
        return multipart;
    }

    private static async Task<EvidenceBundleApiCallOutcome<T>>
        ReadEvidenceBundleOutcomeAsync<T>(HttpResponseMessage resp, CancellationToken ct)
        where T : class
    {
        using var _ = resp;
        if (resp.IsSuccessStatusCode)
        {
            try
            {
                var value = await resp.Content.ReadFromJsonAsync<T>(JsonOptions, ct)
                                      .ConfigureAwait(false);
                if (value is null)
                {
                    return EvidenceBundleApiCallOutcome<T>.Failure(
                        "DESERIALIZATION_FAILURE",
                        "Backend returned empty body.",
                        (int)resp.StatusCode);
                }
                return EvidenceBundleApiCallOutcome<T>.Success(value, (int)resp.StatusCode);
            }
            catch (System.OperationCanceledException) { throw; }
            catch
            {
                return EvidenceBundleApiCallOutcome<T>.Failure(
                    "DESERIALIZATION_FAILURE",
                    "Backend response could not be parsed.",
                    (int)resp.StatusCode);
            }
        }
        // Non-2xx: try to parse {"code":"…","message":"…"}.
        string? code = null;
        string? safeMessage = null;
        try
        {
            var err = await resp.Content.ReadFromJsonAsync<EvidenceBundleApiErrorDto>(JsonOptions, ct)
                                .ConfigureAwait(false);
            code = err?.Code;
            safeMessage = err?.Message;
        }
        catch { /* fall back to generic */ }
        if (string.IsNullOrWhiteSpace(code))
        {
            code = "HTTP_" + (int)resp.StatusCode;
        }
        if (string.IsNullOrWhiteSpace(safeMessage))
        {
            safeMessage = ParseErrorMessage("", resp.StatusCode);
        }
        return EvidenceBundleApiCallOutcome<T>.Failure(code, safeMessage, (int)resp.StatusCode);
    }


    // ── Phase 10.19J — Operator audit/evidence review (read-only) ────────────
    //
    // Consumes the Phase 10.19I backend endpoints. The wrapper
    // (OperatorAuditReviewApiClient) catches exceptions and surfaces null
    // to the caller; these methods throw on non-2xx, matching the rest of
    // the ApiClient idiom. 404 returns null for single-row lookups —
    // consistent with GetCurrentShiftAsync (line 245).
    public async Task<OperatorAuditEventPageDto?> GetOperatorAuditEventsAsync(
        OperatorAuditReviewQuery query,
        CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var url = BuildOperatorAuditEventsUrl(query);
        var resp = await GetWithRefreshAsync(url);
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorAuditEventPageDto>(JsonOptions, ct);
    }

    public async Task<OperatorAuditEventDetailDto?> GetOperatorAuditEventAsync(
        string eventId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(eventId))
            throw new ArgumentException("eventId is required", nameof(eventId));
        var resp = await GetWithRefreshAsync(
            $"api/v1/operator/audit/events/{Uri.EscapeDataString(eventId.Trim())}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorAuditEventDetailDto>(JsonOptions, ct);
    }

    public async Task<OperatorAuditEventDetailDto?> GetOperatorAuditIntentAsync(
        string intentId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(intentId))
            throw new ArgumentException("intentId is required", nameof(intentId));
        var resp = await GetWithRefreshAsync(
            $"api/v1/operator/audit/intents/{Uri.EscapeDataString(intentId.Trim())}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorAuditEventDetailDto>(JsonOptions, ct);
    }

    public async Task<OperatorAuditEventDetailDto?> GetOperatorAuditEvidenceAsync(
        string registrationId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(registrationId))
            throw new ArgumentException("registrationId is required", nameof(registrationId));
        var resp = await GetWithRefreshAsync(
            $"api/v1/operator/audit/evidence/{Uri.EscapeDataString(registrationId.Trim())}");
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp);
        return await resp.Content.ReadFromJsonAsync<OperatorAuditEventDetailDto>(JsonOptions, ct);
    }

    private static string BuildOperatorAuditEventsUrl(OperatorAuditReviewQuery q)
    {
        var sb = new System.Text.StringBuilder("api/v1/operator/audit/events?");
        sb.Append("page=").Append(System.Math.Max(0, q.Page));
        int size = q.Size <= 0 ? 50 : (q.Size > 200 ? 200 : q.Size);
        sb.Append("&size=").Append(size);
        AppendParam(sb, "tenantId",      q.TenantId);
        AppendParam(sb, "entityType",    q.EntityType);
        AppendParam(sb, "action",        q.Action);
        AppendParam(sb, "operationName", q.OperationName);
        AppendParam(sb, "permissionKey", q.PermissionKey);
        if (q.Accepted.HasValue)
        {
            sb.Append("&accepted=").Append(q.Accepted.Value ? "true" : "false");
        }
        if (q.From.HasValue)
        {
            sb.Append("&from=").Append(Uri.EscapeDataString(q.From.Value.ToString("yyyy-MM-ddTHH:mm:ss")));
        }
        if (q.To.HasValue)
        {
            sb.Append("&to=").Append(Uri.EscapeDataString(q.To.Value.ToString("yyyy-MM-ddTHH:mm:ss")));
        }
        return sb.ToString();
    }

    private static void AppendParam(System.Text.StringBuilder sb, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append('&').Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim()));
    }

    // ── Phase 10.20G — Operator permission admin (read-only) ─────────────────
    //
    // Consumes the Phase 10.20F backend endpoints. Single-row 404s map to
    // null (consistent with GetCurrentShiftAsync). The wrapper
    // (OperatorPermissionAdminApiClient) catches all other exceptions and
    // collapses them to null for safe UI fallback.
    public async Task<OperatorPermissionAdminPageDto<OperatorPermissionDefinitionAdminDto>?>
        GetOperatorPermissionDefinitionsAsync(
            OperatorPermissionDefinitionAdminQuery query, CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var url = BuildPermissionDefinitionsUrl(query);
        var resp = await GetWithRefreshAsync(url);
        await EnsureSuccessAsync(resp);
        return await resp.Content
            .ReadFromJsonAsync<OperatorPermissionAdminPageDto<OperatorPermissionDefinitionAdminDto>>(JsonOptions, ct);
    }

    public async Task<OperatorPermissionAdminPageDto<OperatorRolePermissionGrantAdminDto>?>
        GetOperatorPermissionRoleGrantsAsync(
            OperatorRoleGrantAdminQuery query, CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var url = BuildPermissionRoleGrantsUrl(query);
        var resp = await GetWithRefreshAsync(url);
        await EnsureSuccessAsync(resp);
        return await resp.Content
            .ReadFromJsonAsync<OperatorPermissionAdminPageDto<OperatorRolePermissionGrantAdminDto>>(JsonOptions, ct);
    }

    public async Task<OperatorPermissionAdminPageDto<OperatorUserPermissionOverrideAdminDto>?>
        GetOperatorPermissionUserOverridesAsync(
            OperatorUserOverrideAdminQuery query, CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var url = BuildPermissionUserOverridesUrl(query);
        var resp = await GetWithRefreshAsync(url);
        await EnsureSuccessAsync(resp);
        return await resp.Content
            .ReadFromJsonAsync<OperatorPermissionAdminPageDto<OperatorUserPermissionOverrideAdminDto>>(JsonOptions, ct);
    }

    public async Task<OperatorPermissionEffectiveAdminDto?>
        GetOperatorPermissionEffectiveAdminAsync(
            OperatorPermissionEffectiveAdminQuery query, CancellationToken ct = default)
    {
        if (query is null) throw new ArgumentNullException(nameof(query));
        var url = BuildPermissionEffectiveAdminUrl(query);
        var resp = await GetWithRefreshAsync(url);
        if (resp.StatusCode == HttpStatusCode.NotFound) return null;
        await EnsureSuccessAsync(resp);
        return await resp.Content
            .ReadFromJsonAsync<OperatorPermissionEffectiveAdminDto>(JsonOptions, ct);
    }

    // ── Phase 10.21G — operator permission authoritative-status summary ─────
    //
    // Consumes the Phase 10.21G backend endpoint
    // (GET /api/v1/admin/operator-permissions/authoritative-status).
    // Read-only. The wrapper
    // (OperatorPermissionAdminApiClient.GetAuthoritativeStatusAsync)
    // catches all exceptions and returns null for safe UI fallback.
    public async Task<OperatorPermissionAuthoritativeStatusDto?>
        GetOperatorPermissionAuthoritativeStatusAsync(CancellationToken ct = default)
    {
        var resp = await GetWithRefreshAsync("api/v1/admin/operator-permissions/authoritative-status");
        await EnsureSuccessAsync(resp);
        return await resp.Content
            .ReadFromJsonAsync<OperatorPermissionAuthoritativeStatusDto>(JsonOptions, ct);
    }

    private static string BuildPermissionDefinitionsUrl(OperatorPermissionDefinitionAdminQuery q)
    {
        var sb = new System.Text.StringBuilder("api/v1/admin/operator-permissions/definitions?");
        sb.Append("page=").Append(System.Math.Max(0, q.Page));
        sb.Append("&size=").Append(ClampAdminPageSize(q.Size));
        if (q.Active.HasValue)    sb.Append("&active=").Append(q.Active.Value ? "true" : "false");
        if (q.Dangerous.HasValue) sb.Append("&dangerous=").Append(q.Dangerous.Value ? "true" : "false");
        AppendParam(sb, "category",      q.Category);
        AppendParam(sb, "permissionKey", q.PermissionKey);
        return sb.ToString();
    }

    private static string BuildPermissionRoleGrantsUrl(OperatorRoleGrantAdminQuery q)
    {
        var sb = new System.Text.StringBuilder("api/v1/admin/operator-permissions/role-grants?");
        sb.Append("page=").Append(System.Math.Max(0, q.Page));
        sb.Append("&size=").Append(ClampAdminPageSize(q.Size));
        if (q.Active.HasValue) sb.Append("&active=").Append(q.Active.Value ? "true" : "false");
        AppendParam(sb, "role",              q.Role);
        AppendParam(sb, "permissionKey",     q.PermissionKey);
        AppendParam(sb, "tenantScopePolicy", q.TenantScopePolicy);
        return sb.ToString();
    }

    private static string BuildPermissionUserOverridesUrl(OperatorUserOverrideAdminQuery q)
    {
        var sb = new System.Text.StringBuilder("api/v1/admin/operator-permissions/user-overrides?");
        sb.Append("page=").Append(System.Math.Max(0, q.Page));
        sb.Append("&size=").Append(ClampAdminPageSize(q.Size));
        if (q.UserId.HasValue)  sb.Append("&userId=").Append(q.UserId.Value);
        if (q.Active.HasValue)  sb.Append("&active=").Append(q.Active.Value ? "true" : "false");
        if (q.Expired.HasValue) sb.Append("&expired=").Append(q.Expired.Value ? "true" : "false");
        AppendParam(sb, "tenantId",      q.TenantId);
        AppendParam(sb, "storeId",       q.StoreId);
        AppendParam(sb, "permissionKey", q.PermissionKey);
        AppendParam(sb, "grantType",     q.GrantType);
        return sb.ToString();
    }

    private static string BuildPermissionEffectiveAdminUrl(OperatorPermissionEffectiveAdminQuery q)
    {
        var sb = new System.Text.StringBuilder("api/v1/admin/operator-permissions/effective?");
        bool first = true;
        if (q.UserId.HasValue) { sb.Append("userId=").Append(q.UserId.Value); first = false; }
        AppendQueryParam(sb, ref first, "tenantId", q.TenantId);
        AppendQueryParam(sb, ref first, "storeId",  q.StoreId);
        // If nothing was appended the URL ends with a stray '?', which is
        // harmless but ugly. Strip it.
        if (first && sb.Length > 0 && sb[sb.Length - 1] == '?') sb.Length -= 1;
        return sb.ToString();
    }

    private static void AppendQueryParam(System.Text.StringBuilder sb, ref bool first, string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        sb.Append(first ? "" : "&").Append(name).Append('=').Append(Uri.EscapeDataString(value.Trim()));
        first = false;
    }

    private static int ClampAdminPageSize(int requested)
    {
        if (requested <= 0)  return 50;
        if (requested > 200) return 200;
        return requested;
    }

    // ── Phase 10.20I — Operator permission admin mutations (default OFF) ────
    //
    // Each method consumes the matching Phase 10.20H endpoint. Non-2xx
    // responses are translated into a synthetic failure envelope so the
    // wrapper service can render the typed backend error
    // ({status, code, message}) without throwing into the UI.
    //
    // Security: no method logs the request body — `reason` and
    // `approvalTicketId` are free-text fields that may carry incident
    // detail. The configured HttpClient handler does not record request
    // bodies; this method does not call EnsureSuccessAsync on a failure
    // path because the typed error body is the value we want to surface.
    public Task<OperatorPermissionAdminMutationResponseDto<OperatorUserPermissionOverrideAdminDto>?>
        CreateOperatorUserOverrideAsync(
            OperatorPermissionUserOverrideCreateRequestDto request,
            CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return PostMutationAsync<OperatorPermissionUserOverrideCreateRequestDto,
                                 OperatorUserPermissionOverrideAdminDto>(
            "api/v1/admin/operator-permissions/user-overrides", request, ct);
    }

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorUserPermissionOverrideAdminDto>?>
        RevokeOperatorUserOverrideAsync(
            long id,
            OperatorPermissionUserOverrideRevokeRequestDto request,
            CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return PostMutationAsync<OperatorPermissionUserOverrideRevokeRequestDto,
                                 OperatorUserPermissionOverrideAdminDto>(
            $"api/v1/admin/operator-permissions/user-overrides/{id}/revoke", request, ct);
    }

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorRolePermissionGrantAdminDto>?>
        CreateOperatorRoleGrantAsync(
            OperatorPermissionRoleGrantCreateRequestDto request,
            CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return PostMutationAsync<OperatorPermissionRoleGrantCreateRequestDto,
                                 OperatorRolePermissionGrantAdminDto>(
            "api/v1/admin/operator-permissions/role-grants", request, ct);
    }

    public Task<OperatorPermissionAdminMutationResponseDto<OperatorRolePermissionGrantAdminDto>?>
        RevokeOperatorRoleGrantAsync(
            long id,
            OperatorPermissionRoleGrantRevokeRequestDto request,
            CancellationToken ct = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        return PostMutationAsync<OperatorPermissionRoleGrantRevokeRequestDto,
                                 OperatorRolePermissionGrantAdminDto>(
            $"api/v1/admin/operator-permissions/role-grants/{id}/revoke", request, ct);
    }

    private async Task<OperatorPermissionAdminMutationResponseDto<TItem>?>
        PostMutationAsync<TReq, TItem>(string url, TReq request, CancellationToken ct)
        where TReq : class
        where TItem : class
    {
        var resp = await PostWithRefreshAsync(url, request);
        if ((int)resp.StatusCode >= 200 && (int)resp.StatusCode < 300)
        {
            return await resp.Content
                .ReadFromJsonAsync<OperatorPermissionAdminMutationResponseDto<TItem>>(JsonOptions, ct);
        }
        // Non-2xx: parse the typed error body. Never throw on a known
        // mutation rejection — the UI needs to display the typed message.
        OperatorPermissionAdminMutationErrorBodyDto? error = null;
        try
        {
            error = await resp.Content
                .ReadFromJsonAsync<OperatorPermissionAdminMutationErrorBodyDto>(JsonOptions, ct);
        }
        catch { /* error body absent or malformed — fall through */ }
        return new OperatorPermissionAdminMutationResponseDto<TItem>
        {
            Success           = false,
            Message           = error?.Message ?? $"Backend rejected the mutation (HTTP {(int)resp.StatusCode}).",
            AuditSource       = null,
            Item              = null,
            BackendStatusCode = (int)resp.StatusCode,
            BackendErrorCode  = error?.Code,
        };
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
