# Customer Management — additive API contract

Requested 2026-10-04. Read with FLOW_3_CREDIT_DEBT.md, database design §§2–4, 7–8, 44–51,
35.20 and the shared API conventions. This extends the customer directory; existing flow contracts remain intact.

## Repository analysis and implementation plan

- Existing: Domain FarmerProfile/User/UserAddress, CustomerGroup/CustomerGroupAssignment,
  FarmerCreditProfile/CreditTier/CreditLimitHistory/CreditReservation, DebtAccount/DebtEntry/DebtTransaction,
  Orders, Payments and PaymentAllocations; EF mappings, migrations, soft-delete/audit/concurrency interceptors.
- Existing APIs: Auth register creates FARMER User + FarmerProfile; Orders and Payments expose transaction reads.
  Credit/debt posting implementations remain placeholders owned by F3.3–F3.5.
- Missing: customer directory, staff-managed customer creation/editing, group administration, customer history and summaries.
- Add Application/Features/Customers contracts, validators, services and composable write steps;
  Api/Features/Customers controllers; customer tests. Append registrations to DI and customer locks to
  IRowLockService/RowLockService. Infrastructure implements the ordered default-group switch.
- No entity, column, table, relationship or migration change. Application owns each mutation's transaction,
  stages Domain changes and audit rows, calls SaveChanges once, then commits.

## Identity and scope

Customer id is farmerProfileId. Registered customers are global identities linked to FARMER users.
Groups, orders, credit, debt and payments are scoped to the resolved active store. There is exactly one
operational store; no hard-coded store id or client-selected store scope. Foreign-store group/tier/list ids
are refused and foreign-store transactions are excluded. This does not introduce multi-store operation.

There is no standalone WALK_IN customer table, customer code, per-customer payment term or pending AR
confirmation state. WALK_IN identities continue to be recorded as name/phone snapshots through the existing
counter-sale/order API; they never receive a User, FarmerProfile or credit profile from this module.
CustomerCode and PendingConfirmationDebt are null to state that those concepts are unavailable.
PaymentTermDays comes from the customer's credit tier; change that policy through tier/group APIs.

## Directory endpoints

All endpoints are Operate (ADMIN, STORE_OWNER, SALES_STAFF), except status changes (Manage).

| Method | Route | Request / response |
|---|---|---|
| GET | /api/customers | CustomerListRequest → PagedResult<CustomerResponse> |
| GET | /api/customers/{farmerProfileId} | CustomerResponse with default address and debt summary |
| POST | /api/customers | CreateCustomerRequest → 201 CustomerResponse + Location |
| PUT | /api/customers/{farmerProfileId} | UpdateCustomerRequest → CustomerResponse |
| POST | /api/customers/{farmerProfileId}/status | `{status}` → CustomerResponse; Manage |
| GET | /api/customers/{farmerProfileId}/orders | CustomerOrderListRequest → PagedResult<CustomerOrderResponse> |
| GET | /api/customers/{farmerProfileId}/debts | CustomerDebtSummaryResponse (summary, distinct from F3.4 /debt account) |
| GET | /api/customers/{farmerProfileId}/payments | existing PaymentListRequest filters → PagedResult<CustomerPaymentResponse> |

Group assignment/history and group administration use the existing FLOW_3 §3 contract.
Credit-tier administration and customer `/credit`, `/credit/limit`, `/credit/activate|suspend|block`,
`/credit/history` and `/credit/reservations` implement FLOW_3 §4 with its existing DTO shapes and role gates.
Farmer `/api/me/credit` remains outside this staff-management task.

Create request: fullName, password (existing 8–128 character policy), phoneNumber?, email?, address?, notes?,
customerType (REGISTERED only), customerGroupId?, allowCreditPurchase?, creditTierId?, creditLimit?,
creditChangeReason?. Update uses the same editable fields without password/customerType. Identity creation is
explicit through the REGISTERED endpoint; it hashes the provided password without issuing tokens or recording login.
At least one valid email or Vietnamese mobile number is required, matching Auth; contact uniqueness is global.
No client aggregate, actor, role, store id or transaction status is accepted by these DTOs.

Address is `{recipientName, recipientPhone, addressLine, province, ward?, district?}` and updates the user's
default address. Omitted address/group/credit settings leave those relationships untouched; name/contact/notes
are replacement fields. Separate status endpoint follows the existing UserStatus values.

Directory filters: search (name/phone), customerType, customerGroupId, status, hasDebt, page/pageSize.
WALK_IN filter returns an empty directory because those identities have no stable customer id.
SortBy: NAME (default), CREATED_AT, TOTAL_ORDERS, CURRENT_DEBT; descending boolean; id breaks ties.
Unassigned Farmers resolve to the active store's default group, including the group filter/member counts.
TotalOrders counts non-cancelled orders. TotalPurchaseAmount is the sum of COMPLETED order totals
(gross completed purchases, excludes pending/partially fulfilled orders; this is not the sales-revenue report).

Order history: search order number, status, paymentStatus (UNPAID/PARTIALLY_PAID/PAID), fromDate/toDate inclusive
Vietnam days, paging. Payment status is derived from active ORDER allocations of PAID payments; zero-total
orders are PAID. PaymentMethods contains distinct confirmed methods because one order can have several payments.
Debt payment history fixes the route's customer and DEBT_REPAYMENT context, reuses PaymentQueries and returns
confirmation actor plus all debt allocations (including reversed allocations with their actual status).

## Credit/debt rules

CurrentOutstandingDebt/ConfirmedDebt = debt_accounts.current_balance; overdue debt = outstanding entries
whose dueDate is before today in Vietnam; TotalPaid = posted PAYMENT ledger decreases, excluding reversals.
PendingConfirmationDebt is null; pending orders/reservations are not debt.
AvailableCredit = creditLimit − currentBalance − remaining active/partially-consumed reservations.
AllowCreditPurchase requires ACTIVE user, ACTIVE credit profile and ACTIVE debt account; consumers must check
this flag even when mathematical AvailableCredit is positive. A negative available credit is preserved.

Enabling credit without a profile creates a profile and zero-balance debt account atomically, resolving an active
tier from request → assigned group → default group. A missing/foreign/inactive tier is 422.
Disabling existing credit suspends the profile; it preserves the limit, outstanding debt and reservations.
Reactivating/suspending/blocking existing credit is Manage, including when requested on the customer edit form;
a reason is required. Limit/tier edits are Operate and require a reason, history and audit. A lower limit than
exposure is permitted by FLOW_3 §4.3; future credit orders are refused by the settlement guard's owner (F3.3).
Changing group moves an existing profile to the group's active tier without changing its personal limit and
records history/audit. Group default-tier edits do not retroactively change members' profiles.

## Verification

Unit validators and HTTP authorization/validation tests run offline. Service queries, constraint transitions,
aggregate calculations and atomic rollback are covered by opt-in PostgreSQL tests (AGRISAGE_DB_TESTS=1),
using the repository's rollback-only RealDb session. Never run migrations or persist seed/demo customer records.

## Implementation report (2026-10-04)

35 staff endpoints are implemented: 10 directory/history/group-assignment routes, 11 customer-group routes,
8 customer-credit routes and 6 credit-tier routes. All use the existing role gates, validation filter and
RFC 7807 exception handler. Creating a registered customer, assigning its group, initializing its credit/debt
account and writing audit logs share one transaction and one SaveChanges. Contact uniqueness also handles
database races as 409; invalid input is 400, missing resources 404, wrong role 403 and business-state errors 422.

Created files:

- `src/AgriSage.Api/Features/Customers/`: `CustomersController.cs`, `CustomerGroupsController.cs`,
  `CustomerCreditController.cs`, `CreditTiersController.cs`.
- `src/AgriSage.Application/Features/Customers/`: `CustomerContracts.cs`, `CustomerValidators.cs`,
  `CustomerService.cs`, `CustomerWrites.cs`, `CustomerGroupContracts.cs`, `CustomerGroupValidators.cs`,
  `CustomerGroupService.cs`.
- `src/AgriSage.Application/Features/Credit/`: `CustomerCreditContracts.cs`, `CustomerCreditValidators.cs`,
  `CustomerCreditService.cs`.
- `src/AgriSage.Infrastructure/Persistence/CustomerGroupDefaultSwitcher.cs`: pre-clears the prior default flag
  under a store lock to avoid immediate partial-unique-index conflicts; both Domain mutations, timestamps and
  audit records are still persisted through the normal SaveChanges transaction. Switching defaults works in
  either id order; no business timestamp is assigned manually.
- `tests/AgriSage.UnitTests/Application/Customers/CustomerValidatorTests.cs`,
  `tests/AgriSage.IntegrationTests/Api/CustomersHttpTests.cs`,
  `tests/AgriSage.IntegrationTests/Infrastructure/Customers/CustomersDatabaseTests.cs`.
- `docs/reference/api-flows/CUSTOMER_MANAGEMENT.md` (this contract and report).

Modified files:

- `src/AgriSage.Application/DependencyInjection.cs`: register customer/group/credit services and shared steps.
- `src/AgriSage.Infrastructure/DependencyInjection.cs`: register the default-switch persistence adapter.
- `src/AgriSage.Application/Common/Interfaces/IRowLockService.cs` and
  `src/AgriSage.Infrastructure/Persistence/RowLockService.cs`: append store and Farmer row locks.
- `docs/reference/api-flows/FLOW_3_CREDIT_DEBT.md`: link this additive contract.

Verification: restore succeeded; solution build succeeded; 529 unit and 514 offline integration tests passed.
166 opt-in database/storage tests are skipped in the offline run. All 13 Customer PostgreSQL tests passed,
including normalized duplicate phone/email, paging/search/filter/sort, missing customer, group/tier histories,
completed order totals, date/payment-status filters, mixed cash/payOS payments, debt/payment ledger summaries,
foreign-store data exclusion, role checks and an injected failure after SaveChanges in an owned transaction.
The failure test verifies that User, FarmerProfile and audit rows are rolled back; no test data is committed.
The additional historical-credit-tier deletion guard is checked against PostgreSQL too.

Database impact: no schema change, migration, dependency, seed or persistent development-data change.

Design limitations retained: no standalone persistent WALK_IN customer, customer code, pending-confirmation AR,
per-customer term override or multi-store mode. Existing counter orders support WALK_IN without an account.
F3.3–F3.5's reservation/fulfillment/debt-repayment posting placeholders remain their owner's work; this module
reads the existing tables and does not introduce a second debt ledger or implement those separate flows.
