# API Contract — Customers, Pricing, Credit and Debt (Group B)

Version 1.0 — 2026-10-02. Scope: tasks B1–B5 and the real implementations of the interfaces that group A
uses (`API_CONTRACT_SALES.md` §9) plus the interfaces group C (payments, returns) needs from B.

Sources: `DATABASE_DESIGN.md` §3–4, §7–8, §18–20, §44–51, §XVI, §XIX, §35.6–35.7; `BUSINESS_RULES.md`
rules 1–5, 22–29, 32. When this file and the design disagree, the design wins and this file is corrected.

**Changing this contract:** PR on this file first, then code.

---

## 1. Conventions

Same as `API_CONTRACT_SALES.md` §1 (routes, JSON, enum strings, money with ≤2 decimals, paging,
`PagedResult`, error codes, `ApiRoles`). In addition:

- **Customer id = `farmerProfileId`** everywhere (`/api/customers/{farmerProfileId}`), because orders, credit
  and debt all reference `farmer_profiles.id`. The `userId` is returned for information only.
- Every money-changing action (credit limit, debt adjust/cancel/manual entry) writes an `audit_logs` row.
- Document number: debt entry `DE-yyyyMMdd-NNNN` (shared `DocumentNumbers`; `entry_number` is unique).
- Overdue is **derived**, never stored: `isOverdue = dueDate < today (Vietnam) and outstanding > 0`,
  `overdueDays = today − dueDate`. No interest or penalty (rule 24).

## 2. Tasks and order

| Task | Content | Depends on | Delivers to others |
|---|---|---|---|
| B1 | Farmer profile, addresses, staff customer views | — | addresses for A2 (decision D1 of group A) |
| B2 | Customer groups and assignment | B1 | — |
| B3 | Price lists, group ↔ price list, public catalog price | B2 | real `IPriceResolver` for A1/A2 |
| B4 | Credit tiers, credit profiles, exposure | B1 | real `IOrderSettlementGuard`, `ICreditReservationAdjuster` |
| B5 | Debt ledger, debt actions | B4 | real `IFulfillmentFinancialPosting`, `IDebtRepaymentPosting`, `IDebtReturnPosting` |

B3 should be merged as early as possible: A1/A2 run on a temporary price resolver until then.

---

## 3. B1 — Farmer profile and addresses

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

`AddressRequest` = the `DeliveryAddressRequest` of the sales contract plus
`addressType: "HOME | FARM | OTHER"` and `isDefault: boolean`.

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

### 3.2 Staff

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/customers` | Operate | query: `search` (name, phone, email), `customerGroupId`, `hasOutstandingDebt`, `page`, `pageSize` | `200 PagedResult<CustomerListItem>` |
| GET | `/api/customers/{farmerProfileId}` | Operate | — | `200 CustomerResponse` |
| PUT | `/api/customers/{farmerProfileId}` | Operate | `{ dateOfBirth?, gender?, notes? }` | `200 CustomerResponse` |
| GET | `/api/customers/{farmerProfileId}/addresses` | Operate | — | `200 AddressResponse[]` |

`CustomerListItem`: `farmerProfileId, userId, fullName, phoneNumber, email, accountStatus,
customerGroup {id, code, name}, creditStatus (null if no profile), creditLimit, outstandingBalance, createdAt`.

`CustomerResponse` = `FarmerProfileResponse` + `notes`, `accountStatus`, `addresses[]`,
`credit` (the B4 summary or `null`), `debt` (the B5 account summary or `null`).

Staff do **not** create Farmer accounts in this contract (decision B-D1).

---

## 4. B2 — Customer groups

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/customer-groups` | Operate | query: `isActive`, `search`, `page`, `pageSize` | `200 PagedResult<CustomerGroupResponse>` |
| GET | `/api/customer-groups/{id}` | Operate | — | `200 CustomerGroupResponse` |
| POST | `/api/customer-groups` | Manage | `{ code, name, description?, priority }` | `201 CustomerGroupResponse` |
| PUT | `/api/customer-groups/{id}` | Manage | `{ name, description?, priority }` | `200 CustomerGroupResponse` |
| POST | `/api/customer-groups/{id}/activate` | Manage | — | `200` |
| POST | `/api/customer-groups/{id}/deactivate` | Manage | — | `200` |
| POST | `/api/customer-groups/{id}/set-default` | Manage | — | `200` |
| DELETE | `/api/customer-groups/{id}` | Manage | — | `204` |
| PUT | `/api/customers/{farmerProfileId}/group` | Operate | `{ customerGroupId, reason? }` | `200 CustomerResponse` |
| GET | `/api/customers/{farmerProfileId}/group-history` | Operate | — | `200 GroupAssignmentResponse[]` |

`CustomerGroupResponse`: `id, code, name, description, priority, isDefault, isActive, memberCount,
currentPriceList {id, code, name} | null, createdAt`.

`GroupAssignmentResponse`: `id, customerGroup {id, code, name}, effectiveFrom, effectiveTo, assignedBy,
reason`.

Rules:
- `code` unique per store (case-insensitive, 409), immutable after creation.
- Exactly one active default group per store: `set-default` moves the flag; the default cannot be
  deactivated or deleted (422).
- Deactivating a group that still has current members → 422 (move them first).
- DELETE only if the group was never assigned and never linked to a price list (otherwise 409 → deactivate).
- Assignment (rules 3–4): ends the current assignment (`effective_to = now`) and creates a new one in one
  transaction; assigning the current group again is a no-op `200`. Inactive group → 422.
- A Farmer with no assignment belongs to the **default group** (decision B-D2); no row is created at
  registration.

---

## 5. B3 — Price lists

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/price-lists` | Operate | query: `status`, `isWalkInDefault`, `search`, `page`, `pageSize` | `200 PagedResult<PriceListResponse>` |
| GET | `/api/price-lists/{id}` | Operate | — | `200 PriceListResponse` |
| POST | `/api/price-lists` | Manage | `PriceListRequest` | `201 PriceListResponse` (DRAFT) |
| PUT | `/api/price-lists/{id}` | Manage | `PriceListRequest` without `code` | `200 PriceListResponse` |
| POST | `/api/price-lists/{id}/activate` | Manage | — | `200` |
| POST | `/api/price-lists/{id}/deactivate` | Manage | — | `200` |
| DELETE | `/api/price-lists/{id}` | Manage | — | `204` |
| GET | `/api/price-lists/{id}/items` | Operate | query: `search`, `page`, `pageSize` | `200 PagedResult<PriceListItemResponse>` |
| PUT | `/api/price-lists/{id}/items` | Manage | `{ items: [ { storeProductId, productPackagingId, sellingPrice } ] }` | `200 { created, updated }` |
| DELETE | `/api/price-lists/{id}/items/{itemId}` | Manage | — | `204` |
| PUT | `/api/customer-groups/{id}/price-list` | Manage | `{ priceListId, effectiveFrom? }` | `200 CustomerGroupResponse` |
| GET | `/api/customer-groups/{id}/price-lists` | Operate | — | `200 GroupPriceListResponse[]` (history) |

`PriceListRequest`: `{ code, name, description?, effectiveFrom, effectiveTo?, isWalkInDefault }`.

`PriceListResponse`: `id, code, name, description, effectiveFrom, effectiveTo, isWalkInDefault, status,
itemCount, groups [{id, code, name}], createdAt`.

`PriceListItemResponse`: `id, storeProductId, productPackagingId, sku, productName, packagingName,
sellingPrice`.

Rules:
- `code` unique per store (409). `effectiveTo` > `effectiveFrom` when given.
- Items: only ACTIVE **sale** packagings of the given store product; `sellingPrice` ≥ 0, ≤ 2 decimals;
  one row per (store product, packaging) — the bulk PUT upserts; at most 500 lines per call.
  Prices in VND should be whole numbers: payOS accepts only whole VND (decision C-D9 of
  `API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md`), so a fractional total must be settled partly in cash.
  The UI should warn on a fractional price; the API still accepts up to 2 decimals.
- Items may change while the list is ACTIVE; this affects carts and **new** orders only (order lines keep
  their snapshot). Each change is audited.
- At most one ACTIVE walk-in default list at any time (activating a second one → 422).
- Group ↔ price list (design §20): one active list per group; linking ends the previous link
  (`effective_to = effectiveFrom`) in the same transaction. Only DRAFT or ACTIVE lists can be linked.
- DELETE only DRAFT lists never linked to a group and never used by an order (`price_list_id_snapshot`);
  otherwise 409 → deactivate.
- Public catalog (existing `GET /api/catalog/products` and `/products/{id}`): each sale packaging gets a
  new field `price` from the ACTIVE walk-in default list (`null` if none). Adding a field is non-breaking.

**Price resolution** (real `IPriceResolver`, sales contract §9.1):
1. Farmer → current assignment, or the default group (B-D2).
2. Group → its current price-list link whose list is ACTIVE and inside its own validity at `at`.
3. No such list (or walk-in) → the ACTIVE walk-in default list (decision B-D3).
4. No list at all → `BusinessRuleException` "no applicable price list" (A turns this into 422).
5. Prices: `price_list_items.selling_price` per (store product, packaging); a missing pair = no price.

---

## 6. B4 — Credit

### 6.1 Credit tiers

| Method | Route | Roles | Body |
|---|---|---|---|
| GET | `/api/credit-tiers` (+ `/{id}`) | Operate | query: `isActive`, `search`, paging |
| POST | `/api/credit-tiers` | Manage | `{ code, name, description?, defaultCreditLimit, defaultPaymentTermDays }` |
| PUT | `/api/credit-tiers/{id}` | Manage | same without `code` |
| POST | `/api/credit-tiers/{id}/activate`, `/deactivate` | Manage | — |

`CreditTierResponse`: `id, code, name, description, defaultCreditLimit, defaultPaymentTermDays, isActive,
profileCount`. Code unique per store; values ≥ 0. Changing a tier never changes existing profiles' limits
(the profile is authoritative, design §44) nor confirmed orders' terms (snapshot).

### 6.2 Farmer credit profile

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/customers/{farmerProfileId}/credit` | Operate | — | `200 CreditSummaryResponse` / `404` no profile |
| POST | `/api/customers/{farmerProfileId}/credit` | Operate | `{ creditTierId, creditLimit?, note? }` | `201 CreditSummaryResponse` |
| PUT | `/api/customers/{farmerProfileId}/credit/limit` | Operate | `{ creditTierId?, creditLimit, reason }` | `200 CreditSummaryResponse` |
| POST | `/api/customers/{farmerProfileId}/credit/activate` | Manage | `{ reason }` | `200` |
| POST | `/api/customers/{farmerProfileId}/credit/suspend` | Manage | `{ reason }` | `200` |
| POST | `/api/customers/{farmerProfileId}/credit/block` | Manage | `{ reason }` | `200` |
| GET | `/api/customers/{farmerProfileId}/credit/history` | Operate | — | `200 CreditLimitHistoryResponse[]` |
| GET | `/api/customers/{farmerProfileId}/credit/reservations` | Operate | query: `activeOnly` | `200 CreditReservationResponse[]` |
| GET | `/api/me/credit` | FARMER | — | `200 CreditSummaryResponse` without `note`/`approvedBy` |

`CreditSummaryResponse`:

```json
{
  "profileId": "uuid", "farmerProfileId": "uuid", "status": "ACTIVE",
  "creditTier": { "id": "uuid", "code": "GOLD", "name": "Vàng" },
  "creditLimit": 50000000.00,
  "paymentTermDays": 30,
  "outstandingReceivable": 12000000.00,
  "reservedCredit": 8000000.00,
  "availableCredit": 30000000.00,
  "approvedBy": "uuid", "approvedAt": "…", "note": null, "version": 4
}
```

`paymentTermDays` = the tier's `defaultPaymentTermDays`; `outstandingReceivable` =
`debt_accounts.current_balance`; `reservedCredit` = Σ remaining of active credit reservations;
`availableCredit` = limit − outstanding − reserved (never stored, design §45).

Rules:
- Registered Farmers only; one profile per Farmer per store (409). Creating the profile also creates the
  Farmer's debt account (`ACTIVE`, balance 0) if it does not exist.
- `creditTierId` is required (it is the source of the payment term, decision B-D4); `creditLimit`
  defaults to the tier's default limit.
- Limit changes (rule 22: Admin, Owner, Sales) write `credit_limit_histories` + audit log; a limit below
  current exposure is allowed and only blocks new credit orders.
- Credit is usable only while the profile is ACTIVE and the debt account is ACTIVE.

### 6.3 Real `IOrderSettlementGuard` (sales contract §9.2)

Inside A's confirmation transaction:
1. FULL_PAYMENT: `IOrderPrepaymentLedger.GetPaidAmountAsync(order) ≥ order.TotalAmount`, else 422
   "payment does not cover the order total" (decision D3 of group A).
2. CREDIT: lock the credit profile row (`IRowLockService.LockCreditProfileAsync`, added by B4), require
   ACTIVE profile and debt account, `required = total − paid` (≥ 0); `FarmerCreditProfile.EnsureCanReserve`
   with fresh outstanding/reserved figures; create the `credit_reservations` row (one active per order) when
   `required > 0`; return `CreditTermDays = tier default`.
3. `ReleaseAsync`: release the unused remainder of the order's reservation (status derived, §35.3).

### 6.4 `ICreditReservationAdjuster` — provided by B4, called by C

```csharp
public interface ICreditReservationAdjuster
{
    // A payment for an already confirmed CREDIT order became PAID: release the same amount of the
    // order's unused credit reservation (design §XVI-D). Runs inside C's payment transaction.
    Task OnOrderPrepaymentAsync(Guid orderId, decimal paidAmount, Guid actorId, CancellationToken cancellationToken);
}
```

---

## 7. B5 — Debt

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/debt-accounts` | Operate | query: `search`, `hasOutstanding`, `overdueOnly`, paging | `200 PagedResult<DebtAccountListItem>` |
| GET | `/api/customers/{farmerProfileId}/debt` | Operate | — | `200 DebtAccountResponse` |
| GET | `/api/customers/{farmerProfileId}/debt/transactions` | Operate | query: `fromDate`, `toDate`, paging | `200 PagedResult<DebtTransactionResponse>` |
| GET | `/api/customers/{farmerProfileId}/debt/allocation-preview` | Operate | query: `amount` | `200 AllocationPreviewResponse` |
| POST | `/api/customers/{farmerProfileId}/debt/manual-entries` | Manage | `{ amount, dueDate, reason }` | `201 DebtEntryResponse` |
| GET | `/api/debt-entries` | Operate | query: `farmerProfileId`, `status`, `overdueOnly`, `dueFrom`, `dueTo`, `orderId`, paging | `200 PagedResult<DebtEntryListItem>` |
| GET | `/api/debt-entries/{id}` | Operate | — | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/dispute` | Operate | `{ reason }` | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/keep` | Operate | `{ reason }` | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/change-due-date` | Operate | `{ newDueDate, reason }` | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/adjust` | Manage | `{ amount, reason }` | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/cancel` | Manage | `{ reason }` | `200 DebtEntryResponse` |
| GET | `/api/me/debt` | FARMER | — | `200 DebtAccountResponse` |
| GET | `/api/me/debt-entries` (+ `/{id}`) | FARMER | query: `status`, paging | own entries only |
| POST | `/api/me/debt-entries/{id}/dispute` | FARMER | `{ reason }` | `200 DebtEntryResponse` |

`DebtAccountResponse`: `id, farmerProfileId, status, currentBalance, overdueAmount, openEntryCount,
oldestDueDate, lastTransactionAt, version`.

`DebtEntryResponse`:

```json
{
  "id": "uuid", "entryNumber": "DE-20261002-0001",
  "sourceType": "DELIVERY", "orderId": "uuid", "orderNumber": "OD-…",
  "deliveryId": "uuid | null", "deliveryAttemptId": "uuid | null", "sourceStockMovementId": "uuid | null",
  "fulfillmentValue": 20000000.00, "prepaymentAppliedAmount": 10000000.00,
  "originalAmount": 10000000.00, "outstandingAmount": 4000000.00,
  "dueDate": "2026-11-01", "isOverdue": false, "overdueDays": 0,
  "status": "PARTIALLY_PAID",
  "actions": [ { "id": "uuid", "actionType": "DISPUTE", "reason": "…", "adjustmentAmount": null,
                 "oldDueDate": null, "newDueDate": null, "createdBy": "uuid", "createdAt": "…" } ],
  "transactions": [ DebtTransactionResponse ],
  "createdAt": "…"
}
```

`DebtTransactionResponse`: `id, debtEntryId, transactionType, amountDelta, balanceAfter, occurredAt,
status, paymentAllocationId, salesReturnId, debtEntryActionId, note, createdBy`.

`AllocationPreviewResponse`: `{ amount, allocations: [ { debtEntryId, entryNumber, dueDate,
outstandingAmount, allocatedAmount } ], unallocatedAmount }` — oldest due date first (rule 27).

Rules (design §35.6, rule 51):
- `adjust.amount` > 0 is the amount to **reduce** (stored as negative ADJUSTMENT_OUT); ≤ outstanding.
  Increasing receivable is only via `manual-entries` (MANUAL_ADJUSTMENT entry + ADJUSTMENT_IN).
- `cancel` posts ADJUSTMENT_OUT for the full outstanding and ends the entry CANCELLED.
- DISPUTE marks the entry DISPUTED and does not block payments; KEEP/ADJUST/CANCEL resolve it.
- CHANGE_DUE_DATE records old and new due dates; the new date cannot be in the past.
- Every posting: lock/version the debt account, insert the transaction, update `current_balance`,
  update the entry outstanding and derived status, in one transaction. Posted transactions are never
  edited or deleted.

### 7.1 Real `IFulfillmentFinancialPosting` (sales contract §9.3)

Per call: `value = Σ FulfilledValue`; `prepayment = IOrderPrepaymentLedger.ConsumeAsync(order, value)`
(chronological, rule 25); `unpaid = value − prepayment`. If `unpaid > 0` (CREDIT orders only — a
FULL_PAYMENT order with unpaid value is a bug → exception): consume `unpaid` from the order's credit
reservation, `DebtAccount.CreateCreditSaleEntry` (source DELIVERY/PICKUP, due date = `FulfilledAt` Vietnam
day + `order.CreditTermDaysSnapshot`), CREDIT_SALE transaction. No `SaveChangesAsync`.

### 7.2 `IDebtRepaymentPosting` — provided by B5, called by C

```csharp
public interface IDebtRepaymentPosting
{
    // A DEBT_REPAYMENT payment became PAID (cash confirmed by staff or payOS webhook). Creates the DEBT
    // payment allocations (explicit list, or oldest due date first when null) and a PAYMENT debt
    // transaction per allocation. Any unallocated rest stays on the payment. Runs in C's transaction.
    Task<IReadOnlyList<DebtAllocationResult>> ApplyAsync(
        Payment payment, IReadOnlyList<RequestedDebtAllocation>? requested, Guid? actorId,
        CancellationToken cancellationToken);
}

public sealed record RequestedDebtAllocation(Guid DebtEntryId, decimal Amount);
public sealed record DebtAllocationResult(Guid DebtEntryId, Guid PaymentAllocationId, decimal Amount);
```

### 7.3 `IDebtReturnPosting` — provided by B5, called by C (returns)

```csharp
public interface IDebtReturnPosting
{
    // Reduces the unpaid debt attributable to the returned goods first (rule 32): open entries of the
    // same order, the entry of the same fulfillment source first, then oldest due date. Posts RETURN debt
    // transactions linked to the sales return. Returns the amount applied; the caller refunds the rest.
    Task<decimal> ApplyReturnAsync(Guid orderId, Guid salesReturnId, decimal returnValue, Guid actorId,
        Guid? sourceStockMovementId, CancellationToken cancellationToken);
}
```

`DebtAccount` has no return-posting method yet: B5 adds it to the Domain (RETURN transaction, negative
delta) with unit tests.

---

## 8. Interface B needs from C

```csharp
// Owner C (Payments). Reads/consumes ORDER payment allocations (prepayment pool, design §37).
public interface IOrderPrepaymentLedger
{
    Task<decimal> GetPaidAmountAsync(Guid orderId, CancellationToken cancellationToken);      // Σ active ORDER allocations of PAID payments
    Task<decimal> GetAvailableAsync(Guid orderId, CancellationToken cancellationToken);       // Σ (allocated − consumed)
    Task<decimal> ConsumeAsync(Guid orderId, decimal maxAmount, CancellationToken cancellationToken); // oldest first, returns consumed
}
```

Temporary implementation until C delivers (B registers it, removed in A7): `GetPaidAmountAsync` =
order total for FULL_PAYMENT and 0 for CREDIT; `ConsumeAsync` = 0. It is a development stand-in only and is
never enabled outside tests/dev.

All B interface implementations work inside the caller's transaction and never call `SaveChangesAsync`.

---

## 9. Test expectations

Each task: unit, offline HTTP (401/403 per role, 400), rolled-back real PostgreSQL. In addition:

| Task | Must prove |
|---|---|
| B1 | one default address, Farmer isolation (404 on others' ids), 10-address limit |
| B2 | one default group, assignment history (only one current), default-group fallback |
| B3 | resolution order (group list → walk-in default → none), validity windows, one walk-in default, item upsert, snapshot unaffected by later price edits |
| B4 | available credit formula with debt + reservations; two concurrent credit confirmations cannot exceed the limit; limit history + audit |
| B5 | design §XVI examples B, C and E (50M/20M/30M, prepayment 10M, repayment 10M + 20M); ADJUST/CANCEL/manual entry signs; balance never negative |

---

## 10. Decisions (proposed defaults — applied unless the team objects before B starts)

| # | Decision |
|---|---|
| B-D1 | Staff cannot create Farmer accounts yet (Farmers self-register; unregistered buyers are WALK_IN). Revisit with the Auth decisions pending with the mentor |
| B-D2 | A Farmer without a group assignment belongs to the store's default group; no assignment row is created at registration |
| B-D3 | A group without an applicable price list falls back to the walk-in default list |
| B-D4 | A credit profile requires a credit tier; the payment term of a credit order = tier `defaultPaymentTermDays`, snapshotted at confirmation |
| B-D5 | Roles: groups, price lists, credit tiers, credit status changes, debt ADJUST/CANCEL/manual entries = Manage; customer views, group assignment, credit limit (rule 22), DISPUTE/KEEP/CHANGE_DUE_DATE = Operate; Farmer only `/api/me/...` |
| B-D6 | Debt repayment money enters only through C's payment API; B never creates payments, only allocates them (`IDebtRepaymentPosting`) |
