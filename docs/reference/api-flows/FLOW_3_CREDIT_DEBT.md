# Flow L3 — Customer Groups, Credit Sale and Debt Collection (owner: Teammate 3)

Version 2.0 — 2026-10-03. Conventions, shared shapes, interfaces and ownership rules: [README.md](README.md).

### Additive credit/debt contract (2026-10-04 user request)

- `POST /api/customers/{id}/credit-eligibility` (Operate), `{ orderAmount }`: eligibility,
  reasonCode/message, limit, debt, reserved credit, available/after-order credit, overdue figures.
  Eligibility is checked when building CREDIT drafts, and rechecked under the customer row lock when
  confirming/reserving. Drafts remain editable and consume no credit; only confirmed commitments reserve.
- Existing credit administration routes remain authoritative. Term selection uses `creditTierId`;
  AllowCreditPurchase is derived from profile/customer/account ACTIVE status.
- `Credit:BlockCreditWhenOverdue` is an optional server policy (default false), using Vietnam dates.
- All exposure/ledger changes lock FarmerProfile first (also used by customer administration), then
  read profile/account/reservations. Shared steps never save. One debt per successful stock movement,
  protected by a filtered unique index, retains partial delivery/pickup support.
- Debt list adds search (name/phone/entry/order), created-date filters and sort
  (`CreatedAt`, `DueDate`, `OutstandingAmount`, `DaysOverdue`) with `descending`.
- Debt detail adds confirmed totalPaid, customer credit figures, order products and payment history.
- `GET /api/debt-entries/{id}/payments`, `/ledger` (Operate) expose histories.
- `POST /api/payments/bank-transfer` (Operate), `{ farmerProfileId, amount, paymentDate?, note?, reference? }`
  creates a DEBT_REPAYMENT Payment PENDING; no allocation or debt reduction yet.
  `POST /api/payments/{id}/confirm` and `/reject` (Manage); reject requires `{ reason }` and maps to FAILED.
  Confirmation allocates oldest due first under the customer lock. Repeated confirmation returns the
  PAID receipt without reposting. Staff-recorded cash continues through `/api/payments/cash`.
- `GET /api/debt-entries/dashboard` (Manage) returns outstanding/overdue/collections and top/recent lists.
- `GET /api/reports/debt-aging`, `/debt-collections` remain F3.6 routes.
- Return posting is idempotent per sales return, validates its order/store/source and posts RETURN ledger
  rows without modifying original debt. The return owner refunds any remainder.
- Migration CreditDebtPostingSafety extends only the payment enum check and adds unique source indexes.
Sources: `DATABASE_DESIGN.md` §7–8, §20, §44–51, §XVI, §XIX, §35.6–35.7, §35.20; `BUSINESS_RULES.md` rules 1–5,
22–29, 32.

L3 decides **who may buy on credit, how much and for how long**, records the debt when credit goods are handed
over and collects it. The customer type (customer group) drives both the price list and the credit tier, and the
tier's term is the debt term (decision F-D2).

Conventions specific to L3:
- Customer id = `farmerProfileId` (`/api/customers/{farmerProfileId}/...`).
- Every money-changing action (credit limit or tier change, debt adjust/cancel/manual entry) writes an
  `audit_logs` row.
- Overdue is **derived**, never stored: `isOverdue = dueDate < today (Vietnam) and outstanding > 0`,
  `overdueDays = today − dueDate`. No interest or penalty (rule 24).

---

## 1. Demo script

| # | Step (screen) | API | Effect |
|---|---|---|---|
| 1 | Owner creates credit tiers (e.g. NEW 0 days / REGULAR 30 days / LOYAL 60 days) | `POST /api/credit-tiers` | tiers with default limit and term |
| 2 | Owner creates customer groups and links each to a price list and a credit tier | `POST /api/customer-groups`, `PUT /{id}/price-list`, `PUT /{id}/credit-tier` | group → prices + debt term |
| 3 | Sales moves a farmer into "Khách quen" | `PUT /api/customers/{id}/group` | assignment history; credit profile tier follows the group |
| 4 | Sales opens the farmer's credit profile | `POST /api/customers/{id}/credit` | tier from the group, limit = tier default, debt account created |
| 5 | A credit order above the available credit is refused; a smaller one is confirmed (L1) | `POST /api/orders/{id}/confirm` | credit reservation, term snapshotted |
| 6 | Goods delivered in two trips (L2) | attempts complete | one debt entry per fulfillment, due = day + term |
| 7 | Owner sees overdue debts | `GET /api/debt-entries?overdueOnly=true`, `GET /api/reports/debt-aging` | |
| 8 | Farmer pays cash at the counter, choosing entries; later pays online | `POST /api/payments/cash` (DEBT_REPAYMENT), `POST /api/me/payments/payos` | PAYMENT debt transactions, oldest due first online |
| 9 | A disputed entry is kept, another gets a new due date, a wrong one is adjusted | `/dispute`, `/keep`, `/change-due-date`, `/adjust` | debt actions + ledger |

---

## 2. Tasks

| Task | Content | Depends on | Delivers to |
|---|---|---|---|
| F3.1 | Customer groups, assignment, group ↔ price list links | — (F1.1 reads the links) | F1.1 group prices |
| F3.2 | Credit tiers, group default tier, credit profiles and limits, tier follows the group | F0.2 migration, F3.1 | F3.3 |
| F3.3 | Real `IOrderSettlementGuard` and `ICreditReservationAdjuster` | M1 (`IOrderPrepaymentLedger`) | F1.4 credit confirmation, F1.3/F2.4 |
| F3.4 | Debt ledger, debt actions, real `IFulfillmentFinancialPosting` | F3.3; end-to-end after M2 | F1.5, F2.6 |
| F3.5 | Real `IDebtRepaymentPosting`, allocation preview, real `IDebtReturnPosting` | F3.4 | F1.3 cash repayment, F2.4 online repayment, F4.4 |
| F3.6 | **New** debt reports | F3.5 | — |

---

## 3. F3.1 — Customer groups and price-list links

The additive customer directory, staff creation/editing and customer history contract is documented in
[CUSTOMER_MANAGEMENT.md](CUSTOMER_MANAGEMENT.md). It uses the same FarmerProfile identity and group/credit rules.

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
| PUT | `/api/customer-groups/{id}/price-list` | Manage | `{ priceListId, effectiveFrom? }` | `200 CustomerGroupResponse` |
| GET | `/api/customer-groups/{id}/price-lists` | Operate | — | `200 GroupPriceListResponse[]` (history) |

`CustomerGroupResponse`: `id, code, name, description, priority, isDefault, isActive, memberCount,
currentPriceList {id, code, name} | null, defaultCreditTier {id, code, name} | null (F3.2), createdAt`.

`GroupAssignmentResponse`: `id, customerGroup {id, code, name}, effectiveFrom, effectiveTo, assignedBy,
reason`.

`GroupPriceListResponse`: `id, priceList {id, code, name, status}, effectiveFrom, effectiveTo`.

Rules:
- `code` unique per store (case-insensitive, 409), immutable after creation.
- Exactly one active default group per store: `set-default` moves the flag; the default cannot be
  deactivated or deleted (422).
- Deactivating a group that still has current members → 422 (move them first).
- DELETE only if the group was never assigned and never linked to a price list or credit tier (otherwise 409 →
  deactivate).
- Assignment (rules 3–4): ends the current assignment (`effective_to = now`) and creates a new one in one
  transaction; assigning the current group again is a no-op `200`. Inactive group → 422. From F3.2 on, the same
  transaction moves the credit profile to the new group's tier (§4.3).
- A Farmer with no assignment belongs to the **default group** (decision B-D2); no row is created at
  registration.
- Group ↔ price list (design §20): one active list per group; linking ends the previous link
  (`effective_to = effectiveFrom`) in the same transaction. Only DRAFT or ACTIVE lists can be linked. The price
  lists themselves and the resolution algorithm belong to L1 (FLOW_1 §3); F1.1's resolver reads these links.

---

## 4. F3.2 — Credit tiers, group tier and credit profiles

Needs the migration `CustomerGroupDefaultCreditTier` (task F0.2, design §35.20): `customer_groups.default_credit_tier_id`.

### 4.1 Credit tiers

| Method | Route | Roles | Body |
|---|---|---|---|
| GET | `/api/credit-tiers` (+ `/{id}`) | Operate | query: `isActive`, `search`, paging |
| POST | `/api/credit-tiers` | Manage | `{ code, name, description?, defaultCreditLimit, defaultPaymentTermDays }` |
| PUT | `/api/credit-tiers/{id}` | Manage | same without `code` |
| POST | `/api/credit-tiers/{id}/activate`, `/deactivate` | Manage | — |

`CreditTierResponse`: `id, code, name, description, defaultCreditLimit, defaultPaymentTermDays, isActive,
profileCount, groups [{id, code, name}]`. Code unique per store; values ≥ 0. Changing a tier never changes
existing profiles' limits (the profile is authoritative, design §44) nor confirmed orders' terms (snapshot).
Deactivating a tier that is still the default tier of a group → 422 (unlink it first).

### 4.2 Group default tier (new, decision F-D2)

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| PUT | `/api/customer-groups/{id}/credit-tier` | Manage | `{ creditTierId: "uuid | null" }` | `200 CustomerGroupResponse` |

- The tier must be ACTIVE and of the same store (422); `null` removes the link. Audited.
- Changing a group's tier does **not** change the profiles of its current members (the profile is authoritative,
  §44); the next group change of a member, or an explicit limit/tier change, applies it.

### 4.3 Farmer credit profile

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/customers/{farmerProfileId}/credit` | Operate | — | `200 CreditSummaryResponse` / `404` no profile |
| POST | `/api/customers/{farmerProfileId}/credit` | Operate | `{ creditTierId?, creditLimit?, note? }` | `201 CreditSummaryResponse` |
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

`CreditLimitHistoryResponse`: `id, oldCreditTier {id, code, name} | null, newCreditTier, oldCreditLimit,
newCreditLimit, reason, changedBy, changedAt`.

`CreditReservationResponse`: `id, orderId, orderNumber, reservedAmount, consumedAmount, releasedAmount,
remainingAmount, status, createdAt`.

Rules:
- Registered Farmers only; one profile per Farmer per store (409). Creating the profile also creates the
  Farmer's debt account (`ACTIVE`, balance 0) if it does not exist.
- Tier of a new profile (decision F-D2): `creditTierId` from the request → default tier of the Farmer's current
  group → default tier of the store's default group → 422 "choose a credit tier". The tier is the source of the
  payment term (decision B-D4); `creditLimit` defaults to the tier's default limit.
- **The tier follows the customer type:** when `PUT /api/customers/{id}/group` moves a Farmer who has a profile to
  a group whose default tier differs from the profile's tier, the same transaction calls
  `FarmerCreditProfile.ChangeCreditLimit(currentLimit, newTierId, "Customer group changed: A → B", …)` (history row
  written by the Domain) and an audit log. The personal limit is unchanged. A group without a default tier leaves
  the profile's tier as it is.
- Limit changes (rule 22: Admin, Owner, Sales) write `credit_limit_histories` + audit log; a limit below
  current exposure is allowed and only blocks new credit orders.
- Credit is usable only while the profile is ACTIVE and the debt account is ACTIVE.
- Confirmed orders keep their snapshotted term whatever happens to the tier or the group later.

---

## 5. F3.3 — Settlement guard and credit reservation adjuster

**Real `IOrderSettlementGuard`** (README §4.4), inside L1's confirmation transaction:
1. FULL_PAYMENT: `IOrderPrepaymentLedger.GetPaidAmountAsync(order) ≥ order.TotalAmount`, else 422
   "payment does not cover the order total" (decision D3).
2. CREDIT: lock the credit profile row (`IRowLockService.LockCreditProfileAsync`, added by F3.3), require
   ACTIVE profile and debt account, `required = total − paid` (≥ 0); `FarmerCreditProfile.EnsureCanReserve`
   with fresh outstanding/reserved figures; create the `credit_reservations` row (one active per order) when
   `required > 0`; return `CreditTermDays` = the profile tier's `defaultPaymentTermDays` (decision B-D4).
3. `ReleaseAsync`: release the unused remainder of the order's reservation (status derived, §35.3).

**Real `ICreditReservationAdjuster`** (README §4.5): a payment for an already confirmed CREDIT order became PAID →
release the same amount of the order's unused credit reservation (design §XVI-D). Called by L1's
`PaymentAllocator`.

---

## 6. F3.4 — Debt ledger, debt actions and fulfillment posting

| Method | Route | Roles | Body | Response |
|---|---|---|---|---|
| GET | `/api/debt-accounts` | Operate | query: `search`, `hasOutstanding`, `overdueOnly`, paging | `200 PagedResult<DebtAccountListItem>` |
| GET | `/api/customers/{farmerProfileId}/debt` | Operate | — | `200 DebtAccountResponse` |
| GET | `/api/customers/{farmerProfileId}/debt/transactions` | Operate | query: `fromDate`, `toDate`, paging | `200 PagedResult<DebtTransactionResponse>` |
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

`DebtAccountListItem`: `id, farmerProfileId, fullName, phoneNumber, customerGroup {id, code, name},
currentBalance, overdueAmount, oldestDueDate`.

`DebtEntryListItem`: `id, entryNumber, farmerProfileId, fullName, sourceType, orderNumber, originalAmount,
outstandingAmount, dueDate, isOverdue, overdueDays, status`.

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

Rules (design §35.6, rule 51):
- Document number `DE-yyyyMMdd-NNNN` (`entry_number` is unique).
- `adjust.amount` > 0 is the amount to **reduce** (stored as negative ADJUSTMENT_OUT); ≤ outstanding.
  Increasing receivable is only via `manual-entries` (MANUAL_ADJUSTMENT entry + ADJUSTMENT_IN).
- `cancel` posts ADJUSTMENT_OUT for the full outstanding and ends the entry CANCELLED.
- DISPUTE marks the entry DISPUTED and does not block payments; KEEP/ADJUST/CANCEL resolve it.
- CHANGE_DUE_DATE records old and new due dates; the new date cannot be in the past.
- Every posting: lock/version the debt account, insert the transaction, update `current_balance`,
  update the entry outstanding and derived status, in one transaction. Posted transactions are never
  edited or deleted.

**Real `IFulfillmentFinancialPosting`** (README §4.6), called by L1's `FulfillmentPostingService` for pickups and
L2's deliveries: `value = Σ FulfilledValue`; `prepayment = IOrderPrepaymentLedger.ConsumeAsync(order, value)`
(chronological, rule 25); `unpaid = value − prepayment`. If `unpaid > 0` (CREDIT orders only — a FULL_PAYMENT
order with unpaid value is a bug → exception): consume `unpaid` from the order's credit reservation,
`DebtAccount.CreateCreditSaleEntry` (source DELIVERY/PICKUP, due date = `FulfilledAt` Vietnam day +
`order.CreditTermDaysSnapshot`), CREDIT_SALE transaction. No `SaveChangesAsync`.

---

## 7. F3.5 — Debt repayment and return posting

| Method | Route | Roles | Query | Response |
|---|---|---|---|---|
| GET | `/api/customers/{farmerProfileId}/debt/allocation-preview` | Operate | `amount` | `200 AllocationPreviewResponse` |
| GET | `/api/me/debt/allocation-preview` | FARMER | `amount` | `200 AllocationPreviewResponse` (own debt, before an online repayment) |

`AllocationPreviewResponse`: `{ amount, allocations: [ { debtEntryId, entryNumber, dueDate,
outstandingAmount, allocatedAmount } ], unallocatedAmount }` — oldest due date first (rule 27).

The money itself enters only through payments (decision B-D6): cash at the counter with L1's
`POST /api/payments/cash` (`DEBT_REPAYMENT`, optional explicit entries), online with L2's payOS routes (oldest due
first, decision C-D1). L3 never creates payments.

**Real `IDebtRepaymentPosting`** (README §4.7): creates the DEBT payment allocations (explicit list, or oldest due
date first when null) and one PAYMENT debt transaction per allocation, through the debt account. Explicit
allocations must reference open entries of the payer and not exceed their outstanding (422).

**Real `IDebtReturnPosting`** (README §4.8), called by L4's return inspection: reduces the unpaid debt attributable
to the returned goods first (rule 32) — open entries of the same order, the entry of the same fulfillment source
first, then oldest due date — with RETURN debt transactions linked to the sales return; returns the amount
applied. `DebtAccount` has no return-posting method yet: F3.5 adds it to the Domain (RETURN transaction, negative
delta) with unit tests.

---

## 8. F3.6 — Debt reports (new, `Manage`)

| Method | Route | Query | Response |
|---|---|---|---|
| GET | `/api/reports/debt-aging` | `asOf` (Vietnam day, default today), `customerGroupId?` | `200 DebtAgingReportResponse` |
| GET | `/api/reports/debt-collections` | `fromDate`, `toDate` (≤ 366 days), `groupBy` = `DAY` (default) \| `METHOD` \| `STAFF` | `200 DebtCollectionReportResponse` |
| GET | `/api/reports/debt-by-customer-group` | — | `200 DebtByGroupReportResponse` |

`DebtAgingReportResponse`:

```json
{
  "asOf": "2026-10-31",
  "rows": [ { "farmerProfileId": "uuid", "fullName": "…", "phoneNumber": "…",
              "customerGroup": { "id": "uuid", "code": "REGULAR", "name": "Khách quen" },
              "notDue": 5000000.00, "days1To30": 2000000.00, "days31To60": 0.00,
              "days61To90": 0.00, "over90": 0.00, "total": 7000000.00 } ],
  "totals": { "notDue": 5000000.00, "days1To30": 2000000.00, "days31To60": 0.00,
              "days61To90": 0.00, "over90": 0.00, "total": 7000000.00 }
}
```

Outstanding per entry at `asOf` = Σ `amount_delta` of its POSTED debt transactions up to the end of that day
(ledger, so a past date gives the past picture); bucket by `asOf − dueDate`.

`DebtCollectionReportResponse`: `rows [{ key, label, paymentCount, collectedAmount }]`, `totals` — collected =
PAYMENT debt transactions in the period; `METHOD` = CASH / PAYOS of the payment; `STAFF` = the payment's
`confirmed_by` (payOS webhooks under `PAYOS`).

`DebtByGroupReportResponse`: `rows [{ customerGroup {id, code, name}, customersWithDebt, outstanding,
overdueAmount, totalCreditLimit, utilization }]` with `utilization = outstanding ÷ totalCreditLimit` (null when
the limit total is 0).

---

## 9. Tests — must prove

| Task | Must prove |
|---|---|
| F3.1 | one default group, assignment history (only one current), default-group fallback, group ↔ price list history (one current link) |
| F3.2 | tier resolution request → group → default group → 422; a group change moves the profile tier (limit unchanged, history + audit); a group without tier keeps the tier; a tier used by a group cannot be deactivated; available credit formula with debt + reservations; limit history + audit |
| F3.3 | two concurrent credit confirmations cannot exceed the limit; FULL_PAYMENT without enough payment refused; prepayment before delivery shrinks the reservation (§XVI-D) |
| F3.4 | design §XVI examples B and C (50M/20M/30M, prepayment 10M); due date = fulfillment day + tier days; ADJUST/CANCEL/manual entry signs; balance never negative |
| F3.5 | design §XVI-E (10M + 20M); explicit allocations; return posting reduces the right entries first |
| F3.6 | aging buckets at a past `asOf`, collections per method, group totals |

---

## 10. Decisions that apply to L3

| # | Decision |
|---|---|
| B-D2 | A Farmer without a group assignment belongs to the store's default group; no assignment row is created at registration |
| B-D3 | A group without an applicable price list uses the walk-in default list; one order snapshots one price list |
| B-D4 | A credit profile requires a credit tier; the payment term of a credit order = tier `defaultPaymentTermDays`, snapshotted at confirmation |
| B-D5 | Roles: groups, price-list links, group tier, credit tiers, credit status changes (activate/suspend/block), debt ADJUST/CANCEL/manual entries = Manage; customer group assignment, credit profile creation and limit changes (rule 22), DISPUTE/KEEP/CHANGE_DUE_DATE = Operate; Farmers only `/api/me/...` |
| B-D6 | Debt repayment money enters only through payments (L1 cash, L2 payOS); L3 never creates payments, only allocates them |
| C-D1 | payOS debt repayments are allocated oldest due date first (no entry selection online) |
| F-D2 | Debt term depends on the customer type: group → default credit tier → term days. No crop seasons |
| F-D5 | Reports are Manage-only and owned by the flow whose data they read |
