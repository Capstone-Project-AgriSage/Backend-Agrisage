# Flow L1 — Counter Sale, Pay Now (owner: Lead)

Version 2.0 — 2026-10-03. Conventions, shared shapes (`OrderResponse`, `PaymentResponse`, `DeliveryAddressRequest`),
interfaces and ownership rules: [README.md](README.md). Sources: `DATABASE_DESIGN.md` §18–19, §30–37, §7.2–7.4,
§XVI-A, §XVIII, §35.3–35.4, §35.7, §35.18; `BUSINESS_RULES.md` rules 6–21, 25.

L1 builds the **sales core** that L2 and L3 reuse: price resolution, order creation (`OrderBuilder`), cash payments
and allocation (`PaymentAllocator`), confirmation with FEFO reservation (`OrderConfirmer`), fulfillment posting
(`FulfillmentPostingService`) and cancellation (`OrderCanceller`).

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Owner creates the walk-in default price list and its prices | `POST /api/price-lists`, `PUT /{id}/items`, `POST /{id}/activate` | ACTIVE walk-in list; catalog shows `price` |
| 2 | Sales opens a counter order for a walk-in customer | `POST /api/orders` (`WALK_IN`, `FULL_PAYMENT`, `PICKUP`) | order `PENDING_CONFIRMATION`, prices snapshotted |
| 3 | Sales overrides one price with a reason | `PUT /api/orders/{id}/items/{itemId}/price` | suggested price, actor, reason kept |
| 4 | Customer pays cash | `POST /api/payments/cash` (`ORDER_PAYMENT`) | payment PAID + ORDER allocation |
| 5 | Sales confirms; screen shows FEFO lots | `GET /api/orders/{id}/fefo-suggestions`, `POST /api/orders/{id}/confirm` | lots reserved (earliest expiry first), order `CONFIRMED` |
| 6 | Goods handed over with the actual lots | `POST /api/orders/{id}/pickup` | SALE movement, on hand ↓, reservation consumed, prepayment consumed, order `COMPLETED` |
| 7 | Same sale in one call | `POST /api/counter-sales/preview`, `POST /api/counter-sales` | steps 2–6 atomically |
| 8 | Another order is cancelled after payment | `POST /api/orders/{id}/cancel` | reservation released, allocation reversed, PENDING refund requested (completed in L4) |
| 9 | Owner reads the day's sales | `GET /api/reports/sales` | revenue, cost of goods, gross profit |

---

## 2. Tasks

| Task | Content | Depends on | Delivers to |
|---|---|---|---|
| F1.1 | Price lists, walk-in default, real `IPriceResolver` (all 5 steps), catalog price | — | everyone who orders |
| F1.2 | Staff counter orders + `OrderBuilder` | F1.1 (tests may fake the resolver) | F2.3 |
| F1.3 | Cash payments, payment queries, real `IOrderPrepaymentLedger`, `PaymentAllocator` | F1.2 | F2.4, F3.3–F3.5 |
| F1.4 | Confirmation, FEFO suggestions, reservation, preparing/ready | F1.2, F1.3 | F2.5, F1.7 |
| F1.5 | Pickup, cancel-remaining, `FulfillmentPostingService` | F1.4 | F2.6, F3.4, F4.4 |
| F1.6 | Order cancellation, `OrderCanceller`, real `IOrderPaymentCancellation` | F1.4 | F2.3, F4.5 |
| F1.7 | **New** quick counter sale | F1.2–F1.5 | — |
| F1.8 | **New** sales report | F1.5 | — |

Milestone **M1** = F1.1–F1.3 merged; **M2** = F1.4–F1.5 merged (README §6).

---

## 3. F1.1 — Price lists (Read: `Operate`, write: `Manage`) — done

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/price-lists` | Operate | query: `status`, `isWalkInDefault`, `search`, `page`, `pageSize` | `200 PagedResult<PriceListResponse>` |
| GET | `/api/price-lists/{id}` | Operate | — | `200 PriceListResponse` |
| POST | `/api/price-lists` | Manage | `PriceListRequest` | `201 PriceListResponse` (DRAFT) |
| PUT | `/api/price-lists/{id}` | Manage | `UpdatePriceListRequest` (= `PriceListRequest` without `code`) | `200 PriceListResponse` |
| POST | `/api/price-lists/{id}/activate` | Manage | — | `200 PriceListResponse` |
| POST | `/api/price-lists/{id}/deactivate` | Manage | — | `200 PriceListResponse` |
| DELETE | `/api/price-lists/{id}` | Manage | — | `204` |
| GET | `/api/price-lists/{id}/items` | Operate | query: `search`, `page`, `pageSize` | `200 PagedResult<PriceListItemResponse>` |
| PUT | `/api/price-lists/{id}/items` | Manage | `{ items: [ { storeProductId, productPackagingId, sellingPrice } ] }` | `200 { created, updated }` |
| DELETE | `/api/price-lists/{id}/items/{itemId}` | Manage | — | `204` |

The group ↔ price list routes (`PUT /api/customer-groups/{id}/price-list`, `GET /api/customer-groups/{id}/price-lists`)
belong to L3 (FLOW_3 §3).

`PriceListRequest`: `{ code, name, description?, effectiveFrom, effectiveTo?, isWalkInDefault }`.

`PriceListResponse`: `id, code, name, description, effectiveFrom, effectiveTo, isWalkInDefault, status,
itemCount, groups [{id, code, name}], createdAt`.

`PriceListItemResponse`: `id, storeProductId, productPackagingId, sku, productName, packagingName,
sellingPrice`.

Rules:
- `code` unique per store, ignoring case (409), immutable. There is no unique index on it, so the check is done by
  the Application (decision Q3 of the FLOW_1 plan). `effectiveTo` > `effectiveFrom` when given.
- Items: only ACTIVE **sale** packagings of the given store product; `sellingPrice` ≥ 0, ≤ 2 decimals;
  one row per (store product, packaging) — the bulk PUT upserts; at most 500 lines per call; a pair repeated in
  one call → 400. Lines that cannot be priced → 422 with `errors: { "items[3]": [reason] }` and nothing is saved.
  Pricing a removed pair again revives its row (`PriceListItem.Reinstate`; the unique index counts deleted rows).
  `created` counts new and revived lines, `updated` the lines whose price changed.
  Prices in VND should be whole numbers: payOS accepts only whole VND (decision C-D9), so a fractional total must
  be settled partly in cash. The UI should warn on a fractional price; the API still accepts up to 2 decimals.
- Items may change while the list is ACTIVE; this affects carts and **new** orders only (order lines keep
  their snapshot). Every write is audited in `audit_logs` (entity `PRICE_LIST`; actions `PRICE_LIST_CREATED`,
  `_UPDATED`, `_ACTIVATED`, `_DEACTIVATED`, `_DELETED`, `PRICE_LIST_ITEMS_CHANGED` with old/new prices,
  `PRICE_LIST_ITEM_REMOVED`).
- At most one ACTIVE walk-in default list at any time (activating a second one, or flagging an ACTIVE list as
  walk-in default while another one is ACTIVE → 422; the partial unique index answers a race with 409).
- DELETE only DRAFT lists never linked to a group and never used by an order (`price_list_id_snapshot`);
  otherwise 409 → deactivate.
- `groups` in `PriceListResponse` = groups linked now or from a future date (links not ended).
- Public catalog (existing `GET /api/catalog/products` and `/products/{id}`), from the ACTIVE walk-in default list
  valid now (`null` if none): each packaging of the detail gets `price` (sale packagings only), each list row gets
  `fromPrice` (lowest price among its ACTIVE sale packagings). Adding fields is non-breaking.

**Price resolution** (real `IPriceResolver`, README §4.1) — F1.1 implements all five steps, including the group
step, by reading the group tables directly (L3 only builds the screens that fill them):
1. Farmer → current `customer_group_assignments` row, or the store's default group (decision B-D2).
2. Group → its current `customer_group_price_lists` link whose list is ACTIVE and inside its own validity at `at`.
3. No such list (or walk-in) → the ACTIVE walk-in default list (decision B-D3).
4. No list at all → `BusinessRuleException` "no applicable price list" (→ 422).
5. Prices: `price_list_items.selling_price` per (store product, packaging); a missing pair = no price.

---

## 4. F1.2 — Staff counter orders (`Operate`)

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/orders` | `CreateCounterOrderRequest` | `201 OrderResponse` |
| GET | `/api/orders` | `OrderListRequest` (query) | `200 PagedResult<OrderListItem>` |
| GET | `/api/orders/{id}` | — | `200 OrderResponse` |
| PUT | `/api/orders/{id}` | `UpdateOrderRequest` | `200 OrderResponse` |
| POST | `/api/orders/{id}/items` | `OrderItemRequest` | `200 OrderResponse` |
| PUT | `/api/orders/{id}/items/{itemId}` | `{ quantity }` | `200 OrderResponse` |
| PUT | `/api/orders/{id}/items/{itemId}/price` | `{ unitPrice, reason }` | `200 OrderResponse` |
| DELETE | `/api/orders/{id}/items/{itemId}/price` | — | `200 OrderResponse` (back to suggested price) |
| DELETE | `/api/orders/{id}/items/{itemId}` | — | `200 OrderResponse` |

`CreateCounterOrderRequest`:

```json
{
  "customerType": "REGISTERED | WALK_IN",
  "farmerProfileId": "uuid | null",
  "customerName": "string ≤150 | null",
  "customerPhone": "string ≤20 | null",
  "settlementType": "FULL_PAYMENT | CREDIT",
  "fulfillmentType": "PICKUP | DELIVERY",
  "addressId": "uuid | null",
  "deliveryAddress": "DeliveryAddressRequest | null",
  "note": "string ≤1000 | null",
  "items": [ OrderItemRequest ]
}
```

`OrderItemRequest`:

```json
{ "storeProductId": "uuid", "productPackagingId": "uuid", "quantity": 5,
  "unitPrice": "number | null", "overrideReason": "string ≤500 | null" }
```

`OrderListRequest` (query): `status`, `customerType`, `settlementType`, `fulfillmentType`, `source`,
`farmerProfileId`, `fromDate`, `toDate` (Vietnam days, on `createdAt`), `search` (order number, customer
name or phone), `page`, `pageSize`. Sorted newest first.

`UpdateOrderRequest`: `{ addressId?, deliveryAddress?, note? }` — only while `PENDING_CONFIRMATION`.

Rules:
- REGISTERED: `farmerProfileId` required; name/phone are taken from the Farmer (client values ignored). A
  farmer without an account who needs group prices or credit is first created with L2's `POST /api/customers`
  (store-managed account, decision B-D1).
- WALK_IN: `farmerProfileId` must be null, `customerName` required, `settlementType` must be FULL_PAYMENT
  (CREDIT → 422); price list = the walk-in default price list.
- CREDIT: REGISTERED only; the ACTIVE credit profile is checked at confirmation (F1.4), not here.
- DELIVERY needs exactly one of `addressId` (one of the Farmer's addresses, copied as a snapshot) or
  `deliveryAddress` (decision D1: `addressId` works once F2.1 is merged).
- Snapshot at creation: customer group, price list, customer name and phone, address, and per line SKU, name,
  packaging name, conversion, suggested price.
- Only sellable store products of ACTIVE products and ACTIVE **sale** packagings (`isSaleUnit`), else 422; a line
  without a price in the resolved list → 422.
- Price override (decision D4): `unitPrice` different from the suggested price requires `overrideReason`; the
  server stores the suggested price, actor and reason. `unitPrice` without a different value is ignored.
- Items, header and prices change only while `PENDING_CONFIRMATION` (→ 422 otherwise).

**`OrderBuilder`** (shared step, README §4.9): input = customer (Farmer id or walk-in name/phone), source,
settlement, fulfillment, address snapshot, lines (+ optional overrides, staff only); output = a tracked
`PENDING_CONFIRMATION` `Order` with its number and snapshots. Used by `POST /api/orders`, F2.3 checkout (source
FARMER_WEB / FARMER_MOBILE, no overrides) and F1.7.

---

## 5. F1.3 — Cash payments and payment queries

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
(`confirmation_source = STAFF`, `confirmed_by` = caller) and allocates it through `PaymentAllocator`, in one
transaction.

- ORDER_PAYMENT: `orderId` required and stored in `payments.order_id` (design §35.18); payer = the order's
  Farmer (null for WALK_IN). The order must not be CANCELLED, PARTIALLY_CANCELLED or COMPLETED; `amount` ≤
  order total − already paid (no overpayment, decision C-D4, 422). One ORDER allocation, only to that order. If the
  order is a confirmed CREDIT order, `ICreditReservationAdjuster.OnOrderPrepaymentAsync` (design §XVI-D).
- DEBT_REPAYMENT (the cash counter of L3's flow): `farmerProfileId` required; `amount` ≤ the Farmer's current debt
  balance (422). `IDebtRepaymentPosting.ApplyAsync` with `debtAllocations` (or null = oldest due first, rule 27);
  explicit allocations must sum to `amount`. The whole amount is allocated (no floating cash). Until F3.5 is merged
  the temporary implementation refuses DEBT_REPAYMENT (422).
- Allocations are only created on PAID payments (design §35.7); the context binds the allocation type.
- payOS payments (F2.4) appear in the same lists and summaries.

`PaymentListRequest` (query): `paymentContext`, `paymentMethod`, `status`, `orderId`, `farmerProfileId`,
`fromDate`, `toDate`, `search` (payment number), paging.

`OrderPaymentSummary`: `{ orderId, orderTotal, paidAmount, availablePrepayment, consumedPrepayment,
remainingToPay, payments: [PaymentListItem], refunds: [RefundResponse] }` (`refunds` = cancelled-order refunds,
managed by F4.5).

**Real `IOrderPrepaymentLedger`** (README §4.2):
- `GetPaidAmountAsync` = Σ active ORDER allocations of PAID payments for the order.
- `GetAvailableAsync` = Σ (allocated − prepayment consumed) of those allocations.
- `ConsumeAsync(orderId, max)` consumes oldest allocations first (rule 25) via `Payment.ConsumePrepayment`,
  returns the consumed amount.
- All three also count allocations added earlier in the same unit of work (README §3.2).

**`PaymentAllocator`** (shared step, README §4.9): `AllocateAsync(Payment paidPayment, IReadOnlyList<RequestedDebtAllocation>? requested, Guid? actorId, ct)`;
used by the cash endpoint, the payOS webhook and sync (F2.4) and F1.7.

---

## 6. F1.4 — Confirmation and stock reservation (`Operate`)

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/orders/{id}/fefo-suggestions` | — | `200 FefoSuggestionResponse` |
| POST | `/api/orders/{id}/confirm` | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/start-preparing` | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/mark-ready` | — | `200 OrderResponse` |
| GET | `/api/orders/{id}/reservation` | — | `200 ReservationResponse` |

`confirm` — one transaction owned by `OrderConfirmer`:
1. Lock the order; must be `PENDING_CONFIRMATION` with ≥1 line.
2. `IOrderSettlementGuard.EnsureCanConfirmAsync` (README §4.4): payment / credit check, credit reservation and the
   credit term for CREDIT orders. A failure → 422 and nothing is saved.
3. FEFO allocation per line over eligible lots (decision D6: ACTIVE, not expired, available > 0; earliest expiry
   first, no-expiry lots last, then oldest lot). Lock the chosen lot balances in id order.
4. `InventoryLot.Reserve` per lot; create the reservation header (one active per order) and its items.
5. `order.Confirm(...)` with the credit term; one `SaveChangesAsync`; commit.
- Not enough available stock for any line → 422 listing the lines; nothing is reserved (no partial
  reservation).
- Version conflict on a lot balance or the order → 409.
- The core (steps 2–5 on a tracked order, optionally with explicit lots instead of FEFO) is the shared step used
  by F1.7.

`start-preparing` (`CONFIRMED` → `PREPARING`) is optional; `mark-ready` accepts `CONFIRMED` or `PREPARING`
(decision D10).

`FefoSuggestionResponse`:

```json
{
  "orderId": "uuid",
  "items": [
    {
      "orderItemId": "uuid", "baseQuantity": 125, "remainingBaseQuantity": 125,
      "lots": [
        { "inventoryLotId": "uuid", "lotNumber": "L01", "expiryDate": "2027-01-31",
          "availableBaseQuantity": 100, "suggestedBaseQuantity": 100 },
        { "inventoryLotId": "uuid", "lotNumber": "L02", "expiryDate": "2027-03-31",
          "availableBaseQuantity": 80, "suggestedBaseQuantity": 25 }
      ],
      "shortageBaseQuantity": 0
    }
  ]
}
```

`ReservationResponse`:

```json
{
  "id": "uuid", "orderId": "uuid", "status": "ACTIVE",
  "reservedAt": "…", "reservedBy": "uuid",
  "releasedAt": null, "releasedBy": null, "releaseReason": null,
  "items": [
    { "id": "uuid", "orderItemId": "uuid", "inventoryLotId": "uuid", "lotNumber": "L01",
      "expiryDate": "2027-01-31", "reservedBaseQuantity": 100, "consumedBaseQuantity": 0,
      "releasedBaseQuantity": 0, "remainingBaseQuantity": 100 }
  ]
}
```

Reservation status is derived (§35.3), never set by a request.

---

## 7. F1.5 — Fulfillment posting and Pickup (`Operate`)

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/orders/{id}/pickup` | `PickupRequest` | `200 OrderResponse` |
| POST | `/api/orders/{id}/items/{itemId}/cancel-remaining` | `{ reason }` | `200 OrderResponse` |

`PickupRequest`:

```json
{
  "items": [
    { "orderItemId": "uuid",
      "lots": [ { "inventoryLotId": "uuid", "baseQuantity": 100 },
                { "inventoryLotId": "uuid", "baseQuantity": 25 } ] }
  ],
  "note": "string ≤1000 | null"
}
```

- Only PICKUP orders in `CONFIRMED`, `PREPARING`, `READY_FOR_FULFILLMENT` or `PARTIALLY_FULFILLED`.
- Staff send the **actual** lots handed over. A reserved lot is issued from its reservation
  (`IssueReserved`); a different lot must be eligible and available, is issued with `IssueUnreserved`, and
  the same quantity of the original reservation is released.
- Per line, the sum of lot quantities ≤ remaining base quantity; quantities must be multiples of the
  packaging conversion (whole packages only).
- Partial pickup is allowed; the order becomes `PARTIALLY_FULFILLED` until the rest is picked up or
  cancelled with cancel-remaining (which releases the remaining reservation, no stock movement, and calls
  `IOrderSettlementGuard.ReleaseAsync`; when nothing stays open the order becomes PARTIALLY_CANCELLED and
  `IOrderPaymentCancellation` runs as in §8).
- `pickupCompletedAt/By` are set when the order reaches a terminal status at the counter.

**`FulfillmentPostingService`** (shared step, reused by F2.6 and F1.7), one call inside the caller's transaction:
lock lot balances in id order → reject expired/blocked/quarantined lots → SALE stock movement + one item per lot
with cost snapshot (weighted average cost) → decrease on hand and reserved → consume reservation items →
`order.RecordFulfillment` → `IFulfillmentFinancialPosting.PostAsync` (README §4.6) → post the movement. The caller
saves once and commits.

---

## 8. F1.6 — Order cancellation (`Operate`)

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/orders/{id}/cancel` | `{ reason }` (required, ≤1000) | `200 OrderCancellationResponse` |

`OrderCancellationResponse` = `OrderResponse` + `refunds: [CancellationRefundInfo]` (what staff must hand back;
the refunds are completed with F4.5's routes).

- Allowed before anything is fulfilled (`PENDING_CONFIRMATION`, `CONFIRMED`, `PREPARING`,
  `READY_FOR_FULFILLMENT`); after a partial fulfillment use cancel-remaining (§7) instead.
- **`OrderCanceller`** (shared step; F2.3's Farmer cancel calls it after the ownership check), in one transaction:
  `order.Cancel` → release the inventory reservation → `IOrderSettlementGuard.ReleaseAsync` →
  `IOrderPaymentCancellation.ReverseForCancelledOrderAsync` (README §4.3).
- Real `IOrderPaymentCancellation`: cancels PENDING payOS links through `IPaymentGateway.CancelPaymentLinkAsync`
  (the method is added by F2.4; payOS payments cannot exist before it), reverses the unconsumed ORDER allocation
  of each PAID payment and requests one PENDING refund per payment (`Order.RequestCancellationRefund`, CASH for
  cash, BANK_TRANSFER for payOS).

---

## 9. F1.7 — Quick counter sale (new, `Operate`)

For the most frequent case: a customer at the counter pays cash and takes the goods now (decision F-D3).

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/counter-sales/preview` | `CounterSaleRequest` (`lots` ignored) | `200 CounterSalePreviewResponse` (nothing saved) |
| POST | `/api/counter-sales` | `CounterSaleRequest` (`lots` required) | `201 CounterSaleResponse` |

`CounterSaleRequest`:

```json
{
  "customerType": "REGISTERED | WALK_IN",
  "farmerProfileId": "uuid | null",
  "customerName": "string ≤150 | null",
  "customerPhone": "string ≤20 | null",
  "note": "string ≤1000 | null",
  "items": [
    { "storeProductId": "uuid", "productPackagingId": "uuid", "quantity": 5,
      "unitPrice": "number | null", "overrideReason": "string ≤500 | null",
      "lots": [ { "inventoryLotId": "uuid", "baseQuantity": 125 } ] }
  ]
}
```

`CounterSalePreviewResponse`: `{ customerGroupId, priceListId, totalAmount, items: [ { storeProductId,
productPackagingId, sku, productName, packagingName, quantity, conversionToBase, baseQuantity, suggestedUnitPrice,
unitPrice, lineTotalAmount, lots: [FEFO lots as in FefoSuggestionResponse], shortageBaseQuantity } ] }`.

`CounterSaleResponse`: `{ order: OrderResponse, payment: PaymentResponse }` — the order is `COMPLETED`.

Rules:
- Source COUNTER, settlement FULL_PAYMENT, fulfillment PICKUP, payment CASH are implied (no fields for them).
  Credit or delivery sales use the normal step endpoints.
- Customer and line rules = F1.2 (walk-in name required, price overrides with reason).
- `lots` required per line; Σ lot quantities = the line's base quantity (the whole order is handed over now);
  every lot eligible (ACTIVE, not expired, available ≥ quantity) — FEFO suggests (preview), staff confirm.
- One transaction (endpoint service): `OrderBuilder` → cash `Payment` PAID for the order total + `PaymentAllocator`
  → `OrderConfirmer` core with the given lots → `FulfillmentPostingService` → order COMPLETED → one
  `SaveChangesAsync` → commit. Any failure rolls back everything (no order, payment or stock change remains).
- The cash amount is the order total (decision C-D4: change is given physically).

---

## 10. F1.8 — Sales report (new, `Manage`)

| Method | Route | Query | Response |
|---|---|---|---|
| GET | `/api/reports/sales` | `fromDate`, `toDate` (Vietnam days, ≤ 366 days), `groupBy` = `DAY` (default) \| `PRODUCT` \| `STAFF` \| `CUSTOMER_GROUP` | `200 SalesReportResponse` |

`SalesReportResponse`:

```json
{
  "fromDate": "2026-10-01", "toDate": "2026-10-31", "groupBy": "DAY",
  "rows": [
    { "key": "2026-10-02", "label": "2026-10-02", "orderCount": 12,
      "fulfilledValue": 15250000.00, "costOfGoods": 11800000.00, "grossProfit": 3450000.00,
      "returnValue": 250000.00, "netSales": 15000000.00 }
  ],
  "totals": { "orderCount": 12, "fulfilledValue": 15250000.00, "costOfGoods": 11800000.00,
              "grossProfit": 3450000.00, "returnValue": 250000.00, "netSales": 15000000.00 }
}
```

- Revenue is recognized at fulfillment: `fulfilledValue` = Σ per SALE movement line of round2(base quantity ×
  order item unit price ÷ conversion), on the movement's posting day.
- `costOfGoods` = Σ `total_cost_snapshot` of the SALE movement items (rounded to 2 decimals); `grossProfit` =
  fulfilled − cost.
- `returnValue` = Σ `return_value` of returns completed in the period (L4 data, read only); `netSales` =
  fulfilled − returns.
- `STAFF` = order creator; `CUSTOMER_GROUP` = the order's customer group snapshot (walk-in → key `WALK_IN`).

---

## 11. Tests — must prove

| Task | Must prove |
|---|---|
| F1.1 | resolution order (group list → walk-in default → none), validity windows, one walk-in default, item upsert, catalog price, snapshot unaffected by later price edits |
| F1.2 | WALK_IN/REGISTERED/CREDIT rules, snapshots unchanged after a price change, override audit |
| F1.3 | no overpayment, allocation only on PAID and only to the payment's own order, prepayment consumed oldest first, reversal refused once consumed, DEBT_REPAYMENT goes through `IDebtRepaymentPosting` |
| F1.4 | no overselling with two concurrent confirmations on the same lot; expired/blocked lots skipped; nothing reserved on shortage; FULL_PAYMENT without payment refused once F3.3 is merged |
| F1.5 | on hand / reserved / cost after pickup, partial pickup then rest, swapped lot, cancel-remaining, rollback on failure |
| F1.6 | cancelling a paid order reverses the unconsumed part and requests one refund per payment; reservation and credit reservation released |
| F1.7 | preview saves nothing; one call ends COMPLETED with payment, reservation consumed and SALE movement; a failure in the last step (e.g. lot became BLOCKED) leaves no order, payment or stock change; the ledger sees the payment added in the same unit of work |
| F1.8 | design §XVI-A example; partial pickup counted on its posting day; return subtracted; Vietnam day boundaries |

---

## 12. Decisions that apply to L1

| # | Decision |
|---|---|
| D3 | A FULL_PAYMENT order is confirmed only when its PAID order payments cover the total. At the counter staff record the cash payment first, then confirm; online Farmers pay through payOS before staff confirm. CREDIT orders may be confirmed with any prepayment (the rest is reserved credit) |
| D4 | Admin, Store Owner and Sales may override a line price; `reason` is mandatory; suggested price, actor and reason are stored |
| D6 | FEFO order: earliest expiry first; lots without expiry after all dated lots; ties by oldest lot (first receipt) |
| D7 | Order confirmation and pickup: Admin, Store Owner, Sales |
| D9 | Numbers `OD-yyyyMMdd-NNNN` (orders); payments `PM-yyyyMMdd-NNNN` |
| D10 | `start-preparing` / `mark-ready` are optional steps |
| B-D2 | A Farmer without a group assignment belongs to the store's default group |
| B-D3 | A group without an applicable price list uses the walk-in default list; no per-product fallback (one order snapshots one price list); a missing pair = the line cannot be ordered (422) |
| C-D4 | No overpayment: an order payment ≤ order total − already paid; a debt repayment ≤ current debt balance and fully allocated. Staff record only the amount due and give change physically |
| C-D5 | Cash payments and payment queries = Operate |
| C-D9 | payOS amounts are whole VND; VND price lists and price overrides should use whole numbers |
| F-D3 | `POST /api/counter-sales` is the only composite endpoint (README §7) |
| F-D5 | Reports are Manage-only and owned by the flow whose data they read |
