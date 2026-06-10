using System.Text.Json.Serialization;

namespace PosSystem.Core.DTOs;

// ── Auth ──────────────────────────────────────────────────────────────────────

public class LoginResponse
{
    [JsonPropertyName("accessToken")]
    public string AccessToken { get; set; } = "";

    [JsonPropertyName("refreshToken")]
    public string RefreshToken { get; set; } = "";

    [JsonPropertyName("user")]
    public UserInfo User { get; set; } = new();

    [JsonPropertyName("tenant")]
    public TenantInfo? Tenant { get; set; }
}

public class UserInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = "";

    [JsonPropertyName("role")]
    public string Role { get; set; } = "";
}

public class TenantInfo
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

// ── Pagination ────────────────────────────────────────────────────────────────

public class PageResponse<T>
{
    [JsonPropertyName("content")]
    public List<T> Content { get; set; } = [];

    [JsonPropertyName("totalElements")]
    public long TotalElements { get; set; }

    [JsonPropertyName("totalPages")]
    public int TotalPages { get; set; }

    [JsonPropertyName("number")]
    public int Number { get; set; }
}

// ── Products ──────────────────────────────────────────────────────────────────

public class ProductDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("measurementShortName")]
    public string? MeasurementShortName { get; set; }

    [JsonPropertyName("measurementName")]
    public string? MeasurementName { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("cost")]
    public decimal Cost { get; set; }

    [JsonPropertyName("stock")]
    public decimal Stock { get; set; }

    [JsonPropertyName("isPos")]
    public bool IsPos { get; set; } = true;

    [JsonPropertyName("isDelete")]
    public bool IsDelete { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("barcodes")]
    public List<ProductBarcodeDto> Barcodes { get; set; } = [];

    [JsonPropertyName("prices")]
    public List<ProductPriceDto> Prices { get; set; } = [];
}

public class ProductBarcodeDto
{
    [JsonPropertyName("barcode")]
    public string Barcode { get; set; } = "";
}

public class ProductPriceDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("priceListId")]
    public long PriceListId { get; set; }

    [JsonPropertyName("cashCurrency")]
    public long CashCurrency { get; set; }

    [JsonPropertyName("cashPrice")]
    public decimal CashPrice { get; set; }
}

// ── Customers ─────────────────────────────────────────────────────────────────

public class CustomerDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }

    [JsonPropertyName("totalDebt")]
    public decimal TotalDebt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class CreateCustomerRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

public class UpdateCustomerRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("phone")]
    public string? Phone { get; set; }

    [JsonPropertyName("address")]
    public string? Address { get; set; }
}

// ── Reference data ────────────────────────────────────────────────────────────

public class ProductTypeDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

public class BranchDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
}

public class CashboxDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "";
}

public class PriceListDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    // Bug H3: real backend currency id, if the backend supplies one. When present
    // it is the single source of truth for the order's currencyId; when absent we
    // fall back to the UZS=1 / USD=2 code-based guess.
    [JsonPropertyName("currencyId")]
    public long? CurrencyId { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

public class WarehouseDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

// ── Measurements ─────────────────────────────────────────────────────────────

public class MeasurementDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("shortName")]
    public string ShortName { get; set; } = "";

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;
}

// ── Product create ────────────────────────────────────────────────────────────

public class CreateProductPriceRequest
{
    [JsonPropertyName("priceListId")]
    public long PriceListId { get; set; }

    [JsonPropertyName("cashPrice")]
    public decimal CashPrice { get; set; }

    [JsonPropertyName("cashCurrency")]
    public long CashCurrency { get; set; }
}

public class CreateProductRequest
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("measurementUuid")]
    public string MeasurementUuid { get; set; } = "";

    [JsonPropertyName("type")]
    public long Type { get; set; }

    [JsonPropertyName("price")]
    public decimal? Price { get; set; }

    [JsonPropertyName("cost")]
    public decimal? Cost { get; set; }

    [JsonPropertyName("stock")]
    public decimal? Stock { get; set; }

    [JsonPropertyName("barcode")]
    public string? Barcode { get; set; }

    [JsonPropertyName("isPos")]
    public bool IsPos { get; set; } = true;

    [JsonPropertyName("prices")]
    public List<CreateProductPriceRequest>? Prices { get; set; }
}

// ── Order (sale sync) ─────────────────────────────────────────────────────────

public class CreateOrderRequest
{
    [JsonPropertyName("branchUuid")]
    public string BranchUuid { get; set; } = "";

    [JsonPropertyName("customerUuid")]
    public string? CustomerUuid { get; set; }

    [JsonPropertyName("currencyId")]
    public long CurrencyId { get; set; }

    [JsonPropertyName("paymentType")]
    public string PaymentType { get; set; } = "CASH";

    [JsonPropertyName("isPos")]
    public bool IsPos { get; set; } = true;

    [JsonPropertyName("dealType")]
    public int DealType { get; set; } = 0;

    [JsonPropertyName("deliveryType")]
    public string DeliveryType { get; set; } = "SELF";

    [JsonPropertyName("priceListId")]
    public long PriceListId { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("items")]
    public List<CreateOrderItemRequest> Items { get; set; } = [];

    [JsonPropertyName("transactions")]
    public List<CreateTransactionRequest> Transactions { get; set; } = [];

    // Bug C1: makes the order POST idempotent. Populated with the sale's stable
    // LocalId so a retry after a lost response (timeout) or a 401-refresh re-POST
    // is deduplicated server-side instead of creating a duplicate order.
    // Field name/serialization mirrors DebtPaymentRequest.IdempotencyKey exactly.
    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }
}

public class CreateOrderItemRequest
{
    [JsonPropertyName("productUuid")]
    public string ProductUuid { get; set; } = "";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("discountPrice")]
    public decimal DiscountPrice { get; set; }
}

public class CreateTransactionRequest
{
    [JsonPropertyName("cashboxUuid")]
    public string CashboxUuid { get; set; } = "";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("currencyId")]
    public long CurrencyId { get; set; }

    [JsonPropertyName("isDebt")]
    public bool IsDebt { get; set; }

    [JsonPropertyName("isCashback")]
    public bool IsCashback { get; set; }
}

public class OrderResponse
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("orderNumber")]
    public string OrderNumber { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";
}

// ── Debt payment ──────────────────────────────────────────────────────────────

public class DebtPaymentRequest
{
    [JsonPropertyName("customerUuid")]
    public string CustomerUuid { get; set; } = "";

    [JsonPropertyName("branchUuid")]
    public string BranchUuid { get; set; } = "";

    [JsonPropertyName("cashboxUuid")]
    public string CashboxUuid { get; set; } = "";

    [JsonPropertyName("currencyCode")]
    public string CurrencyCode { get; set; } = "UZS";

    [JsonPropertyName("paymentType")]
    public string PaymentType { get; set; } = "CASH";

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }
}
