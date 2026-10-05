# Credit sales and debt collection — implementation assessment

## Baseline (2026-10-04)

Entities already exist: FarmerProfile/User (customer and active status), CustomerGroup/Assignment,
Store/StoreMember, FarmerCreditProfile, CreditTier, CreditLimitHistory, CreditReservation,
Order/OrderItem, Payment/PaymentAllocation, DebtAccount/Entry/Action/Transaction,
InventoryLot/Balance/Reservation, StockMovement/Item, Delivery/Attempt, SalesReturn/Item/Refund, AuditLog.
There is no SeasonalCredit entity, farmer credit-request entity or per-order credit-approval workflow.
CreateCustomerCreditRequest is a staff configuration DTO, not a farmer approval request.

Existing APIs/services: CustomerService/CustomerWrites, CustomerGroupService, CustomerCreditService,
Orders/confirmation/pickup/cancellation/counter-sale services, PaymentService/PaymentAllocator/
OrderPrepaymentLedger, PaymentQueries and SalesReportService. Credit configuration/tier/history endpoints
already work. Credit confirmation, fulfillment debt creation, debt repayment and debt returns currently
resolve to Temporary implementations. Debt management/report endpoints do not yet exist.

## Reconciliation with the supplied request

- AllowCreditPurchase is derived from ACTIVE profile/account/customer. No duplicate Customer field.
- PaymentTermDays lives on CreditTier; change a customer's tier to select a different term. Confirmed
  orders keep their term snapshot. No individual duplicate term field.
- Lowering a limit below exposure is explicitly supported by FLOW_3, and blocks future credit.
- Availability includes active reservations as well as outstanding debt.
- Partial fulfillment requires one debt per successful stock movement, rather than one per order.
  Duplicate invocation for the same source must be harmless. This preserves DATABASE_DESIGN §49.
  The user explicitly confirmed this choice on 2026-10-04: one Debt Entry per fulfillment.
- SettlementType.CREDIT differs from PaymentMethod (CASH/PAYOS). Reuse settlement type for orders.
- The requested manual BANK_TRANSFER receipt is additive: reuse Payment PENDING/PAID/FAILED,
  staff confirmation and audit rejection reason. No new payment or debt-payment table.
- No existing overdue-blocking policy; an opt-in server policy is added, default false.
- Delivery/return operational APIs are not present. Existing cross-flow posting contracts are the
  integration boundary; pickup is the currently implemented fulfillment API.

## Plan / ownership

Reuse all entities, customer administration, order orchestration and payments. Add eligibility and real
cross-flow credit/debt posting implementations; add debt queries/actions/reports and bank receipt actions.
Only transaction owners save/commit; shared posting steps never save. Serialize exposure changes with
the existing FarmerProfile row lock (same lock used by customer configuration), then read balances.
Order/payment locks continue to protect their source operations. Append audit and ledger records.

One local reviewed migration: add BANK_TRANSFER to the payment-method check and unique fulfillment-source
and payment-allocation ledger indexes. No table removal or data deletion, no application to shared DB.
Potentially obsolete Temporary credit/debt implementations are removed after their tests are replaced.

Validation: domain/validator tests, HTTP contracts/auth, model/migration review, PostgreSQL posting and
concurrency tests when a suitable migrated test database is available. Never claim skipped DB tests passed.

## Delivered behavior

- Credit draft construction checks customer eligibility and sellable base-unit stock. Drafts do not hold
  reservations. Confirmation repeats the check under the customer lock, atomically reserves stock/credit,
  and snapshots the tier term. There is no farmer request or per-order approval.
- Cash/prepayment still uses existing PaymentService, PaymentAllocator and OrderPrepaymentLedger.
  A paid order prepayment shrinks the unused credit reservation. Full-payment confirmation now requires
  sufficient confirmed funding. Cancellation releases unused credit (including a cancelled line while
  other lines remain), without deleting posted debts.
- Successful pickup/fulfillment applies chronological prepayment and creates only unpaid receivable,
  CREDIT_SALE ledger and account balance in the owner's transaction. Due date = Vietnam fulfillment
  day + the order's snapshotted term. Duplicate stock source does not consume prepayment or create debt again.
- Debt repayments use existing Payment/Allocation entities. Explicit cash allocations or oldest-due
  allocation are validated against fresh outstanding balances. Partial payments retain every receipt.
  Bank receipts remain PENDING without affecting debt; Manage confirms or rejects (FAILED with audit reason).
  Repeated confirmation of a paid bank receipt returns the receipt without posting again.
- Return posting validates return/order/store/source, requires a received return, caps reduction to the
  attributable open receivable and appends RETURN transactions. OriginalAmount stays unchanged; the
  return owner refunds any remainder. No new standalone return or delivery workflow is introduced.
- Overdue is computed at read time, including partly paid debt. Optional server policy
  `Credit:BlockCreditWhenOverdue` (environment `Credit__BlockCreditWhenOverdue`) defaults to false.
- Credit/customer summaries include outstanding, reservations, available credit (clamped for display),
  confirmed total paid, open count, overdue and oldest due date. Reservations still count in eligibility.
- Debt entries expose customer, order/products, payment, ledger and action history; queries support
  search, paging, status/customer/order/date/due filters and sort. Dashboard and historical aging,
  collection-by-day/method/staff and group reports use Vietnam business dates.

## APIs / authorization

Existing customer credit/tier/group endpoints are reused (FLOW_3 §4); changing a customer's tier selects
the payment term. `GET /api/customers/{id}/debts` keeps its existing summary response.

| Routes | Roles |
|---|---|
| POST `/api/customers/{id}/credit-eligibility` | Operate |
| Existing POST `/api/orders`, confirm/pickup/cancel/line-cancel | Operate |
| GET `/api/debt-accounts`, `/api/debt-entries`, `/api/debt-entries/{id}` | Operate |
| GET `/api/customers/{id}/debt`, `/debt/entries`, `/debt/transactions`, `/debt/allocation-preview` | Operate |
| GET `/api/debt-entries/{id}/payments`, `/ledger` | Operate |
| POST `/api/debt-entries/{id}/dispute`, `/keep`, `/change-due-date` | Operate |
| POST `/api/debt-entries/{id}/adjust`, `/cancel`; `/api/customers/{id}/debt/manual-entries` | Manage |
| Existing POST `/api/payments/cash` with DEBT_REPAYMENT | Operate |
| POST `/api/payments/bank-transfer` | Operate |
| POST `/api/payments/{id}/confirm`, `/reject` | Manage |
| GET `/api/debt-entries/dashboard` | Manage |
| GET `/api/reports/debt-aging`, `/debt-collections`, `/debt-by-customer-group` | Manage |
| GET `/api/me/credit`, `/api/me/debt`, `/api/me/debt-entries` (+ detail), `/api/me/debt/allocation-preview`; POST own dispute | Farmer, own identity only |

Operate = ADMIN/STORE_OWNER/SALES_STAFF; Manage = ADMIN/STORE_OWNER. Existing permission rules remain:
Sales may create profiles/change limits; enabling/disabling an existing profile requires Manage.
All queries and financial postings derive the single active store, never take an untrusted StoreId.
Foreign-store entries are hidden; Farmer access derives the customer from current user and checks ownership.

## File inventory

Created production Application files:
- `Features/Credit/CreditEligibilityService.cs`, `OrderSettlementGuard.cs`, `CreditReservationAdjuster.cs`.
- `Features/Debt/DebtContracts.cs`, `DebtService.cs`, `FulfillmentFinancialPosting.cs`,
  `DebtRepaymentPosting.cs`, `DebtReturnPosting.cs`.
- `Features/Payments/BankDebtPaymentService.cs`; `Features/Reports/DebtReportService.cs`.

Created API controllers:
- `Features/Customers/CreditEligibilityController.cs`, `MeCreditController.cs`.
- `Features/Debt/DebtController.cs`, `MeDebtController.cs`.
- `Features/Payments/BankDebtPaymentsController.cs`; `Features/Reports/DebtReportsController.cs`.

Modified production files:
- Application `DependencyInjection.cs`; Credit `CustomerCreditContracts.cs`, `CustomerCreditService.cs`;
  Customers `CustomerContracts.cs`, `CustomerService.cs`, `CustomerWrites.cs`;
  Orders `OrderBuilder.cs`, `FulfillmentPostingService.cs`, `OrderCanceller.cs`, `OrderPickupService.cs`;
  Payments `PaymentContracts.cs`, `PaymentQueries.cs`, `PaymentService.cs`.
- Domain `DebtAccount.cs`, `DebtTransaction.cs`, `Payment.cs`, `PaymentMethod.cs`.
- API `Program.cs` (credit policy binding).
- Infrastructure Debt entry/transaction mappings, model snapshot and migration
  `20261004055235_CreditDebtPostingSafety` (source + designer).
- `DATABASE_DESIGN.md`, `FLOW_3_CREDIT_DEBT.md`, this assessment/handoff.

All five production Temporary credit/debt steps were removed from `Application/Common/Placeholders`.
Their isolation behavior now lives only in `tests/TestDoubles/Stub*.cs`, linked by the two test projects.
Existing inventory/order/payment tests use those isolation doubles; the new credit/debt database tests
resolve the real production steps from DI.

New tests: `CreditDebtCollectionTests`, `CreditDebtValidatorTests`, `CreditDebtHttpTests`,
`CreditDebtPostingSafetyMigrationTests`, `CreditDebtDatabaseTests`, `CreditTestDatabase`.
Existing tests updated: model/reviewed migration list, customer summary expectation, order builder setup,
test-double references, and HTTP test factories' Windows EventLog configuration (test hosts only).

## Migration / deployment / remaining verification

Migration changes the allowed payment method values and creates two filtered unique source indexes.
It retains all 67 tables and existing FK indexes; no columns/tables/data are dropped. Existing duplicates,
if any, must be reviewed before deployment; creating the unique index will refuse duplicates. Down refuses
to restore the old payment check if BANK_TRANSFER receipts remain, rather than deleting them.
The migration has NOT been applied to the shared database. Deployment must apply the reviewed migration
before enabling bank receipts and the new posting safety constraints.

Initially no dedicated PostgreSQL test database was available. After the user started Docker, all 14
dedicated credit/debt tests passed on isolated PostgreSQL 16.15 (see the follow-up verification below).
Tests gated by
`AGRISAGE_CREDIT_TEST_CONNECTION_STRING` create a unique temporary schema on an explicitly supplied test
database, apply migrations there, use separate connections for races, and dispose only that schema.
They never fall back to shared Supabase settings. Existing RealDb tests remain gated by AGRISAGE_DB_TESTS.
The existing opt-in database/storage tests remain outside this targeted follow-up run.

Delivery and return operation APIs, and payOS link/webhook endpoints, are still owned by their respective
repository flows and were absent at baseline. Their existing cross-feature contracts now resolve to real
financial posting implementations. End-to-end delivery/return/payOS verification waits for those owners;
the currently implemented pickup and staff collection paths are wired.

Seasonal Credit / farmer approval-request code remaining in production: none. The staff configuration
DTO `CreateCustomerCreditRequest` is retained; it does not implement an approval-request workflow.

## Final verification (2026-10-04)

- `dotnet restore AgriSage.sln --disable-build-servers -m:1 -nr:false`: succeeded; all projects up to date.
- `dotnet build AgriSage.sln --no-restore --disable-build-servers -m:1 -nr:false`: succeeded, zero warnings/errors.
- `dotnet test AgriSage.sln --no-build --no-restore -m:1 -nr:false --logger "console;verbosity=quiet"`:
  UnitTests 548 passed, 0 failed, 0 skipped; IntegrationTests 547 passed, 0 failed, 180 skipped.
  Of the skipped tests, 14 are the new dedicated PostgreSQL credit/debt tests; the other 166 are existing
  opt-in database/storage tests. This was the initial run without a dedicated DB; the 14 new tests were
  subsequently executed successfully against PostgreSQL as documented below.
- HTTP tests ran without an EventLog environment override. The three shared HTTP factory types disable
  machine-wide Windows EventLog writes only for their in-process test hosts.
- `git -c core.whitespace=cr-at-eol diff --check`: succeeded. Model/migration safety, DI registration,
  API routing/Swagger, authorization, validators and Domain financial invariants passed their tests.
- Final source/diff review: controllers remain HTTP boundaries; financial owners commit once, shared
  posting steps never save; debt changes append ledger records; no new entities/repositories/approval flow.
- No migration was applied to the shared database; no claim of deployment or end-to-end Delivery/Return/payOS
  execution is made. The additive migration and those integration boundaries are described above.

## PostgreSQL follow-up verification (2026-10-04)

After the user started Docker and requested the DB tests, a temporary `postgres:16` container provided
PostgreSQL 16.15 on a dynamically allocated localhost-only port. Its database was isolated from Supabase,
used test-only credentials and stored data in tmpfs. No shared database was accessed or migrated.

With `AGRISAGE_CREDIT_TEST_CONNECTION_STRING` set only for that test process:

```powershell
dotnet test tests/AgriSage.IntegrationTests/AgriSage.IntegrationTests.csproj --no-build --no-restore -m:1 -nr:false --filter FullyQualifiedName~CreditDebtDatabaseTests --logger "console;verbosity=normal"
```

Result: **14 passed, 0 failed, 0 skipped**, approximately 53 seconds. No production-code or test fixes
were required. Each test applied the migration chain to its own schema and ran the real posting services.

Verified on PostgreSQL:
- Customer eligibility, disabled/inactive states, zero limit and exact boundary.
- Concurrent order confirmation permits only one reservation above the remaining limit.
- Successful pickup creates debt once, with the Vietnam fulfillment date and snapshotted term.
- Partial fulfillments and prepayment produce only unpaid debt; cancellation releases reservations.
- Full-payment orders require funding and create no debt at pickup.
- Multiple partial cash receipts preserve history; concurrent receipts cannot overpay.
- Pending/rejected bank receipts leave balances unchanged; concurrent repeated confirmation posts once.
- Failed bank confirmation rolls back status/allocations/ledger.
- Due-today/overdue policy and dashboard, foreign-store/customer ownership, historical reports.
- Return adjustment reduces debt without changing OriginalAmount and is idempotent on retry.

After the run, PostgreSQL reported zero remaining `credit_test_*` schemas. The temporary container was
stopped and automatically removed; the downloaded image remains available for future runs. The earlier
548-unit/547-integration results remain valid; this was an additional targeted run, not a rerun of all tests.
