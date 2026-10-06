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

## 4. F1.2 — Staff counter orders (`Operate`) — done

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
- WALK_IN: `farmerProfileId` must be null, `customerName` is optional (blank → "Khách lẻ"), `customerPhone` optional
  (normalized, 422 when not a Vietnamese mobile number), `settlementType` must be FULL_PAYMENT (CREDIT → 422); price list
  = the walk-in default price list.
- CREDIT: REGISTERED only; the ACTIVE credit profile is checked at confirmation (F1.4), not here.
- DELIVERY needs exactly one of `addressId` (one of the Farmer's addresses, copied as a snapshot) or
  `deliveryAddress` (400 when both or none; a walk-in order or another user's address → 422). PICKUP must carry neither
  (400). The typed recipient phone is normalized.
- Snapshot at creation: customer group, price list, customer name and phone, address, and per line SKU, name,
  packaging name, conversion, suggested price.
- Only sellable store products of ACTIVE products and ACTIVE **sale** packagings (`isSaleUnit`), else 422; a line
  without a price in the resolved list → 422.
- Price override (decision D4): `unitPrice` different from the suggested price requires `overrideReason`; the
  server stores the suggested price, actor and reason. `unitPrice` without a different value is ignored.
- Items, header and prices change only while `PENDING_CONFIRMATION` (→ 422 otherwise).
- The same (store product, packaging) pair may appear once: twice in the create request → 400; `POST .../items` for a pair
  already on the order → 422 (change that line's quantity). Quantity 1..100 000 000, at most 100 lines per order.
- A line added later is priced by the order's own price list (the snapshot), not by whatever list applies today.
- `PUT .../items/{itemId}/price` always needs `reason`; a `unitPrice` equal to the suggested price restores it (no
  override). `PUT /api/orders/{id}`: `note` null = unchanged, blank = cleared; `addressId`/`deliveryAddress` only on a
  DELIVERY order (422 on PICKUP), at most one of them (400).
- Audit (`audit_logs`, entity `ORDER`): `PRICE_OVERRIDE` (suggested vs new price, reason), `PRICE_OVERRIDE_REMOVED`,
  `ORDER_UPDATED`, `ORDER_ITEM_QUANTITY_CHANGED`, `ORDER_ITEM_REMOVED`.
- List: `fromDate`/`toDate` are Vietnam calendar days on `createdAt`; `search` matches order number, customer name or
  phone; unknown enum text → 400.

**`OrderBuilder`** (shared step, README §4.9, `Application/Features/Orders`): `BuildAsync(OrderDraft)` — input = customer (Farmer id or walk-in name/phone), source,
settlement, fulfillment, address snapshot, lines (+ optional overrides, staff only); output = a tracked
`PENDING_CONFIRMATION` `Order` with its number and snapshots, **not yet added to the context** (the caller adds it, then
calls `RecordPriceOverrides(order)` so the audit rows join the same save). `OrderDraft.AllowPriceOverride` is true only
for staff. Reusable pieces: `ResolveLinesAsync` (catalog checks + prices of a given list, `errors` per line as
`items[i]`), `ResolveDeliveryAddressAsync`, `OrderBuilder.AddLine`. `OrderQueries.GetAsync` / `ToResponse` build the
shared `OrderResponse`. Used by `POST /api/orders`, F2.3 checkout (source FARMER_WEB / FARMER_MOBILE, no overrides)
and F1.7.

---

## 5. F1.3 — Cash payments and payment queries — done

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
- payOS payments (F2.4) appear in the same lists and summaries. `paymentMethod` text is `CASH` / `PAYOS` and
  `confirmationSource` `STAFF` / `PAYOS_WEBHOOK` (`PaymentText`, not `EnumText`, which would split `PAY_OS`).
- `POST /api/payments/{id}/cancel`: only PENDING (a PAID or already closed payment → 422); a payOS payment → 422 "cancel
  through payOS" until F2.4 replaces it. The row is locked first (`IRowLockService.LockPaymentAsync`); audit
  `PAYMENT_CANCELLED` keeps the reason. Receiving cash audits `PAYMENT_RECEIVED`. The order is locked
  (`LockOrderAsync`) before the remaining amount is checked.
- `/api/me/...`: the Farmer is the signed-in user's profile; a user without one → 403; another Farmer's payment or order →
  404. Payer name = the paying Farmer, else the order's customer (walk-in).
- `PaymentQueries` (`FindFarmerProfileIdAsync`, `GetAsync`, `ListAsync`, `GetOrderSummaryAsync`) and the shared
  `RefundResponse` (`Application/Features/Returns/RefundResponse.cs`, built by `RefundResponse.From`) are reused by L2/L3/L4.

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
- It reads the order's payments from the database **and** the ones still unsaved in the same unit of work
  (`DbSet.Local`), with status and allocations taken from the tracked objects, so F1.7 sees the payment it just added.

**`PaymentAllocator`** (shared step, README §4.9, `Application/Features/Payments`): `AllocateAsync(payment, order?, debtAllocations?,
actorId)` for a payment that just became PAID. ORDER_PAYMENT → one ORDER allocation of the whole payment to its order (the
domain refuses another order), plus `ICreditReservationAdjuster` when the order is a CONFIRMED / PREPARING / READY /
PARTIALLY_FULFILLED CREDIT order. DEBT_REPAYMENT → `IDebtRepaymentPosting.ApplyAsync`, and any amount left unallocated → 422.
A payment that is not PAID → 422.
- All three also count allocations added earlier in the same unit of work (README §3.2).

**`PaymentAllocator`** (shared step, README §4.9): `AllocateAsync(Payment paidPayment, IReadOnlyList<RequestedDebtAllocation>? requested, Guid? actorId, ct)`;
used by the cash endpoint, the payOS webhook and sync (F2.4) and F1.7.

---

## 6. F1.4 — Confirmation and stock reservation (`Operate`) — done

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/orders/{id}/fefo-suggestions` | — | `200 FefoSuggestionResponse` |
| POST | `/api/orders/{id}/confirm` | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/start-preparing` | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/mark-ready` | — | `200 OrderResponse` |
| GET | `/api/orders/{id}/reservation` | — | `200 ReservationResponse` |

`confirm` — one transaction owned by `OrderConfirmer`:
1. Lock the order; must be `PENDING_CONFIRMATION` with ≥1 line. Every line is checked **again**: the store product
   still sellable and active, the product ACTIVE, the packaging ACTIVE and a sale unit (the same rule as when the order
   was built, `OrderBuilder.GetSellabilityProblem`). A line that was switched off meanwhile → 422 with `errors` per line,
   nothing reserved; staff remove the line or cancel the order. The price stays the snapshot taken at creation. Once
   confirmed an order is a commitment and is not checked again (pickup and delivery go ahead).
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
- Built as: `FefoAllocator` (pure, `Application/Features/Orders`), `OrderConfirmer.ConfirmCoreAsync(order, chosenLots?)`
  (returns the new `InventoryReservation`, never saves; chosen lots = `LotPick(orderItemId, lotId, baseQuantity)`, each line's
  picks must add up to its base quantity, lots must be of the line's product and sellable), `OrderConfirmationService`
  (endpoint transaction), `IRowLockService.LockOrderAsync` then `LockLotBalancesAsync` (ids sorted, `FOR UPDATE`).
  Lot candidates are read once to get ids, locked, then read again so the balances are the committed ones.
- Order of checks: status/lines → no open reservation → settlement guard → lots → FEFO → reserve. A refusal at any step
  saves nothing. The `errors` of a shortage are keyed `items[i]` (index in the order's response order) with the SKU and
  the missing base units. Audit: `ORDER_CONFIRMED` (reserved lots), `ORDER_PREPARING_STARTED`, `ORDER_MARKED_READY`.
- `fefo-suggestions`: PENDING → FEFO over sellable lots now (`availableBaseQuantity` = the lot's available stock before
  this order, only lots with a suggested quantity are listed, `shortageBaseQuantity` per line); CONFIRMED / PREPARING /
  READY / PARTIALLY_FULFILLED → the open lines of the reservation (available = suggested = remaining reserved); CANCELLED /
  PARTIALLY_CANCELLED / COMPLETED → 422. `reservation` returns the order's latest reservation (404 when none).
- The payment check of decision D3 is the real settlement guard's (F3.3); L1 does not duplicate it. Until then
  `TemporaryOrderSettlementGuard` lets FULL_PAYMENT orders through and refuses CREDIT (422).

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

## 7. F1.5 — Fulfillment posting and Pickup (`Operate`) — done

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
- Per line, the sum of lot quantities ≤ remaining base quantity and a whole number of packages (a multiple of the
  packaging conversion); a single lot need not hold whole packages.
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

Built as `FulfillmentPostingService.PostAsync(order, lines, source, deliveryId?, attemptId?, actorId, at)` in
`Application/Features/Orders` (`FulfillmentLine(orderItemId, lotId, baseQuantity)`; returns the movement and the
`FulfilledLine`s). Rules settled while building:
- All checks run before anything changes: status, lines of the same (item, lot) are added together, per item the sum ≤
  remaining base quantity and the **sum over the lots is a whole number of packages** (not each lot: stock is in base units
  and FEFO splits a line between lots at any base quantity), the order has an open reservation, every
  lot exists, belongs to the item's product and `IsEligibleForSale(today)`. Problems come back as one 422 with `errors`
  keyed `items[i]` (index in the order's response order).
- A lot that holds the item's reservation is issued with `IssueReserved` and the reservation item is consumed. For
  another lot the reservation **moves**: the quantity is reserved there (`InventoryLot.Reserve`, so it must be free —
  stock reserved for other orders cannot be taken), the same quantity of the item's reservation is released in other lots
  (lots not picked in this call first), and the new line is consumed (`InventoryReservation.ReserveMore` adds the line or
  grows the one of that lot; there is one line per item and lot). So the reservation ends CONSUMED (database design §35.3:
  RELEASED means nothing was ever consumed) and the released lines show where the stock was held before. Picks that hold their own reservation
  are processed first so one pick never releases what another still needs. A lot used both ways gets two movement items.
- `FulfilledValue = round2(base quantity × unit price ÷ conversion)` per order item, summed over its lots.
- When the order ends (COMPLETED, CANCELLED, PARTIALLY_CANCELLED) any reservation still open is released.
- When a handover (pickup or delivery) hands over the last open line and so ends the order as PARTIALLY_CANCELLED (another
  line had been cancelled before), `FulfillmentPostingService` calls `IOrderPaymentCancellation` too, after the
  fulfillment posting: the prepayment of the cancelled part is given back (`paid − max(consumed, fulfilled value)`) exactly
  as when the last remainder is cancelled.
- The note of a pickup is kept in the `ORDER_PICKED_UP` audit row (`reason`). Cancel-remaining audits
  `ORDER_ITEM_REMAINING_CANCELLED` with the reason (also stored as `cancelReason` on the order when this ends it as CANCELLED or PARTIALLY_CANCELLED), releases the reservation of that line (no stock movement), and when the
  order ends calls `IOrderSettlementGuard.ReleaseAsync` and, for CANCELLED / PARTIALLY_CANCELLED,
  `IOrderPaymentCancellation` (empty until F1.6). Both routes lock the order first.

---

## 8. F1.6 — Order cancellation (`Operate`) — done

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/orders/{id}/cancel` | `{ reason }` (required, ≤1000) | `200 OrderCancellationResponse` |

`OrderCancellationResponse` = `{ order: OrderResponse, refunds: [ { refundId, refundNumber, paymentId, refundMethod,
amount } ] }` (what staff must hand back; the refunds are completed with F4.5's routes).

- Allowed before anything is fulfilled (`PENDING_CONFIRMATION`, `CONFIRMED`, `PREPARING`,
  `READY_FOR_FULFILLMENT`); after a partial fulfillment use cancel-remaining (§7) instead.
- **`OrderCanceller`** (shared step; F2.3's Farmer cancel calls it after the ownership check), in one transaction:
  `order.Cancel` → release the inventory reservation → `IOrderSettlementGuard.ReleaseAsync` →
  `IOrderPaymentCancellation.ReverseForCancelledOrderAsync` (README §4.3).
- Real `IOrderPaymentCancellation` (`OrderPaymentCancellation`, `Application/Features/Payments`): cancels PENDING cash
  payments; a PENDING payOS payment → 422 "cancel the online payment first" until F2.4 replaces that refusal with
  `IPaymentGateway.CancelPaymentLinkAsync` in this same file (the method is added by F2.4; payOS payments cannot exist
  before it). Every PAID payment gives back its unconsumed ORDER prepayment (`Payment.ReleaseUnconsumedPrepayment`,
  design §35.21) and one PENDING refund per payment is requested (`Order.RequestCancellationRefund`, CASH for cash,
  BANK_TRANSFER for payOS, `RF-yyyyMMdd-NNNN` consecutive in the same save). Payments are locked in id order first.
- **What is refundable** = paid − max(prepayment consumed, value of what was fulfilled), never below 0, where the fulfilled
  value is Σ round2(fulfilled base quantity × unit price ÷ conversion). The fulfilled value is counted as well because the
  consumption of prepayment is posted with the debt (F3.4); once that exists the two agree. Newest payments give back
  first, so the oldest keep covering what was delivered; an allocation is reversed when everything in it goes back,
  otherwise it shrinks to what stays (it keeps `ACTIVE`; the money given back becomes the payment's unallocated amount).
  A repeated call finds nothing more to give back (idempotent).
- `OrderCanceller.CancelAsync(order, actorId, at, reason)` (shared step, never saves; the caller locks the order and
  loads its items) returns the requested refunds. The endpoint audits `ORDER_CANCELLED` (old/new status, refunds, reason);
  payments audit `PAYMENT_CANCELLED` and `ORDER_PREPAYMENT_RELEASED`. A PARTIALLY_FULFILLED order → 422 (use cancel-remaining,
  which now gives back the undelivered prepayment through the same code); a reservation is cancelled and its stock freed.

---

## 9. F1.7 — Quick counter sale (new, `Operate`) — done

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
- Built as `CounterSaleService` (`Application/Features/Orders`) over the shared steps: `OrderBuilder`, `CashPayments.CreatePaidAsync`
  (new shared step; the cash route uses it too) + `PaymentAllocator`, `OrderConfirmer.ConfirmCoreAsync` with the lots as `LotPick`s,
  `FulfillmentPostingService`, `FefoProposals.ProposeAsync` (new shared read; the pending order's suggestions use it too). Line i of the
  request is order item i. Nothing is saved before the final `SaveChangesAsync`, so the steps find what an earlier step just added
  through the tracked entities: the ledger reads the unsaved payment (`DbSet.Local`), and `FulfillmentPostingService` finds the
  unsaved reservation there. A total of 0 → 422 (a payment must be above 0).
- Errors: missing `lots` on a line → 400 `items[i].lots`; lots that do not add up to the line, an unsellable lot, another
  product's lot or not enough stock → 422 with `errors` per line (`items[i]`), nothing saved. The same lot named twice for a line
  counts once as the sum. Audit: `PAYMENT_RECEIVED`, `ORDER_CONFIRMED`, `PRICE_OVERRIDE` (if any) and `COUNTER_SALE_COMPLETED`.
- The preview builds and prices the order without adding it to the context and proposes FEFO lots per line, each against the free
  stock (lines of one product share it); it saves and reserves nothing.

---

## 10. F1.8 — Sales report (new, `Manage`) — done

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

- Revenue is recognized at fulfillment, from the POSTED SALE stock movements whose posting time falls in the period
  (Vietnam days, both included). A stock movement item names no order line, so the value is computed per (movement, order,
  product): `fulfilledValue` = round2(base quantity × Σ line totals ÷ Σ base quantities of the order's lines of that product)
  — the line's own price when the order sells the product in one packaging, the weighted average per base unit when it sells
  several (design §35.22). A fully handed-over order adds up to its total. Overridden prices count (the line total is used).
- `costOfGoods` = Σ `total_cost_snapshot` of the SALE movement items of that line, rounded to 2 decimals; `grossProfit` =
  fulfilled − cost (before returns).
- `returnValue` = Σ `return_value` of the lines of returns **completed** in the period (`SalesReturn.CompletedAt`; L4's data,
  read only; 0 until L4 completes returns), attributed through the line's lot (product) and the return's order (staff, group);
  `netSales` = fulfilled − returns.
- Lines are rounded first and then added, so every grouping has the same totals and the rows add up to them.
  `totals.orderCount` counts each order once even when it appears in several rows (an order handed over on two days is in two
  DAY rows); a row's `orderCount` counts the orders with a sale line in it (0 for a row that has only returns).
- Keys and labels: DAY → `yyyy-MM-dd` (ascending); PRODUCT → store product id, label `name (SKU)`; STAFF → creator's user id,
  label full name; CUSTOMER_GROUP → group id with its name, `WALK_IN` ("Khách lẻ") for walk-in orders, `UNGROUPED`
  ("Chưa phân nhóm") for a registered customer without a group. Non-DAY groupings are ordered by fulfilled value, largest first.
- `fromDate` and `toDate` are required (400 otherwise), `toDate` ≥ `fromDate`, at most 366 days; unknown `groupBy` → 400.
  Reversed or cancelled movements are not counted.

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
