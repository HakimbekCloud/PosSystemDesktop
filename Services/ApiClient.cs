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

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ApiClient(HttpClient http, SettingsRepository settings)
    {
        _http = http;
        _settings = settings;
        ApplyBaseUrl();
        ApplyAuthToken();
    }

    public void ApplyBaseUrl()
    {
        var url = _settings.Get("api_base_url");
        if (string.IsNullOrWhiteSpace(url)) return;
        if (Uri.TryCreate(url.TrimEnd('/') + "/", UriKind.Absolute, out var uri))
            _http.BaseAddress = uri;
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
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<LoginResponse>(JsonOptions)
               ?? throw new InvalidOperationException("Server javobi noto'g'ri");
    }

    // ── Products ──────────────────────────────────────────────────────────────

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        var all = new List<ProductDto>();
        int page = 0;
        int totalPages;

        do
        {
            var result = await _http.GetFromJsonAsync<PageResponse<ProductDto>>(
                $"api/products?is_pos=true&page={page}&size=200", JsonOptions);

            if (result is null) break;
            all.AddRange(result.Content);
            totalPages = result.TotalPages;
            page++;
        }
        while (page < totalPages && page < 10); // max 10 pages safety cap

        return all;
    }

    // ── Customers ─────────────────────────────────────────────────────────────

    public async Task<List<CustomerDto>> GetCustomersAsync()
    {
        var all = new List<CustomerDto>();
        int page = 0;
        int totalPages;

        do
        {
            var result = await _http.GetFromJsonAsync<PageResponse<CustomerDto>>(
                $"api/customers?page={page}&size=200", JsonOptions);

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
        var result = await _http.GetFromJsonAsync<PageResponse<BranchDto>>(
            "api/branches?page=0&size=50", JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<CashboxDto>> GetCashboxesAsync()
    {
        var result = await _http.GetFromJsonAsync<PageResponse<CashboxDto>>(
            "api/cashboxes?page=0&size=50", JsonOptions);
        return result?.Content ?? [];
    }

    public async Task<List<PriceListDto>> GetPriceListsAsync()
    {
        var result = await _http.GetFromJsonAsync<PageResponse<PriceListDto>>(
            "api/price-lists?page=0&size=50", JsonOptions);
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

        var order = new CreateOrderRequest
        {
            BranchUuid   = branchUuid,
            CustomerUuid = string.IsNullOrEmpty(sale.CustomerRemoteUuid)
                               ? null : sale.CustomerRemoteUuid,
            CurrencyId   = currencyId,
            PaymentType  = paymentType,
            IsPos        = true,
            DealType     = 0,
            DeliveryType = "SELF",
            PriceListId  = priceListId,
            Comment      = string.IsNullOrEmpty(sale.Note) ? null : sale.Note,
            Items = sale.Items
                .Where(i => !string.IsNullOrEmpty(i.ProductRemoteUuid))
                .Select(i => new CreateOrderItemRequest
                {
                    ProductUuid  = i.ProductRemoteUuid,
                    Quantity     = i.Quantity,
                    Price        = i.Price,
                    DiscountPrice = i.Discount
                }).ToList(),
            Transactions =
            [
                new CreateTransactionRequest
                {
                    CashboxUuid = cashboxUuid,
                    Amount      = sale.PaidAmount,
                    CurrencyId  = currencyId,
                    IsDebt      = sale.PaidAmount < sale.TotalAmount,
                    IsCashback  = false
                }
            ]
        };

        var response = await _http.PostAsJsonAsync("api/orders", order);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        return result?.Uuid ?? "";
    }
}
