# Flow L3 — Customer Groups, Credit Sale and Debt Collection (owner: Teammate 3)

Version 2.1 — 2026-11-03 (debt policy, owner-only actions; see the two sections below). Version 2.0 — 2026-10-03.
Conventions, shared shapes, interfaces and ownership rules: [README.md](README.md).

### Debt policy (approved by the mentor, 2026-11-03)

- **The system never blocks or limits a customer's credit on its own. Every credit suspension or block is done by the
  store owner, with a reason.** ("Hệ thống không bao giờ tự khóa hay tự hạn chế tín dụng của khách. Mọi việc khóa
  hoặc ngưng tín dụng đều do Chủ thực hiện và có ghi lý do.")
- No late fee, penalty or interest (rule 24). Overdue only drives reminders and the owner's follow-up.
- Debt is repaid by crop season; extensions are allowed; forgiving debt ("xóa nợ") is a real forgiveness, recorded with
  a reason, never a deleted row. The crop-season design is pending (F3.7, F3.8+): until it ships, the due date is the
  credit-tier term (F-D2 below).
- Overdue debt still counts against the credit limit (the limit is set by the owner). An order above the available
  credit is refused as `INSUFFICIENT_CREDIT` with `availableCredit`, never as an overdue or locked customer.

### Owner-only actions (F3.21, 2026-11-03)

Nine actions change money owed or the customer's credit standing. Only ADMIN or the STORE_OWNER of the active store
may take them, and they **cannot be delegated**: a permission granted to Sale staff (override, role default or
inheritance) is refused with `403` and nothing is written. The check is on the server, on every path, including the
customer form (`PUT /api/customers/{id}` switching `allowCreditPurchase`).

| Action | Route | Permission |
|---|---|---|
| Reduce a debt | `POST /api/debt-entries/{id}/adjust` | `DEBT.ADJUST` |
| Forgive a debt | `POST /api/debt-entries/{id}/cancel` | `DEBT.CANCEL` |
| Manual debt entry | `POST /api/customers/{farmerProfileId}/debt/manual-entries` | `DEBT.MANUAL` |
| Change a due date | `POST /api/debt-entries/{id}/change-due-date` (**Operate → Manage, 2026-11-03**) | `DEBT.DUE_DATE` |
| Confirm / reject a bank transfer | `POST /api/payments/{id}/confirm`, `/reject` | `BANK_PAYMENTS.CONFIRM`, `.REJECT` |
| Suspend / block / reactivate credit | `POST /api/customers/{farmerProfileId}/credit/suspend`, `/block`, `/activate` | `CREDIT.SUSPEND`, `.BLOCK`, `.ACTIVATE` |

Sale staff still record bank transfers (PENDING, no debt reduction), dispute or keep a debt entry, collect cash and
sell on credit within the limit. Until F3.21 part 2, the permission screen may still list these nine codes as
grantable; such grants have no effect.

### Additive credit/debt contract (2026-10-04 user request)

- `POST /api/customers/{id}/credit-eligibility` (Operate), `{ orderAmount }`: eligibility,
  reasonCode/message, limit, debt, reserved credit, available/after-order credit, overdue figures.
  Eligibility is checked when building CREDIT drafts, and rechecked under the customer row lock when
  confirming/reserving. Drafts remain editable and consume no credit; only confirmed commitments reserve.
- Existing credit administration routes remain authoritative. Term selection uses `creditTierId`;
  AllowCreditPurchase is derived from profile/customer/account ACTIVE status.
- Overdue debt (Vietnam dates) is reported in `hasOverdueDebt` / `overdueAmount` but never refuses credit by itself
  (debt policy above). The former `Credit:BlockCreditWhenOverdue` switch and the `OVERDUE_DEBT` reason were removed
  on 2026-11-03.
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
| POST | `/api/customers/{farmerProfileId}/debt/manual-entries` | Manage | `{ amount, dueDate?, reason, cropSeasonId? }` (F3.13: `dueDate` may be left out with a season) | `201 DebtEntryResponse` |
| GET | `/api/debt-entries` | Operate | query: `farmerProfileId`, `status`, `overdueOnly`, `dueFrom`, `dueTo`, `orderId`, paging | `200 PagedResult<DebtEntryListItem>` |
| GET | `/api/debt-entries/{id}` | Operate | — | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/dispute` | Operate | `{ reason }` | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/keep` | Operate | `{ reason }` | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/change-due-date` | Manage (owner-only, not delegable; was Operate until 2026-11-03) | `{ newDueDate, reason, reasonCode }` (F3.14; reasonCode required since A1, 2026-11-05) | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/adjust` | Manage | `{ amount, reason, reasonCode }` (F3.14; reasonCode required) | `200 DebtEntryResponse` |
| POST | `/api/debt-entries/{id}/cancel` | Manage | `{ reason, reasonCode }` (F3.14; reasonCode required) | `200 DebtEntryResponse` |
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

## 8b. F3.11 — Crop seasons for debt collection (new, 2026-11-03)

A season is the store's debt-collection calendar entry (CS-04): credit orders confirmed in its order window
`[orderWindowStart, harvestStart)` belong to it, and their debt is due on `paymentDueDate = harvestEnd + graceDays`.
Seasons chain without gap or overlap: only the first season takes `orderWindowStart`; each later one opens on the
previous `harvestStart`, and a season is only added after the last one. `status` is derived from today's Vietnam date
(PLANNED → OPEN → HARVESTING (harvest end day included) → COLLECTING → CLOSED); only closing is stored (F3.19).
Inventory seasonal rules stay a separate calendar; `seasonalRuleId` only links one for shared reporting.

| Method | Route | Role / permission | Body / query | Response |
|---|---|---|---|---|
| GET | `/api/crop-seasons` | Operate / `CROP_SEASONS.READ` | `year?` (harvest start year), `status?`, `page`, `pageSize` | `200 PagedResult<CropSeasonResponse>`, newest harvest first |
| GET | `/api/crop-seasons/{id}` | Operate / `CROP_SEASONS.READ` | — | `200 CropSeasonResponse` / `404` |
| POST | `/api/crop-seasons` | Manage, owner-only / `CROP_SEASONS.MANAGE` | `{ code, name, harvestStart, harvestEnd, graceDays?, orderWindowStart?, cropType?, seasonalRuleId? }` | `201 CropSeasonResponse`; `409` duplicate code; `422` gap, overlap or missing first `orderWindowStart` |
| PUT | `/api/crop-seasons/{id}` | Manage, owner-only / `CROP_SEASONS.MANAGE` | `{ name, harvestStart, harvestEnd, graceDays, version, seasonalRuleId?, applyNewDueDateToCollectingDebts?, reason? }` | `200 CropSeasonResponse`; `409` stale `version`; `422` closed season or harvest past the next season |
| GET | `/api/me/crop-seasons/current` | Farmer / `MY_CROP_SEASONS.READ` | — | `200 MyCurrentCropSeasonResponse` |

```json
CropSeasonResponse {
  "id": "uuid", "code": "DX-2026", "name": "Đông Xuân 2026–2027", "cropType": "RICE",
  "orderWindowStart": "2026-10-01", "harvestStart": "2027-02-15", "harvestEnd": "2027-03-15",
  "graceDays": 30, "paymentDueDate": "2027-04-14", "status": "OPEN", "seasonalRuleId": null,
  "closedAt": null, "version": 0,
  "originated": { "outstandingAmount": 2000000, "openEntryCount": 2 },
  "collecting": { "outstandingAmount": 1000000, "openEntryCount": 1 }
}
MyCurrentCropSeasonResponse {
  "cropSeasonId": "uuid|null", "code": "DX-2026|null", "name": "…|null", "harvestStart": "…|null", "harvestEnd": "…|null",
  "expectedDueDate": "2027-04-14|null", "dueRule": "SEASON|CAPPED|TERM_DAYS|NONE", "termDays": 30|null, "isEstimate": true
}
```

- `originated` = open debt that arose in the season (reports); `collecting` = open debt collected in it now (it moves
  there on rollover; used to close the season).
- Changing `harvestStart` moves the next season's `orderWindowStart` in the same transaction. Orders and debts keep
  their origin season.
- By default recorded debt keeps its due date (D17). `applyNewDueDateToCollectingDebts: true` (owner, `reason`
  required) moves entries collected in this season that are still on its old due date to the new one, only later,
  each with a `CHANGE_DUE_DATE` action and audit. **Disputed entries are not moved** (the owner decides those), nor paid or
  cancelled ones. It runs under the store lock and locks every Farmer concerned in id order, then reads the entries, so a
  payment, adjustment, dispute or rollover running at that moment is never overwritten or missed (F3.20 review).
  The `200` response of that update carries `debtExtension: { moved, skippedDisputed }` (only then; `null` otherwise, and on a plain
  read): how many entries got the new date and how many disputed ones were left on the old date for the owner to decide. The audit
  `CROP_SEASON_UPDATED` records both numbers (`debtsMovedToNewDueDate`, `debtsLeftBecauseDisputed`).
- `MyCurrentCropSeasonResponse` is an estimate shown before ordering: the debt and its real due date are created at
  delivery. `CAPPED` = the store's optional term cap applied; `TERM_DAYS` = no season covers today, the credit-tier term
  applies; `NONE` = no credit profile.
- Writes are audited (`CROP_SEASON_CREATED`, `CROP_SEASON_UPDATED`, `DEBT_CHANGE_DUE_DATE`).

## 8c. F3.12 — Store debt policy (new, 2026-11-03)

One policy per store (`store_debt_policies`): the default grace of new seasons (CS-05), reminder offsets (CS-09, D10),
rollover limits (CS-08, D5), the on-time seasons before suggesting a higher limit (CS-14) and the optional term cap (D4).
A store that has not saved one reads the documented defaults (`isDefault: true`, `version: 0`); the first save creates
its row.

| Method | Route | Role / permission | Body | Response |
|---|---|---|---|---|
| GET | `/api/debt-policy` | Operate / `DEBT_POLICY.READ` | — | `200 DebtPolicyResponse` |
| PUT | `/api/debt-policy` | Manage, owner-only / `DEBT_POLICY.MANAGE` | `UpdateDebtPolicyRequest` (whole policy) | `200 DebtPolicyResponse`; `400` invalid value; `409` stale `version`; `403` Sale staff |

```json
DebtPolicyResponse {
  "defaultGraceDays": 30, "reminderOffsets": { "n2": -7, "n4": 14, "n5": 30 },
  "staffRolloverLimit": 1, "ownerReviewAfterRollovers": 2, "onTimeSeasonsForLimitRaise": 2,
  "maxCreditTermDays": null, "maxTotalExtensionDays": 365, "version": 0, "isDefault": true
}
UpdateDebtPolicyRequest {
  "defaultGraceDays": 45, "reminderOffsets": { "n2": -10, "n4": 7, "n5": 21 },
  "staffRolloverLimit": 1, "ownerReviewAfterRollovers": 2, "onTimeSeasonsForLimitRaise": 2,
  "maxCreditTermDays": null, "maxTotalExtensionDays": 365, "version": 0, "reason": "…|null"
}
```

- Rules (`400` from the request validator, `422` from the Domain if it is bypassed): `defaultGraceDays` 0–365;
  `n2 < 0 < n4 < n5` (days from the due date; N1 = harvest start and N3 = due date need no setting);
  `staffRolloverLimit ≥ 0`; `ownerReviewAfterRollovers ≥ max(staffRolloverLimit, 1)`; `onTimeSeasonsForLimitRaise ≥ 1`;
  `maxCreditTermDays` null (off) or ≥ 1; `maxTotalExtensionDays ≥ 1` (required, default 365); `reason` ≤ 1000.
- `version` is the one last read. The first save also needs `version: 0` and returns `version: 1` when a value
  changed, so an editor still holding the default gets `409`. Saving the same values changes nothing and is not audited.
- Changing `defaultGraceDays` only proposes the grace of seasons created later; existing seasons keep theirs.
- Audited as `DEBT_POLICY_CHANGED` (`STORE_DEBT_POLICY`, old and new values, optional reason).

## 8d. F3.14 — Rollover of one entry and reason codes (new, 2026-11-03)

A rollover moves an unpaid entry to a later season: only its collection season and due date change (D9). The amount, the
ledger, the origin season and the original due date stay; `rolloverCount` grows by one.

| Method | Route | Role / permission | Body | Response |
|---|---|---|---|---|
| POST | `/api/debt-entries/{id}/rollover` | Operate / `DEBT.ROLLOVER` (delegable) | `{ reasonCode, note, targetCropSeasonId?, overrideExtensionLimit? }` | `200 DebtEntryResponse`; `400` missing/unknown/non-hardship code or note; `403` staff past the rollover limit; `404`; `422` see below |

- `reasonCode` ∈ `MAT_MUA`, `THIEN_TAI`, `KHO_KHAN`, `KHAC` (hardship only); `note` is always required.
- No `targetCropSeasonId`: the next open season (by harvest start) whose `paymentDueDate` is later than the entry's due
  date. The new due date is the target season's `paymentDueDate` (no term cap on a rollover).
- Who (CS-08): Sale staff while `rolloverCount < staffRolloverLimit` of the debt policy (first rollover by default);
  then only the owner (`403` for staff). ADMIN and STORE_OWNER may always roll over.
- Total extension (D5): new due date − original due date ≤ `maxTotalExtensionDays` (365). Staff past it → `422`. The
  owner past it → `422` unless `overrideExtensionLimit: true` (recorded in the audit).
- `422`: no later open season, target season of another store or closed, same season or not a later due date, entry
  disputed, paid or cancelled.
- Concurrent rollovers of one entry are serialized per Farmer: one succeeds, the other gets `422`.
- Audited as `DEBT_ROLLOVER` (old and new due date and season, `rolloverCount`, `originalDueDate`, `reasonCode`,
  `overrideExtensionLimit`).

**Reason codes on the owner actions (D7, A1 - mandatory since 2026-11-05).** `adjust`, `cancel` and `change-due-date` require a
`reasonCode` (the 7 codes: `MAT_MUA`, `THIEN_TAI`, `KHO_KHAN`, `TRI_AN`, `SAI_SOT`, `KHONG_THU_HOI`, `KHAC`); missing or unknown
-> `400` (and `422` when the service is called without one). `PUT /api/crop-seasons/{id}` with `applyNewDueDateToCollectingDebts: true`
also requires `reasonCode` (it changes the due date of each entry); every moved entry stores it. `change-due-date` stays owner-only, never in the past, does not
count as a rollover, and records `exceedsTotalExtension` in its audit when it goes past the limit.

`DebtActionResponse` adds `reasonCode`, `isPrePolicy` (a debt change, i.e. adjust, cancel, due date or rollover,
recorded without a code: "Trước khi áp dụng chính sách"; since the code is mandatory, only changes made before 2026-11-05 show it), `oldCropSeasonId`, `newCropSeasonId`.

**Farmer notifications** (once per action, through the audit outbox): `DEBT_ROLLOVER` and `DEBT_CHANGE_DUE_DATE` →
`DEBT_DUE_DATE_CHANGED`; `DEBT_ADJUST` → `DEBT_REDUCED`; `DEBT_CANCEL` → `DEBT_CANCELLED`; `DEBT_KEEP` →
`DEBT_DISPUTE_RESOLVED`. The owner's season due-date move (8b) notifies through the same `DEBT_CHANGE_DUE_DATE`.

## 8e. F3.17 — Debt by season: entries, Farmer groups, dashboard (new, 2026-11-03)

Fields are only added; existing fields and routes stay. `DebtSeasonReference = { id, code, name }`.

**Entries** (`GET /api/debt-entries`, `/api/customers/{id}/debt/entries`, `/api/me/debt-entries`, and the `/{id}` details)
add `originSeason` (where the debt arose, never changes), `collectionSeason` (where it is collected now), `originalDueDate`,
`rolloverCount`, `isRolledOver`, `dueState`, `needsDecision`. New list filters: `originSeasonId`, `collectionSeasonId`,
`dueState`, `rolledOverOnly`, `needsDecision` (`400` on an unknown `dueState` or an empty id). The Farmer routes still
force the caller's own customer.

- `dueState` (Vietnam date): `OVERDUE` due before today; `DUE_SOON` due today up to 7 days ahead; `NOT_DUE` later; `null`
  when nothing is left to pay.
- `needsDecision` ("Cần quyết định"): still owed, not disputed, and either due + N5 ≤ today or `rolloverCount ≥
  ownerReviewAfterRollovers` (both from the debt policy, defaults 30 and 2).
- Actions add `actorName` and `actorRole` (CS-13).

**Farmer debt** (`GET /api/me/debt`, also `GET /api/customers/{id}/debt`) adds `groups[]`: open debt by (collection season,
due date), earliest first: `{ collectionSeason|null, dueDate, outstandingAmount, entryCount, dueState }`. No season = the
credit-tier term.

**Dashboard** (`GET /api/debt-entries/dashboard`) adds:
- `currentSeason` + `currentSeasonOutstanding`: the open season collected next (earliest `paymentDueDate` not yet past)
  and the open debt collected in it; `null` / `0` without one.
- `dueSoonAmount`: open debt due today up to 7 days ahead.
- `needsDecisionCount` + `needsDecision[]` (first 20 by due date, `DebtEntryListItem`).
- `creditLimitAlertCount` + `creditLimitAlerts[]` (first 20, fullest first): active credit customers whose used credit
  (outstanding debt + remaining active reservations) reaches 90 % of the limit:
  `{ farmerProfileId, fullName, phoneNumber, creditLimit, usedCredit, availableCredit, state: NEAR_LIMIT|AT_LIMIT }`. The
  owner decides: raise the limit, reduce the debt or collect part of it. Nothing changes the limit automatically.

F3.17 receipt (2026-11-06): the payment detail of a `DEBT_REPAYMENT` payment (`GET /api/payments/{id}`, `/api/me/payments/{id}`, the receipt returned by cash / bank debt payments) has `outstandingBySeason[]`: `{ seasonId, seasonCode, seasonName, paidAmount, outstandingNow }` per season that now collects debt this payment paid or the customer still owes (a null season = debt of no season, last). `paidAmount` = what this payment allocated (active allocations); `outstandingNow` = what the customer owes there at the time of the read, not right after the payment. `null` on other payments. Read-only, no schema change.

## 8f. F3.15 — Roll a whole season over (new, 2026-11-03)

| Method | Route | Role / permission | Body | Response |
|---|---|---|---|---|
| POST | `/api/crop-seasons/{id}/debt/rollover` | Operate / `DEBT.ROLLOVER` | `{ reasonCode, note, targetCropSeasonId?, entryIds?, dryRun?, overrideExtensionLimit? }` | `200 CropSeasonRolloverResponse`; `400` code, note or list invalid (≤ 500 ids); `404` season; `422` target or size |

```json
CropSeasonRolloverResponse {
  "sourceSeasonId": "uuid", "targetSeason": { "id": "uuid", "code": "HT-2027", "name": "Hè Thu 2027" },
  "newDueDate": "2027-09-14", "dryRun": false,
  "rolledOver": [ { "entryId": "uuid", "entryNumber": "DE-…", "farmerProfileId": "uuid", "oldDueDate": "2027-04-14",
                    "newDueDate": "2027-09-14", "outstandingAmount": 2000000 } ],
  "skipped": [ { "entryId": "uuid", "entryNumber": "DE-…|null", "reason": "DISPUTED" } ]
}
```

- `{id}` is the season the debt is collected in. Without `entryIds`: every open entry collected there; with `entryIds`: only
  those (duplicates ignored). More than 500 open entries → `422`, roll them over in lists.
- Target: the given season (open, a later due date than the source) or, by default, the next open season whose due date is
  later than the source's. Closed, unknown, the same or an earlier season → `422`. Every entry gets the target's
  `paymentDueDate`.
- Each entry follows F3.14; one that does not qualify is skipped with `reason`: `NOT_FOUND` (not an entry of this store),
  `NOT_IN_SEASON`, `NOTHING_OWED`, `DISPUTED`, `NOT_LATER` (its own due date is not before the target's), `NEEDS_OWNER`
  (Sale staff past the staff rollover limit), `EXCEEDS_TOTAL_EXTENSION` (past the limit without the owner's override).
- The qualifying entries move in one transaction; the store is locked first, then every Farmer in `farmerProfileId` order
  (the order the credit and customer services use), so it runs safely beside payments. A second call skips what already
  moved.
- `dryRun: true` returns the same result and writes nothing (no lock, no audit, no notification). It is a preview: it reads without the store and Farmer locks, so if debts change between the preview and the real run (a payment, a dispute, a season being closed), the real run — which checks again under the locks — can differ or be refused. Show it as "what would happen now".
- Each moved entry gets a `ROLLOVER` action, a `DEBT_ROLLOVER` audit and a Farmer notification (`DEBT_DUE_DATE_CHANGED`);
  the run is audited once as `CROP_SEASON_DEBT_ROLLOVER` on the source season.

## 8g. F3.19 — Closing a season, limit suggestions, debt by season (new, 2026-11-03)

| Method | Route | Role / permission | Body / query | Response |
|---|---|---|---|---|
| GET | `/api/crop-seasons/{id}/closing-check` | Operate / `CROP_SEASONS.READ` | — | `200 CropSeasonClosingCheckResponse`; `404` |
| POST | `/api/crop-seasons/{id}/close` | Manage, owner-only / `CROP_SEASONS.MANAGE` | `{ version, note? }` | `200 CropSeasonResponse` (`status: CLOSED`); `409` stale `version`; `422` not closable |
| GET | `/api/crop-seasons/{id}/limit-suggestions` | Manage / `DEBT.READ_DASHBOARD` | — | `200 LimitSuggestionsResponse`; `422` season not closed |
| GET | `/api/reports/debt-by-season` | Manage / `DEBT.READ_DASHBOARD` | `year?`, `page`, `pageSize` | `200 PagedResult<DebtBySeasonItem>`, newest harvest first |

```json
CropSeasonClosingCheckResponse {
  "seasonId": "uuid", "status": "COLLECTING", "canClose": false, "reasons": ["OPEN_ENTRIES"],
  "openEntryCount": 2, "openEntries": [ { "entryId": "uuid", "entryNumber": "DE-…", "farmerProfileId": "uuid",
                                          "fullName": "…", "outstandingAmount": 200000, "status": "PARTIALLY_PAID" } ],
  "openCreditOrderCount": 0, "openCreditOrders": [ { "orderId": "uuid", "orderNumber": "OD-…", "status": "CONFIRMED" } ]
}
DebtBySeasonItem {
  "season": { "id": "uuid", "code": "DX-2026", "name": "…" }, "harvestStart": "2027-02-15", "paymentDueDate": "2027-04-14",
  "status": "CLOSED", "closedAt": "…|null", "isFrozen": true,
  "origin": { "originatedAmount": 2000000, "collectedAmount": 1000000, "returnedAmount": 0, "reducedAmount": 0,
              "cancelledAmount": 0, "carriedOverAmount": 1000000, "outstandingAmount": 1000000, "entryCount": 2, "customerCount": 1 },
  "collection": { "carriedInAmount": 0, "toCollectAmount": 0, "openEntryCount": 0 }
}
LimitSuggestionsResponse {
  "seasonId": "uuid", "requiredSeasons": 2, "seasons": [ { "id": "uuid", "code": "DX-2026", "name": "…" }, … ],
  "items": [ { "farmerProfileId": "uuid", "fullName": "…", "phoneNumber": "…", "creditLimit": 20000000,
               "creditStatus": "ACTIVE", "paidAmount": 3000000, "lastPaidOn": "2027-09-10", "onTimeSeasons": 3 } ],
  "page": 1, "pageSize": 20, "totalCount": 1
}
```

- **Closing (D11).** `reasons`: `ALREADY_CLOSED`; `HARVEST_NOT_ENDED` (not collecting yet: the harvest end day still counts as
  harvesting); `OPEN_ENTRIES` (debt still collected in the season, disputed included); `OPEN_CREDIT_ORDERS` (credit orders
  of the season not completed or cancelled). Lists show at most 50 items; counts are complete. `close` locks the store,
  checks again, records `closed_summary` (the report figures at that moment), `closed_at`, `closed_by` and audits
  `CROP_SEASON_CLOSED` (figures, note). A closed season never changes, reopens or receives debt: rollovers lock the store
  before reading their target, so one that waits behind a closing is refused afterwards.
- **Report (D9).** `origin`: debt that arose in the season, whatever season collects it now; `originated − collected −
  returned − reduced − cancelled = outstanding` (posted ledger lines; `reduced` = ADJUST, `cancelled` = CANCEL);
  `carriedOver` = the part of `outstanding` now collected in a later season. `collection`: debt collected in the season
  now; `carriedIn` = the part that arose elsewhere (or had no season). A closed season shows its `closed_summary`
  (`isFrozen: true`); later payments on carried-over debt appear under the season that collects it.
- **Limit suggestions (D12, CS-14; rule of 05/11 agreed with the Lead).** `GET /api/crop-seasons/{id}/limit-suggestions?page=&pageSize=` (20 by default). A season where the customer owed nothing is skipped when counting. A customer is suggested when the season being closed holds debt of theirs [assumption, pending approval: the customer must have bought on credit this season] and their last `requiredSeasons` (`onTimeSeasonsForLimitRaise`, default 2) seasons with debt were all on time: all the debt that arose in each paid by real payments on or before the original due date, never rolled over, moved, reduced, forgiven or returned. `items[].onTimeSeasons` = consecutive on-time seasons with debt; the list is ordered by it (most first), then by name. `seasons` is a reference (the last N closed seasons up to this one; empty when fewer exist). Read-only: the owner changes the limit through the existing credit-limit API.

## 8h. F3.18 — Debt reminders at fixed milestones (new, 2026-11-03)

No new route. The background job `DebtReminders` (same `BackgroundJobs:DebtReminders` switch) no longer reminds every day
for every entry due within `DebtReminderDays`; that option is removed. It sends the milestones of the store debt policy
(D10, CS-09), per customer and group:

| Milestone | When | To | Notification type |
|---|---|---|---|
| N1 | harvest start of the season the debt is collected in, until its N2 | the Farmer, one per (Farmer, season) | `DEBT_DUE_SOON` |
| N2 | due date + `reminderOffsets.n2` (default −7) | the Farmer, per (Farmer, due date) | `DEBT_DUE_SOON` |
| N3 | the due date | the Farmer; one summary ("N nhóm nợ", counted in debt groups) to the owner and each Sale staff member per day | `DEBT_DUE_SOON`, then `DEBT_OVERDUE` after the day |
| N4 | due date + `n4` (default 14) | the Farmer; one summary (in debt groups) to the owner and each Sale staff member per day | `DEBT_OVERDUE` |
| N5 | due date + `n5` (default 30) | the Farmer; one summary (in debt groups) to the owner | `DEBT_OVERDUE` |

- A run sends only the latest milestone reached for each (Farmer, due date): a job that did not run for days sends one
  reminder, never the missed ones. A milestone is sent once (deduplication keys `debt-reminder:{farmerProfileId}:{seasonId}:N1`,
  `debt-reminder:{farmerProfileId}:{yyyyMMdd of the due date}:N2..N5`, and `debt-reminder-staff:{storeId:N}:{yyyyMMdd}:N3..N5`).
- Disputed, paid and cancelled entries are not reminded (a kept dispute is reminded again). A rollover or a due-date change
  moves the due date, so the schedule follows the new one. Debt without a season keeps N2–N5.
- Dates are Vietnam's (17:00 UTC = 00:00). Customers are processed in pages of `BatchSize`, saved page by page; the job only
  writes notifications and never touches the ledger.
- Reminders sent under the old daily key (`debt:{entryId}:{yyyyMMdd}`) stay as history and do not block the new ones.
- The summaries are built from the group reminders **saved** per Vietnam day (`[start of the day, start of the next day)`), not from
  what the current run sent. Their key is `debt-reminder-staff:{storeId:N}:{yyyyMMdd}:N3..N5` — the **store is part of the key**, so
  two stores never share or suppress each other's summaries. A run that failed after saving the Farmer reminders but before the
  summaries is repaired by a later run, **on any later day**: every (day, milestone) **since T0** that has Farmer reminders of this
  store but no summary of this store gets one, once, with **that day's** figures; the text starts with "Ngày dd/MM/yyyy:" for a day
  before today. There is no limit of days. To bound the work of one run after a long outage, a run writes at most 30 summaries
  (`BackgroundJobs:DebtSummaryBatch`, default 30, 1–1000; one summary per day and milestone N3/N4/N5, so up to three a day — it is a
  count of summaries, not of days, and it limits what one run writes, it does not by itself prove the run is light), **oldest first**; the rest is taken by the next runs — a pair already
  written drops out of the candidates, so nothing is skipped and nothing is written twice. Only Farmers with a debt account in this
  store are counted (a Farmer with accounts in several stores cannot be told apart by the reminder key — single active store today).
- Recipients of a recovered summary: members of this store who are still **active**, with an eligible role (owner and Sale staff for
  N3/N4, owner for N5), when it is written, **and** who already existed at the end of that day; a member who left or lost the role
  receives nothing, and nobody who joined after the day receives a historic summary. The figures are those at the time of the
  summary (the text ends with "(tổng hợp lúc HH:mm dd/MM/yyyy)", Vietnam time); a later reminder of the same day is not added to a summary that exists. Nothing is sent to a Farmer twice. A recovered
  notification is created when the repair runs; its key and text carry the original day (F3.20 review).
- Rollout: enable the job after the seasons are entered; without seasons only N2–N5 apply.

**Starting reminders (F3.20, new 2026-11-03) — T0.** The job sends **nothing** for a store until its owner starts reminders:
`store_debt_policies.reminders_start_date` (T0, `null` = not started; one nullable `date` column, the only schema change).
This stops the first run on existing data from sending every old overdue group a catch-up reminder.

| Route | Who | Notes |
|---|---|---|
| `POST /api/debt-reminders/initialize` `{ dryRun?: bool (default true), reason?: string }` | owner only (`DEBT_POLICY.MANAGE`, not delegable; staff 403 even as dry run) | |

- `dryRun: true` (also an empty body) counts and **writes nothing, sends nothing**. `dryRun: false` sets T0 = **today in Vietnam
  time**, writes the audit `DEBT_REMINDERS_STARTED` (T0, counts) in **one transaction** under the store lock, and sends no
  notification. A failure rolls everything back (no T0, no policy row). Two starts at once: one start, the others answer
  `alreadyStarted: true`. Run again: T0 never changes, nothing is written.
- Response: `{ dryRun, alreadyStarted, startDate, farmers, activeGroups, skipped: { N1..N5 }, skippedTotal, groupsWithLaterMilestones }`.
  `skipped` counts, per milestone, the catch-up reminders the job would have sent for milestones before T0 and now never will.
- Rule of the job at every run: a milestone dated **before T0 is never sent** — also for a debt entered, or given a season, after
  the start (the check is by date, not by a stored mark). N1 follows the same rule by the season's harvest start. Milestones on or
  after T0 are sent as above.
- Debts whose milestones were skipped stay on every debt screen and report; only automatic reminding is skipped. An old debt that
  already passed N5 is not reminded again by this schedule — the owner reviews it ("Cần quyết định") and reminds by hand.
- `GET /api/debt-policy` adds `remindersStartDate` (`null` = not started). `PUT /api/debt-policy` never changes it.
- Rollout order: enter the seasons, then **start reminders last**.

## 8i. F3.13 — Orders join a season, debt due date by season (new, 2026-11-03)

Two moments (D15), no new route:

**1. Order confirmation (credit orders only).** The order joins the season whose order window `[orderWindowStart,
harvestStart)` contains the **Vietnam** day of the confirmation (midnight = 17:00 UTC), under the Farmer lock, and keeps it
(`orders.crop_season_id`; editing the season later does not move it). No season for that day (none entered, or past the
last one) → no season, the credit-tier term as before. Credit is reserved at confirmation; **no debt exists yet**.
`OrderResponse` adds `originSeason` (`{ id, code, name }`) and `expectedDueDate`: while the order can still be delivered
(confirmed, preparing, ready, partially fulfilled), the due date its debt would get if delivered today; `null` otherwise.
`ORDER_CONFIRMED` audit carries `cropSeasonId`. The Farmer sees the estimate before ordering in
`GET /api/me/crop-seasons/current` (8b) and staff in `POST /api/customers/{id}/credit-eligibility`.

**2. Delivery or pickup.** Each fulfilment creates its own debt entry for the unpaid value (a two-part delivery gives two
entries of the same origin season). Origin season = collection season = the order's season. Due date, with `fulfilledDay`
the Vietnam day of the delivery (owner L3 choice B, 2026-11-03):

| Case | Due date | `dueRule` |
|---|---|---|
| order has a season, `paymentDueDate ≥ fulfilledDay` | the season's `paymentDueDate` | `SEASON` |
| …and the store cap `maxCreditTermDays` is shorter (`fulfilledDay + cap < paymentDueDate`) | `fulfilledDay + cap` | `CAPPED` |
| order has a season, delivered **after** its `paymentDueDate` | `fulfilledDay + credit-tier term` | `TERM_DAYS` (`lateForSeason`) |
| no season | `fulfilledDay + credit-tier term` (as before) | `TERM_DAYS` |

The cap only shortens a season due date, never the tier term. `originalDueDate` = this date. The values the date was computed
from are stored with the entry (`due_basis`, D17): rule, season id/code, harvest end, grace days, season due date, tier term,
cap (when it applied), delivery day, `lateForSeason`; later changes of the season or the tier never move an existing debt.

**Credit check** `POST /api/customers/{id}/credit-eligibility` adds `expectedCropSeason` (`{ id, code, name }|null`),
`expectedDueDate` and `dueRule` (the estimate as if delivered today; `null` without a credit tier). Overdue debt still never
refuses credit (CS-10).

**Manual debt** `POST /api/customers/{id}/debt/manual-entries` adds optional `cropSeasonId`. With a season (open, of this
store) the debt belongs to it (origin = collection) and `dueDate` may be left out: it takes the season's due date (cap
included); a season already past its due date needs an explicit `dueDate` (`422`), a closed or unknown season is `422`. An
explicit `dueDate` with a season is kept and recorded as `MANUAL`. Without a season `dueDate` is required, as before.

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
| B-D5 | Roles: groups, price-list links, group tier, credit tiers, credit status changes (activate/suspend/block), debt ADJUST/CANCEL/manual entries, CHANGE_DUE_DATE (since 2026-11-03) = Manage; customer group assignment, credit profile creation and limit changes (rule 22), DISPUTE/KEEP = Operate; Farmers only `/api/me/...`. The nine owner-only actions (section above) cannot be delegated to Sale staff (F3.21) |
| B-D6 | Debt repayment money enters only through payments (L1 cash, L2 payOS); L3 never creates payments, only allocates them |
| C-D1 | payOS debt repayments are allocated oldest due date first (no entry selection online) |
| F-D2 | Debt term depends on the customer type: group → default credit tier → term days. ~~No crop seasons~~ — reopened 2026-11-03: debt by crop season is approved by the mentor (lead agreed); seasons reuse the inventory seasonal rules (`inventory_seasonal_rules`) with a debt-side table, design pending F3.7/F3.9. Until then the tier term applies |
| F-D5 | Reports are Manage-only and owned by the flow whose data they read |
