# API Contract — Payments, payOS, Stocktake/Adjustments and Returns (Group C)

Version 1.6 — 2026-10-03 (all decisions settled; interfaces and temporary implementations in the code). Scope: tasks C1–C5 and the interfaces group C provides to groups A and B.

Sources: `DATABASE_DESIGN.md` §28–29, §36–37, §52–54, §7.5, §XVI-D, §XXII, §35.7–35.11; `BUSINESS_RULES.md`
rules 10, 25–27, 30–33, 52–58. When this file and the design disagree, the design wins and this file is
corrected.

**Changing this contract:** PR on this file first, then code.

---

## 1. Conventions

Same as `API_CONTRACT_SALES.md` §1 (routes, JSON, enum strings, money ≤2 decimals, paging, errors,
`ApiRoles`). Document numbers (shared `DocumentNumbers`, Vietnam day):

| Document | Format |
|---|---|
| Payment | `PM-yyyyMMdd-NNNN` |
| Stocktake | `ST-yyyyMMdd-NNNN` |
| Sales return | `RT-yyyyMMdd-NNNN` |
| Refund | `RF-yyyyMMdd-NNNN` |
| Adjustment / return stock movement | `SM-yyyyMMdd-NNNN` (existing) |

## 2. Tasks and order

| Order | Task | Content | Depends on | Delivers to others |
|---|---|---|---|---|
| 1 | **C1** | Cash payments, payment queries, `IOrderPrepaymentLedger`, `IOrderPaymentCancellation` | — (B interfaces via temporary implementations) | A3 (decision D3), B4/B5 |
| 2 | **C2** | Stocktake and manual stock adjustments | — | A6 (decision D5) |
| 3 | **C3** | payOS payment links and webhook | C1 | Farmer online payment |
| 4 | **C4** | Sales returns: request → approve → receive → inspect → settle | A4/A6 merged, B5 `IDebtReturnPosting` | — |
| 5 | **C5** | Refunds | C4 | — |

C1 goes first: under decision D3 a FULL_PAYMENT order can only be confirmed once its payment is recorded,
so the real counter flow needs C1.

---

## 3. C1 — Payments (cash) and payment queries

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/payments/cash` | Operate | `CashPaymentRequest` | `201 PaymentResponse` (PAID) |
| GET | `/api/payments` | Operate | `PaymentListRequest` (query) | `200 PagedResult<PaymentListItem>` |
| GET | `/api/payments/{id}` | Operate | — | `200 PaymentResponse` |
| GET | `/api/orders/{id}/payments` | Operate | — | `200 OrderPaymentSummary` |
| POST | `/api/payments/{id}/cancel` | Operate | `{ reason? }` | `200 PaymentResponse` (only PENDING) |
| GET | `/api/me/payments` (+ `/{id}`) | FARMER | query: `status`, paging | own payments only |
| GET | `/api/me/orders/{id}/payments` | FARMER | — | `200 OrderPaymentSummary` (own order) |

`CashPaymentRequest`:

```json
{
  "paymentContext": "ORDER_PAYMENT | DEBT_REPAYMENT",
  "amount": 1250000.00,
  "orderId": "uuid | null",
  "farmerProfileId": "uuid | null",
  "debtAllocations": [ { "debtEntryId": "uuid", "amount": 500000.00 } ],
  "note": "string ≤1000 | null"
}
```

Cash is received physically by the staff member, so one request creates the payment, marks it PAID
(`confirmation_source = STAFF`, `confirmed_by` = caller) and allocates it, in one transaction.

- ORDER_PAYMENT: `orderId` required and stored in `payments.order_id` (design §35.18); payer = the order's
  Farmer (null for WALK_IN). The order must not be CANCELLED, PARTIALLY_CANCELLED or COMPLETED; `amount` ≤
  order total − already paid (no overpayment, 422). Creates one ORDER allocation (only to that order). If the
  order is a confirmed CREDIT order, calls B's `ICreditReservationAdjuster` (design §XVI-D).
- DEBT_REPAYMENT: `farmerProfileId` required; `amount` ≤ the Farmer's current debt balance (422).
  Calls B's `IDebtRepaymentPosting.ApplyAsync` with `debtAllocations` (or null = oldest due first, rule 27);
  explicit allocations must sum to `amount`. The whole amount is allocated (no floating cash).
- Allocations are only created on PAID payments (design §35.7); the context binds the allocation type.

`PaymentResponse`:

```json
{
  "id": "uuid", "paymentNumber": "PM-20261002-0001",
  "paymentContext": "ORDER_PAYMENT", "paymentMethod": "CASH",
  "amount": 1250000.00, "currency": "VND", "status": "PAID",
  "payerFarmerProfileId": "uuid | null", "payerName": "…",
  "confirmationSource": "STAFF", "confirmedBy": "uuid", "confirmedAt": "…",
  "checkoutUrl": null, "providerOrderCode": null,
  "initiatedAt": "…", "failedAt": null, "cancelledAt": null, "note": null,
  "unallocatedAmount": 0.00,
  "allocations": [
    { "id": "uuid", "allocationType": "ORDER", "orderId": "uuid", "orderNumber": "OD-…",
      "debtEntryId": null, "entryNumber": null, "allocatedAmount": 1250000.00,
      "prepaymentConsumedAmount": 0.00, "status": "ACTIVE", "allocatedAt": "…" }
  ]
}
```

`PaymentListRequest` (query): `paymentContext`, `paymentMethod`, `status`, `orderId`, `farmerProfileId`,
`fromDate`, `toDate`, `search` (payment number), paging.

`OrderPaymentSummary`: `{ orderId, orderTotal, paidAmount, availablePrepayment, consumedPrepayment,
remainingToPay, payments: [PaymentListItem] }`.

### 3.1 `IOrderPrepaymentLedger` — real implementation (contract in `API_CONTRACT_CUSTOMERS_CREDIT.md` §8)

- `GetPaidAmountAsync` = Σ active ORDER allocations of PAID payments for the order.
- `GetAvailableAsync` = Σ (allocated − prepayment consumed) of those allocations.
- `ConsumeAsync(orderId, max)` consumes oldest allocations first (rule 25) via
  `Payment.ConsumePrepayment`, returns the consumed amount; runs in the caller's transaction.

### 3.2 `IOrderPaymentCancellation` — provided to A (order cancellation)

```csharp
public interface IOrderPaymentCancellation
{
    // Called by A in its own transaction right after the order became CANCELLED, or PARTIALLY_CANCELLED after
    // its last open quantity was cancelled (decision C-D2, design §35.18):
    // 1. cancel the order's PENDING payOS payments (payOS cancel API first);
    // 2. per PAID payment of the order: reverse its unconsumed ORDER allocation (a consumed allocation keeps
    //    its consumed part; design §35.7) and request one PENDING cancelled-order refund for that amount
    //    through Order.RequestCancellationRefund — method CASH for a cash payment, BANK_TRANSFER for payOS.
    // Returns the requested refunds so staff know what to hand back. Never saves or commits.
    Task<IReadOnlyList<CancellationRefundInfo>> ReverseForCancelledOrderAsync(
        Order order, Guid actorId, string reason, CancellationToken cancellationToken);
}

public sealed record CancellationRefundInfo(
    Guid RefundId, string RefundNumber, Guid PaymentId, RefundMethod RefundMethod, decimal Amount);
```

Refunds of a cancelled order (Manage; staff pay back outside the system, no automatic payOS refund):

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/orders/{id}/refunds` | — | `200 RefundResponse[]` |
| POST | `/api/orders/{id}/refunds` | `{ originalPaymentId, refundMethod, amount, note? }` | `201 RefundResponse` (retry after a FAILED/CANCELLED one) |
| POST | `/api/orders/{id}/refunds/{refundId}/complete` | `{ externalReference?, proofFileUrl?, note? }` | `200 RefundResponse` |
| POST | `/api/orders/{id}/refunds/{refundId}/fail` | `{ note? }` | `200 RefundResponse` |
| POST | `/api/orders/{id}/refunds/{refundId}/cancel` | `{ reason }` | `200 RefundResponse` |

- Only for CANCELLED / PARTIALLY_CANCELLED orders (Domain). For one payment, PENDING + COMPLETED cancelled-order
  refunds ≤ the amount reversed from it by the cancellation (Application, under a row lock on the payment).
- The Farmer sees the refunds of their own order in `GET /api/me/orders/{id}/payments` (`refunds` field).
- `RefundResponse` is the same shape for return refunds and cancelled-order refunds: `id, refundNumber,
  source (SALES_RETURN | ORDER), salesReturnId, orderId, originalPaymentId, refundMethod, amount, status,
  externalReference, proofFileUrl, requestedBy/At, completedBy/At, cancelledBy/At, cancelReason, note`.
- Completing refunds sets the payment PARTIALLY_REFUNDED / REFUNDED (the Payment Domain method is added by C,
  shared with C5).

---

## 4. C2 — Stocktake and manual adjustments

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
current on hand and `unit_cost_snapshot` = current average cost (null when on hand = 0).

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

`StocktakeResponse`: `id, stocktakeNumber, status, note, createdBy, createdAt, startedBy, startedAt,
completedBy, completedAt, totals {lines, counted, withDifference, differenceCostValue},
movements [{id, movementNumber, movementType}], items [{id, inventoryLotId, sku, productName, lotNumber,
expiryDate, systemQuantitySnapshot, countedQuantity, differenceQuantity, unitCostSnapshot,
differenceCostValue, reasonCode, note, countedBy, countedAt}]`.

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
- This is how a delivery incident that destroys goods is posted (decision D5); A6 links the returned
  movement id when resolving the incident.

---

## 5. C3 — payOS

Verified against the payOS .NET SDK `payOS` 2.1.0 and its earlier use in the team's NutriPlan project
(2026-10-02). Items marked *(verify)* come from the payOS documentation and must be checked against the
store's payOS account before go-live.

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/me/payments/payos` | FARMER | `PayOsPaymentRequest` | `201 PayOsPaymentResponse` |
| POST | `/api/payments/payos` | Operate | `PayOsPaymentRequest` (+ `farmerProfileId` for debt) | `201 PayOsPaymentResponse` |
| POST | `/api/me/payments/{id}/cancel` | FARMER | — | `200 PaymentResponse` (own PENDING) |
| POST | `/api/payments/{id}/sync` | Operate; FARMER own | — | `200 PaymentResponse` |
| POST | `/api/payments/payos/webhook` | anonymous | payOS payload | `200` / `400` |

`PayOsPaymentRequest`:

```json
{
  "paymentContext": "ORDER_PAYMENT | DEBT_REPAYMENT",
  "orderId": "uuid | null",
  "amount": "number | null"
}
```

A payOS debt repayment has no entry selection: it is allocated oldest due date first when PAID (decision C-D1;
the app shows the split with B's `allocation-preview`). Choosing specific debt entries is done in cash at the
counter.

`PayOsPaymentResponse`: `{ paymentId, paymentNumber, amount, checkoutUrl, qrCode, providerOrderCode,
expiresAt, status }`. `qrCode` is the VietQR payload string (the client renders it as a QR image).

### 5.1 Facts about payOS that shape the rules

| Fact | Consequence |
|---|---|
| `amount` is an integer (VND, `long`) for links, webhooks and status queries | a payOS payment amount must be whole VND (rule below) |
| `orderCode` is a positive `long`, unique per merchant; the SDK refuses values > 9007199254740991 | server-generated, never reused, unique index on `provider_order_code` |
| Create-link signature = HMAC-SHA256(checksum key, `amount=…&cancelUrl=…&description=…&orderCode=…&returnUrl=…`) | computed by the SDK, never by clients |
| Webhook body = `{ code, desc, success, data { orderCode, amount, description, reference, transactionDateTime, paymentLinkId, code, desc, counterAccount… }, signature }`; signature = HMAC-SHA256 over the `data` fields sorted by name | verified with the SDK (`Webhooks.VerifyAsync`); paid = `success` and `data.code == "00"` |
| Links can expire (`expiredAt`, unix seconds); link statuses PENDING, PROCESSING, PAID, UNDERPAID, CANCELLED, EXPIRED, FAILED | every link gets an expiry; the status query (`PaymentRequests.GetAsync(orderCode)`) is used for reconciliation |
| A link can be cancelled by order code with a reason (`PaymentRequests.CancelAsync`) | cancellation goes to payOS first, then locally |
| `returnUrl` / `cancelUrl` receive query parameters (`code`, `id`, `cancel`, `status`, `orderCode`) from a browser redirect | never trusted: the frontend only reads `orderCode` and asks our API |
| The webhook URL is registered once per payment channel (payOS dashboard or `Webhooks.ConfirmAsync(url)`); payOS calls it with a test payload and expects 2xx | the endpoint answers 200 to the test call; it needs a public HTTPS URL (a tunnel such as ngrok or cloudflared in development) |
| `description` at most 25 characters, at most 9 when the receiving bank account is not linked through payOS *(verify)* | description = payment number without dashes, e.g. `PM202610020001` (14 chars); if the account needs ≤ 9, use the last 9 digits of `orderCode` |
| No separate sandbox *(verify)*: tests use real small transfers | real payOS tests are opt-in (like `RealStorageFact`) and never run in CI |

### 5.2 Rules

- ORDER: `amount` defaults to the order's remaining amount to pay; a Farmer only pays their own orders.
  DEBT: `amount` required (≤ balance); a Farmer only pays their own debt.
- **Whole VND only (decision C-D9):** a payOS amount with a fractional part is refused (422); the fraction
  is paid in cash. Price lists in VND should use whole numbers.
- Creates a PENDING PAYOS payment with a new `providerOrderCode` (time-based + random suffix, retried on a
  unique clash), the description of §5.1, `expiredAt = now + PayOS:LinkExpiryMinutes` (default 30), then
  `IPaymentGateway.CreatePaymentLinkAsync`. The target order is stored in `payments.order_id`; a debt
  repayment is allocated oldest due date first when PAID (decision C-D1). If payOS refuses, the payment is
  marked FAILED and the API answers 503 without provider details.
- At most one PENDING payOS payment per order: a new request cancels the previous link first.
- Cancel: `PaymentRequests.CancelAsync(orderCode, reason)` at payOS, then `Payment.Cancel`. If payOS reports
  the link as already PAID, nothing is cancelled and the payment is synced instead.
- `POST /{id}/sync` queries `GetAsync(orderCode)`: EXPIRED / CANCELLED / FAILED → payment FAILED / CANCELLED;
  PAID → decision C-D8; PENDING / PROCESSING → unchanged. After the redirect the client polls
  `GET /api/me/payments/{id}` and may call `sync` once the link has expired.
- Secrets `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey` only in User Secrets / environment, never in
  `appsettings*.json`, never logged; `PayOS:ReturnUrl`, `PayOS:CancelUrl`, `PayOS:LinkExpiryMinutes` are not
  secret. Missing configuration → 503 on payOS endpoints only (like Storage).
- Adapter (decision C-D7, settled): `Infrastructure/Payments/PayOsPaymentGateway` wraps the SDK `PayOSClient`
  (shared `HttpClient`; `MaxRetries = 0` for link creation so a timeout never creates two links; an
  uncertain result is resolved with `GetAsync(orderCode)`).
- `IPaymentGateway` changes in C3 (nobody implements it yet): `VerifyWebhookAsync` (the SDK verification is
  async; no `.Result`), `GetPaymentLinkAsync(orderCode)`, `CancelPaymentLinkAsync(orderCode, reason)`,
  `ExpiresAt` and `QrCode` in the create request/result, `long` amounts at the adapter boundary.

### 5.3 Webhook (one transaction, idempotent, design §36)

1. Body without `data` or `signature` (registration test, health check) → `200`, nothing changes.
2. `VerifyWebhookAsync`: invalid signature → `400` and a warning log (no payload values).
3. Lock the payment by `providerOrderCode` (`IRowLockService.LockPaymentAsync`, added by C3).
   Unknown code (including the registration test order) → `200` + information log.
4. Already PAID (duplicate delivery) → `200`, nothing changes.
5. `data.amount` ≠ payment amount → `200`, payment stays PENDING, error log for staff follow-up. An
   overpayment is not accepted as paid.
6. Paid (`success` and `data.code == "00"`) → `MarkPaid(PAYOS_WEBHOOK, providerTransactionId = data.reference)`,
   raw `data` stored in `provider_metadata`, then the same allocation as cash
   (ORDER allocation to `payments.order_id` + `ICreditReservationAdjuster`, or `IDebtRepaymentPosting` with no
   explicit allocations = oldest due first). Not paid → `MarkFailed`.
7. Paid for a payment cancelled locally → `200`, critical log (money received on a cancelled payment) for
   manual handling; nothing is allocated automatically.
8. Unexpected exception → `500` so payOS retries; business rejections after a valid signature never answer
   4xx, so payOS does not retry them forever.

Kept from NutriPlan: SDK verification, `success && code == "00"`, a link expiry, 200 for the registration
test. Not repeated: cancelling a payment because the return URL says `cancel=true` (only payOS's own cancel or
status API changes a payment here), accepting `amount ≥` as paid, no row lock against duplicate concurrent
webhooks, returning raw exception messages to clients.

---

## 6. C4 — Sales returns

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

`CreateReturnRequest`: `{ orderId, reasonSummary?, note?, items: [ReturnItemRequest] }`.

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
  `IDebtReturnPosting.ApplyReturnAsync` (B5) returns the debt reduction → `total_debt_adjustment`; the rest is
  `total_refund_amount`; one RETURN_IN movement for RESTOCK lines back into the **original lot** at the
  original COGS unit cost (`ReceiveStock`), linked per line; WRITE_OFF lines change no stock (the goods
  already left stock at sale). Status → COMPLETED when nothing remains to refund, else PARTIALLY_RESOLVED.
- The original order is never modified.

`SalesReturnResponse`: header (number, order, Farmer, status, all actor/timestamp fields, totals) +
`items` (source ids, lot, quantity, prices, return value, COGS, reason, condition, disposition,
`returnStockMovementId`, `debtAdjustmentTransactionId`) + `refunds`.

---

## 7. C5 — Refunds

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/returns/{id}/refunds` | Manage | `RefundRequest` | `201 RefundResponse` (PENDING) |
| POST | `/api/returns/{id}/refunds/{refundId}/complete` | Manage | `{ externalReference?, proofFileUrl?, note? }` | `200` |
| POST | `/api/returns/{id}/refunds/{refundId}/fail` | Manage | `{ note? }` | `200` |
| POST | `/api/returns/{id}/refunds/{refundId}/cancel` | Manage | `{ reason }` | `200` |

`RefundRequest`: `{ refundMethod: "CASH | BANK_TRANSFER | OTHER_EXTERNAL", amount, originalPaymentId?,
externalReference?, note? }`.

- No automatic payOS refund (rule 33): staff pay back outside the system and record it.
- Σ refunds not CANCELLED/FAILED ≤ `total_refund_amount`; a FAILED refund is retried with a new one.
- Completing the last refund completes the return (COMPLETED rule, §35.9). When `originalPaymentId` is set,
  the payment becomes PARTIALLY_REFUNDED / REFUNDED by the completed refunded sum (C5 adds that Domain
  method).
- `proofFileUrl` (optional): an image uploaded with `POST /api/files/delivery-proofs` (decision C-D3).

---

## 8. Interfaces

All interfaces below and their temporary implementations are already in the code on `main` (`Features/Payments`,
`Features/Credit`, `Features/Debt`; `Common/Placeholders`) — see `API_CONTRACT_SALES.md` §9 for the replacement
procedure. C1 replaces `TemporaryOrderPrepaymentLedger` and `TemporaryOrderPaymentCancellation`.

Provided by C (real implementations):

| Interface | Defined in | Used by |
|---|---|---|
| `IOrderPrepaymentLedger` | `API_CONTRACT_CUSTOMERS_CREDIT.md` §8 | B4, B5 |
| `IOrderPaymentCancellation` | this file §3.2 | A2 (order cancel) |
| `IPaymentGateway` (changes listed in §5.2) | `Application/Common/Interfaces` | C3 only |

Used by C (owned by B): `ICreditReservationAdjuster`, `IDebtRepaymentPosting`, `IDebtReturnPosting`
(`API_CONTRACT_CUSTOMERS_CREDIT.md` §6.4, §7.2, §7.3). Until B delivers them, C registers temporary
implementations: the adjuster does nothing, repayment posting refuses DEBT_REPAYMENT (422 "debt is not
available yet"), return posting applies 0 (everything is refunded).

C use cases own their transactions (cash payment, webhook, stocktake completion, adjustment,
return inspection, refund); the B interfaces they call never save or commit.

---

## 9. Test expectations

Each task: unit, offline HTTP (401/403 per role, 400, webhook without auth reachable), rolled-back real
PostgreSQL. In addition:

| Task | Must prove |
|---|---|
| C1 | no overpayment, allocation only on PAID and only to the payment's own order, prepayment consumed oldest first, reversal refused once consumed; cancelling a paid order reverses the unconsumed part and requests one refund per payment, refund cap per payment, completing refunds marks the payment (PARTIALLY_)REFUNDED |
| C2 | adjustment movements and costs, uncounted line blocks completion, stale line blocks completion until refreshed and recounted (only that line), never below reserved |
| C3 | webhook idempotent (same payload twice = one PAID, also concurrently), bad signature 400, registration test and unknown code 200, amount mismatch and overpayment stay PENDING, fractional amount 422, expired link synced to FAILED; adapter tested with a fake gateway, real payOS only opt-in |
| C4 | returnable quantity counts in-flight returns, return value rounding example, RETURN_IN at original COGS into the original lot, debt-first settlement (design §XXII: 8M → debt 8M; 15M → debt 10M + refund 5M) |
| C5 | refund total cap, failed refund retried, return COMPLETED only when refunds cover the total |

---

## 10. Decisions

All decisions of group C are settled by the team (2026-10-02); none is pending.

| # | Decision |
|---|---|
| C-D7 | Use the official payOS .NET SDK (`payOS` 2.1.0, already used in NutriPlan) **only** inside `Infrastructure/Payments/PayOsPaymentGateway`; Application depends only on `IPaymentGateway`. Adding the package to `AgriSage.Infrastructure.csproj` is approved |
| C-D8 | Missed webhook: when `sync` finds the link PAID at payOS, the payment is applied exactly like a webhook (same lock, amount check, idempotency and allocation) with `confirmation_source = PAYOS_WEBHOOK` and `provider_metadata.confirmedVia = "STATUS_QUERY"`. No schema change |
| C-D9 | payOS amounts are whole VND; a fractional amount is refused (422) and that part is paid in cash. VND price lists and price overrides should use whole numbers |
| C-D1 | **Schema change (migration `PaymentOrderLinkAndOrderRefunds`, design §35.18):** `payments.order_id` (NULL FK orders; ORDER_PAYMENT ⇒ NOT NULL, DEBT_REPAYMENT ⇒ NULL). A PENDING payOS payment knows its order from creation; an ORDER payment is allocated only to that order. payOS debt repayments are allocated oldest due date first (no entry selection online) |
| C-D2 | **Schema change (same migration):** `refunds.sales_return_id` becomes NULL, new `refunds.order_id`; exactly one source; a cancelled-order refund needs its original payment. Cancelling an order (or its last open quantity) reverses the unconsumed ORDER allocations and requests one PENDING refund per payment through the Order aggregate; staff pay back in cash or by bank transfer and complete it |
| C-D3 | Refund proof images (return refunds and cancelled-order refunds) use the delivery photo upload `POST /api/files/delivery-proofs`. A photo referenced by `delivery_attempts.proof_image_url`, `delivery_incidents.evidence_image_url` or `refunds.proof_file_url` can no longer be deleted through `DELETE /api/files/delivery-proofs` (422); A6 adds the check for attempts/incidents, C5 extends it to refunds |
| C-D4 | No overpayment: an order payment ≤ order total − already paid; a debt repayment ≤ current debt balance and is fully allocated. At the counter staff record only the amount due and give change physically |
| C-D5 | Roles: cash payments, payment queries, stocktake create/count/refresh, return request/receive/inspect = Operate; stocktake completion, manual stock adjustments, return approve/reject/complete-inspection and every refund (return or cancelled order) = Manage; Farmers pay, request returns and cancel only their own. Unlike goods receipt confirmation (open to Sales), stocktake completion and manual adjustments stay with Admin/Store Owner because they can **decrease** stock — the person who counts is not the person who approves |
| C-D6 | **Schema change (migration `StocktakeItemSnapshotTime`, design §35.19):** each stocktake line stores `snapshot_at`; a line is stale when a movement for its lot was posted between the snapshot (minus 5 minutes) and the count, which also catches offsetting movements. A stale line blocks completion; `POST /api/stocktakes/{id}/refresh-stale` re-snapshots only the stale lines and clears their counts for a recount (§4.1) |
