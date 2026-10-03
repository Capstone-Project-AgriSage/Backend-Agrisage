# Flow L2 — Online Order, payOS and Delivery (owner: Teammate 2)

Version 2.0 — 2026-10-03. Conventions, shared shapes, interfaces and ownership rules: [README.md](README.md).
Sources: `DATABASE_DESIGN.md` §3–4, §32–43, §36, §7.3–7.4, §XVII, §35.4–35.5, §35.18; `BUSINESS_RULES.md`.

L2 covers the Farmer side (profile, addresses, cart, online order, payOS) and the delivery side (delivery notes,
assignment, trips, failures and incidents). It reuses L1's `OrderBuilder`, `PaymentAllocator`, `OrderCanceller` and
`FulfillmentPostingService` (README §4.9).

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Farmer fills the profile and adds a farm address | `PUT /api/me/profile`, `POST /api/me/addresses` | first address = default |
| 2 | Farmer fills the cart | `POST /api/me/cart/items` | prices recalculated on every read |
| 3 | Farmer checks out for delivery | `POST /api/me/orders` (`DELIVERY`, `addressId`) | order `PENDING_CONFIRMATION`, cart CONVERTED |
| 4 | Farmer pays online | `POST /api/me/payments/payos` → payOS page → webhook | payment PAID + ORDER allocation |
| 5 | Sales confirms (L1) | `POST /api/orders/{id}/confirm` | stock reserved FEFO |
| 6 | Sales creates two delivery notes (two trips) and assigns a driver | `POST /api/deliveries`, `POST /{id}/assign` | `DL-…` notes, lot allocations from the reservation |
| 7 | Driver leaves and delivers part of trip 1 (customer absent for the rest) | `POST /{id}/dispatch`, `POST /{id}/attempts`, `POST /api/files/delivery-proofs`, `POST /{id}/attempts/{attemptId}/complete` | SALE movement for the delivered part only, delivery `RETRY_PENDING` |
| 8 | Driver reports a damaged bag | `POST /{id}/incidents`, later `POST /{id}/incidents/{incidentId}/resolve` | incident linked to an L4 stock adjustment |
| 9 | Second attempt succeeds; trip 2 delivered | attempts again | order `COMPLETED` |
| 10 | Farmer follows the deliveries; owner reads the delivery report | `GET /api/me/orders/{id}/deliveries`, `GET /api/reports/deliveries` | |

---

## 2. Tasks

| Task | Content | Depends on | Delivers to |
|---|---|---|---|
| F2.1 | Farmer profile, addresses, staff customer views, store-managed Farmer accounts | — | L1 (`addressId`, REGISTERED customers), L3 |
| F2.2 | Farmer cart | F1.1 resolver (tests may fake it) | F2.3 |
| F2.3 | Online checkout, my orders, Farmer cancel | M1 (`OrderBuilder`), F1.6 for cancel | — |
| F2.4 | payOS adapter, links, cancel, sync, webhook | F1.3 (`PaymentAllocator`) for the allocation part | F1.6 (cancel links), L3 (online debt repayment) |
| F2.5 | Delivery notes | F1.4 (reservation) | F2.6 |
| F2.6 | Delivery attempts and incidents | M2 (`FulfillmentPostingService`), F4.2 for incident adjustments | — |
| F2.7 | **New** Farmer delivery tracking, delivery report | F2.6 | — |

---

## 3. F2.1 — Farmer profile, addresses and customers

### 3.1 Farmer (`FARMER`)

| Method | Route | Body | Response |
|---|---|---|---|
| GET | `/api/me/profile` | — | `200 FarmerProfileResponse` |
| PUT | `/api/me/profile` | `{ fullName, dateOfBirth?, gender? }` | `200 FarmerProfileResponse` |
| GET | `/api/me/addresses` | — | `200 AddressResponse[]` |
| POST | `/api/me/addresses` | `AddressRequest` | `201 AddressResponse` |
| PUT | `/api/me/addresses/{id}` | `AddressRequest` | `200 AddressResponse` |
| DELETE | `/api/me/addresses/{id}` | — | `204` (soft delete) |
| POST | `/api/me/addresses/{id}/set-default` | — | `200 AddressResponse` |

`AddressRequest` = `DeliveryAddressRequest` (README §2) plus `addressType: "HOME | FARM | OTHER"` and
`isDefault: boolean`.

`AddressResponse`: `id, recipientName, recipientPhone, addressLine, ward, district, province, latitude,
longitude, addressType, isDefault, createdAt`.

`FarmerProfileResponse`:

```json
{
  "farmerProfileId": "uuid", "userId": "uuid",
  "fullName": "Nguyễn Văn A", "phoneNumber": "0901234567", "email": null,
  "dateOfBirth": "1980-05-01", "gender": "MALE",
  "customerGroup": { "id": "uuid", "code": "REGULAR", "name": "Khách quen" },
  "createdAt": "…"
}
```

Rules:
- `gender`: `MALE | FEMALE | OTHER | null`. Phone and email are not changed here (Auth concern).
- At most one default address (design §4). The first address becomes default; `set-default` or
  `isDefault: true` moves the flag in the same transaction; deleting the default leaves no default.
- At most 10 active addresses per Farmer (422 beyond).
- Another user's address id → 404.
- `farmer_profiles.notes` is a staff note: not visible or editable by the Farmer.
- `customerGroup` = current assignment, or the default group (decision B-D2).

### 3.2 Staff

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| POST | `/api/customers` | Operate | `CreateCustomerRequest` | `201 CustomerResponse` |
| GET | `/api/customers` | Operate | query: `search` (name, phone, email), `customerGroupId`, `hasOutstandingDebt`, `page`, `pageSize` | `200 PagedResult<CustomerListItem>` |
| GET | `/api/customers/{farmerProfileId}` | Operate | — | `200 CustomerResponse` |
| PUT | `/api/customers/{farmerProfileId}` | Operate | `{ dateOfBirth?, gender?, notes? }` | `200 CustomerResponse` |
| GET | `/api/customers/{farmerProfileId}/addresses` | Operate | — | `200 AddressResponse[]` |

**Customer id = `farmerProfileId`** everywhere, because orders, credit and debt all reference
`farmer_profiles.id`. The `userId` is returned for information only.

`CustomerListItem`: `farmerProfileId, userId, fullName, phoneNumber, email, accountStatus,
customerGroup {id, code, name}, creditStatus (null if no profile), creditLimit, outstandingBalance, createdAt`.

`CustomerResponse` = `FarmerProfileResponse` + `notes`, `accountStatus`, `addresses[]`,
`credit` (L3's `CreditSummaryResponse` or `null`), `debt` (L3's `DebtAccountResponse` or `null`).

Cross-flow fields: `creditStatus`, `creditLimit`, `outstandingBalance` and `hasOutstandingDebt` are plain reads of
`farmer_credit_profiles` / `debt_accounts` (null when absent). `credit` and `debt` are returned as `null` by F2.1;
L3 fills them when F3.2 / F3.4 exist (a small PR on L2's customer query, agreed between the two owners).

**Store-managed Farmer account (decision B-D1)** — for farmers who cannot register in the app themselves
but must be REGISTERED customers (customer group prices, credit, debt):

`CreateCustomerRequest`:

```json
{
  "fullName": "string, required, ≤150",
  "phoneNumber": "string, required (normalized like registration)",
  "email": "string ≤255 | null",
  "dateOfBirth": "date | null",
  "gender": "MALE | FEMALE | OTHER | null",
  "notes": "string ≤1000 | null",
  "customerGroupId": "uuid | null",
  "address": "AddressRequest | null"
}
```

- One transaction: `User` (role FARMER, status ACTIVE, `phone_verified = false`) + `FarmerProfile`
  (+ group assignment when `customerGroupId` is given, + first address when `address` is given).
- The phone number is required (the farmer's identifier at the counter); phone / email already used → 409,
  exactly like self-registration (same normalization, same unique indexes).
- **No usable password:** `password_hash` is the hash of a random 32-byte secret that is never stored in
  clear, shown or logged. Nobody — staff included — can sign in to this account yet; the counter flows use
  the `farmerProfileId`.
- The farmer takes over the account later by verifying the phone number and setting a password, through the
  OTP / forgot-password flow that is still waiting for the mentor's decision on Auth. Until that flow exists,
  self-registering with the same phone answers 409 ("this phone already has a store account, ask the store").
- The action is written to `audit_logs` (actor, new user id; never the secret).

---

## 4. F2.2 — Farmer cart (`FARMER`)

The cart never stores prices: every response recalculates them through `IPriceResolver` (README §4.1).
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
the cart (it is only reserved at confirmation, F1.4).

---

## 5. F2.3 — Online checkout and my orders (`FARMER`)

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

- Lines come from the ACTIVE cart; empty cart → 422. The order is built with L1's `OrderBuilder` (no price
  overrides: `unitPrice` = suggested). The cart becomes CONVERTED with `convertedOrderId`, in the same transaction.
- DELIVERY needs exactly one of `addressId` (one of the Farmer's own addresses, copied as a snapshot) or
  `deliveryAddress`.
- Snapshot at creation: customer group, price list, customer name and phone, address, and per line SKU,
  name, packaging name, conversion, suggested price.
- A Farmer only sees and cancels their own orders (someone else's id → 404, not 403, to avoid leaking ids).
- Cancel only while `PENDING_CONFIRMATION`, otherwise 422; it runs L1's `OrderCanceller` (a PENDING payOS link is
  cancelled, a PAID payment gets a PENDING refund request).
- `GET /api/me/orders/{id}/payments` (with refunds) is L1's route (FLOW_1 §5).

---

## 6. F2.4 — payOS

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
the app shows the split with L3's `allocation-preview`). Choosing specific debt entries is done in cash at the
counter (F1.3).

`PayOsPaymentResponse`: `{ paymentId, paymentNumber, amount, checkoutUrl, qrCode, providerOrderCode,
expiresAt, status }`. `qrCode` is the VietQR payload string (the client renders it as a QR image).

### 6.1 Facts about payOS that shape the rules

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

### 6.2 Rules

- ORDER: `amount` defaults to the order's remaining amount to pay; a Farmer only pays their own orders.
  DEBT: `amount` required (≤ balance); a Farmer only pays their own debt.
- **Whole VND only (decision C-D9):** a payOS amount with a fractional part is refused (422); the fraction
  is paid in cash.
- Creates a PENDING PAYOS payment with a new `providerOrderCode` (time-based + random suffix, retried on a
  unique clash), the description of §6.1, `expiredAt = now + PayOS:LinkExpiryMinutes` (default 30), then
  `IPaymentGateway.CreatePaymentLinkAsync`. The target order is stored in `payments.order_id`; a debt
  repayment is allocated oldest due date first when PAID (decision C-D1). If payOS refuses, the payment is
  marked FAILED and the API answers 503 without provider details.
- At most one PENDING payOS payment per order: a new request cancels the previous link first.
- Cancel: `PaymentRequests.CancelAsync(orderCode, reason)` at payOS, then `Payment.Cancel`. If payOS reports
  the link as already PAID, nothing is cancelled and the payment is synced instead.
- `POST /{id}/sync` queries `GetAsync(orderCode)`: EXPIRED / CANCELLED / FAILED → payment FAILED / CANCELLED;
  PAID → decision C-D8; PENDING / PROCESSING → unchanged. After the redirect the client polls
  `GET /api/me/payments/{id}` and may call `sync` once the link has expired.
- Secrets `PayOS:ClientId`, `PayOS:ApiKey`, `PayOS:ChecksumKey` only in User Secrets / environment /
  `appsettings.Local.json`, never in committed `appsettings*.json`, never logged; `PayOS:ReturnUrl`,
  `PayOS:CancelUrl`, `PayOS:LinkExpiryMinutes` are not secret. Missing configuration → 503 on payOS endpoints only
  (like Storage).
- Adapter (decision C-D7): `Infrastructure/Payments/PayOsPaymentGateway` wraps the SDK `PayOSClient`
  (shared `HttpClient`; `MaxRetries = 0` for link creation so a timeout never creates two links; an
  uncertain result is resolved with `GetAsync(orderCode)`).
- `IPaymentGateway` changes in F2.4 (nobody implements it yet): `VerifyWebhookAsync` (the SDK verification is
  async; no `.Result`), `GetPaymentLinkAsync(orderCode)`, `CancelPaymentLinkAsync(orderCode, reason)`,
  `ExpiresAt` and `QrCode` in the create request/result, `long` amounts at the adapter boundary.

### 6.3 Webhook (one transaction, idempotent, design §36)

1. Body without `data` or `signature` (registration test, health check) → `200`, nothing changes.
2. `VerifyWebhookAsync`: invalid signature → `400` and a warning log (no payload values).
3. Lock the payment by `providerOrderCode` (`IRowLockService.LockPaymentAsync`, added by F2.4).
   Unknown code (including the registration test order) → `200` + information log.
4. Already PAID (duplicate delivery) → `200`, nothing changes.
5. `data.amount` ≠ payment amount → `200`, payment stays PENDING, error log for staff follow-up. An
   overpayment is not accepted as paid.
6. Paid (`success` and `data.code == "00"`) → `MarkPaid(PAYOS_WEBHOOK, providerTransactionId = data.reference)`,
   raw `data` stored in `provider_metadata`, then L1's `PaymentAllocator` (ORDER allocation to `payments.order_id`
   + `ICreditReservationAdjuster`, or `IDebtRepaymentPosting` with no explicit allocations = oldest due first).
   Not paid → `MarkFailed`.
7. Paid for a payment cancelled locally → `200`, critical log (money received on a cancelled payment) for
   manual handling; nothing is allocated automatically.
8. Unexpected exception → `500` so payOS retries; business rejections after a valid signature never answer
   4xx, so payOS does not retry them forever.

Kept from NutriPlan: SDK verification, `success && code == "00"`, a link expiry, 200 for the registration
test. Not repeated: cancelling a payment because the return URL says `cancel=true` (only payOS's own cancel or
status API changes a payment here), accepting `amount ≥` as paid, no row lock against duplicate concurrent
webhooks, returning raw exception messages to clients.

---

## 7. F2.5 — Delivery notes

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

\* DELIVERY_STAFF only see deliveries assigned to them (others → 404); the other roles see all (decision D8).

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
  quantity (else 422). Several delivery notes per order = several trips.
- Address: snapshot of `deliveryAddress` if given, otherwise of the order's address.
- Lot allocations are created from the order's reservation items (FEFO order) at creation.
- `assignedToUserId` = a staff user id from `/api/staff` with role DELIVERY_STAFF and an ACTIVE store
  membership; the server resolves the store member (the client never sends `storeMemberId`, decision D2).

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

`DeliveryListItem`: `id, deliveryNumber, orderId, orderNumber, status, assignedTo {userId, fullName},
recipientName, province, scheduledAt, dispatchedAt, completedAt, itemCount, createdAt`.

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

## 8. F2.6 — Delivery attempts and incidents

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
- Something delivered → one transaction (`DeliveryAttemptCompleter`): L1's `FulfillmentPostingService` →
  SALE movement linked to the attempt → delivery and order statuses (§XVII).
- A photo saved on an attempt or incident can no longer be deleted through `DELETE /api/files/delivery-proofs`
  (F2.6 adds this check to that endpoint; F4.5 extends it to refund proof photos, decision C-D3).

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

- `relatedStockMovementId` must be an ADJUSTMENT movement created through L4's `POST /api/inventory/adjustments`
  (decision D5); F2.6 never posts that stock itself.
- A customer refusing goods **before** handover is an incident (`CUSTOMER_REFUSED`), not a sales return.

---

## 9. F2.7 — Delivery tracking and delivery report (new)

| Method | Route | Roles | Query | Response |
|---|---|---|---|---|
| GET | `/api/me/orders/{id}/deliveries` | FARMER (own order) | — | `200 MyDeliveryResponse[]` |
| GET | `/api/reports/deliveries` | Manage | `fromDate`, `toDate` (≤ 366 days), `groupBy` = `DAY` (default) \| `STAFF` | `200 DeliveryReportResponse` |

`MyDeliveryResponse` — no lots, costs or internal notes:

```json
{
  "id": "uuid", "deliveryNumber": "DL-20261002-0001", "status": "RETRY_PENDING",
  "scheduledAt": "…", "dispatchedAt": "…", "completedAt": null,
  "assignedTo": { "fullName": "…", "phoneNumber": "…" },
  "items": [ { "orderItemId": "uuid", "productName": "…", "packagingName": "Bao 25kg",
               "plannedQuantity": 3, "deliveredQuantity": 2, "remainingQuantity": 1 } ],
  "attempts": [ { "attemptNumber": 1, "status": "PARTIAL_SUCCESS", "startedAt": "…", "completedAt": "…",
                  "receiverName": "…", "proofImageUrl": "…", "failureReasonCode": "CUSTOMER_ABSENT" } ]
}
```

Quantities are packaging units (deliveries are whole packages). Someone else's order → 404.

`DeliveryReportResponse`:

```json
{
  "fromDate": "2026-10-01", "toDate": "2026-10-31", "groupBy": "STAFF",
  "rows": [ { "key": "uuid", "label": "Trần Văn B", "deliveries": 20, "attempts": 24,
              "successful": 18, "partial": 3, "failed": 3, "successRate": 0.75 } ],
  "failureReasons": [ { "code": "CUSTOMER_ABSENT", "count": 2 } ],
  "incidents": [ { "incidentType": "DAMAGED", "open": 1, "resolved": 3 } ],
  "totals": { "deliveries": 20, "attempts": 24, "successful": 18, "partial": 3, "failed": 3, "successRate": 0.75 }
}
```

Attempts are counted on their completion day; `successRate` = successful ÷ completed attempts.

---

## 10. Tests — must prove

| Task | Must prove |
|---|---|
| F2.1 | one default address, Farmer isolation (404 on others' ids), 10-address limit; staff-created Farmer: user + profile in one transaction, duplicate phone 409, the account cannot sign in (no known password), no secret in logs or responses |
| F2.2 | one ACTIVE cart, price recalculated, Farmer isolation |
| F2.3 | checkout snapshots and converts the cart in one transaction; Farmer cancel only while PENDING_CONFIRMATION; isolation |
| F2.4 | webhook idempotent (same payload twice = one PAID, also concurrently), bad signature 400, registration test and unknown code 200, amount mismatch and overpayment stay PENDING, fractional amount 422, expired link synced to FAILED; adapter tested with a fake gateway, real payOS only opt-in |
| F2.5 | planned quantity limit, lot change keeps history and moves the reservation, delivery-staff scope |
| F2.6 | design example 20 → 18 + 2, proof required, one attempt in progress, failed attempt posts nothing, two trips complete the order |
| F2.7 | Farmer sees only own deliveries without lots/costs; report counts per staff and failure reasons |

---

## 11. Decisions that apply to L2

| # | Decision |
|---|---|
| D1 | Farmer addresses come from F2.1; until F2.1 is merged, checkout and counter orders accept only an inline `deliveryAddress`; `addressId` is added to the same request once F2.1 exists (no route change) |
| D2 | A delivery is assigned with the staff **user id** (`assignedToUserId`); the server resolves the ACTIVE store member |
| D5 | F2.6 records every incident resolution; when a resolution must move stock, the movement is made through L4's stock adjustment and linked with `relatedStockMovementId` |
| D8 | DELIVERY_STAFF use `/api/deliveries`, scoped to deliveries assigned to them |
| D9 | Delivery notes `DL-yyyyMMdd-NNNN` |
| B-D1 | Staff create store-managed Farmer accounts with `POST /api/customers` (phone required, no usable password); Farmers can still self-register; unregistered buyers stay WALK_IN |
| C-D1 | `payments.order_id` set from creation for ORDER payments; payOS debt repayments are allocated oldest due date first |
| C-D3 | Proof photos referenced by attempts, incidents or refunds cannot be deleted through `DELETE /api/files/delivery-proofs` (422) |
| C-D7 | The official payOS .NET SDK (`payOS` 2.1.0) is used only inside `Infrastructure/Payments/PayOsPaymentGateway`; adding the package is approved |
| C-D8 | Missed webhook: when `sync` finds the link PAID, the payment is applied exactly like a webhook (`confirmation_source = PAYOS_WEBHOOK`, `provider_metadata.confirmedVia = "STATUS_QUERY"`) |
| C-D9 | payOS amounts are whole VND; a fractional amount is refused (422) and that part is paid in cash |
| F-D5 | Reports are Manage-only and owned by the flow whose data they read |
