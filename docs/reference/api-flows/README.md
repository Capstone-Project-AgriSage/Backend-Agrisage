# API Flows — Conventions, Ownership and Cross-Flow Interfaces

Version 2.0 — 2026-10-03. Replaces the three module contracts (`API_CONTRACT_SALES.md`,
`API_CONTRACT_CUSTOMERS_CREDIT.md`, `API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md`). Work is now split by
**flow**: each person owns one end-to-end business flow that can be demonstrated on its own.

| Flow | File | Owner | Demo in one sentence |
|---|---|---|---|
| L1 | [FLOW_1_COUNTER_SALE.md](FLOW_1_COUNTER_SALE.md) | Lead | A customer at the counter gets a price, pays cash, the order is confirmed with FEFO lots and handed over |
| L2 | [FLOW_2_ONLINE_ORDER_DELIVERY.md](FLOW_2_ONLINE_ORDER_DELIVERY.md) | Teammate 2 | A Farmer orders online, pays with payOS, staff deliver in one or several trips, failures and incidents are handled |
| L3 | [FLOW_3_CREDIT_DEBT.md](FLOW_3_CREDIT_DEBT.md) | Teammate 3 | Customer groups carry prices and a credit tier, credit sales respect the limit, debt is created on fulfillment and collected |
| L4 | [FLOW_4_INVENTORY_RETURNS.md](FLOW_4_INVENTORY_RETURNS.md) | Teammate 4 | Stock is imported, watched (expiry, low stock), counted, adjusted, reported, and returned goods come back |

Every route and DTO of the old contracts is kept unchanged (moved into one flow file); flows only **add**
endpoints and fields. Business rules come from `DATABASE_DESIGN.md`; when a flow file and the design disagree,
the design wins and the flow file is corrected.

**Changing a contract:** open a separate PR that edits the flow file (or this README) first, get it approved,
then change the code. Never change a route, field name or status code only in code.

---

## 1. General conventions

| Topic | Rule |
|---|---|
| Base path | `/api/...`, plural kebab-case nouns: `/api/orders`, `/api/deliveries` |
| State changes | `POST /{id}/<verb>` — `/confirm`, `/cancel`, `/dispatch`, `/complete` |
| Child rows | `/{id}/items`, `/{id}/items/{itemId}` |
| Own data of a Farmer | `/api/me/...`; identity comes only from the JWT, the client never sends a user/farmer id |
| Delete | `DELETE` = soft delete or semantic cancel, never physical delete |
| JSON | camelCase; ids are UUID strings; dates `yyyy-MM-dd`; timestamps ISO 8601 UTC |
| Enum values | UPPER_SNAKE strings (`PENDING_CONFIRMATION`); accepted case-insensitively in requests |
| Money | `number` with at most 2 decimals; never rounded silently (more decimals → 400) |
| Quantities | `quantity` / `plannedQuantity` = packaging units (bags, boxes); any field named `…BaseQuantity` = inventory base units |
| Lists | `page` (default 1), `pageSize` (default 20, max 100); response `PagedResult` (below) |
| Created | `201 Created` + `Location` header + full response body |
| Updated / state change | `200 OK` + full response body of the parent resource |
| Deleted | `204 No Content` |
| Server-owned values | prices, totals, base quantities, statuses, numbers, actor ids and timestamps are computed by the server; a client value for them is ignored or rejected |
| Route ids | `{id:guid}` route constraints, so fixed segments (`/import-template`, `/stock-summary`) never clash with ids |
| Reports | `GET /api/reports/...`, read-only, `AsNoTracking`, aggregated in SQL, Vietnam days through `BusinessCalendar`; each flow owns the reports of its own data |

`PagedResult<T>`:

```json
{ "items": [ ], "page": 1, "pageSize": 20, "totalCount": 57, "totalPages": 3 }
```

Errors use the existing `GlobalExceptionHandler` (RFC 7807 `application/problem+json` with `traceId`):

| Code | When |
|---|---|
| 400 | Request validation (FluentValidation); body has `errors: { field: [messages] }` |
| 401 | No/invalid token |
| 403 | Role not allowed, or the resource belongs to someone else (Farmer / assigned delivery staff checks) |
| 404 | Resource not found (or soft-deleted); also someone else's id under `/api/me/...` |
| 409 | Concurrency conflict (`version`), unique-index race, document number clash — client may retry |
| 422 | Business rule / wrong state (`BusinessRuleException`, `DomainException`); may carry `errors: { item: [messages] }` when several items fail (decision F-D8) |
| 503 | External dependency unavailable (storage, payOS) |

Role gates (`ApiRoles`):

| Name | Roles |
|---|---|
| `Manage` | ADMIN, STORE_OWNER |
| `Operate` | ADMIN, STORE_OWNER, SALES_STAFF |
| `Read` | ADMIN, STORE_OWNER, SALES_STAFF, DELIVERY_STAFF |
| Farmer | FARMER (only `/api/me/...`) |

Document numbers (shared `DocumentNumbers`, Vietnam day UTC+7, highest number of the day + 1):

| Document | Format | Flow |
|---|---|---|
| Goods receipt | `GR-yyyyMMdd-NNNN` (existing) | L4 |
| Stock movement (every type) | `SM-yyyyMMdd-NNNN` (existing) | all |
| Order | `OD-yyyyMMdd-NNNN` | L1 |
| Payment | `PM-yyyyMMdd-NNNN` | L1, L2 |
| Delivery note | `DL-yyyyMMdd-NNNN` | L2 |
| Debt entry | `DE-yyyyMMdd-NNNN` | L3 |
| Stocktake | `ST-yyyyMMdd-NNNN` | L4 |
| Sales return | `RT-yyyyMMdd-NNNN` | L4 |
| Refund | `RF-yyyyMMdd-NNNN` | L1 (requested by cancellation), L4 |

---

## 2. Shared response shapes

Used by more than one flow. The owner flow builds them; the others reuse the same DTO classes.

### OrderResponse (owner L1, used by L2)

```json
{
  "id": "uuid",
  "orderNumber": "OD-20261002-0001",
  "source": "COUNTER",
  "customerType": "REGISTERED",
  "farmerProfileId": "uuid | null",
  "customerName": "Nguyễn Văn A",
  "customerPhone": "0901234567",
  "customerGroupId": "uuid | null",
  "priceListId": "uuid | null",
  "settlementType": "FULL_PAYMENT",
  "creditTermDays": null,
  "fulfillmentType": "DELIVERY",
  "deliveryAddress": {
    "recipientName": "Nguyễn Văn A", "recipientPhone": "0901234567",
    "addressLine": "Ấp 3", "ward": "Xã X", "district": "Huyện Y", "province": "Cần Thơ",
    "latitude": null, "longitude": null
  },
  "status": "PENDING_CONFIRMATION",
  "subtotalAmount": 1250000.00,
  "totalAmount": 1250000.00,
  "note": null,
  "createdBy": "uuid", "createdAt": "2026-10-02T03:15:00Z",
  "confirmedBy": null, "confirmedAt": null,
  "pickupCompletedBy": null, "pickupCompletedAt": null,
  "completedAt": null,
  "cancelledBy": null, "cancelledAt": null, "cancelReason": null,
  "version": 3,
  "items": [ OrderItemResponse ]
}
```

`deliveryAddress` is `null` for PICKUP orders.

### OrderItemResponse

```json
{
  "id": "uuid",
  "storeProductId": "uuid",
  "productPackagingId": "uuid",
  "sku": "SKU-001",
  "productName": "Phân bón NPK 16-16-8",
  "packagingName": "Bao 25kg",
  "quantity": 5,
  "conversionToBase": 25,
  "baseQuantity": 125,
  "suggestedUnitPrice": 250000.00,
  "unitPrice": 250000.00,
  "lineTotalAmount": 1250000.00,
  "priceOverridden": false,
  "overrideReason": null,
  "overriddenBy": null,
  "fulfilledBaseQuantity": 0,
  "cancelledBaseQuantity": 0,
  "remainingBaseQuantity": 125,
  "status": "PENDING"
}
```

### OrderListItem

`id, orderNumber, source, customerType, customerName, customerPhone, settlementType, fulfillmentType,
status, totalAmount, itemCount, createdAt, confirmedAt`.

### DeliveryAddressRequest (L1 counter orders, L2 checkout/addresses/deliveries)

```json
{
  "recipientName": "string, required, ≤150",
  "recipientPhone": "string, required, ≤20 (normalized like other phones)",
  "addressLine": "string, required, ≤500",
  "ward": "string ≤150 | null",
  "district": "string ≤150 | null",
  "province": "string, required, ≤150",
  "latitude": "number(10,7) | null",
  "longitude": "number(10,7) | null"
}
```

### PaymentResponse (owner L1, used by L2 and L3)

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

`PaymentListItem`: `id, paymentNumber, paymentContext, paymentMethod, amount, status, payerName,
confirmedAt, initiatedAt`.

### RefundResponse (requested by L1 cancellation, managed by L4)

`id, refundNumber, source (SALES_RETURN | ORDER), salesReturnId, orderId, originalPaymentId, refundMethod,
amount, status, externalReference, proofFileUrl, requestedBy/At, completedBy/At, cancelledBy/At, cancelReason,
note` — the same shape for return refunds and cancelled-order refunds.

---

## 3. Who owns what

### 3.1 Folders

| Flow | Application / Api feature folders | Infrastructure |
|---|---|---|
| L1 | `Orders` (staff side, confirmation, pickup, cancellation), `Pricing`, `Payments` (cash, allocation, prepayment ledger, cancellation), `CounterSales`, `Reports/Sales*` | — |
| L2 | `Customers` (profile, addresses, staff customer views), `Carts`, `Orders` **Farmer side only** (`MeOrders*` files), `Deliveries`, `Payments/PayOs*` files, `Reports/Delivery*` | `Payments/PayOsPaymentGateway` |
| L3 | `CustomerGroups`, `Credit`, `Debt`, `Reports/Debt*` | — |
| L4 | `Inventory` (existing + alerts, adjustments, stock card), `Stocktakes`, `GoodsReceipts` (existing + import), `Suppliers` (existing), `Returns`, `Refunds`, `Reports/Inventory*` | Excel reader (F4.3) |

Shared files every flow may touch — **append only**, small PRs, rebase on `main` before opening the PR:
`Application/DependencyInjection.cs`, `Common/Interfaces/IRowLockService.cs` + `Infrastructure/Persistence/RowLockService.cs`
(one method per row type), `Common/DocumentNumbers.cs`, `Api/.../ApiRoles.cs`. Domain entities are shared:
add a method to an aggregate only for your flow's use case and say so in the PR.

### 3.2 Composable use cases (needed by F1.7 and by every cross-flow call)

- An **endpoint service** opens the transaction (`IAgriSageDbContext.BeginTransactionAsync`), calls steps,
  saves once and commits.
- A **step** (`OrderBuilder`, `PaymentAllocator`, the core of `OrderConfirmer`, `FulfillmentPostingService`, every
  interface implementation of §4) works on tracked entities inside the caller's transaction and **never** calls
  `SaveChangesAsync`, `BeginTransactionAsync` or `CommitAsync`.
- Steps hand **entities** (not ids) to the next step, so nothing created earlier in the same unit of work has to
  be re-read from the database before the single `SaveChangesAsync`. A step that must aggregate rows another step
  may have just added also reads the tracked `Added` entities (`DbSet<T>.Local`); this applies to the prepayment
  ledger (payment allocations) and is proven by the F1.7 tests.
- Two documents of the same type in one unit of work take consecutive numbers (`DocumentNumbers.NextAsync`
  once, then + 1 in memory).
- This is what lets `POST /api/counter-sales` run create → pay → confirm → pickup as one atomic operation
  with the same code as the single-step endpoints.

### 3.3 Branches, migrations, verification

- Branch per task: `feature/f1-2-counter-orders`, `feature/f4-3-receipt-import`, …
- Schema: the only migration of this round is `CustomerGroupDefaultCreditTier` (lead, task F0.2). Any other
  schema need → tell the lead first; one migration at a time across the team.
- Every task delivers unit tests (rules, status derivation), offline HTTP tests (401/403 per role, 400
  validation, Swagger lists the routes) and rolled-back real PostgreSQL tests (`[RealDbFact]`) for every
  endpoint that writes, plus the "must prove" list of its flow file.

---

## 4. Cross-flow interfaces

The first eight are **already in the code on `main`** with temporary implementations
(`Application/Common/Placeholders/Temporary*.cs`, one registration line each in `Application/DependencyInjection.cs`,
tagged with the owner task). Consumers just inject the interface. The owner task replaces its registration line
with the real implementation and deletes the `Temporary*` file. Signatures change only with a PR on this file
first. Implementations never call `SaveChangesAsync` or open a transaction (§3.2).

| Interface | Folder | Owner task | Used by | Temporary behaviour until then |
|---|---|---|---|---|
| `IPriceResolver` | `Features/Pricing` | F1.1 | F1.2, F1.7, F2.2, F2.3 | **real implementation done** (`PriceResolver`) |
| `IOrderPrepaymentLedger` | `Features/Payments` | F1.3 | F3.3, F3.4 | **real implementation done** (`OrderPrepaymentLedger`) |
| `IOrderPaymentCancellation` | `Features/Payments` | F1.6 | F1.5 (cancel-remaining), F1.6, F2.3 (Farmer cancel) | returns an empty list |
| `IOrderSettlementGuard` | `Features/Credit` | F3.3 | F1.4 (confirm), F1.6 (cancel), F1.7 | accepts FULL_PAYMENT without the payment check, refuses CREDIT (422) |
| `ICreditReservationAdjuster` | `Features/Credit` | F3.3 | F1.3 (`PaymentAllocator`), F2.4 | does nothing |
| `IFulfillmentFinancialPosting` | `Features/Debt` | F3.4 | F1.5 (`FulfillmentPostingService`, also used by F2.6) | nothing for FULL_PAYMENT, throws for CREDIT |
| `IDebtRepaymentPosting` | `Features/Debt` | F3.5 | F1.3 (`PaymentAllocator`), F2.4 | refuses DEBT_REPAYMENT (422 "debt is not available yet") |
| `IDebtReturnPosting` | `Features/Debt` | F3.5 | F4.4 | applies 0 (everything is refunded) |
| `IPaymentGateway` (changes in FLOW_2 §6) | `Common/Interfaces` | F2.4 | F2.4, F1.6 (cancel pending payOS links) | not implemented yet |

### 4.1 `IPriceResolver` — owner F1.1

```csharp
public interface IPriceResolver
{
    // Price list that applies to the customer at the given moment: the Farmer's customer-group list, or the walk-in
    // default list when farmerProfileId is null or the group has none (decision B-D3). No list → BusinessRuleException.
    Task<PriceContext> GetContextAsync(Guid? farmerProfileId, DateTimeOffset at, CancellationToken cancellationToken);

    // Selling price per (store product, packaging) in that list; a missing pair = no price (the line cannot be ordered).
    Task<IReadOnlyDictionary<PriceLine, decimal>> GetPricesAsync(
        Guid priceListId, IReadOnlyCollection<PriceLine> lines, CancellationToken cancellationToken);
}

public sealed record PriceContext(Guid? CustomerGroupId, Guid PriceListId);
public readonly record struct PriceLine(Guid StoreProductId, Guid ProductPackagingId);
```

### 4.2 `IOrderPrepaymentLedger` — owner F1.3

```csharp
public interface IOrderPrepaymentLedger
{
    Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken);   // Σ active ORDER allocations of PAID payments
    Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken);    // Σ (allocated − prepayment consumed)
    Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken); // oldest first (rule 25), returns consumed
}
```

### 4.3 `IOrderPaymentCancellation` — owner F1.6

```csharp
public interface IOrderPaymentCancellation
{
    // Called right after the order became CANCELLED, or PARTIALLY_CANCELLED with nothing left open (decision C-D2,
    // design §35.18):
    // 1. cancel the order's PENDING payOS payments (payOS cancel API first, through IPaymentGateway);
    // 2. per PAID payment of the order: reverse its unconsumed ORDER allocation (a consumed allocation keeps its
    //    consumed part; design §35.7) and request one PENDING cancelled-order refund for that amount through
    //    Order.RequestCancellationRefund — method CASH for a cash payment, BANK_TRANSFER for payOS.
    // Returns the requested refunds so staff know what to hand back.
    Task<IReadOnlyList<CancellationRefundInfo>> ReverseForCancelledOrderAsync(
        Order order, Guid actorId, string reason, CancellationToken cancellationToken);
}

public sealed record CancellationRefundInfo(
    Guid RefundId, string RefundNumber, Guid PaymentId, RefundMethod RefundMethod, decimal Amount);
```

### 4.4 `IOrderSettlementGuard` — owner F3.3

```csharp
public interface IOrderSettlementGuard
{
    // Inside the confirmation transaction, before any stock is reserved.
    // FULL_PAYMENT: PAID order payments (IOrderPrepaymentLedger) must cover the order total (decision D3).
    // CREDIT: ACTIVE credit profile and debt account, required credit = total − paid ≤ available credit,
    //   credit reservation created; returns the credit term (days) of the profile's tier (decision B-D4).
    // Throws BusinessRuleException when the order cannot be confirmed.
    Task<SettlementResult> EnsureCanConfirmAsync(Order order, Guid actorId, CancellationToken cancellationToken);

    // The order (or its remainder) is cancelled: release the unused credit reservation.
    Task ReleaseAsync(Order order, Guid actorId, string? reason, CancellationToken cancellationToken);
}

public sealed record SettlementResult(int? CreditTermDays);   // set for CREDIT orders only
```

### 4.5 `ICreditReservationAdjuster` — owner F3.3

```csharp
public interface ICreditReservationAdjuster
{
    // A payment for an already confirmed CREDIT order became PAID: release the same amount of the order's unused
    // credit reservation (design §XVI-D). Runs inside the payment transaction.
    Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken);
}
```

### 4.6 `IFulfillmentFinancialPosting` — owner F3.4

```csharp
public interface IFulfillmentFinancialPosting
{
    // Called by FulfillmentPostingService inside the fulfillment transaction, after stock is posted.
    // Applies available order prepayment (oldest first), consumes the credit reservation for the unpaid part and
    // creates the Debt Entry + CREDIT_SALE Debt Transaction when unpaid > 0
    // (due date = fulfillment Vietnam day + order.CreditTermDaysSnapshot).
    Task PostAsync(FulfillmentPostingContext context, CancellationToken cancellationToken);
}

public enum FulfillmentSource { Pickup, Delivery }

public sealed record FulfillmentPostingContext(
    Order Order,
    IReadOnlyList<FulfilledLine> Lines,
    FulfillmentSource Source,
    Guid? DeliveryId,             // DELIVERY only (debt_entries.delivery_id)
    Guid? DeliveryAttemptId,      // DELIVERY only
    Guid StockMovementId,
    Guid ActorId,
    DateTimeOffset FulfilledAt);

// FulfilledValue = round2(fulfilled base quantity × unit price ÷ conversion), per line, computed by the caller.
public sealed record FulfilledLine(Guid OrderItemId, long FulfilledBaseQuantity, decimal FulfilledValue);
```

### 4.7 `IDebtRepaymentPosting` — owner F3.5

```csharp
public interface IDebtRepaymentPosting
{
    // A DEBT_REPAYMENT payment became PAID (cash confirmed by staff or payOS webhook/sync). Creates the DEBT payment
    // allocations (explicit list, or oldest due date first when null) and one PAYMENT debt transaction per allocation.
    Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(
        Payment payment, IReadOnlyList<RequestedDebtAllocation>? requested, Guid? actorId,
        CancellationToken cancellationToken);
}

public sealed record RequestedDebtAllocation(Guid DebtEntryId, decimal Amount);
public sealed record DebtAllocationResult(Guid DebtEntryId, Guid PaymentAllocationId, decimal Amount);
```

### 4.8 `IDebtReturnPosting` — owner F3.5

```csharp
public interface IDebtReturnPosting
{
    // Reduces the unpaid debt attributable to the returned goods first (rule 32): open entries of the same order,
    // the entry of the same fulfillment source first, then oldest due date. Posts RETURN debt transactions linked
    // to the sales return. Returns the amount applied; the caller refunds the rest.
    Task<decimal> ApplyReturnAsync(Guid orderId, Guid salesReturnId, decimal returnValue, Guid actorId,
        Guid? sourceStockMovementId, CancellationToken cancellationToken);
}
```

### 4.9 Shared steps (classes, not interfaces)

| Step | Owner | Reused by | Contract |
|---|---|---|---|
| `AuditTrail` (`Application/Common`, done) | F1.1 | every flow that must write `audit_logs` (price lists, overrides, store-managed accounts, credit limits, debt actions…) | `Record(action, entityType, entityId, storeId, oldValues?, newValues?, reason?)` adds one row (camelCase JSON, actor = current user) to the caller's unit of work and never saves; never pass secrets |
| `OrderBuilder` (`Application/Features/Orders`, done) | F1.2 | F2.3 (checkout), F1.7 | `BuildAsync(OrderDraft)` builds a PENDING_CONFIRMATION order with price and customer snapshots from a customer + lines; validates sellable products/sale packagings and price overrides |
| `PaymentAllocator` (`Application/Features/Payments`, done) | F1.3 | F2.4 (webhook/sync), F1.7 | `AllocateAsync(payment, order?, debtAllocations?, actorId)`; a payment just became PAID: ORDER → one ORDER allocation to `payments.order_id` + `ICreditReservationAdjuster` for a confirmed CREDIT order; DEBT → `IDebtRepaymentPosting` |
| `OrderConfirmer` core (`Application/Features/Orders`, done) | F1.4 | F1.7 | settlement guard → FEFO/explicit lots → `InventoryLot.Reserve` → reservation → `order.Confirm` |
| `FulfillmentPostingService` (`Application/Features/Orders`, done) | F1.5 | F2.6, F1.7 | `PostAsync(order, lines, source, deliveryId?, attemptId?, actorId, at)`; FLOW_1 §7 |
| `OrderCanceller` | F1.6 | F2.3 (Farmer cancel, after the ownership check) | FLOW_1 §8: cancel + reservation release + `IOrderSettlementGuard.ReleaseAsync` + `IOrderPaymentCancellation` |

Owners publish these first (milestones M1/M2 of §6) so the other flows build on real code.

---

## 5. Old task → new task

| Old | New | Old | New |
|---|---|---|---|
| A1 cart | F2.2 | B4 credit API | F3.2 |
| A2 staff orders | F1.2 | B4 guard + adjuster | F3.3 |
| A2 Farmer checkout | F2.3 | B5 ledger, actions, fulfillment posting | F3.4 |
| A2 cancel | F1.6 | B5 repayment + return posting | F3.5 |
| A3 confirm / FEFO | F1.4 | C1 cash payments + ledger | F1.3 |
| A4 pickup + posting | F1.5 | C1 order payment cancellation | F1.6 |
| A5 delivery notes | F2.5 | C1 cancelled-order refund routes | F4.5 |
| A6 attempts / incidents | F2.6 | C2 stocktake / adjustments | F4.2 |
| B1 profile / addresses / customers | F2.1 | C3 payOS | F2.4 |
| B2 customer groups | F3.1 | C4 returns | F4.4 |
| B3 price lists | F1.1 | C5 refunds | F4.5 |
| B3 group ↔ price list | F3.1 | — | — |

New tasks: F0.1 (these documents), F0.2 (migration `CustomerGroupDefaultCreditTier`, lead), F1.7, F1.8, F2.7, F3.6,
F4.1, F4.3, F4.6.

---

## 6. Order of work and milestones

| Phase | L1 (Lead) | L2 | L3 | L4 |
|---|---|---|---|---|
| 0 | F0.1 docs → F0.2 migration | — | — | — |
| 1 | F1.1 → F1.2 → F1.3 | F2.1 → F2.2 → F2.4 | F3.1 → F3.2 | F4.1 → F4.2 (F4.3 done by the lead) |
| **M1** | F1.1–F1.3 merged | F2.3 can start | F3.3 can start | — |
| 2 | F1.4 → F1.5 → F1.6 | F2.3 → F2.5 | F3.3 → F3.4 | F4.4 → F4.5 |
| **M2** | F1.4–F1.5 merged | F2.6 can start | end-to-end credit sale | end-to-end return |
| 3 | F1.7 → F1.8 | F2.6 → F2.7 | F3.5 → F3.6 | F4.6 |

Before a milestone, tests of a dependent flow build their data through Domain entities or fakes, never by
waiting for the other flow.

---

## 7. Decisions of the flow split (2026-10-03)

| # | Decision |
|---|---|
| F-D1 | Work is split by flow (L1–L4, §5 mapping). Routes and DTOs of the old contracts are kept unchanged; decisions D1–D10, B-D1–B-D6 and C-D1–C-D9 keep their ids and stay valid |
| F-D2 | Debt term depends on the customer type: a customer group has a default credit tier (`customer_groups.default_credit_tier_id`, migration `CustomerGroupDefaultCreditTier`, design §35.20). Credit profile tier = request → current group's tier → default group's tier → 422; a group change moves the profile to the new group's tier (limit unchanged, history written). No crop seasons |
| F-D3 | `POST /api/counter-sales` (F1.7) is the only composite endpoint: PICKUP + FULL_PAYMENT + CASH in one transaction; staff must send the lots (FEFO suggests, staff confirm) |
| F-D4 | Every refund endpoint (return refunds and cancelled-order refunds) belongs to L4 (F4.5); L1 only requests cancelled-order refunds through `IOrderPaymentCancellation` |
| F-D5 | Reports are `GET /api/reports/...` with role Manage, owned by the flow whose data they read |
| F-D6 | Lots are marked EXPIRED by `POST /api/inventory/lots/expire-due` (Manage); every reservation/sale check still uses the expiry date itself, so an unmarked expired lot is never sold |
| F-D7 | Excel import (F4.3) uses ClosedXML 0.105.1 (MIT, approved 2026-10-03) only inside `Infrastructure/Spreadsheets`; the lead implemented F4.3 so the import is available now |
| F-D8 | A 422 may carry `errors` (`BusinessRuleException(message, errors)`) when several items fail, e.g. the rows of an Excel import or the short lines of a confirmation |
