# ShefPos — Backend API Talablari (WPF Desktop + Admin Panel)

> **Maqsad:** WPF desktop POS (`PosSystem/`) va uning ichidagi 8 bo'limli **Admin Panel** (`Views/Admin/`) to'liq ishlashi uchun Ham-Pos (Spring Boot) backend tomonidan kerak bo'ladigan barcha REST endpointlarini, DTO'larini, request/response namunalarini bir joyda yig'gan dokumentatsiya.
>
> **Status legendasi:**
> - **[MAVJUD]** — backend'da hozir bor, frontend'da ham foydalanilmoqda.
> - **[MAVJUD-FOYDALANILMAGAN]** — backend'da bor, lekin WPF hali chaqirmaydi.
> - **[YANGI]** — backend'da yo'q, qo'shilishi kerak.
>
> **Asoslar:**
> - **Base URL:** `https://shefpos.uz` (configurable, `App.config` → `api_base_url`).
> - **Tenant header:** har bir so'rovda `X-Tenant-ID: <subdomain>`.
> - **Auth header:** `Authorization: Bearer <access_token>`.
> - **Content-Type:** `application/json; charset=utf-8`.
> - **Sahifalash:** Spring `Pageable` — `?page=0&size=20&sort=field,asc`. Response shakli: `PageResponse<T>` (`content`, `totalElements`, `totalPages`, `number`).
> - **Pul birligi:** `BigDecimal` (Java) ↔ `decimal` (C#) ↔ JSON `number` (string emas).
> - **UUID:** server tomonidagi unikal kalit; WPF lokal yozuvlarda `RemoteUuid` deb saqlanadi.
> - **Idempotency:** har bir buyurtma/qaytarish/qarz to'lovi `idempotencyKey` (UUID v4) bilan keladi — bir xil kalit 2-marta yuborilsa server bir xil natija qaytarishi shart.

---

## 0. Mundarija

1. [Asosiy konventsiyalar](#1-asosiy-konventsiyalar)
2. [Auth (Autentifikatsiya)](#2-auth)
3. [Reference data (Filiallar, Kassalar, Narx ro'yxati, Birliklar)](#3-reference-data)
4. [Mahsulotlar](#4-mahsulotlar)
5. [Kategoriyalar](#5-kategoriyalar)
6. [Ombor / Inventarizatsiya](#6-ombor)
7. [Sotuv / Zakazlar](#7-sotuv)
8. [Mijozlar](#8-mijozlar)
9. [Yetkazib beruvchilar (Suppliers)](#9-suppliers)
10. [Xodimlar](#10-xodimlar)
11. [Hisobotlar](#11-hisobotlar)
12. [Sozlamalar](#12-sozlamalar)
13. [Audit va API loglar](#13-audit)
14. [Xato javoblar (Error response)](#14-errors)

---

<a id="1-asosiy-konventsiyalar"></a>
## 1. Asosiy konventsiyalar

### 1.1 Sahifalash (Pageable)

Har qanday ro'yxat endpoint quyidagi query parametrlarni qabul qiladi:

| Param | Tur | Default | Tavsif |
|---|---|---|---|
| `page` | int | 0 | Sahifa raqami (0-dan) |
| `size` | int | 20 | Sahifadagi yozuvlar soni (max 200) |
| `sort` | string | — | `field,asc` yoki `field,desc` (bir nechta sort qatordan-qator qabul qilinadi) |

**Response shakli** (`PageResponse<T>`):

```json
{
  "content": [ /* T turidagi elementlar */ ],
  "totalElements": 1842,
  "totalPages": 93,
  "number": 0,
  "size": 20,
  "first": true,
  "last": false
}
```

### 1.2 Umumiy timestamp format

`yyyy-MM-dd'T'HH:mm:ss[.SSS]` (ISO 8601, UTC). C# tomondan `DateTime.ToUniversalTime().ToString("O")`.

### 1.3 Standart audit maydonlari

Har bir domain entity uchun response'da:

```json
{
  "uuid": "…",
  "createdAt": "2026-05-17T08:21:14.532Z",
  "updatedAt": "2026-05-17T09:14:08.221Z",
  "active": true
}
```

---

<a id="2-auth"></a>
## 2. Auth (Autentifikatsiya)

### 2.1 `POST /api/v1/auth/login` **[MAVJUD]**

Tenant foydalanuvchisini kiritadi, access + refresh JWT qaytaradi.

**Request:**
```json
{
  "username": "kassir01",
  "password": "Pa$$w0rd"
}
```

**Response 200:**
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiJ9…",
  "refreshToken": "eyJhbGciOiJIUzI1NiJ9…",
  "user": {
    "id": 14,
    "username": "kassir01",
    "role": "CASHIER",
    "permissions": ["ORDER_CREATE", "PRODUCT_VIEW", "CUSTOMER_VIEW"]
  },
  "tenant": { "id": 7, "name": "ShefPos Demo MChJ" }
}
```

**Error 401:** `{"message":"Login yoki parol noto'g'ri"}`

### 2.2 `POST /api/v1/auth/refresh` **[MAVJUD]**

`Authorization: Bearer <refreshToken>` header bilan yuboriladi (body bo'sh). Yangi access+refresh token qaytaradi (yuqoridagi `TokenResponse` formatida).

### 2.3 `POST /api/v1/auth/logout` **[MAVJUD-FOYDALANILMAGAN]**

Bearer access token bilan. Server bu sessionni revoke qiladi.

**Response 200:** `{"message":"logged out"}`

### 2.4 `GET /api/v1/auth/sessions` **[MAVJUD-FOYDALANILMAGAN]**

Joriy user'ning faol sessionlari ro'yxati — Sozlamalar → Xavfsizlik tab uchun.

**Response:**
```json
[
  {
    "id": 102,
    "ipAddress": "192.168.1.34",
    "userAgent": "PosSystem/1.0 WPF",
    "startedAt": "2026-05-17T08:00:00Z",
    "lastSeenAt": "2026-05-17T09:14:21Z",
    "revoked": false
  }
]
```

### 2.5 `DELETE /api/v1/auth/sessions/{sessionId}` **[MAVJUD-FOYDALANILMAGAN]**
204 No Content. Sessionni revoke qiladi.

---

<a id="3-reference-data"></a>
## 3. Reference data

WPF ilk bor login bo'lganda bularni cache qiladi (`SyncService.SyncReferenceDataAsync`).

### 3.1 Filiallar — `/api/branches`

| Method | Path | Status |
|---|---|---|
| `GET`    | `/api/branches?page=0&size=50&name=…` | **[MAVJUD]** |
| `POST`   | `/api/branches` | **[MAVJUD-FOYDALANILMAGAN]** |
| `GET`    | `/api/branches/{uuid}` | **[MAVJUD-FOYDALANILMAGAN]** |
| `PUT`    | `/api/branches/{uuid}` | **[MAVJUD-FOYDALANILMAGAN]** |
| `DELETE` | `/api/branches/{uuid}` | **[MAVJUD-FOYDALANILMAGAN]** |

**`BranchResponseDTO`:**
```json
{
  "id": 3,
  "uuid": "c9a02b6e-…",
  "organizationUuid": "0a13…",
  "name": "Chilonzor",
  "address": "Bunyodkor 18A",
  "phone": "+998 71 200 18 18",
  "active": true,
  "tenantId": "shefpos",
  "createdAt": "2026-01-12T08:11:02Z"
}
```

**`BranchCreateDTO`:**
```json
{
  "organizationUuid": "0a13-…",
  "name": "Mirobod",
  "address": "Amir Temur 22",
  "phone": "+998 71 234 56 78"
}
```

### 3.2 Kassalar — `/api/cashboxes` **[MAVJUD]**

| Method | Path |
|---|---|
| `GET` | `/api/cashboxes?page=…&size=…` |
| `POST` | `/api/cashboxes` |

**`CashboxResponse`:**
```json
{
  "uuid": "1d5b…",
  "branchUuid": "c9a02b6e-…",
  "name": "Asosiy kassa",
  "currencyCode": "UZS",
  "balance": 12450000.00
}
```

**`CreateCashboxRequest`:**
```json
{
  "branchUuid": "c9a02b6e-…",
  "name": "Asosiy kassa",
  "currencyCode": "UZS"
}
```

### 3.3 Narx ro'yxati — `/api/price-lists` **[MAVJUD]**

Standart CRUD (`PriceListCreateDTO`, `PriceListUpdateDTO`, `PriceListResponseDTO`).

```json
{
  "id": 1,
  "uuid": "…",
  "name": "POS narxlari",
  "currency": "UZS",
  "active": true
}
```

### 3.4 Birliklar — `/api/measurements` **[MAVJUD]**

```json
{
  "uuid": "…",
  "name": "Kilogramm",
  "shortName": "kg",
  "precision": 3,
  "active": true
}
```

### 3.5 Omborlar — `/api/warehouses` **[MAVJUD]**

```json
{
  "uuid": "…",
  "name": "Markaziy ombor",
  "address": "Sergeli, 14-uy",
  "active": true
}
```

### 3.6 Mahsulot turlari — `/api/product-types` **[MAVJUD]**

```json
{
  "id": 1,
  "uuid": "…",
  "name": "Ichimliklar",
  "description": "Suv, soda, sok",
  "active": true
}
```

### 3.7 Brendlar — `/api/brands` **[MAVJUD-FOYDALANILMAGAN]**

```json
{
  "uuid": "…",
  "name": "Coca-Cola",
  "description": "Atlanta, USA",
  "active": true
}
```

### 3.8 Tashkilotlar — `/api/organizations` **[MAVJUD-FOYDALANILMAGAN]**

```json
{
  "uuid": "…",
  "name": "ShefPos Demo MChJ",
  "description": "Bosh tashkilot",
  "active": true
}
```

---

<a id="4-mahsulotlar"></a>
## 4. Mahsulotlar (Admin → Mahsulotlar, POS savatcha)

### 4.1 `GET /api/products` **[MAVJUD]**

Query params: `q`, `name`, `barcode`, `price_list`, `is_pos`, `page`, `size`.

**`ProductResponseDTO`:**
```json
{
  "uuid": "5fa42-…",
  "name": "Coca-Cola 1.5L",
  "description": "",
  "ikpu": "10101001001000000",
  "cost": 9800.00,
  "stock": 84.000,
  "ndsType": 1,
  "type": 1,
  "measurementUuid": "…",
  "measurementName": "Dona",
  "measurementShortName": "dona",
  "brandUuid": "…",
  "brandName": "Coca-Cola",
  "vatPercent": 12.00,
  "useMark": false,
  "isFavourite": false,
  "isDelete": false,
  "isPos": true,
  "barcode": "5449000000996",
  "price": 14000.00,
  "prices": [
    { "id": 8, "priceListId": 1, "cashCurrency": 1, "cashPrice": 14000.00 }
  ],
  "barcodes": [
    { "id": 4, "barcode": "5449000000996" }
  ],
  "createdAt": "…",
  "updatedAt": "…"
}
```

### 4.2 `POST /api/products` **[MAVJUD]**

**`ProductCreateDTO`:**
```json
{
  "name": "Pepsi 1.5L",
  "measurementUuid": "…",
  "type": 1,
  "brandUuid": "…",
  "cost": 9500.00,
  "stock": 60.0,
  "openingWarehouseUuid": "…",
  "openingIdempotencyKey": "c64f-…",
  "barcode": "5410188006767",
  "isPos": true,
  "prices": [
    { "priceListId": 1, "cashCurrency": 1, "cashPrice": 13500.00 }
  ]
}
```

Server kamida `barcode` yoki `barcodes`, va `price` yoki `prices`'dan birini talab qiladi.

### 4.3 `PUT /api/products/{uuid}` **[MAVJUD-FOYDALANILMAGAN]**

`ProductUpdateDTO` (CreateDTO bilan deyarli bir xil, `openingWarehouseUuid` yo'q).

### 4.4 `GET /api/products/{uuid}` **[MAVJUD-FOYDALANILMAGAN]**

### 4.5 `DELETE /api/products/{uuid}` **[MAVJUD-FOYDALANILMAGAN]**

Soft delete (`isDelete=true`).

### 4.6 `POST /api/products/import` **[YANGI]**

Excel/CSV importi — admin "Import" tugmasi uchun.

**Request:** `multipart/form-data` — `file` (xlsx/csv), `branchUuid`, `warehouseUuid`, `priceListId`.

**Response 200:**
```json
{
  "importId": "…",
  "totalRows": 1842,
  "successCount": 1820,
  "errorCount": 22,
  "errors": [
    { "row": 14, "field": "barcode", "message": "Barkod takrorlangan: 5449000000996" }
  ]
}
```

### 4.7 `GET /api/products/export?format=xlsx` **[YANGI]**

Stream'lashtirilgan `.xlsx` qaytaradi (`Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`).

### 4.8 `GET /api/products/low-stock?threshold=` **[YANGI]**

Kam qoldiqdagi mahsulotlar — Ombor bo'limidagi "Past qoldiq" tab uchun.

**Response:** `Page<ProductLowStockDTO>`:
```json
{
  "content": [
    {
      "productUuid": "…",
      "name": "Pepsi 1.5L",
      "code": "PP-001",
      "categoryName": "Ichimliklar",
      "stock": 5.0,
      "minStock": 10.0,
      "unit": "dona",
      "status": "low"   // low | out
    }
  ]
}
```

`minStock` Product entity'ga qo'shilishi kerak.

---

<a id="5-kategoriyalar"></a>
## 5. Kategoriyalar **[YANGI]**

Hozirgi backend `product-types`'ni "tur" sifatida ishlatadi, lekin Admin UI'da to'liq kategoriya daraxti (parent/child + ikon + rang) kerak.

### 5.1 `GET /api/categories?page=…` **[YANGI]**

```json
{
  "uuid": "…",
  "parentUuid": null,
  "name": "Ichimliklar",
  "icon": "drink",
  "color": "#155EEF",
  "productCount": 12,
  "active": true,
  "createdAt": "…"
}
```

### 5.2 `POST /api/categories` **[YANGI]**

```json
{
  "name": "Gazli ichimliklar",
  "parentUuid": "…",
  "icon": "drink",
  "color": "#155EEF"
}
```

### 5.3 `PUT /api/categories/{uuid}` **[YANGI]**
### 5.4 `DELETE /api/categories/{uuid}` **[YANGI]**
### 5.5 `GET /api/categories/tree` **[YANGI]**

Daraxt shaklida `{ uuid, name, children: [ … ] }`.

---

<a id="6-ombor"></a>
## 6. Ombor / Inventarizatsiya

### 6.1 `GET /api/inventory/stock?warehouseUuid=…&q=…` **[YANGI]**

Joriy qoldiq — Ombor bo'limidagi "Joriy qoldiq" tab uchun.

```json
{
  "content": [
    {
      "productUuid": "…",
      "name": "Coca-Cola 1.5L",
      "code": "CC-001",
      "categoryName": "Ichimliklar",
      "warehouseUuid": "…",
      "warehouseName": "Markaziy",
      "stock": 84.0,
      "minStock": 10.0,
      "unit": "dona",
      "value": 823200.00,
      "status": "active"  // active | low | out
    }
  ]
}
```

### 6.2 `GET /api/inventory/movements?warehouseUuid=…&type=…&from=…&to=…` **[YANGI]**

Harakat jurnali — "Harakatlar tarixi" tab uchun.

**Query params:** `warehouseUuid?`, `type?` (`INBOUND` | `OUTBOUND` | `TRANSFER` | `WRITEOFF` | `INVENTORY` | `SALE`), `from`, `to`, `q?` (doc.no / partner search), `page`, `size`.

```json
{
  "content": [
    {
      "uuid": "…",
      "documentNo": "KIR-00248",
      "type": "INBOUND",
      "partnerType": "SUPPLIER",
      "partnerName": "Coca-Cola Uzbekistan",
      "partnerUuid": "…",
      "itemsCount": 14,
      "totalAmount": 4820000.00,
      "createdByName": "D. Tursunov",
      "status": "DONE",
      "createdAt": "2026-05-13T14:22:00Z"
    }
  ]
}
```

### 6.3 `POST /api/inventory/inbound` **[YANGI]**

Yetkazib beruvchidan kirim qilish.

```json
{
  "warehouseUuid": "…",
  "supplierUuid": "…",
  "documentNo": "KIR-00249",
  "occurredAt": "2026-05-17T09:00:00Z",
  "items": [
    {
      "productUuid": "…",
      "quantity": 24,
      "unitCost": 9800.00
    }
  ],
  "comment": "Mart oyi yetkazma",
  "idempotencyKey": "uuid-v4"
}
```

**Response 201:** `InventoryDocumentResponse` (yuqoridagi `MovementRow` + `items[]`).

### 6.4 `POST /api/inventory/transfer` **[YANGI]**

Filiallar / omborlar orasidagi transfer.

```json
{
  "fromWarehouseUuid": "…",
  "toWarehouseUuid": "…",
  "documentNo": "TRF-00042",
  "items": [{ "productUuid": "…", "quantity": 6 }],
  "comment": "Markaziy → Chilonzor",
  "idempotencyKey": "…"
}
```

### 6.5 `POST /api/inventory/writeoff` **[YANGI]**

Yaroqsizlik / hisobdan chiqarish (akt).

```json
{
  "warehouseUuid": "…",
  "documentNo": "AKT-00011",
  "reason": "EXPIRED",   // EXPIRED | DAMAGED | OTHER
  "items": [{ "productUuid": "…", "quantity": 3, "unitCost": 14000 }],
  "comment": "Muddati o'tgan",
  "idempotencyKey": "…"
}
```

### 6.6 `POST /api/inventory/inventory-count` **[YANGI]**

Inventarizatsiya — haqiqiy qoldiqni server qoldig'i bilan tenglash.

```json
{
  "warehouseUuid": "…",
  "documentNo": "INV-00008",
  "items": [
    { "productUuid": "…", "actualQuantity": 82, "expectedQuantity": 84 }
  ],
  "idempotencyKey": "…"
}
```

### 6.7 `POST /api/inventory/adjustments` **[MAVJUD]**

Bir mahsulot uchun bitta tezkor tuzatish (allaqachon mavjud).

**`InventoryAdjustmentRequest`:**
```json
{
  "warehouseUuid": "…",
  "productUuid": "…",
  "quantity": -2.0,
  "reason": "DAMAGED",
  "comment": "Sinib qoldi",
  "unitCost": 9800.00,
  "idempotencyKey": "…"
}
```

### 6.8 `GET /api/inventory/alerts` **[YANGI]**

Kam qoldiq / tugagan mahsulotlar ogohlantirishlari.

```json
[
  {
    "severity": "danger",         // danger | warning
    "productUuid": "…",
    "productName": "Bug'doy non",
    "stock": 0,
    "minStock": 15,
    "unit": "dona",
    "message": "Bug'doy non tugagan. Yetkazib beruvchiga buyurtma berish kerak."
  }
]
```

### 6.9 `GET /api/inventory/kpi?warehouseUuid=…&from=…&to=…` **[YANGI]**

Ombor sahifasi yuqorisidagi KPI lar.

```json
{
  "totalStockValue": 184220000.00,
  "inboundAmount": 6900000.00,
  "outboundAmount": 284000.00,
  "alertsCount": 5
}
```

---

<a id="7-sotuv"></a>
## 7. Sotuv / Zakazlar

### 7.1 `POST /api/orders` **[MAVJUD]**

WPF checkout buni chaqiradi (`ApiClient.SyncSaleAsync`).

**`CreateOrderRequest`:**
```json
{
  "branchUuid": "…",
  "customerUuid": "…",
  "currencyId": 1,
  "paymentType": "CASH",
  "isPos": true,
  "dealType": 0,
  "deliveryType": "SELF",
  "priceListId": 1,
  "comment": "Mijoz qaytarib so'radi",
  "items": [
    {
      "productUuid": "…",
      "quantity": 2.0,
      "price": 14000.00,
      "discountPrice": 0
    }
  ],
  "transactions": [
    {
      "cashboxUuid": "…",
      "amount": 28000.00,
      "currencyId": 1,
      "isDebt": false,
      "isCashback": false
    }
  ]
}
```

**Invariant:** `sum(transactions.amount) == sum(items.price*items.quantity - items.discountPrice)`. Aks holda 400.

**`OrderResponse`:** to'liq sxema yuqoridagi DTO referansda; minimal jo'natma:
```json
{
  "uuid": "…",
  "orderNumber": "ZK-2026-00128",
  "status": "PAID",
  "totalAmount": 28000.00,
  "paidAmount": 28000.00,
  "debtAmount": 0.00,
  "items": [ … ],
  "payments": [ … ],
  "createdAt": "…"
}
```

**`paymentType` enum:** `CASH`, `CARD`, `TRANSFER`, `MIXED`, `CLICK`, `PAYME`, `UZUM`.
**`deliveryType` enum:** `SELF`, `COURIER`.

### 7.2 `GET /api/orders` **[MAVJUD]**

Filtrlar: `from`, `to`, `branchUuid`, `cashboxUuid`, `cashierId`, `status`, `paymentType`, `customerUuid`, `q` (zakaz raqami).

### 7.3 `GET /api/orders/{uuid}` **[MAVJUD]**

### 7.4 `POST /api/orders/{uuid}/payment` **[MAVJUD]**

Mavjud zakazga qo'shimcha to'lov.

**`OrderPaymentRequest`:**
```json
{
  "cashboxUuid": "…",
  "paymentType": "CASH",
  "amount": 5000.00,
  "currencyCode": "UZS",
  "paidAt": "2026-05-17T10:00:00Z",
  "idempotencyKey": "…"
}
```

### 7.5 `POST /api/orders/{uuid}/returns` **[MAVJUD]**

Zakaz bo'yicha qaytarish.

**`ReturnRequest`:**
```json
{
  "warehouseUuid": "…",
  "cashboxUuid": "…",
  "reason": "Mijoz qaytardi",
  "items": [{ "productUuid": "…", "quantity": 1 }],
  "idempotencyKey": "…"
}
```

### 7.6 `GET /api/orders/{uuid}/returns` **[MAVJUD]**

Ro'yxat — `List<ReturnResponse>`.

### 7.7 `DELETE /api/orders/{uuid}` **[YANGI]**

Tasdiqlanmagan (DRAFT) zakazni o'chirish.

---

<a id="8-mijozlar"></a>
## 8. Mijozlar

### 8.1 `GET /api/customers` **[MAVJUD]**

Query: `q`, `tier`, `hasDebt`, `branchUuid`, `page`, `size`.

### 8.2 `GET /api/customers/{uuid}` **[MAVJUD]**

### 8.3 `POST /api/customers` **[MAVJUD]**

**`CreateCustomerRequest`:**
```json
{
  "name": "Alisher Karimov",
  "phone": "+998901234567",
  "address": "Toshkent sh., Chilonzor"
}
```

### 8.4 `PUT /api/customers/{uuid}` **[MAVJUD]**

`UpdateCustomerRequest` — yuqoridagiga teng.

### 8.5 `POST /api/debt/pay` **[MAVJUD]**

**`DebtPaymentRequest`:**
```json
{
  "customerUuid": "…",
  "branchUuid": "…",
  "cashboxUuid": "…",
  "currencyCode": "UZS",
  "paymentType": "CASH",
  "amount": 240000.00,
  "idempotencyKey": "…"
}
```

Response — yangilangan `CustomerResponse`.

### 8.6 `GET /api/customers/{uuid}/orders?page=…` **[YANGI]**

Mijoz tarixi.

```json
{
  "content": [
    {
      "uuid": "…",
      "orderNumber": "ZK-2026-00128",
      "createdAt": "2026-05-14T14:22:00Z",
      "totalAmount": 280000.00,
      "paidAmount": 280000.00,
      "debtAmount": 0,
      "paymentType": "CASH"
    }
  ]
}
```

### 8.7 `GET /api/customers/{uuid}/debt-history?page=…` **[YANGI]**

```json
{
  "content": [
    {
      "uuid": "…",
      "type": "INCREASE",   // INCREASE | DECREASE
      "amount": 240000.00,
      "balanceAfter": 480000.00,
      "orderUuid": "…",
      "comment": "Zakaz #128 qarz",
      "createdAt": "…"
    }
  ]
}
```

### 8.8 `GET /api/customers/{uuid}/stats` **[YANGI]**

```json
{
  "totalSpent": 8240000.00,
  "ordersCount": 42,
  "averageCheck": 196190.00,
  "firstVisit": "2025-08-14T11:00:00Z",
  "lastVisit": "2026-05-14T16:32:00Z",
  "currentDebt": 0,
  "tier": "GOLD"            // GOLD | SILVER | BRONZE
}
```

### 8.9 `GET /api/customers/kpi` **[YANGI]**

Mijozlar bo'limidagi KPI.

```json
{
  "totalCustomers": 1842,
  "newThisMonth": 86,
  "activeThisMonth": 342,
  "totalDebt": 4280000.00
}
```

### 8.10 `POST /api/customers/import` / `GET /api/customers/export` **[YANGI]**

Mahsulotdagi import/export bilan bir xil format.

### 8.11 Mijoz daraja qoidalari **[YANGI]**

`tier` ma'lumot — backend tomonidan hisoblanadi (admin yuqori panel ko'rsatadi):

| Tier | Shart |
|---|---|
| `GOLD` | `totalSpent >= 5_000_000` yoki `ordersCount >= 30` |
| `SILVER` | `totalSpent >= 1_500_000` |
| `BRONZE` | qolganlar |

Server `tier` ni `CustomerResponse`'ga qo'shishi kerak (hozirda yo'q).

---

<a id="9-suppliers"></a>
## 9. Yetkazib beruvchilar (Suppliers) **[BUTUN MODUL YANGI]**

Backend'da hozir umuman yo'q. Admin → "Yetkazib beruvchilar" bo'limi uchun.

### 9.1 `GET /api/suppliers?q=…&status=…&category=…&page=…` **[YANGI]**

```json
{
  "content": [
    {
      "uuid": "…",
      "code": "YB-001",
      "name": "Coca-Cola Uzbekistan",
      "contactPerson": "Aziz Karimov",
      "phone": "+998 71 200 18 00",
      "email": "orders@coca-cola.uz",
      "address": "Toshkent sh., Yangi Olmazor",
      "categoryUuid": "…",
      "categoryName": "Ichimliklar",
      "totalOrders": 42,
      "totalDebt": 6240000.00,
      "lastOrderAt": "2026-05-13T09:00:00Z",
      "status": "ACTIVE",   // ACTIVE | INACTIVE | OVERDUE
      "createdAt": "…"
    }
  ]
}
```

### 9.2 `POST /api/suppliers` **[YANGI]**

```json
{
  "name": "Sof Sut MChJ",
  "contactPerson": "Begzod Yo'ldoshev",
  "phone": "+998 99 111 22 33",
  "email": null,
  "address": "Toshkent vil., Bo'ka tumani",
  "categoryUuid": "…",
  "ikpu": "302145679",
  "bankAccount": "20208000300010000001"
}
```

### 9.3 `PUT /api/suppliers/{uuid}` **[YANGI]**
### 9.4 `DELETE /api/suppliers/{uuid}` **[YANGI]** (soft)
### 9.5 `GET /api/suppliers/{uuid}` **[YANGI]** — to'liq detal

### 9.6 `GET /api/suppliers/{uuid}/orders` **[YANGI]**

Yetkazib beruvchidan kirim (purchase order) tarixi — `Page<PurchaseOrderResponse>`.

### 9.7 `POST /api/suppliers/{uuid}/debt/pay` **[YANGI]**

```json
{
  "cashboxUuid": "…",
  "amount": 1240000.00,
  "currencyCode": "UZS",
  "paymentType": "TRANSFER",
  "idempotencyKey": "…"
}
```

### 9.8 `GET /api/suppliers/kpi` **[YANGI]**

```json
{
  "totalSuppliers": 24,
  "activeSuppliers": 19,
  "totalDebt": 18420000.00,
  "pendingOrders": 8
}
```

---

<a id="10-xodimlar"></a>
## 10. Xodimlar (Employees)

Hozir backend faqat `tenant users` (Auth uchun) bilan ishlaydi. Admin → "Xodimlar" bo'limida xodim kartochkasi (filial, smena, telefon, status) kerak — bu **yangi** entity.

### 10.1 `GET /api/employees?q=…&branchUuid=…&role=…&status=…` **[YANGI]**

```json
{
  "content": [
    {
      "uuid": "…",
      "userId": 14,                     // /api/v1/users dan link
      "fullName": "Mavluda Rashidova",
      "phone": "+998 90 123 45 67",
      "role": "CASHIER",                // CASHIER | MANAGER | STOREKEEPER | ADMIN
      "branchUuid": "…",
      "branchName": "Chilonzor",
      "shift": "MORNING",               // MORNING | EVENING | NIGHT | PERMANENT
      "shiftLabel": "Tongi (08-16)",
      "status": "ONLINE",               // ONLINE | OFFLINE | LEAVE
      "hiredAt": "2024-08-12",
      "monthlySales": 4280000.00,
      "checksCount": 84,
      "averageCheck": 50950.00,
      "active": true,
      "createdAt": "…"
    }
  ]
}
```

### 10.2 `POST /api/employees` **[YANGI]**

```json
{
  "fullName": "Bobur Nazarov",
  "phone": "+998 90 765 43 21",
  "role": "CASHIER",
  "branchUuid": "…",
  "shift": "MORNING",
  "hiredAt": "2026-05-01",
  "createUserAccount": true,
  "username": "bnazarov",
  "password": "Initial123",
  "rolePermissions": ["ORDER_CREATE", "PRODUCT_VIEW"]
}
```

Response — yuqoridagi `EmployeeResponse` + (agar `createUserAccount=true` bo'lsa) `userCredentials`.

### 10.3 `PUT /api/employees/{uuid}` **[YANGI]**
### 10.4 `DELETE /api/employees/{uuid}` **[YANGI]** (soft)
### 10.5 `PATCH /api/employees/{uuid}/status` **[YANGI]**

```json
{ "status": "LEAVE" }
```

### 10.6 `GET /api/employees/{uuid}/shifts?month=2026-05` **[YANGI]**

Smena jadvali (kalendar uchun).

```json
[
  { "date": "2026-05-17", "shift": "MORNING", "branchUuid": "…" },
  { "date": "2026-05-18", "shift": "OFF" }
]
```

### 10.7 `PUT /api/employees/{uuid}/shifts` **[YANGI]**

```json
{
  "month": "2026-05",
  "days": [
    { "date": "2026-05-17", "shift": "MORNING" },
    { "date": "2026-05-18", "shift": "OFF" }
  ]
}
```

### 10.8 `GET /api/employees/kpi` **[YANGI]**

```json
{
  "total": 12,
  "onShiftNow": 4,
  "monthlyPayroll": 38240000.00,
  "attendanceRate": 0.942
}
```

### 10.9 Rollar va ruxsatlar — `/api/v1/roles`, `/api/v1/permissions` **[MAVJUD-FOYDALANILMAGAN]**

Xodimlar matritsasi (kartochkadagi "Ruxsatlar" tab) shu endpointlardan o'qiydi:

```http
GET /api/v1/roles?q=
GET /api/v1/permissions?module=ORDER
PUT /api/v1/roles/{roleId}/permissions
```

```json
{ "permissionCodes": ["ORDER_CREATE", "ORDER_VIEW", "PRODUCT_VIEW"] }
```

---

<a id="11-hisobotlar"></a>
## 11. Hisobotlar **[BARCHASI YANGI]**

Admin → "Hisobotlar" bo'limi 4 KPI + 5 ta chart ko'rsatadi.

### 11.1 `GET /api/reports/sales/kpi?from=…&to=…&branchUuid=…` **[YANGI]**

```json
{
  "revenue": 56440000.00,
  "profit": 18240000.00,
  "ordersCount": 1248,
  "averageCheck": 45220.00,
  "deltas": {
    "revenue": 0.082,        // o'tgan davrga nisbatan o'zgarish (8.2%)
    "profit": 0.064,
    "ordersCount": 0.041,
    "averageCheck": 0.013
  }
}
```

### 11.2 `GET /api/reports/sales/daily?from=…&to=…&branchUuid=…` **[YANGI]**

Kunlik bar chart.

```json
[
  { "date": "2026-05-11", "label": "Du", "revenue": 6240000.00, "orders": 142 },
  { "date": "2026-05-12", "label": "Se", "revenue": 7120000.00, "orders": 156 }
]
```

### 11.3 `GET /api/reports/sales/hourly?date=2026-05-17&branchUuid=…` **[YANGI]**

```json
[
  { "hour": 8,  "label": "08", "revenue": 240000,   "orders": 6 },
  { "hour": 10, "label": "10", "revenue": 580000,   "orders": 14 }
]
```

### 11.4 `GET /api/reports/payments/breakdown?from=…&to=…` **[YANGI]**

Donut chart.

```json
[
  { "paymentType": "CARD",  "label": "Karta", "amount": 22840000.00, "share": 0.40, "color": "#22C55E" },
  { "paymentType": "CASH",  "label": "Naqd",  "amount": 18400000.00, "share": 0.33, "color": "#155EEF" },
  { "paymentType": "CLICK", "label": "Click", "amount":  8120000.00, "share": 0.14, "color": "#3B82F6" },
  { "paymentType": "PAYME", "label": "Payme", "amount":  5840000.00, "share": 0.10, "color": "#F59E0B" },
  { "paymentType": "DEBT",  "label": "Qarz",  "amount":  1240000.00, "share": 0.02, "color": "#94A3B8" }
]
```

### 11.5 `GET /api/reports/products/top?from=…&to=…&limit=10` **[YANGI]**

```json
[
  {
    "rank": 1,
    "productUuid": "…",
    "name": "Coca-Cola 1.5L",
    "soldQuantity": 284,
    "revenue": 3976000.00
  }
]
```

### 11.6 `GET /api/reports/cashiers/performance?from=…&to=…&branchUuid=…` **[YANGI]**

```json
[
  {
    "employeeUuid": "…",
    "initials": "MR",
    "name": "Mavluda Rashidova",
    "sales": 4280000.00,
    "checks": 84,
    "averageCheck": 50950.00
  }
]
```

### 11.7 `GET /api/reports/export?type=…&format=pdf&from=…&to=…` **[YANGI]**

`type`: `sales-daily | sales-hourly | top-products | cashier-perf | full`.
`format`: `pdf | xlsx | csv`.

Stream'lashtirilgan fayl (`Content-Disposition: attachment`).

### 11.8 `GET /api/v1/dashboard` **[MAVJUD-FOYDALANILMAGAN]**

Tenant dashboard'ning umumiy snapshot'i (Login keyin "Bosh sahifa"da ko'rsatish uchun). Mavjud `TenantDashboardResponse`'dan foydalanadi.

---

<a id="12-sozlamalar"></a>
## 12. Sozlamalar

Admin → "Sozlamalar" da 7 tab bor. Hozir backend bu tablar uchun maxsus endpoint berolmaydi — server-side settings store kerak.

### 12.1 `GET /api/settings/general` **[YANGI]**

```json
{
  "businessName": "ShefPos Demo MChJ",
  "legalAddress": "Toshkent sh., Chilonzor t., Bunyodkor 18A",
  "ikpu": "302145678",
  "phone": "+998 71 200 18 18",
  "email": "info@shefpos.uz",
  "logoUuid": null,
  "language": "uz-Latn",
  "currency": "UZS",
  "timezone": "Asia/Tashkent"
}
```

### 12.2 `PUT /api/settings/general` **[YANGI]**

`GET` javobi shaklida `Request`.

### 12.3 `GET /api/settings/receipt` **[YANGI]**

```json
{
  "header": "ShefPos\n+998 71 200 18 18",
  "footer": "Tashrifingiz uchun rahmat!",
  "showLogo": true,
  "showCustomer": true,
  "showCashier": true,
  "showBarcode": false,
  "showTaxLine": true,
  "paperWidthMm": 80,
  "fontSize": 12
}
```

### 12.4 `PUT /api/settings/receipt` **[YANGI]**

### 12.5 `GET /api/settings/payments` **[YANGI]**

```json
{
  "providers": [
    { "key": "CASH",     "label": "Naqd",   "enabled": true,  "config": null },
    { "key": "CARD",     "label": "Karta",  "enabled": true,  "config": { "terminalId": "TX-001" } },
    { "key": "CLICK",    "label": "Click",  "enabled": true,  "config": { "merchantId": "…", "secretKey": "…" } },
    { "key": "PAYME",    "label": "Payme",  "enabled": true,  "config": { "merchantId": "…" } },
    { "key": "UZUM",     "label": "Uzum",   "enabled": false, "config": null },
    { "key": "TRANSFER", "label": "Pul o'tkazma", "enabled": true, "config": null }
  ]
}
```

### 12.6 `PUT /api/settings/payments` **[YANGI]**

### 12.7 `GET /api/settings/integrations` **[YANGI]**

```json
{
  "soliqUz":  { "enabled": true,  "inn": "302145678", "apiKey": "…" },
  "telegram": { "enabled": false, "botToken": null, "chatId": null },
  "oneC":     { "enabled": false, "endpoint": null, "syncIntervalMin": 60 }
}
```

### 12.8 `PUT /api/settings/integrations` **[YANGI]**

### 12.9 `GET /api/settings/security` **[YANGI]**

```json
{
  "autoLogoutMinutes": 15,
  "twoFactorEnabled": false,
  "passwordPolicy": {
    "minLength": 8,
    "requireDigit": true,
    "requireUpper": true
  },
  "sessionMaxConcurrent": 3
}
```

### 12.10 `PUT /api/settings/security` **[YANGI]**

### 12.11 `GET /api/settings/sync` **[YANGI]**

```json
{
  "endpoint": "https://shefpos.uz/api",
  "intervalSeconds": 300,
  "wifiOnly": false,
  "lastSyncAt": "2026-05-17T09:14:00Z"
}
```

### 12.12 `PUT /api/settings/sync` **[YANGI]**

### 12.13 `POST /api/settings/backup` **[YANGI]**

Backup yaratish.

```json
{ "name": "manual-2026-05-17" }
```

Response 202: `{ "jobId": "…", "status": "QUEUED" }`.

### 12.14 `GET /api/settings/backup` **[YANGI]**

```json
{
  "content": [
    {
      "id": "…",
      "name": "auto-2026-05-17",
      "sizeBytes": 14820000,
      "createdAt": "2026-05-17T03:00:00Z",
      "downloadUrl": "/api/settings/backup/…/download"
    }
  ]
}
```

### 12.15 `GET /api/settings/backup/{id}/download` **[YANGI]**

Stream `application/octet-stream`.

### 12.16 `POST /api/settings/backup/{id}/restore` **[YANGI]**

202 Accepted (server fonda ishlaydi).

---

<a id="13-audit"></a>
## 13. Audit va API loglar

### 13.1 `GET /api/v1/system-admin/audit-logs` **[MAVJUD-FOYDALANILMAGAN]**

Sozlamalar → "Audit jurnali" tabi uchun.

Query: `from`, `to`, `userId`, `entityType`, `entityId`, `action`, `requestId`, `tenantId`.

Response — `Page<AuditLogResponse>` (DTO referansda).

### 13.2 `GET /api/v1/system-admin/audit-logs/{id}` **[MAVJUD-FOYDALANILMAGAN]**

To'liq detal — `oldValue`, `newValue`, `changedFields`.

### 13.3 `GET /api/v1/system-admin/api-logs` **[MAVJUD-FOYDALANILMAGAN]**

System-admin / texnik diagnostika.

---

<a id="14-errors"></a>
## 14. Xato javoblar (Error response)

Backend RFC 7807 (`application/problem+json`) yuboradi:

```json
{
  "type": "https://shefpos.uz/errors/validation",
  "title": "Validation failed",
  "status": 400,
  "detail": "items[0].quantity: 0.001 dan kichik bo'lmasligi kerak",
  "instance": "/api/orders",
  "errors": [
    { "field": "items[0].quantity", "message": "DecimalMin 0.001" },
    { "field": "transactions[0].amount", "message": "items jami summasiga teng bo'lishi kerak" }
  ],
  "requestId": "8b6f-…"
}
```

WPF tomon `ApiClient.ParseErrorMessage` ushbu maydonlarni o'qiydi: `message → error → detail → title`. Birinchisi mavjud bo'lsa, foydalanuvchiga ko'rsatiladi.

**Standart HTTP kodlar:**

| Kod | Holat | WPF foydalanuvchiga ko'rsatadigan matn |
|---|---|---|
| 400 | Yaroqsiz ma'lumot | "Noto'g'ri so'rov ma'lumotlari" |
| 401 | Token muddati tugagan / yo'q | "Sessiya muddati tugagan, qayta kiring" — `TryRefreshTokenAsync` ishga tushadi |
| 403 | Ruxsat yo'q | "Bu amalni bajarish uchun ruxsat yo'q" |
| 404 | Topilmadi | "Ma'lumot topilmadi" |
| 409 | Konflikt (masalan, takroriy barkod) | server'dan `detail` |
| 422 | Validation | `errors[]` formati |
| 500 | Server xatosi | "Server ichki xatosi yuz berdi" |
| 503 | Server ishlamayapti | "Server hozircha mavjud emas" |

---

## 15. Ustuvorlik tartibida implementatsiya (Roadmap)

Quyidagi tartibda backend'da qo'shilsa, WPF tomon UI darhol ulanishi mumkin.

### Bosqich 1 — POS asosiy funksional (eng zarur)
- [MAVJUD] Auth, Products, Customers, Orders, Cashbox, Debt — barchasi ishlamoqda.
- [MAVJUD-FOYDALANILMAGAN] `PUT/DELETE /api/products/{uuid}` ni WPF "Mahsulotlar" ekranida ulash.
- [MAVJUD-FOYDALANILMAGAN] `GET /api/orders/{uuid}` va `POST /api/orders/{uuid}/returns` ni "Tarix" ekraniga ulash.

### Bosqich 2 — Admin paneli minimal
- **[YANGI]** `GET /api/inventory/stock` + `/movements` + `/alerts` + `/kpi` — Ombor moduli.
- **[YANGI]** `GET /api/reports/sales/kpi` + `/daily` + `/payments/breakdown` + `/products/top` + `/cashiers/performance` — Hisobotlar.
- **[YANGI]** Suppliers (9.x) — Yetkazib beruvchilar moduli butunligicha.
- **[YANGI]** Employees (10.x) — Xodimlar moduli + `/v1/roles`,`/v1/permissions` ulash.

### Bosqich 3 — Ombor harakatlari
- **[YANGI]** `POST /api/inventory/inbound | transfer | writeoff | inventory-count`.
- **[YANGI]** Categories CRUD (5.x) — hozircha `product-types` ishlatilmoqda.

### Bosqich 4 — Sozlamalar va integratsiyalar
- **[YANGI]** `/api/settings/general | receipt | payments | integrations | security | sync | backup`.
- **[YANGI]** Export/Import endpointlari (Products, Customers, Suppliers).

### Bosqich 5 — Server-side mijoz daraja qoidalari, audit UI
- **[YANGI]** Customer `tier` hisoblash + `/customers/{uuid}/stats`, `/orders`, `/debt-history`.
- **[MAVJUD-FOYDALANILMAGAN]** Audit log brauzeri — Sozlamalar → "Audit jurnali" tab.

---

## 16. WPF-tomon mapping cheat-sheet

| WPF entity (`Core/Entities/`) | Server DTO | Endpoint manbai |
|---|---|---|
| `Product` | `ProductResponseDTO` | `GET /api/products` |
| `Customer` | `CustomerResponse` | `GET /api/customers` |
| `Sale` | `OrderResponse` (read) / `CreateOrderRequest` (write) | `POST /api/orders` |
| `SaleItem` | `OrderItemResponse` / `OrderItemRequest` | nested |
| `PriceList` | `PriceListResponseDTO` | `GET /api/price-lists` |
| `ProductType` | `ProductTypeResponseDTO` | `GET /api/product-types` |
| `AppSetting` | — (lokal) | — |

WPF `ApiClient.cs` qo'shilishi kerak bo'lgan metodlar (Bosqich 2 dan keyin):

```csharp
Task<List<EmployeeDto>>          GetEmployeesAsync(EmployeesQuery q);
Task<EmployeeDto>                CreateEmployeeAsync(CreateEmployeeRequest r);
Task<List<SupplierDto>>          GetSuppliersAsync(SuppliersQuery q);
Task<SupplierDto>                CreateSupplierAsync(CreateSupplierRequest r);
Task<List<StockRowDto>>          GetStockAsync(string warehouseUuid);
Task<List<MovementDto>>          GetMovementsAsync(MovementsQuery q);
Task<List<AlertDto>>             GetAlertsAsync();
Task<SalesKpiDto>                GetSalesKpiAsync(DateRange r);
Task<List<DailySalesDto>>        GetDailySalesAsync(DateRange r);
Task<List<PaymentBreakdownDto>>  GetPaymentBreakdownAsync(DateRange r);
Task<List<TopProductDto>>        GetTopProductsAsync(DateRange r, int limit);
Task<List<CashierPerfDto>>       GetCashierPerformanceAsync(DateRange r);
Task<GeneralSettingsDto>         GetGeneralSettingsAsync();
Task                             UpdateGeneralSettingsAsync(GeneralSettingsDto dto);
```

---

> **Eslatma:** ushbu hujjat WPF `Core/DTOs/ApiDtos.cs` va `Services/ApiClient.cs` ning hozirgi holatiga, `ViewModels/Admin/Modules/ModuleViewModels.cs` da ko'rsatilgan mock ma'lumotlarga va Ham-Pos backend'idagi mavjud kontrollerlar ro'yxatiga asoslanadi. Yangi endpointlar uchun shakl modulning mock'i bilan moslashtirilgan — agar UI sxemasi o'zgarsa, ushbu hujjat ham yangilanishi kerak.
