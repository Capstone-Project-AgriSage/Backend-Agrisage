# Flow L4 — Inventory, Stocktake, Returns and Refunds (owner: Teammate 4)

Version 2.0 — 2026-10-03. Conventions, shared shapes, interfaces and ownership rules: [README.md](README.md).
Sources: `DATABASE_DESIGN.md` §21–29, §52–54, §7.1, §7.5, §VIII, §XXI–XXII, §35.8–35.11, §35.17–35.19;
`BUSINESS_RULES.md` rules 10, 30–33, 52–58.

Inventory is the mentor's main focus. L4 owns everything that changes or explains stock outside a sale: receiving
(already built: suppliers, goods receipts, lots, stock movements), Excel import, stock overview and alerts,
stocktake and manual adjustments, stock card and inventory reports, and the way back — returns and refunds.
The sale itself (FEFO reservation, SALE movement) belongs to L1/L2.

Already on `main` and now maintained by L4: `/api/suppliers`, `/api/goods-receipts` (create/edit/confirm DRAFT
receipts, `GR-` numbers), `GET /api/inventory/lots`, `GET /api/inventory/lots/{id}`,
`POST /api/inventory/lots/{id}/status`, `GET /api/inventory/stock-movements` (+ `/{id}`). Minimum stock levels are
set on store products (`/api/store-products`, field `minStockLevelBase`).

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Download the template, fill it, preview, import | `GET /api/goods-receipts/import-template`, `POST /import/preview`, `POST /import` | DRAFT receipt with validated lines |
| 2 | Confirm the receipt | `POST /api/goods-receipts/{id}/confirm` | lots, STOCK_IN, weighted average cost |
| 3 | Stock overview and alerts | `GET /api/inventory/stock-summary`, `GET /api/inventory/alerts` | low stock, expiring and expired lots |
| 4 | Mark expired lots | `POST /api/inventory/lots/expire-due` | lots EXPIRED (never sold anyway) |
| 5 | Write off expired / damaged goods | `POST /api/inventory/adjustments` | ADJUSTMENT_OUT at average cost |
| 6 | Stocktake: create, count, a sale happens meanwhile, refresh stale lines, complete | `/api/stocktakes…` | ADJUSTMENT_IN / OUT for the differences |
| 7 | Stock card of one product and the period report | `GET /api/inventory/stock-card`, `GET /api/reports/inventory-movement` | opening + every movement = closing |
| 8 | A farmer returns goods: request → approve → receive → inspect → settle | `/api/returns…` | RETURN_IN into the original lot, debt reduced first |
| 9 | Refund the paid remainder; refund a cancelled order | `/api/returns/{id}/refunds…`, `/api/orders/{id}/refunds…` | refunds COMPLETED, payment (PARTIALLY_)REFUNDED |

---

## 2. Tasks

| Task | Content | Depends on | Delivers to |
|---|---|---|---|
| F4.1 | **New** stock summary, alerts, expire-due | — | everyone (stock screens) |
| F4.2 | Stocktake and manual adjustments | — | F2.6 (incident adjustments, decision D5) |
| F4.3 | **New** goods receipt import from Excel — **done by the lead** (2026-10-03), L4 maintains it | — | — |
| F4.4 | Sales returns | fulfilled orders (tests build them through Domain; end-to-end after M2), F3.5 `IDebtReturnPosting` (temporary: applies 0) | F4.5 |
| F4.5 | Refunds of returns and of cancelled orders, payment refunded status, proof-photo delete guard | F4.4, F1.6 | — |
| F4.6 | **New** stock card, inventory movement and valuation reports | — | — |

---

## 3. F4.1 — Stock summary, alerts and expired lots (new)

| Method | Route | Roles | Query / body | Response |
|---|---|---|---|---|
| GET | `/api/inventory/stock-summary` | Operate | `search` (SKU, name), `categoryId`, `lowStockOnly`, `hasStock`, paging | `200 PagedResult<StockSummaryItem>` |
| GET | `/api/inventory/alerts` | Operate | `type` = `EXPIRING` \| `EXPIRED` \| `LOW_STOCK` (omit = all), `withinDays` (1–365, default 30), paging | `200 PagedResult<InventoryAlertItem>` |
| POST | `/api/inventory/lots/expire-due` | Manage | — | `200 { expiredLotCount, lots: [ { id, lotNumber, expiryDate } ] }` |

`StockSummaryItem` (one row per store product):

```json
{
  "storeProductId": "uuid", "sku": "SKU-001", "productName": "…", "baseUnit": "KG",
  "onHandBaseQuantity": 500, "reservedBaseQuantity": 125, "availableBaseQuantity": 375,
  "sellableAvailableBaseQuantity": 300,
  "stockValue": 52500000.00,
  "minStockLevelBase": 400, "isLowStock": true,
  "nearestExpiryDate": "2027-01-31", "lotCount": 3
}
```

- `sellableAvailableBaseQuantity` counts only ACTIVE, not expired lots (what can still be reserved).
- `isLowStock` = `minStockLevelBase` is set and `sellableAvailableBaseQuantity` < it.
- `stockValue` = Σ inventory value of the product's lots; `nearestExpiryDate` among sellable lots with stock.

`InventoryAlertItem`: `type, storeProductId, sku, productName, inventoryLotId | null, lotNumber | null,
expiryDate | null, daysToExpiry | null, lotStatus | null, onHandBaseQuantity, reservedBaseQuantity,
minStockLevelBase | null`.

- EXPIRING: lots with on hand > 0 and expiry in [today, today + `withinDays`] (Vietnam days).
- EXPIRED: lots with on hand > 0 and expiry < today, whatever their status — stock to write off with an
  adjustment (reason EXPIRED) or to move off a reservation.
- LOW_STOCK: store products as defined above (lot fields null).
- `expire-due` marks every ACTIVE lot whose expiry < today as EXPIRED, in one transaction (decision F-D6). Every
  reservation/sale check already refuses expired lots by date, so the endpoint only aligns the status for screens
  and reports; a reservation on such a lot must be moved (pickup with another lot, delivery `ChangeLots`).

---

## 4. F4.2 — Stocktake and manual adjustments

### 4.1 Stocktake

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/stocktakes` | Operate | `CreateStocktakeRequest` | `201 StocktakeResponse` (DRAFT with snapshot lines) |
| GET | `/api/stocktakes` | Operate | query: `status`, `fromDate`, `toDate`, `search`, paging | `200 PagedResult<StocktakeListItem>` |
| GET | `/api/stocktakes/{id}` | Operate | query: `onlyDifferences`, `onlyUncounted` | `200 StocktakeResponse` |
| POST | `/api/stocktakes/{id}/start` | Operate | — | `200 StocktakeResponse` (IN_PROGRESS) |
| PUT | `/api/stocktakes/{id}/counts` | Operate | `{ counts: [ { itemId, countedQuantity, unitCost?, reasonCode?, note? } ] }` | `200 StocktakeResponse` |
| POST | `/api/stocktakes/{id}/refresh-stale` | Operate | — | `200 StocktakeResponse` (stale lines re-snapshotted, their counts cleared) |
| POST | `/api/stocktakes/{id}/complete` | Manage | — | `200 StocktakeResponse` (COMPLETED) |
| POST | `/api/stocktakes/{id}/cancel` | Operate | `{ reason? }` | `200 StocktakeResponse` |
| DELETE | `/api/stocktakes/{id}` | Operate | — | `204` (DRAFT only) |

`CreateStocktakeRequest`: `{ storeProductIds?: uuid[], includeEmptyLots: false, note? }` — no
`storeProductIds` = every store product. One line per lot in scope with `system_quantity_snapshot` =
current on hand and `unit_cost_snapshot` = current average cost (null when on hand = 0). Number `ST-yyyyMMdd-NNNN`.

Rules:
- `countedQuantity` ≥ 0, base units. `difference = counted − snapshot`,
  `differenceCostValue = difference × unitCostSnapshot` (design §35.8).
- A line with a positive difference and no unit cost snapshot needs `unitCost` in the count (422 at
  completion otherwise).
- `reasonCode`: `DAMAGED | EXPIRED | LOST | STOCKTAKE_DIFFERENCE | MANUAL_CORRECTION | OTHER`; required when
  the difference ≠ 0.
- **Stale line (decision C-D6, design §35.19):** a POSTED stock movement for the line's lot has
  `posted_at` in (`snapshot_at` − 5 minutes, `counted_at`]. This also catches movements that cancel each
  other out; a movement after the count does not make the line stale. `GET` marks such lines
  `isStale: true`. `refresh-stale` re-snapshots only those lines (lock the balance `FOR SHARE`, read on hand
  and average cost, then `snapshot_at`) through `Stocktake.RefreshItem` (Domain, done) and clears their
  count, so staff recount just those lots — never the whole stocktake.
- Creating a stocktake takes each line's snapshot the same way (lock, read, `snapshot_at`) and passes it to
  `Stocktake.AddItem(lotId, quantity, snapshotAt, unitCost)`.
- Complete (one transaction): every line counted (§35.8); lock lot balances; refuse (422, listing the lots) if
  any line is stale; one ADJUSTMENT_IN movement (`ReceiveStock` at the snapshot/entered cost) and one
  ADJUSTMENT_OUT movement (`IssueUnreserved` at average cost), both linked by `stocktake_id`; a decrease below
  the lot's reserved quantity is refused (422) — the reservation must be moved first.
- Lots that physically exist but are not in the system are not created by a stocktake (use a goods
  receipt).

`StocktakeListItem`: `id, stocktakeNumber, status, lineCount, countedCount, differenceCount, createdBy, createdAt,
completedAt`.

`StocktakeResponse`: `id, stocktakeNumber, status, note, createdBy, createdAt, startedBy, startedAt,
completedBy, completedAt, totals {lines, counted, withDifference, differenceCostValue},
movements [{id, movementNumber, movementType}], items [{id, inventoryLotId, sku, productName, lotNumber,
expiryDate, systemQuantitySnapshot, snapshotAt, countedQuantity, differenceQuantity, unitCostSnapshot,
differenceCostValue, reasonCode, note, countedBy, countedAt, isStale}]`.

### 4.2 Manual stock adjustment

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/inventory/adjustments` | Manage | `StockAdjustmentRequest` | `201 StockMovementResponse` (existing shape) |

```json
{
  "reasonCode": "DAMAGED | EXPIRED | LOST | MANUAL_CORRECTION | OTHER",
  "note": "string, required, ≤1000",
  "lines": [ { "inventoryLotId": "uuid", "quantityDeltaBase": -5, "unitCost": null } ]
}
```

- One movement per request: all deltas positive → ADJUSTMENT_IN, all negative → ADJUSTMENT_OUT (mixed → 400).
- Positive delta at `unitCost` or, when null, the lot's current average cost (lot with zero stock needs
  `unitCost`); negative delta at average cost, never below the reserved quantity.
- This is how a delivery incident that destroys goods is posted (decision D5); L2's F2.6 links the returned
  movement id when resolving the incident.

---

## 5. F4.3 — Goods receipt import from Excel (`Operate`) — done

Implemented by the lead (2026-10-03, decision F-D7); L4 maintains it. Design §22 "Excel workflow": template →
upload → parse → preview → validate → correct → DRAFT receipt → confirm.

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/goods-receipts/import-template` | — | `200` xlsx file (`AgriSage-goods-receipt-template.xlsx`) |
| POST | `/api/goods-receipts/import/preview` | multipart: `file` + `ReceiptImportRequest` fields | `200 ReceiptImportPreviewResponse` (nothing saved) |
| POST | `/api/goods-receipts/import` | multipart: `file` + `ReceiptImportRequest` fields | `201 GoodsReceiptResponse` (DRAFT, existing shape) |

Multipart form: `file` (.xlsx, ≤ 2 MB, ≤ 500 data rows) and the header fields of `ReceiptImportRequest`:
`supplierId` (required), `receivedAt?`, `supplierInvoiceNumber?` (≤ 100), `supplierInvoiceDate?` (`yyyy-MM-dd`),
`note?` (≤ 1000). Header rules = manual draft (supplier of the store and ACTIVE, received time not in the future).
Preview and import use the `upload` rate limit (30 requests/min per client).

**Template** (generated per store):
- sheet `Receipt` — header row only, columns formatted (SKU/Packaging/LotNumber as text so leading zeros stay, dates
  as `yyyy-mm-dd`);
- sheet `Products` — every ACTIVE store product (not DISCONTINUED) with each ACTIVE **purchase** packaging:
  `SKU` (store SKU or product SKU), `ProductName`, `Packaging` (packaging name, else unit name), `Barcode`,
  `ConversionToBase`, `BaseUnit`, `RequiresLotNumber`, `RequiresExpiryDate` (YES/NO); at most 5,000 lines;
- sheet `Guide` — instructions in Vietnamese.

Receipt sheet columns (the sheet named `Receipt`, else the first sheet; header in row 1; names match ignoring
case, spaces, `_` and `-`; any order; unknown columns ignored; empty rows skipped):

| Column | Required | Meaning |
|---|---|---|
| `SKU` | yes | store SKU (matched first), otherwise the product SKU; case-insensitive |
| `Packaging` | yes | barcode, packaging name, unit name or unit code of a packaging of that product (case-insensitive); when several match, the single ACTIVE purchase packaging among them is taken; it must be an ACTIVE **purchase** packaging |
| `Quantity` | yes | whole packaging units ≥ 1 |
| `UnitCost` | yes | per packaging, ≥ 0, at most 2 decimals, never rounded; typed as text it uses a dot and no thousands separator |
| `LotNumber` | when the product tracks lots | ≤ 100 characters, trimmed |
| `ExpiryDate` | when the product requires expiry | an Excel date cell, or text `yyyy-MM-dd` / `dd/MM/yyyy` |
| `ManufactureDate` | no | same formats |

Formula cells are read as their value.

`ReceiptImportPreviewResponse`:

```json
{
  "fileName": "nhap-kho-0210.xlsx", "rowCount": 25, "validRowCount": 23,
  "subtotalAmount": 125000000.00,
  "rows": [
    { "rowNumber": 2, "sku": "SKU-001", "packaging": "Bao 25kg", "storeProductId": "uuid",
      "productPackagingId": "uuid", "productName": "…", "quantity": 10, "unitCost": 240000.00,
      "lotNumber": "L01", "expiryDate": "2027-01-31", "manufactureDate": null,
      "lineTotalAmount": 2400000.00,
      "errors": [ { "column": "ExpiryDate", "message": "The goods are already expired; expired stock cannot be received." } ] }
  ]
}
```

`rowNumber` is the Excel row; `errors[].column` is one of the sheet columns (or `Row`); `subtotalAmount` sums the
valid rows only.

Rules:
- Every line rule of manual receiving applies unchanged (`ReceiptItemRules`: lot/expiry flags, no expired goods, no
  DISCONTINUED product, ACTIVE purchase packaging only, money rule); each error names its column.
- File-level problems answer **400** with `errors.file`: no file, not `.xlsx`, over 2 MB, not a readable workbook
  (or unpacking beyond 50 MB / 500 entries), a required column missing (named), more than 500 rows, no data row.
- `import` refuses with **422** when any row has an error — `detail` = "N of M rows are invalid…", `errors` =
  `{ "row 3": ["LotNumber: A lot number is required for this product."] }` — and saves nothing. A successful import
  creates one DRAFT receipt with `source_type = EXCEL_TEMPLATE` and `source_file_name` = the uploaded name without
  any client path (the file itself is not stored); it is then edited and confirmed with the existing endpoints.
- The workbook is read in memory with ClosedXML only inside `Infrastructure/Spreadsheets/ClosedXmlReceiptSpreadsheet`
  behind `IReceiptSpreadsheet` (Application, `Features/GoodsReceipts/Import`).

---

## 6. F4.4 — Sales returns

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/orders/{id}/returnable` | Operate | — | `200 ReturnableResponse` |
| POST | `/api/returns` | Operate | `CreateReturnRequest` | `201 SalesReturnResponse` (REQUESTED) |
| GET | `/api/returns` | Operate | query: `status`, `orderId`, `farmerProfileId`, `fromDate`, `toDate`, `search`, paging | `200 PagedResult<SalesReturnListItem>` |
| GET | `/api/returns/{id}` | Operate | — | `200 SalesReturnResponse` |
| POST | `/api/returns/{id}/items` | Operate | `ReturnItemRequest` | `200 SalesReturnResponse` |
| DELETE | `/api/returns/{id}/items/{itemId}` | Operate | — | `200 SalesReturnResponse` |
| POST | `/api/returns/{id}/approve` | Manage | — | `200` |
| POST | `/api/returns/{id}/reject` | Manage | `{ reason }` | `200` |
| POST | `/api/returns/{id}/cancel` | Operate | `{ reason }` | `200` |
| POST | `/api/returns/{id}/receive` | Operate | — | `200` |
| PUT | `/api/returns/{id}/items/{itemId}/inspection` | Operate | `{ conditionStatus, inspectionNote? }` | `200` |
| POST | `/api/returns/{id}/complete-inspection` | Manage | — | `200 SalesReturnResponse` |
| GET | `/api/me/orders/{id}/returnable` | FARMER | — | own order |
| POST | `/api/me/returns` | FARMER | `CreateReturnRequest` | `201` |
| GET | `/api/me/returns` (+ `/{id}`) | FARMER | paging | own returns |
| POST | `/api/me/returns/{id}/cancel` | FARMER | `{ reason }` | own, REQUESTED only |

`CreateReturnRequest`: `{ orderId, reasonSummary?, note?, items: [ReturnItemRequest] }`. Number `RT-yyyyMMdd-NNNN`.

`ReturnItemRequest`:

```json
{
  "orderItemId": "uuid",
  "deliveryItemLotAllocationId": "uuid | null",
  "originalStockMovementItemId": "uuid | null",
  "returnedBaseQuantity": 25,
  "reasonCode": "WRONG_PRODUCT | DAMAGED_PRODUCT | QUALITY_ISSUE | EXPIRED_PRODUCT | DELIVERY_DAMAGE | CUSTOMER_REJECTION | OTHER"
}
```

`ReturnableResponse` lists, per order item and fulfillment source (delivery lot allocation or pickup
stock movement item): lot, fulfilled base quantity, already returned (non-rejected, non-cancelled returns),
returnable quantity, unit price.

Rules (design §35.9–35.11, rules 30, 55–58):
- Only after fulfillment; exactly one source per line matching the order's fulfillment type
  (DELIVERY → allocation, PICKUP → stock movement item); quantity ≤ fulfilled − already returned
  (in-flight returns count).
- Snapshots from the order item: unit price, conversion; `returnValue = round2(qty × unitPrice ÷ conversion)`
  AwayFromZero, multiply first (the only rounded computed money). Original COGS unit cost from the source
  stock movement item.
- Lines change only while REQUESTED. REJECTED records actor and reason in `audit_logs` (no columns).
- Inspection: `conditionStatus` `RESELLABLE | DAMAGED | EXPIRED | UNUSABLE`; disposition derived
  (RESELLABLE → RESTOCK, else WRITE_OFF).
- `complete-inspection` (one transaction): every line inspected → `total_return_amount = Σ return value`;
  L3's `IDebtReturnPosting.ApplyReturnAsync` (README §4.8) returns the debt reduction → `total_debt_adjustment`;
  the rest is `total_refund_amount`; one RETURN_IN movement for RESTOCK lines back into the **original lot** at the
  original COGS unit cost (`ReceiveStock`), linked per line; WRITE_OFF lines change no stock (the goods
  already left stock at sale). Status → COMPLETED when nothing remains to refund, else PARTIALLY_RESOLVED.
- The original order is never modified.
- A refusal **before** handover is a delivery incident (L2), not a return.

`SalesReturnListItem`: `id, returnNumber, orderId, orderNumber, farmerProfileId, customerName, status,
totalReturnAmount, totalRefundAmount, requestedAt`.

`SalesReturnResponse`: header (number, order, Farmer, status, all actor/timestamp fields, totals) +
`items` (source ids, lot, quantity, prices, return value, COGS, reason, condition, disposition,
`returnStockMovementId`, `debtAdjustmentTransactionId`) + `refunds` (`RefundResponse[]`, README §2).

**What the store paid is not for the farmer.** On the `/api/me/returns` routes and on
`GET /api/me/orders/{id}/returnable`, `originalCogsUnitCost` and `returnInventoryCostValue` are always `null`
(`FarmerReturnView`); the staff routes return them as above.

---

## 7. F4.5 — Refunds (`Manage`)

No automatic payOS refund (rule 33): staff pay back outside the system (cash or bank transfer) and record it.
Number `RF-yyyyMMdd-NNNN`. Every refund route is Manage (decision C-D5) and returns `RefundResponse` (README §2).

### 7.1 Refunds of a sales return

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/returns/{id}/refunds` | `RefundRequest` | `201 RefundResponse` (PENDING) |
| POST | `/api/returns/{id}/refunds/{refundId}/complete` | `{ externalReference?, proofFileUrl?, note? }` | `200` |
| POST | `/api/returns/{id}/refunds/{refundId}/fail` | `{ note? }` | `200` |
| POST | `/api/returns/{id}/refunds/{refundId}/cancel` | `{ reason }` | `200` |

`RefundRequest`: `{ refundMethod: "CASH | BANK_TRANSFER | OTHER_EXTERNAL", amount, originalPaymentId?,
externalReference?, note? }`.

- Σ refunds not CANCELLED/FAILED ≤ `total_refund_amount`; a FAILED refund is retried with a new one.
- Completing the last refund completes the return (COMPLETED rule, §35.9).

### 7.2 Refunds of a cancelled order

L1's cancellation (`IOrderPaymentCancellation`, F1.6) **requests** one PENDING refund per paid payment; F4.5
completes them (decision F-D4).

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/orders/{id}/refunds` | — | `200 RefundResponse[]` |
| POST | `/api/orders/{id}/refunds` | `{ originalPaymentId, refundMethod, amount, note? }` | `201 RefundResponse` (retry after a FAILED/CANCELLED one) |
| POST | `/api/orders/{id}/refunds/{refundId}/complete` | `{ externalReference?, proofFileUrl?, note? }` | `200 RefundResponse` |
| POST | `/api/orders/{id}/refunds/{refundId}/fail` | `{ note? }` | `200 RefundResponse` |
| POST | `/api/orders/{id}/refunds/{refundId}/cancel` | `{ reason }` | `200 RefundResponse` |

- Only for CANCELLED / PARTIALLY_CANCELLED orders (Domain: `Order.RequestCancellationRefund`,
  `CompleteCancellationRefund`, `FailCancellationRefund`, `CancelCancellationRefund`). For one payment, PENDING +
  COMPLETED cancelled-order refunds ≤ the amount reversed from it by the cancellation (Application, under a row lock
  on the payment).
- The Farmer sees the refunds of their own order in L1's `GET /api/me/orders/{id}/payments` (`refunds` field).

### 7.3 Common to both

- Completing refunds with an `originalPaymentId` sets the payment PARTIALLY_REFUNDED / REFUNDED by the completed
  refunded sum (F4.5 adds that `Payment` Domain method).
- `proofFileUrl` (optional): an image uploaded with `POST /api/files/delivery-proofs` (decision C-D3); F4.5 extends
  the delete guard of `DELETE /api/files/delivery-proofs` to `refunds.proof_file_url` (F2.6 adds attempts and
  incidents).

---

## 8. F4.6 — Stock card and inventory reports (new)

| Method | Route | Roles | Query | Response |
|---|---|---|---|---|
| GET | `/api/inventory/stock-card` | Operate | `storeProductId` (required), `inventoryLotId?`, `fromDate`, `toDate` (≤ 366 days) | `200 StockCardResponse` |
| GET | `/api/reports/inventory-movement` | Manage | `fromDate`, `toDate` (≤ 366 days), `categoryId?` | `200 InventoryMovementReportResponse` |
| GET | `/api/reports/inventory-valuation` | Manage | `categoryId?` | `200 InventoryValuationReportResponse` |

`StockCardResponse`:

```json
{
  "storeProductId": "uuid", "sku": "SKU-001", "productName": "…", "baseUnit": "KG",
  "fromDate": "2026-10-01", "toDate": "2026-10-31",
  "openingBaseQuantity": 400,
  "lines": [
    { "postedAt": "…", "movementId": "uuid", "movementNumber": "SM-20261002-0003", "movementType": "SALE",
      "reference": { "type": "ORDER", "id": "uuid", "number": "OD-20261002-0001" },
      "lotNumber": "L01", "inBaseQuantity": 0, "outBaseQuantity": 125, "balanceBaseQuantity": 275,
      "unitCost": 4200.000000 }
  ],
  "closingBaseQuantity": 275
}
```

- Only POSTED movements, ordered by `posted_at`; in/out from the sign of `stock_movement_items.quantity_delta_base`
  (design §27: positive = increase), so every type, REVERSAL included, is handled the same way. Opening = Σ
  `quantity_delta_base` before `fromDate` (Vietnam day); closing = opening + Σ lines and must equal the lot
  balances when `toDate` is today (test).
- `reference` = the source document (goods receipt, order, delivery, stocktake, sales return, or none for a manual
  adjustment).

`InventoryMovementReportResponse` (xuất-nhập-tồn), one row per store product:
`storeProductId, sku, productName, baseUnit, openingQuantity, openingValue, stockIn, returnIn, adjustmentIn,
sale, adjustmentOut, reversal, closingQuantity, closingValue` + `totals` (values only). Quantities in base units
(per movement type, signed); values from `stock_movement_items.total_cost_snapshot` (signed like the quantity),
rounded to 2 decimals in the response.

`InventoryValuationReportResponse`: current picture, `rows [{ storeProductId, sku, productName, category,
onHandBaseQuantity, averageUnitCost, stockValue, expiredValue }]`, `byCategory [{ categoryId, name, stockValue }]`,
`totals { stockValue, expiredValue }` — `stockValue` from the lot balances, `expiredValue` = value of lots past
their expiry.

---

## 9. Tests — must prove

| Task | Must prove |
|---|---|
| F4.1 | sellable vs. on hand (expired/blocked lots excluded), low-stock flag, alert windows on Vietnam days, expire-due marks only ACTIVE lots past expiry and is idempotent |
| F4.2 | adjustment movements and costs, uncounted line blocks completion, stale line blocks completion until refreshed and recounted (only that line), never below reserved |
| F4.3 | template round-trip (download → fill → preview → import), per-row errors with column names, no partial receipt on error, lot/expiry/packaging rules identical to manual entry, size/row limits |
| F4.4 | returnable quantity counts in-flight returns, return value rounding example, RETURN_IN at original COGS into the original lot, debt-first settlement (design §XXII: 8M → debt 8M; 15M → debt 10M + refund 5M) |
| F4.5 | refund total cap per return and per payment, failed refund retried, return COMPLETED only when refunds cover the total, payment (PARTIALLY_)REFUNDED, proof photo of a refund cannot be deleted |
| F4.6 | opening + lines = closing = lot balances; report totals match the stock card; valuation equals Σ lot values |

---

## 10. Decisions that apply to L4

| # | Decision |
|---|---|
| C-D2 | Cancelling an order reverses the unconsumed ORDER allocations and requests one PENDING refund per payment through the Order aggregate; staff pay back in cash or by bank transfer and complete it (schema: `refunds.order_id`, design §35.18) |
| C-D3 | Refund proof images use `POST /api/files/delivery-proofs`; a referenced photo cannot be deleted (422) |
| C-D5 | Stocktake create/count/refresh, return request/receive/inspect = Operate; stocktake completion, manual stock adjustments, return approve/reject/complete-inspection and every refund = Manage — they can decrease stock or pay money out; the person who counts is not the person who approves |
| C-D6 | Each stocktake line stores `snapshot_at`; a stale line blocks completion; only stale lines are refreshed and recounted (design §35.19) |
| D5 | Delivery incidents that move stock are posted through `POST /api/inventory/adjustments` and linked by L2 |
| F-D4 | Every refund endpoint belongs to L4 |
| F-D5 | Reports are Manage-only and owned by the flow whose data they read |
| F-D6 | Lots are marked EXPIRED by `POST /api/inventory/lots/expire-due`; sale/reservation checks still use the expiry date |
| F-D7 | Excel import uses ClosedXML 0.105.1 (MIT, approved 2026-10-03), only inside `Infrastructure/Spreadsheets`; F4.3 was done by the lead |
