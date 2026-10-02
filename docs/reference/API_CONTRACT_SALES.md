# API Contract — Sales, Order Fulfillment and Delivery (Group A)

Version 1.3 — 2026-10-02 (decisions D1–D10 settled; §9.4 returns cancelled-order refunds). Scope: tasks A1–A6 (cart, orders, confirmation and stock reservation, pickup,
delivery notes, delivery attempts and incidents) and the cross-module interfaces that group A uses from
groups B (customers, pricing, credit, debt) and C (payments).

This file is the shared reference for routes, request/response bodies, roles and error codes. Business
rules come from `DATABASE_DESIGN.md` (§30–35, §38–43, §7.3–7.4, §XVI–XVIII, §35.3–35.5); when this file and
the design disagree, the design wins and this file must be corrected.

**Changing this contract:** open a separate PR that edits this file first, get it approved, then change the
code. Never change a route, field name or status code only in code.

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
| 404 | Resource not found (or soft-deleted) |
| 409 | Concurrency conflict (`version`), unique-index race, document number clash — client may retry |
| 422 | Business rule / wrong state (`BusinessRuleException`, `DomainException`) |
| 503 | External dependency unavailable (storage) |

Role gates (`ApiRoles`):

| Name | Roles |
|---|---|
| `Manage` | ADMIN, STORE_OWNER |
| `Operate` | ADMIN, STORE_OWNER, SALES_STAFF |
| `Read` | ADMIN, STORE_OWNER, SALES_STAFF, DELIVERY_STAFF |
| Farmer | FARMER (only `/api/me/...`) |

Document numbers (shared `DocumentNumbers`, Vietnam day UTC+7, highest number of the day + 1):

| Document | Format |
|---|---|
| Order | `OD-yyyyMMdd-NNNN` |
| Delivery note | `DL-yyyyMMdd-NNNN` |
| Stock movement (SALE) | `SM-yyyyMMdd-NNNN` (existing) |

---

## 2. Shared response shapes

### OrderResponse

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

### DeliveryAddressRequest

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

---

## 3. A1 — Farmer cart (`FARMER`)

The cart never stores prices: every response recalculates them through `IPriceResolver` (§9).
One ACTIVE cart per Farmer per store; it is created on first use. Walk-in customers have no cart.

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/me/cart` | — | `200 CartResponse` (empty cart if none) |
| POST | `/api/me/cart/items` | `{ storeProductId, productPackagingId, quantity }` | `200 CartResponse` |
| PUT | `/api/me/cart/items/{itemId}` | `{ quantity }` | `200 CartResponse` |
| DELETE | `/api/me/cart/items/{itemId}` | — | `200 CartResponse` |
| DELETE | `/api/me/cart` | — | `204` (cart → ABANDONED) |

- POST with a product+packaging already in the cart **adds** to its quantity (one line per pair).
- `quantity` > 0, integer packaging units.
- Only sellable store products of ACTIVE products and ACTIVE **sale** packagings (`isSaleUnit`), else 422.

`CartResponse`:

```json
{
  "id": "uuid | null",
  "items": [
    {
      "id": "uuid", "storeProductId": "uuid", "productPackagingId": "uuid",
      "sku": "SKU-001", "productName": "…", "packagingName": "Bao 25kg", "imageUrl": "… | null",
      "quantity": 2,
      "unitPrice": 250000.00, "lineTotalAmount": 500000.00,
      "isAvailable": true, "unavailableReason": null
    }
  ],
  "subtotalAmount": 500000.00,
  "priceListId": "uuid | null"
}
```

`isAvailable = false` (with a reason such as `NOT_SELLABLE`, `NO_PRICE`) when the line can no longer be
ordered; checkout then fails with 422 until the line is removed. Available stock is **not** promised by
the cart (it is only reserved at confirmation, A3).

---

## 4. A2 — Orders

### 4.1 Farmer (`FARMER`)

| Method | Route | Body | Response |
|---|---|---|---|
| POST | `/api/me/orders` | `CheckoutRequest` | `201 OrderResponse` |
| GET | `/api/me/orders` | query: `status`, `fromDate`, `toDate`, `page`, `pageSize` | `200 PagedResult<OrderListItem>` |
| GET | `/api/me/orders/{id}` | — | `200 OrderResponse` |
| POST | `/api/me/orders/{id}/cancel` | `{ reason? }` | `200 OrderResponse` |

`CheckoutRequest`:

```json
{
  "source": "FARMER_WEB | FARMER_MOBILE",
  "settlementType": "FULL_PAYMENT | CREDIT",
  "fulfillmentType": "PICKUP | DELIVERY",
  "addressId": "uuid | null",
  "deliveryAddress": "DeliveryAddressRequest | null",
  "note": "string ≤1000 | null"
}
```

- Lines come from the ACTIVE cart; empty cart → 422. The cart becomes CONVERTED with `convertedOrderId`.
- DELIVERY needs exactly one of `addressId` (one of the Farmer's own addresses, copied as a snapshot) or
  `deliveryAddress`. `addressId` works once the address API of B1 exists (see §11).
- Snapshot at creation: customer group, price list, customer name and phone, address, and per line SKU,
  name, packaging name, conversion, suggested price (`unitPrice` = suggested; Farmers cannot override).
- A Farmer only sees and cancels their own orders (someone else's id → 404, not 403, to avoid leaking ids).
- Cancel only while `PENDING_CONFIRMATION`, otherwise 422.

### 4.2 Staff (`Operate`)

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
| POST | `/api/orders/{id}/cancel` | `{ reason }` (required, ≤1000) | `200 OrderResponse` |

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
- REGISTERED: `farmerProfileId` required; name/phone are taken from the Farmer (client values ignored).
- WALK_IN: `farmerProfileId` must be null, `customerName` required, `settlementType` must be FULL_PAYMENT
  (CREDIT → 422); price list = the walk-in default price list.
- CREDIT: REGISTERED only; the ACTIVE credit profile is checked at confirmation (A3), not here.
- Price override: `unitPrice` different from the suggested price requires `overrideReason`; the server
  stores the suggested price, actor and reason. `unitPrice` without a different value is ignored.
- Items, header and prices change only while `PENDING_CONFIRMATION` (→ 422 otherwise).
- Cancel: allowed before anything is fulfilled (`PENDING_CONFIRMATION`, `CONFIRMED`, `PREPARING`,
  `READY_FOR_FULFILLMENT`); in one transaction it releases the inventory reservation (A3), the credit
  reservation (§9.2 `ReleaseAsync`) and reverses the unconsumed order payments (§9.4); the response of
  §9.4 lists the refunds to hand back. After a partial fulfillment use cancel-remaining (A4) instead.

---

## 5. A3 — Confirmation and stock reservation (`Operate`)

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/orders/{id}/fefo-suggestions` | — | `200 FefoSuggestionResponse` |
| POST | `/api/orders/{id}/confirm` | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/start-preparing` | — | `200 OrderResponse` |
| POST | `/api/orders/{id}/mark-ready` | — | `200 OrderResponse` |
| GET | `/api/orders/{id}/reservation` | — | `200 ReservationResponse` |

`confirm` — one transaction owned by `OrderConfirmer`:
1. Lock the order; must be `PENDING_CONFIRMATION` with ≥1 line.
2. `IOrderSettlementGuard.EnsureCanConfirmAsync` (§9): payment / credit check, credit reservation and the
   credit term for CREDIT orders. A failure → 422 and nothing is saved.
3. FEFO allocation per line over eligible lots (ACTIVE, not expired, available > 0; earliest expiry first,
   no-expiry lots last, then oldest lot). Lock the chosen lot balances in id order.
4. `InventoryLot.Reserve` per lot; create the reservation header (one active per order) and its items.
5. `order.Confirm(...)`; one `SaveChangesAsync`; commit.
- Not enough available stock for any line → 422 listing the lines; nothing is reserved (no partial
  reservation).
- Version conflict on a lot balance or the order → 409.

`start-preparing` (`CONFIRMED` → `PREPARING`) is optional; `mark-ready` accepts `CONFIRMED` or `PREPARING`.

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

## 6. A4 — Shared fulfillment posting and Pickup (`Operate`)

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
  cancelled with cancel-remaining (which releases the remaining reservation, no stock movement).
- `pickupCompletedAt/By` are set when the order reaches a terminal status at the counter.

`FulfillmentPostingService` (internal Application service, reused by A6), one call inside the caller's
transaction: lock lot balances in id order → reject expired/blocked/quarantined lots → SALE stock
movement + one item per lot with cost snapshot (weighted average cost) → decrease on hand and reserved →
consume reservation items → `order.RecordFulfillment` → `IFulfillmentFinancialPosting.PostAsync` (§9) →
post the movement. The caller saves once and commits.

---

## 7. A5 — Delivery notes

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/deliveries` | Operate | `CreateDeliveryRequest` | `201 DeliveryResponse` |
| GET | `/api/deliveries` | Read* | `DeliveryListRequest` (query) | `200 PagedResult<DeliveryListItem>` |
| GET | `/api/deliveries/{id}` | Read* | — | `200 DeliveryResponse` |
| GET | `/api/orders/{id}/deliveries` | Operate | — | `200 DeliveryListItem[]` |
| POST | `/api/deliveries/{id}/assign` | Operate | `{ assignedToUserId }` | `200 DeliveryResponse` |
| PUT | `/api/deliveries/{id}/items/{itemId}/lots` | Operate | `ChangeLotsRequest` | `200 DeliveryResponse` |
| POST | `/api/deliveries/{id}/dispatch` | Operate | — | `200 DeliveryResponse` |
| POST | `/api/deliveries/{id}/cancel` | Operate | `{ reason }` | `200 DeliveryResponse` |

\* DELIVERY_STAFF only see deliveries assigned to them (others → 404); the other roles see all.

`CreateDeliveryRequest`:

```json
{
  "orderId": "uuid",
  "items": [ { "orderItemId": "uuid", "plannedQuantity": 3 } ],
  "deliveryAddress": "DeliveryAddressRequest | null",
  "scheduledAt": "timestamp | null",
  "note": "string ≤1000 | null"
}
```

- Order must be DELIVERY and confirmed (`CONFIRMED`, `PREPARING`, `READY_FOR_FULFILLMENT`,
  `PARTIALLY_FULFILLED`).
- `plannedQuantity` in packaging units; across all active deliveries of an order item ≤ its remaining
  quantity (else 422).
- Address: snapshot of `deliveryAddress` if given, otherwise of the order's address.
- Lot allocations are created from the order's reservation items (FEFO order) at creation.
- `assignedToUserId` = a staff user id from `/api/staff` with role DELIVERY_STAFF and an ACTIVE store
  membership; the server resolves the store member (the client never sends `storeMemberId`).

`ChangeLotsRequest` — replaces the allocations of one delivery item before dispatch:

```json
{ "lots": [ { "inventoryLotId": "uuid", "baseQuantity": 50 } ] }
```

The old allocations are released (kept as history) and new ones created; the inventory reservation is
released on the old lot and made on the new lot in the same transaction. Total must equal the item's
planned base quantity not yet delivered.

Status flow: `DRAFT → ASSIGNED → OUT_FOR_DELIVERY`, then derived after attempts (§35.5). Cancel only when
no attempt is IN_PROGRESS; it releases the undelivered remainder.

`DeliveryListRequest` (query): `status`, `orderId`, `assignedToUserId`, `fromDate`, `toDate`
(on `scheduledAt` or `createdAt`), `search` (delivery or order number, recipient), `page`, `pageSize`.

`DeliveryResponse`:

```json
{
  "id": "uuid", "deliveryNumber": "DL-20261002-0001",
  "orderId": "uuid", "orderNumber": "OD-20261002-0001",
  "status": "ASSIGNED",
  "assignedTo": { "userId": "uuid", "fullName": "…", "phoneNumber": "…" },
  "deliveryAddress": { },
  "scheduledAt": null, "dispatchedAt": null, "completedAt": null,
  "note": null, "createdBy": "uuid", "createdAt": "…",
  "cancelledBy": null, "cancelledAt": null, "cancelReason": null,
  "items": [
    {
      "id": "uuid", "orderItemId": "uuid", "sku": "…", "productName": "…", "packagingName": "…",
      "plannedQuantity": 3, "plannedBaseQuantity": 75, "deliveredBaseQuantity": 0,
      "cancelledBaseQuantity": 0, "remainingBaseQuantity": 75, "status": "PENDING",
      "allocations": [
        { "id": "uuid", "inventoryLotId": "uuid", "lotNumber": "L01", "expiryDate": "2027-01-31",
          "allocatedBaseQuantity": 75, "deliveredBaseQuantity": 0, "releasedBaseQuantity": 0,
          "status": "ALLOCATED" }
      ]
    }
  ],
  "attempts": [ DeliveryAttemptResponse ]
}
```

---

## 8. A6 — Delivery attempts and incidents

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/deliveries/{id}/attempts` | Assigned DELIVERY_STAFF, Operate | `StartAttemptRequest` | `201 DeliveryAttemptResponse` |
| POST | `/api/deliveries/{id}/attempts/{attemptId}/complete` | Assigned DELIVERY_STAFF, Operate | `CompleteAttemptRequest` | `200 DeliveryResponse` |
| POST | `/api/deliveries/{id}/attempts/{attemptId}/cancel` | Operate | `{ reason }` | `200 DeliveryResponse` |
| GET | `/api/deliveries/{id}/attempts` | Read* | — | `200 DeliveryAttemptResponse[]` |
| POST | `/api/deliveries/{id}/incidents` | Assigned DELIVERY_STAFF, Operate | `ReportIncidentRequest` | `201 DeliveryIncidentResponse` |
| GET | `/api/deliveries/{id}/incidents` | Read* | — | `200 DeliveryIncidentResponse[]` |
| POST | `/api/deliveries/{id}/incidents/{incidentId}/resolve` | Operate | `ResolveIncidentRequest` | `200 DeliveryIncidentResponse` |

Photos are uploaded first with `POST /api/files/delivery-proofs` (exists); the returned `url` goes in the
bodies below. The server accepts only URLs of the `delivery-proofs` bucket.

`StartAttemptRequest` — omit `items` to take everything still undelivered:

```json
{ "items": [ { "allocationId": "uuid", "attemptedBaseQuantity": 75 } ] }
```

- Delivery must be `OUT_FOR_DELIVERY`; only one attempt IN_PROGRESS (else 422). The attempting member is
  the current user's store membership.

`CompleteAttemptRequest`:

```json
{
  "items": [ { "allocationId": "uuid", "deliveredBaseQuantity": 70 } ],
  "receiverName": "string ≤150 | null",
  "proofImageUrl": "string ≤1000 | null",
  "failureReasonCode": "CUSTOMER_ABSENT | UNREACHABLE | CUSTOMER_REFUSED | DAMAGED | WEATHER | VEHICLE_ISSUE | ADDRESS_ISSUE | OTHER | null",
  "note": "string ≤1000 | null"
}
```

- Missing allocation = delivered 0. `deliveredBaseQuantity` ≤ attempted, whole packages only.
- Result is derived: all delivered → SUCCESS, some → PARTIAL_SUCCESS, none → FAILED.
- Anything delivered → `proofImageUrl` and `receiverName` required (else 400/422).
- Nothing delivered → `failureReasonCode` required; no stock movement, no debt; delivery → RETRY_PENDING.
- Something delivered → one transaction (`DeliveryAttemptCompleter`): `FulfillmentPostingService` (A4) →
  SALE movement linked to the attempt → delivery and order statuses (§XVII).
- A photo saved on an attempt or incident can no longer be deleted through `DELETE /api/files/delivery-proofs`
  (A6 must add this check to that endpoint; C5 extends it to refund proof photos, decision C-D3).

`DeliveryAttemptResponse`:

```json
{
  "id": "uuid", "attemptNumber": 1, "status": "PARTIAL_SUCCESS",
  "attemptedBy": { "userId": "uuid", "fullName": "…" },
  "startedAt": "…", "completedAt": "…",
  "receiverName": "…", "proofImageUrl": "https://…/delivery-proofs/…",
  "failureReasonCode": null, "note": null, "saleStockMovementId": "uuid | null",
  "items": [ { "allocationId": "uuid", "inventoryLotId": "uuid", "lotNumber": "L01",
               "attemptedBaseQuantity": 75, "deliveredBaseQuantity": 70, "failedBaseQuantity": 5 } ]
}
```

`ReportIncidentRequest`:

```json
{
  "incidentType": "CUSTOMER_ABSENT | UNREACHABLE | CUSTOMER_REFUSED | DAMAGED | WEATHER | VEHICLE_ISSUE | ADDRESS_ISSUE | OTHER",
  "description": "string, required, ≤1000",
  "deliveryAttemptId": "uuid | null",
  "allocationId": "uuid | null",
  "affectedBaseQuantity": "integer > 0 | null",
  "evidenceImageUrl": "string ≤1000 | null"
}
```

`ResolveIncidentRequest`:

```json
{
  "resolutionType": "RETRY_DELIVERY | REPLACE_GOODS | RETURN_TO_STORE | WRITE_OFF | CANCEL_REMAINDER | NO_ACTION | OTHER",
  "resolutionNote": "string ≤1000 | null",
  "relatedStockMovementId": "uuid | null"
}
```

`DeliveryIncidentResponse`: `id, deliveryId, deliveryAttemptId, allocationId, incidentType,
affectedBaseQuantity, description, status (OPEN | RESOLVED), resolutionType, resolutionNote,
evidenceImageUrl, relatedStockMovementId, reportedBy, reportedAt, resolvedBy, resolvedAt`.

A customer refusing goods **before** handover is an incident (`CUSTOMER_REFUSED`), not a sales return.

---

## 9. Cross-module interfaces (Application, `Common/Interfaces` or the owning feature)

Group A codes against these interfaces and registers a **temporary implementation** until the owner
delivers the real one. The owner may add members but must not change these signatures without updating
this file.

### 9.1 `IPriceResolver` — owner B (Pricing)

```csharp
public interface IPriceResolver
{
    // Price list that applies to the customer at the given moment: the Farmer's customer-group list,
    // or the walk-in default list when farmerProfileId is null.
    Task<PriceContext> GetContextAsync(Guid? farmerProfileId, DateTimeOffset at, CancellationToken cancellationToken);

    // Selling price per (storeProductId, productPackagingId) in that list; a missing pair = no price.
    Task<IReadOnlyDictionary<(Guid StoreProductId, Guid PackagingId), decimal>> GetPricesAsync(
        Guid priceListId, IReadOnlyCollection<(Guid StoreProductId, Guid PackagingId)> lines,
        CancellationToken cancellationToken);
}

public sealed record PriceContext(Guid? CustomerGroupId, Guid PriceListId);
```

Temporary implementation (A): a fixed price per packaging from configuration or a test fake. No price →
the line cannot be ordered (422).

### 9.2 `IOrderSettlementGuard` — owner B (Credit); reads payments through C's `IOrderPrepaymentLedger`

```csharp
public interface IOrderSettlementGuard
{
    // Called inside the confirmation transaction, before any stock is reserved.
    // FULL_PAYMENT: PAID order payments (ORDER allocations) must cover the order total (decision D3).
    // CREDIT: requires an ACTIVE credit profile, computes required credit
    //   (total − confirmed prepayment), checks available credit and creates the credit reservation.
    // Throws BusinessRuleException when the order cannot be confirmed.
    Task<SettlementResult> EnsureCanConfirmAsync(Order order, Guid actorId, CancellationToken cancellationToken);

    // Called when an order (or its remainder) is cancelled: releases the unused credit reservation.
    Task ReleaseAsync(Order order, Guid actorId, string? reason, CancellationToken cancellationToken);
}

public sealed record SettlementResult(int? CreditTermDays);
```

Temporary implementation (A): accept FULL_PAYMENT, refuse CREDIT with 422 "credit is not available yet".

### 9.3 `IFulfillmentFinancialPosting` — owner B (Debt); consumes prepayment through C's `IOrderPrepaymentLedger`

```csharp
public interface IFulfillmentFinancialPosting
{
    // Called by FulfillmentPostingService inside the fulfillment transaction, after stock is posted.
    // fulfilledValue = Σ fulfilled base quantity × order item unit price, per line (rounded to 2 decimals).
    // Applies available order prepayment, consumes the credit reservation for the unpaid part, creates the
    // Debt Entry + CREDIT_SALE Debt Transaction when unpaid > 0 (due date = fulfillment date + credit term).
    Task PostAsync(FulfillmentPostingContext context, CancellationToken cancellationToken);
}

public sealed record FulfillmentPostingContext(
    Order Order,
    IReadOnlyList<FulfilledLine> Lines,
    string SourceType,            // "DELIVERY" | "PICKUP"
    Guid? DeliveryAttemptId,
    Guid StockMovementId,
    Guid ActorId,
    DateTimeOffset FulfilledAt);

public sealed record FulfilledLine(Guid OrderItemId, long FulfilledBaseQuantity, decimal FulfilledValue);
```

Temporary implementation (A): does nothing (FULL_PAYMENT orders only, while 9.2 refuses CREDIT). It must
not be left registered once B/C deliver the real one (task A7).

### 9.4 `IOrderPaymentCancellation` — owner C (Payments)

Defined in `API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md` §3.2. Called by order cancellation (A2) to reverse
the unconsumed order payments and request one PENDING refund per payment (design §35.18); returns those
refunds so the response can tell staff what to hand back. Called after `Cancel` and after a cancel-remaining
(A4) that leaves the order PARTIALLY_CANCELLED. Temporary implementation (A): returns an empty list.

`relatedStockMovementId` in `ResolveIncidentRequest` (A6) must be an ADJUSTMENT movement created through
C's `POST /api/inventory/adjustments` (decision D5).

Implementations never call `SaveChangesAsync` or open a transaction: the A use case owns both.

---

## 10. Test expectations per task

Every task delivers: unit tests (rules, status derivation), offline HTTP tests (401/403 per role,
400 validation, Swagger lists the routes), and rolled-back real PostgreSQL tests for every endpoint that
writes. In addition:

| Task | Must prove |
|---|---|
| A1 | one ACTIVE cart, price recalculated, Farmer isolation |
| A2 | WALK_IN/REGISTERED/CREDIT rules, snapshots unchanged after a price change, override audit |
| A3 | no overselling with two concurrent confirmations on the same lot; expired/blocked lots skipped; nothing reserved on shortage |
| A4 | on hand / reserved / cost after pickup, partial pickup then rest, swapped lot, rollback on failure |
| A5 | planned quantity limit, lot change keeps history and moves the reservation, delivery-staff scope |
| A6 | design example 20 → 18 + 2, proof required, one attempt in progress, failed attempt posts nothing |

---

## 11. Decisions (settled by the team, 2026-10-02)

| # | Decision | Applies to |
|---|---|---|
| D1 | Farmer addresses come from B1 (`/api/me/addresses`, see `API_CONTRACT_CUSTOMERS_CREDIT.md`). Until B1 is merged, checkout and counter orders accept only an inline `deliveryAddress`; `addressId` is added to the same request once B1 exists (no route change) | A2 |
| D2 | A delivery is assigned with the staff **user id** (`assignedToUserId`, the `id` returned by `/api/staff`); the server resolves the ACTIVE store member. Clients never send `storeMemberId` | A5 |
| D3 | A FULL_PAYMENT order is confirmed only when its PAID order payments cover the total. At the counter staff record the cash payment first (C's payment API), then confirm; online Farmers pay through payOS before staff confirm. CREDIT orders may be confirmed with any prepayment (the rest is reserved credit) | A3, C |
| D4 | Admin, Store Owner and Sales may override a line price; `reason` is mandatory; suggested price, actor and reason are stored | A2 |
| D5 | A6 records every incident resolution. When a resolution must move stock (write-off or return of goods already handed over), the stock movement is made through C's stock adjustment and linked with `relatedStockMovementId`; A6 never posts that stock itself | A6, C |
| D6 | FEFO order: earliest expiry first; lots without expiry after all dated lots; ties by oldest lot (first receipt) | A3, A4, A5 |
| D7 | Order confirmation and pickup: Admin, Store Owner, Sales | A3, A4 |
| D8 | DELIVERY_STAFF use `/api/deliveries`, scoped to deliveries assigned to them | A5, A6 |
| D9 | Numbers `OD-yyyyMMdd-NNNN` (orders) and `DL-yyyyMMdd-NNNN` (deliveries) | A2, A5 |
| D10 | `start-preparing` / `mark-ready` are optional steps | A3 |

## 12. Related contracts

- Group B (customers, addresses, customer groups, price lists, credit, debt):
  `docs/reference/API_CONTRACT_CUSTOMERS_CREDIT.md` — also defines the real implementations of §9.
- Group C (payments/payOS, stocktake and adjustments, returns, refunds):
  `docs/reference/API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md`.
- Group D (AI diagnosis, content, notifications, reports): not written yet.
