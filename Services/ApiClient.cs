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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http, SettingsRepository settings)
    {
        _http = http;
        _settings = settings;
        ApplyBaseUrl();
        ApplyTenantHeader();
        ApplyAuthToken();
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

    public void ApplyTenantHeader()
    {
        _http.DefaultRequestHeaders.Remove("X-Tenant-ID");
        var tenant = _settings.Get("tenant_subdomain");
        if (!string.IsNullOrEmpty(tenant))
            _http.DefaultRequestHeaders.Add("X-Tenant-ID", tenant);
    }

    public void ApplyAuthToken()
    {
        var token = _settings.Get("auth_token");
        _http.DefaultRequestHeaders.Authorization = token is not null
            ? new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token)
            : null;
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

        // Build transactions that sum precisely to apiTotal.
        //
        // sale.PaidAmount   = what the customer physically paid (may include change overpayment)
        // sale.TotalAmount  = discounted cart total  (= subtotal - cart discount)
        // Fully paid        = PaidAmount >= TotalAmount
        //
        // If fully paid, send the whole apiTotal as a single non-debt transaction.
        // If partially paid (with a linked customer), split into a paid + debt pair.
        // If no customer exists, we cannot register debt → treat as fully paid on the server.

        var hasCustomer  = !string.IsNullOrEmpty(sale.CustomerRemoteUuid);
        var isFullyPaid  = sale.PaidAmount >= sale.TotalAmount;

        decimal paidPortion = isFullyPaid || !hasCustomer
            ? apiTotal
            : Math.Clamp(sale.PaidAmount, 0, apiTotal);
        decimal debtPortion = apiTotal - paidPortion;

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

        var response = await PostWithRefreshAsync("api/orders", order);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        return result?.Uuid ?? "";
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

            _settings.Set("auth_token", result.AccessToken);
            _settings.Set("refresh_token", result.RefreshToken);
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

    private async Task<HttpResponseMessage> PostWithRefreshAsync<T>(string url, T body)
    {
        var resp = await _http.SendAsync(BuildPost(url, body));
        if (resp.StatusCode == HttpStatusCode.Unauthorized && await TryRefreshTokenAsync())
        {
            resp.Dispose();
            resp = await _http.SendAsync(BuildPost(url, body));
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

    private static HttpRequestMessage BuildPost<T>(string url, T body) =>
        new(HttpMethod.Post, url) { Content = JsonContent.Create(body) };

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
