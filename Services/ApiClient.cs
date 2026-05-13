using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using PosSystem.Core.DTOs;
using PosSystem.Core.Entities;
using PosSystem.Data.Repositories;

namespace PosSystem.Services;

public class ApiClient
{
    private readonly HttpClient _http;
    private readonly SettingsRepository _settings;
    private const string DefaultBaseUrl = "https://shefpos.uz";

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
        if (string.IsNullOrWhiteSpace(url) || IsLocalBaseUrl(url))
        {
            url = DefaultBaseUrl;
            _settings.Set("api_base_url", url);
        }

        if (!Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri)) return;
        if (_http.BaseAddress == uri) return;
        _http.BaseAddress = uri;
    }

    private static bool IsLocalBaseUrl(string url)
    {
        return Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri) &&
               (uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                uri.Host.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase));
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
        var resp = await _http.GetAsync("api/measurements?page=0&size=200");
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
            var resp = await _http.GetAsync(
                $"api/products?is_pos=true&page={page}&size=200");
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
        var response = await _http.PostAsJsonAsync("api/products", request);
        await EnsureSuccessAsync(response);
        return await response.Content.ReadFromJsonAsync<ProductDto>(JsonOptions)
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
            var resp = await _http.GetAsync($"api/customers?page={page}&size=200");
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

    // ── Reference data ────────────────────────────────────────────────────────

    public async Task<List<BranchDto>> GetBranchesAsync()
    {
        var resp = await _http.GetAsync("api/branches?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<BranchDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<CashboxDto>> GetCashboxesAsync()
    {
        var resp = await _http.GetAsync("api/cashboxes?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<CashboxDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<PriceListDto>> GetPriceListsAsync()
    {
        var resp = await _http.GetAsync("api/price-lists?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<PriceListDto>>(JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<ProductTypeDto>> GetProductTypesAsync()
    {
        var resp = await _http.GetAsync("api/product-types?page=0&size=50");
        await EnsureSuccessAsync(resp);
        var result = await resp.Content.ReadFromJsonAsync<PageResponse<ProductTypeDto>>(JsonOptions);
        return result?.Content ?? [];
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

        var response = await _http.PostAsJsonAsync("api/orders", order);
        await EnsureSuccessAsync(response);
        var result = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        return result?.Uuid ?? "";
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
        // Try to extract a human-readable message from the JSON error body.
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
