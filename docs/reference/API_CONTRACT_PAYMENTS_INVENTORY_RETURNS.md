# API Contract — Payments, payOS, Stocktake/Adjustments and Returns (Group C)

Version 1.0 — 2026-10-02. Scope: tasks C1–C5 and the interfaces group C provides to groups A and B.

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

- ORDER_PAYMENT: `orderId` required; payer = the order's Farmer (null for WALK_IN). The order must not be
  CANCELLED or COMPLETED; `amount` ≤ order total − already paid (no overpayment, 422). Creates one ORDER
  allocation. If the order is a confirmed CREDIT order, calls B's `ICreditReservationAdjuster` (design §XVI-D).
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
    // The order is being cancelled before any fulfillment: reverse its unconsumed ORDER allocations
    // (design §35.7 forbids reversing a consumed one) and cancel its PENDING payOS payments.
    // Returns the reversed amount, which staff hand back outside the system (decision C-D2).
    Task<decimal> ReverseForCancelledOrderAsync(Guid orderId, Guid actorId, string reason, CancellationToken cancellationToken);
}
```

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
- Complete (one transaction): every line counted (§35.8); refuse (422, listing the lots) if a lot had any
  stock movement after the snapshot — those lines are recounted after a fresh stocktake; lock lot balances;
  one ADJUSTMENT_IN movement (`ReceiveStock` at the snapshot/entered cost) and one ADJUSTMENT_OUT movement
  (`IssueUnreserved` at average cost), both linked by `stocktake_id`; a decrease below the lot's reserved
  quantity is refused (422) — the reservation must be moved first.
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

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/me/payments/payos` | FARMER | `PayOsPaymentRequest` | `201 PayOsPaymentResponse` |
| POST | `/api/payments/payos` | Operate | `PayOsPaymentRequest` (+ `farmerProfileId` for debt) | `201 PayOsPaymentResponse` |
| POST | `/api/me/payments/{id}/cancel` | FARMER | — | `200 PaymentResponse` (own PENDING) |
| POST | `/api/payments/payos/webhook` | anonymous | payOS payload | `200` |

`PayOsPaymentRequest`:

```json
{
  "paymentContext": "ORDER_PAYMENT | DEBT_REPAYMENT",
  "orderId": "uuid | null",
  "amount": "number | null",
  "debtAllocations": [ { "debtEntryId": "uuid", "amount": 500000.00 } ]
}
```

`PayOsPaymentResponse`: `{ paymentId, paymentNumber, amount, checkoutUrl, providerOrderCode, status }`.

Rules:
- ORDER: `amount` defaults to the order's remaining amount to pay; a Farmer only pays their own orders.
  DEBT: `amount` required (≤ balance); a Farmer only pays their own debt.
- Creates a PENDING PAYOS payment, a unique `providerOrderCode` (positive, ≤ 9007199254740991, generated by
  the server, retried on unique clash), then `IPaymentGateway.CreatePaymentLinkAsync`. The intended target
  (order or requested debt allocations) is stored with the payment (decision C-D1).
- The return/cancel URLs (`PayOS:ReturnUrl`, `PayOS:CancelUrl`, frontend pages) are never the source of
  truth; clients poll `GET /api/me/payments/{id}`.
- Cancel of a PENDING payOS payment also cancels the link at payOS (`IPaymentGateway` gains
  `CancelPaymentLinkAsync`).
- Secrets `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey` only in User Secrets / environment; never
  logged. The payOS adapter lives in `Infrastructure` behind `IPaymentGateway` (HttpClient + HMAC; no SDK
  package unless the team approves one).

Webhook (one transaction, idempotent, design §36):
1. `VerifyWebhook(raw)`: invalid signature → `400` and a warning log (no payload values).
2. Lock the payment by `providerOrderCode` (`IRowLockService.LockPaymentAsync`, added by C3).
   Unknown code → `200` + warning log.
3. Already PAID (duplicate delivery) → `200`, nothing changes.
4. Amount ≠ payment amount → `200`, payment stays PENDING, error log for staff follow-up.
5. Success → `MarkPaid(PAYOS_WEBHOOK)`, then the same allocation as cash (ORDER allocation +
   `ICreditReservationAdjuster`, or `IDebtRepaymentPosting`); failure → `MarkFailed`.
6. A success for a payment cancelled locally → `200`, critical log (money received on a cancelled
   payment) for manual handling; nothing is allocated automatically.

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

Provided by C (real implementations):

| Interface | Defined in | Used by |
|---|---|---|
| `IOrderPrepaymentLedger` | `API_CONTRACT_CUSTOMERS_CREDIT.md` §8 | B4, B5 |
| `IOrderPaymentCancellation` | this file §3.2 | A2 (order cancel) |
| `IPaymentGateway` (+ `CancelPaymentLinkAsync`) | `Application/Common/Interfaces` | C3 only |

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
| C1 | no overpayment, allocation only on PAID, prepayment consumed oldest first, reversal refused once consumed |
| C2 | adjustment movements and costs, uncounted line blocks completion, stale lot blocks completion, never below reserved |
| C3 | webhook idempotent (same payload twice = one PAID), bad signature 400, amount mismatch stays PENDING, unknown code 200 |
| C4 | returnable quantity counts in-flight returns, return value rounding example, RETURN_IN at original COGS into the original lot, debt-first settlement (design §XXII: 8M → debt 8M; 15M → debt 10M + refund 5M) |
| C5 | refund total cap, failed refund retried, return COMPLETED only when refunds cover the total |

---

## 10. Decisions (proposed defaults — applied unless the team objects before C starts)

| # | Decision |
|---|---|
| C-D1 | **Schema gap:** a PENDING payOS payment has no column for its target order / debt allocations (allocations exist only once PAID). Proposal without migration: keep the intent in `payments.provider_metadata` under a reserved `intent` key (`{"intent":{"orderId":…}}` or `{"intent":{"debtAllocations":[…]}}`), and merge — never overwrite — provider data into the same JSON. Alternative: a reviewed migration adding `target_order_id`; choose one before C3 |
| C-D2 | **Design gap:** `refunds.sales_return_id` is NOT NULL, so money paid for an order cancelled before fulfillment cannot be recorded as a refund. MVP: cancellation reverses the unconsumed ORDER allocations (`IOrderPaymentCancellation`), the payment stays PAID with an unallocated amount, staff hand the money back outside the system and the reason is audited |
| C-D3 | Refund proof images reuse the delivery photo upload (`/api/files/delivery-proofs`) |
| C-D4 | No overpayment: order payments ≤ remaining to pay; debt repayments ≤ current balance, fully allocated |
| C-D5 | Roles: cash payments, stocktake create/count, return request/receive/inspect = Operate; stocktake completion, manual adjustments, return approve/reject/complete-inspection, all refunds = Manage; Farmers pay, request and cancel only their own |
| C-D6 | A stocktake completion is refused if a counted lot moved after its snapshot; staff recount in a new stocktake |
