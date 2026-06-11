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

    // Defaults to true so older backend responses (without this field) keep
    // local customers active; incremental sync explicitly sends false for
    // soft-deleted customers so the client mirrors the tombstone.
    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

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

    // Container classification (CASH / CARD / BANK / ONLINE / SAFE / MOBILE_BANKING / OTHER).
    // Used to route per-method transactions to the correct cashbox.
    [JsonPropertyName("type")]
    public string? Type { get; set; }
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
    [JsonPropertyName("id")]
    public long Id { get; set; }

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

    // Backend rule: required when Stock > 0 (ProductServiceImpl.validateOpeningStockPayload).
    // Ignored server-side when Stock is null or zero, so we only set it when needed.
    [JsonPropertyName("openingWarehouseUuid")]
    public string? OpeningWarehouseUuid { get; set; }

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

    // Bug H1: associates this order with the POS shift it was rung up in, so the
    // backend Z-report (/api/pos/shifts/{uuid}/report) can reconcile the drawer.
    // BACKEND-COORDINATION: the API requirements doc does not yet define a shift
    // field on CreateOrderRequest, so this camelCase "shiftUuid" name (matching
    // the rest of the request) must be confirmed/wired on the Ham-Pos side.
    // Nullable + JsonIgnore-when-null so legacy pending sales (ShiftUuid == null)
    // serialize without the field instead of sending an empty string.
    [JsonPropertyName("shiftUuid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ShiftUuid { get; set; }
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

// ── POS Shift (Phase G.1) ─────────────────────────────────────────────────────
//
// Mirrors Ham-Pos `PosShiftResponse` / `OpenShiftRequest` / `CloseShiftRequest`
// / `PosShiftReportResponse`. UUIDs travel as strings; backend `BigDecimal`
// maps to C# `decimal`; backend `PosShiftStatus` enum is serialized as its
// name (`OPEN` / `CLOSED` / `CANCELLED`).

public class PosShiftResponse
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("branchUuid")]
    public string? BranchUuid { get; set; }

    [JsonPropertyName("cashboxUuid")]
    public string? CashboxUuid { get; set; }

    [JsonPropertyName("openedByUserUuid")]
    public string? OpenedByUserUuid { get; set; }

    [JsonPropertyName("closedByUserUuid")]
    public string? ClosedByUserUuid { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("openedAt")]
    public DateTime? OpenedAt { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTime? ClosedAt { get; set; }

    [JsonPropertyName("openingCashAmount")]
    public decimal? OpeningCashAmount { get; set; }

    [JsonPropertyName("expectedCashAmount")]
    public decimal? ExpectedCashAmount { get; set; }

    [JsonPropertyName("countedCashAmount")]
    public decimal? CountedCashAmount { get; set; }

    [JsonPropertyName("differenceAmount")]
    public decimal? DifferenceAmount { get; set; }

    [JsonPropertyName("openComment")]
    public string? OpenComment { get; set; }

    [JsonPropertyName("closeComment")]
    public string? CloseComment { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class OpenShiftRequest
{
    [JsonPropertyName("cashboxUuid")]
    public string CashboxUuid { get; set; } = "";

    [JsonPropertyName("openingCashAmount")]
    public decimal OpeningCashAmount { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public class CloseShiftRequest
{
    [JsonPropertyName("countedCashAmount")]
    public decimal CountedCashAmount { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }
}

public class PosShiftReportResponse
{
    [JsonPropertyName("shiftUuid")]
    public string ShiftUuid { get; set; } = "";

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    [JsonPropertyName("branchUuid")]
    public string? BranchUuid { get; set; }

    [JsonPropertyName("cashboxUuid")]
    public string? CashboxUuid { get; set; }

    [JsonPropertyName("openedByUserUuid")]
    public string? OpenedByUserUuid { get; set; }

    [JsonPropertyName("closedByUserUuid")]
    public string? ClosedByUserUuid { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("openedAt")]
    public DateTime? OpenedAt { get; set; }

    [JsonPropertyName("closedAt")]
    public DateTime? ClosedAt { get; set; }

    [JsonPropertyName("openingCashAmount")]
    public decimal? OpeningCashAmount { get; set; }

    // Stored at close time (null for OPEN shifts).
    [JsonPropertyName("storedExpectedCashAmount")]
    public decimal? StoredExpectedCashAmount { get; set; }

    [JsonPropertyName("countedCashAmount")]
    public decimal? CountedCashAmount { get; set; }

    [JsonPropertyName("storedDifferenceAmount")]
    public decimal? StoredDifferenceAmount { get; set; }

    // Live computed from cashbox ledger.
    [JsonPropertyName("computedExpectedCashAmount")]
    public decimal? ComputedExpectedCashAmount { get; set; }

    [JsonPropertyName("computedDifferenceAmount")]
    public decimal? ComputedDifferenceAmount { get; set; }

    [JsonPropertyName("cashSalesAmount")]
    public decimal? CashSalesAmount { get; set; }

    [JsonPropertyName("cashbackAmount")]
    public decimal? CashbackAmount { get; set; }

    [JsonPropertyName("refundAmount")]
    public decimal? RefundAmount { get; set; }

    [JsonPropertyName("debtPaymentAmount")]
    public decimal? DebtPaymentAmount { get; set; }

    [JsonPropertyName("cashInAmount")]
    public decimal? CashInAmount { get; set; }

    [JsonPropertyName("cashOutAmount")]
    public decimal? CashOutAmount { get; set; }

    [JsonPropertyName("netCashMovementAmount")]
    public decimal? NetCashMovementAmount { get; set; }

    [JsonPropertyName("transactionCount")]
    public long TransactionCount { get; set; }

    [JsonPropertyName("orderCount")]
    public long OrderCount { get; set; }

    [JsonPropertyName("refundCount")]
    public long RefundCount { get; set; }

    [JsonPropertyName("debtPaymentCount")]
    public long DebtPaymentCount { get; set; }

    [JsonPropertyName("cashInCount")]
    public long CashInCount { get; set; }

    [JsonPropertyName("cashOutCount")]
    public long CashOutCount { get; set; }

    [JsonPropertyName("hasDifference")]
    public bool HasDifference { get; set; }

    [JsonPropertyName("hasTransactionsOutsideShift")]
    public bool HasTransactionsOutsideShift { get; set; }

    [JsonPropertyName("outsideShiftTransactionCount")]
    public long OutsideShiftTransactionCount { get; set; }

    [JsonPropertyName("outsideShiftNetAmount")]
    public decimal? OutsideShiftNetAmount { get; set; }

    [JsonPropertyName("closed")]
    public bool Closed { get; set; }
}

// ── Inventory adjustment (Phase I.1) ──────────────────────────────────────────
//
// Mirrors Ham-Pos `InventoryAdjustmentRequest` / `InventoryAdjustmentResponse`.
// Direction is the AdjustmentDirection enum (IN | OUT) — quantity must always
// be positive; OUT means decrement stock. Backend enforces:
//   • quantity >= 0.001
//   • reason @NotBlank
//   • warehouseUuid + productUuid + direction @NotNull
// Permission required on the backend: INVENTORY_MANAGEMENT (held by ADMIN role,
// not CASHIER) — desktop surfaces a parsed 403 if a non-admin attempts a kirim.

public class CreateInventoryAdjustmentRequest
{
    [JsonPropertyName("warehouseUuid")]
    public string WarehouseUuid { get; set; } = "";

    [JsonPropertyName("productUuid")]
    public string ProductUuid { get; set; } = "";

    // Backend AdjustmentDirection — must be the string "IN" or "OUT".
    [JsonPropertyName("direction")]
    public string Direction { get; set; } = "IN";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("unitCost")]
    public decimal? UnitCost { get; set; }

    [JsonPropertyName("idempotencyKey")]
    public string? IdempotencyKey { get; set; }
}

public class InventoryAdjustmentResponse
{
    [JsonPropertyName("movementUuid")]
    public string MovementUuid { get; set; } = "";

    [JsonPropertyName("movementNo")]
    public string? MovementNo { get; set; }

    [JsonPropertyName("warehouseUuid")]
    public string? WarehouseUuid { get; set; }

    [JsonPropertyName("productUuid")]
    public string? ProductUuid { get; set; }

    [JsonPropertyName("unitUuid")]
    public string? UnitUuid { get; set; }

    [JsonPropertyName("quantity")]
    public decimal? Quantity { get; set; }

    [JsonPropertyName("unitCost")]
    public decimal? UnitCost { get; set; }

    [JsonPropertyName("beforeQuantity")]
    public decimal? BeforeQuantity { get; set; }

    [JsonPropertyName("afterQuantity")]
    public decimal? AfterQuantity { get; set; }

    [JsonPropertyName("weightedAverageCostAfter")]
    public decimal? WeightedAverageCostAfter { get; set; }

    [JsonPropertyName("occurredAt")]
    public DateTime? OccurredAt { get; set; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; set; }
}

// ── Order list (Sale History from server) ────────────────────────────────────
//
// Mirrors the backend OrderResponse fields that are useful for history display.
// The list endpoint is GET /api/orders with optional date range and pagination.

public class OrderListDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("orderNumber")]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal? TotalAmount { get; set; }

    [JsonPropertyName("paidAmount")]
    public decimal? PaidAmount { get; set; }

    [JsonPropertyName("debtAmount")]
    public decimal? DebtAmount { get; set; }

    [JsonPropertyName("discountAmount")]
    public decimal? DiscountAmount { get; set; }

    [JsonPropertyName("paymentType")]
    public string? PaymentType { get; set; }

    [JsonPropertyName("customerUuid")]
    public string? CustomerUuid { get; set; }

    [JsonPropertyName("comment")]
    public string? Comment { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("createdDate")]
    public DateTime? CreatedDate { get; set; }

    [JsonPropertyName("isPos")]
    public bool? IsPos { get; set; }
}

// ── Shift cash movement (Phase 11.3) ─────────────────────────────────────────
//
// Mirrors Ham-Pos `ShiftCashMovementRequest` / `ShiftCashMovementResponse`.
// `amount` must be strictly positive (> 0.01). The backend negates it for
// cash-out so the request body is the same shape for both directions.

public class ShiftCashMovementRequest
{
    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("reason")]
    public string Reason { get; set; } = "";
}

public class ShiftCashMovementResponse
{
    [JsonPropertyName("shiftUuid")]
    public string? ShiftUuid { get; set; }

    [JsonPropertyName("cashboxUuid")]
    public string? CashboxUuid { get; set; }

    [JsonPropertyName("transactionUuid")]
    public string? TransactionUuid { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("amount")]
    public decimal Amount { get; set; }

    [JsonPropertyName("cashboxBalanceAfter")]
    public decimal? CashboxBalanceAfter { get; set; }

    [JsonPropertyName("currencyCode")]
    public string? CurrencyCode { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("performedAt")]
    public DateTime? PerformedAt { get; set; }
}

// ── Returns / Refund ─────────────────────────────────────────────────────────

public class ReturnOrderRequest
{
    [JsonPropertyName("warehouseUuid")]
    public string WarehouseUuid { get; set; } = "";

    [JsonPropertyName("cashboxUuid")]
    public string? CashboxUuid { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("items")]
    public List<ReturnOrderItemRequest> Items { get; set; } = [];

    [JsonPropertyName("idempotencyKey")]
    public string IdempotencyKey { get; set; } = "";
}

public class ReturnOrderItemRequest
{
    [JsonPropertyName("productUuid")]
    public string ProductUuid { get; set; } = "";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }
}

public class ReturnOrderResponse
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("orderUuid")]
    public string OrderUuid { get; set; } = "";

    [JsonPropertyName("totalReturnAmount")]
    public decimal TotalReturnAmount { get; set; }

    [JsonPropertyName("cashRefundAmount")]
    public decimal CashRefundAmount { get; set; }

    [JsonPropertyName("debtReductionAmount")]
    public decimal DebtReductionAmount { get; set; }

    [JsonPropertyName("returnedAt")]
    public DateTime? ReturnedAt { get; set; }

    [JsonPropertyName("orderStatus")]
    public string? OrderStatus { get; set; }

    [JsonPropertyName("items")]
    public List<ReturnOrderItemResponse> Items { get; set; } = [];
}

public class ReturnOrderItemResponse
{
    [JsonPropertyName("productUuid")]
    public string ProductUuid { get; set; } = "";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("unitPrice")]
    public decimal UnitPrice { get; set; }

    [JsonPropertyName("refundAmount")]
    public decimal RefundAmount { get; set; }
}

// ── Order detail (GET /api/orders/{uuid}) ────────────────────────────────────
//
// Mirrors the backend OrderResponse for the single-order detail endpoint.
// Used by ReturnOrderWindow to load item lines for the return flow.
// Note: stockId is a Long warehouse PK on the backend; warehouseUuid is
// resolved by matching it against the warehouses list (WarehouseDto.Id).

public class OrderDetailDto
{
    [JsonPropertyName("uuid")]
    public string Uuid { get; set; } = "";

    [JsonPropertyName("orderNumber")]
    public string? OrderNumber { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal? TotalAmount { get; set; }

    [JsonPropertyName("paidAmount")]
    public decimal? PaidAmount { get; set; }

    [JsonPropertyName("debtAmount")]
    public decimal? DebtAmount { get; set; }

    [JsonPropertyName("branchUuid")]
    public string? BranchUuid { get; set; }

    [JsonPropertyName("customerUuid")]
    public string? CustomerUuid { get; set; }

    // Warehouse numeric PK — maps to WarehouseDto.Id for UUID resolution.
    [JsonPropertyName("stockId")]
    public long? StockId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime? CreatedAt { get; set; }

    [JsonPropertyName("paymentType")]
    public string? PaymentType { get; set; }

    [JsonPropertyName("items")]
    public List<OrderDetailItemDto> Items { get; set; } = [];
}

public class OrderDetailItemDto
{
    [JsonPropertyName("productUuid")]
    public string ProductUuid { get; set; } = "";

    [JsonPropertyName("quantity")]
    public decimal Quantity { get; set; }

    [JsonPropertyName("price")]
    public decimal Price { get; set; }

    [JsonPropertyName("discountPrice")]
    public decimal DiscountPrice { get; set; }

    [JsonPropertyName("totalAmount")]
    public decimal TotalAmount { get; set; }
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
