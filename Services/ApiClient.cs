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
    private const string DefaultBaseUrl = "https://shefpos.uz";

    private readonly SemaphoreSlim _refreshSemaphore = new(1, 1);

    // Bug M3: debounce SessionExpiredMessage — send it at most once per expiry
    // episode so concurrent failing calls can't spam logout/navigation. Reset on
    // any successful refresh or login (ResetSessionExpiry).
    private int _sessionExpiredSent;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http, SettingsRepository settings)
    {
        _http = http;
        _settings = settings;
        ApplyBaseUrl();
    }

    public void ApplyBaseUrl()
    {
        var url = _settings.Get("api_base_url");
        if (string.IsNullOrWhiteSpace(url))
            url = DefaultBaseUrl;

        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri)) return;
        if (_http.BaseAddress == uri) return;
        _http.BaseAddress = uri;
    }

    // Bug M3: re-arm the session-expiry debounce after a successful login/refresh
    // so a later genuine expiry can notify again. Call this from AuthService on a
    // successful login.
    public void ResetSessionExpiry() => Interlocked.Exchange(ref _sessionExpiredSent, 0);

    // Bug M3: send SessionExpiredMessage only once until ResetSessionExpiry().
    private void NotifySessionExpired()
    {
        if (Interlocked.Exchange(ref _sessionExpiredSent, 1) == 0)
            WeakReferenceMessenger.Default.Send(new SessionExpiredMessage());
    }

    // Bug H1: build a request that stamps Authorization + X-Tenant-ID from the
    // CURRENT settings at send time. Never mutate HttpClient.DefaultRequestHeaders,
    // which would tear concurrent in-flight requests. `withAuth` is false for the
    // login endpoint (no stale Bearer token wanted).
    private HttpRequestMessage BuildRequest(
        HttpMethod method, string url, HttpContent? content = null, bool withAuth = true)
    {
        var req = new HttpRequestMessage(method, url) { Content = content };

        var tenant = _settings.Get("tenant_subdomain");
        if (!string.IsNullOrEmpty(tenant))
            req.Headers.Add("X-Tenant-ID", tenant);

        if (withAuth)
        {
            var token = _settings.Get("auth_token");
            if (!string.IsNullOrEmpty(token))
                req.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        return req;
    }

    // ── Auth ──────────────────────────────────────────────────────────────────

    public async Task<LoginResponse> LoginAsync(string username, string password)
    {
        // Login carries the X-Tenant-ID header (from settings) but no Bearer token.
        var req = BuildRequest(HttpMethod.Post, "api/v1/auth/login",
            JsonContent.Create(new { username, password }), withAuth: false);
        var response = await _http.SendAsync(req);
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

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        var all = new List<ProductDto>();
        int page = 0;
        int totalPages;

        do
        {
            var resp = await GetWithRefreshAsync($"api/products?is_pos=true&page={page}&size=200");
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

    public async Task<List<CustomerDto>> GetCustomersAsync()
    {
        var all = new List<CustomerDto>();
        int page = 0;
        int totalPages;

        do
        {
            var resp = await GetWithRefreshAsync($"api/customers?page={page}&size=200");
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
        var mappedType = sale.PaymentType.ToUpperInvariant() switch
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
                // Start from the per-line discount; the cart-wide discount is
                // distributed on top of this below (Bug H2).
                DiscountPrice = Math.Max(0, i.Discount)
            }).ToList();

        if (validItems.Count == 0)
            throw new InvalidOperationException("Serverga yuborish uchun yaroqli mahsulotlar yo'q");

        // Bug H2: distribute the cart-wide discount (sale.Discount) across the
        // line items' discountPrice fields so it actually reaches the server, and
        // so the API's exact-sum invariant holds:
        //   sum(price*qty - discountPrice) == sale.TotalAmount
        DistributeCartDiscount(validItems, Math.Max(0, sale.Discount), sale.TotalAmount);

        // apiTotal is computed ONCE from the final per-line figures (decimal only).
        var apiTotal = validItems.Sum(i => i.Price * i.Quantity - i.DiscountPrice);
        if (apiTotal <= 0)
            throw new InvalidOperationException("Zakaz summasi noldan katta bo'lishi kerak");

        // Build transactions that sum precisely to apiTotal.
        //
        // sale.PaidAmount  = what the customer physically paid (may include
        //                    over-tendered cash; change was given back).
        // The server must never receive more than apiTotal as paid, so clamp.
        // If partially paid AND a customer is linked, split into a paid + debt
        // pair. Without a customer we cannot register debt → treat as fully paid.

        var hasCustomer = !string.IsNullOrEmpty(sale.CustomerRemoteUuid);

        decimal clampedPaid = Math.Min(Math.Max(sale.PaidAmount, 0m), apiTotal);
        decimal paidPortion = hasCustomer ? clampedPaid : apiTotal;
        decimal debtPortion = hasCustomer ? apiTotal - paidPortion : 0m;

        // Round each transaction to 2dp, then fix the last one by the remainder so
        // sum(transactions) == apiTotal exactly.
        paidPortion = Math.Round(paidPortion, 2, MidpointRounding.AwayFromZero);
        debtPortion = Math.Round(debtPortion, 2, MidpointRounding.AwayFromZero);

        var transactions = new List<CreateTransactionRequest>();

        if (paidPortion > 0)
            transactions.Add(new CreateTransactionRequest
            {
                CashboxUuid = cashboxUuid,
                Amount      = paidPortion,
                CurrencyId  = currencyId,
                IsDebt      = false,
                IsCashback  = false
            });

        if (debtPortion > 0)
            transactions.Add(new CreateTransactionRequest
            {
                CashboxUuid = cashboxUuid,
                Amount      = debtPortion,
                CurrencyId  = currencyId,
                IsDebt      = true,
                IsCashback  = false
            });

        // Adjust the last transaction by any rounding remainder so the sum is exact.
        if (transactions.Count > 0)
        {
            var txSum = transactions.Sum(t => t.Amount);
            var diff  = apiTotal - txSum;
            if (diff != 0) transactions[^1].Amount += diff;
        }

        // Bug M4: when both a paid and a debt transaction are emitted the order is
        // genuinely mixed, regardless of the single type the UI selected. Otherwise
        // keep the mapped single type (paid-only → mapped type, debt-only → mapped).
        var hasPaidTx = transactions.Any(t => !t.IsDebt);
        var hasDebtTx = transactions.Any(t => t.IsDebt);
        var paymentType = (hasPaidTx && hasDebtTx) ? "MIXED" : mappedType;

        var order = new CreateOrderRequest
        {
            BranchUuid     = branchUuid,
            CustomerUuid   = hasCustomer ? sale.CustomerRemoteUuid : null,
            CurrencyId     = currencyId,
            PaymentType    = paymentType,
            IsPos          = true,
            DealType       = 0,
            DeliveryType   = "SELF",
            PriceListId    = priceListId,
            Comment        = string.IsNullOrEmpty(sale.Note) ? null : sale.Note,
            Items          = validItems,
            Transactions   = transactions,
            // Bug C1: stable idempotency key so retries don't create duplicates.
            IdempotencyKey = sale.LocalId
        };

        var response = await PostWithRefreshAsync("api/orders", order);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        return result?.Uuid ?? "";
    }

    // Bug H2: distribute the cart-wide discount proportionally across the items'
    // discountPrice fields (on top of any existing per-line discount), using the
    // largest-remainder method so that EXACTLY:
    //     sum(price*qty - discountPrice) == targetTotal
    // Decimal arithmetic only. Guards: never let a line's discountPrice exceed its
    // line total; clamp the residual when the cart discount exceeds what the lines
    // can absorb; handles single-line, zero, and free (total == 0) sales.
    private static void DistributeCartDiscount(
        List<CreateOrderItemRequest> items, decimal cartDiscount, decimal targetTotal)
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
            var refreshToken = _settings.Get("refresh_token");
            if (string.IsNullOrEmpty(refreshToken))
            {
                NotifySessionExpired();
                return false;
            }

            // Refresh sends the refresh token as the Bearer (NOT the expired access
            // token) plus the tenant header — per-request, no shared-header mutation.
            var req = BuildRequest(HttpMethod.Post, "api/v1/auth/refresh", withAuth: false);
            req.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", refreshToken);

            using var resp = await _http.SendAsync(req);

            if (resp.StatusCode == HttpStatusCode.Unauthorized)
            {
                // Refresh token itself is expired or revoked — force re-login.
                NotifySessionExpired();
                return false;
            }

            if (!resp.IsSuccessStatusCode)
                return false; // Network/server error — don't force re-login

            var result = await resp.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions);
            if (result is null) return false;

            _settings.Set("auth_token", result.AccessToken);
            _settings.Set("refresh_token", result.RefreshToken);
            // A successful refresh re-arms the debounce so a future expiry notifies.
            ResetSessionExpiry();
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

    // All three helpers build a FRESH HttpRequestMessage for the initial call and,
    // after a successful refresh, for the retry — an HttpRequestMessage cannot be
    // reused, and the rebuilt request automatically picks up the NEW token from
    // settings via BuildRequest (Bug H1 + Bug C1 retry safety).
    private async Task<HttpResponseMessage> GetWithRefreshAsync(string url)
    {
        var resp = await _http.SendAsync(BuildRequest(HttpMethod.Get, url));
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.SendAsync(BuildRequest(HttpMethod.Get, url));
        }
        return resp;
    }

    private async Task<HttpResponseMessage> PostWithRefreshAsync<T>(string url, T body)
    {
        var resp = await _http.SendAsync(BuildRequest(HttpMethod.Post, url, JsonContent.Create(body)));
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.SendAsync(BuildRequest(HttpMethod.Post, url, JsonContent.Create(body)));
        }
        return resp;
    }

    private async Task<HttpResponseMessage> PutWithRefreshAsync<T>(string url, T body)
    {
        var resp = await _http.SendAsync(BuildRequest(HttpMethod.Put, url, JsonContent.Create(body)));
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.SendAsync(BuildRequest(HttpMethod.Put, url, JsonContent.Create(body)));
        }
        return resp;
    }

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
