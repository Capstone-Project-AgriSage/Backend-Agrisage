# Current State — Agent Context

> Update this file when the implementation phase materially changes.
> It is mutable project state, not an architectural source of truth.

## Current Phase

`Business APIs — split by flow` (foundation below is done). Four people, one end-to-end flow each
(`docs/reference/api-flows/`): L1 counter sale (lead), L2 online order & delivery, L3 credit & debt,
L4 inventory & returns. Order of work and milestones M1/M2: `api-flows/README.md` §6. Before a task, read the
README and only your flow file's task section; reuse other flows' shared steps and interfaces.

## Foundation Epic Target

When complete, the team should have:

```text
AgriSage.sln
4 production projects
2 test projects
project references and DI
common Domain/Application foundation
67 Domain entities
AgriSageDbContext abstraction + implementation
67-table EF Core configuration
soft delete / audit / concurrency foundation
reviewed InitialCreate migration
Supabase development schema
reference seed data
database-foundation integration tests
setup documentation
```

## Progress

- Implemented — User-requested Auth P0 + in-app notifications + scheduled operations + audit-history APIs
  (2026-10-07): hashed rotating refresh tokens, live sessions/security-version checks, logout/revocation,
  single-use verification/password-reset challenges, SMTP email adapter and future configurable SMS
  gateway; owned notification APIs, transactional outbox/retries/deduplication, debt/inventory alerts,
  payOS reconciliation, authentication maintenance and store-scoped audit history. The current model
  has 71 tables; the historical baseline remains 67. Migration
  `20261007062351_AuthSessionsNotificationsOperations` is reviewed, tested on isolated PostgreSQL,
  and applied to the configured shared Supabase database on 2026-10-07 after explicit user authorization;
  EF migration history confirms no pending migrations. Jobs default to disabled until configuration.
  Contract/configuration: `docs/reference/api-flows/AUTH_NOTIFICATIONS_OPERATIONS.md`.
  Google login and dynamic permissions remain deferred for this release at the user's request.

- Done — AGRI-7 Solution & core configuration: `AgriSage.sln` (.NET 10), 4 src + 2 test projects,
  project references, central package versions (`Directory.Packages.props`), `Program.cs`,
  `AddApplication()` / `AddInfrastructure()`, JWT bearer + Swagger setup, `JwtOptions` validated on start,
  User Secrets / environment variable configuration. No entities, DbContext or migrations yet.
- Done — AGRI-8 Common foundations: Domain `BaseEntity` → `AuditableEntity` → `SoftDeletableEntity`
  (+ `DomainException`); Application `Common/Exceptions`, `PaginationRequest`/`PagedResult<T>` + validator,
  `Common/Interfaces` (clock, current user, storage, payment gateway, AI client + contracts),
  minimal `IAgriSageDbContext` (SaveChangesAsync only). `DateTimeProvider` registered.
  Deferred to AGRI-12: DbSets, transaction abstraction, audit/soft-delete interceptors, query filters.
- Done — AGRI-9 Master data Domain model (tables 1–23): 23 entities + 9 enums under
  `Domain/Features/{Identity,Stores,Customers,Products,Pricing,Suppliers,GoodsReceipts}`, all `SoftDeletableEntity`.
  Aggregates: `Product` → Packagings, `GoodsReceipt` → Items (DRAFT-only editing, Confirm/Cancel, totals).
  `ProductPackaging.Status`, `ProductReview.Status`, `FarmerProfile.Gender` stay `string` (values undefined in DB design).
  Enum ↔ UPPER_SNAKE DB string mapping, FKs without navigations and all constraints/indexes are AGRI-12.
- Docs — AGRI-10 planning: recorded rounding, reservation/order/delivery/debt lifecycle and
  payment-allocation decisions in `BUSINESS_RULES.md` (#45–53), `DECISIONS.md`,
  `BACKEND_CODING_RULES.md` (#61–62) and `DATABASE_DESIGN.md` §XXXV; fixed the missing
  `GoodsReceipts` Domain folder and added `SoftDeletableChildEntity`/`IHasConcurrencyVersion`
  to `BACKEND_ARCHITECTURE.md` §9.2 — all ahead of implementing AGRI-10 (tables 24–51).
- Done — AGRI-10 Transactional Domain model (tables 24–51): 28 entities + 33 enums under
  `Domain/Features/{Inventory,Orders,Payments,Deliveries,Credit,Debt}`. Aggregate roots: InventoryLot (+Balance),
  StockMovement, Stocktake, InventoryReservation, Cart, Order, Payment, Delivery (items, lot allocations, attempts),
  DeliveryIncident, CreditTier, FarmerCreditProfile (+limit history), CreditReservation, DebtAccount (sole creator of
  DebtTransactions), DebtEntry (+actions). Children derive `SoftDeletableChildEntity`; `GoodsReceiptItem` moved to it
  (AGRI-9 fix) and money inputs now reject >2 decimals (`Guard.Money`, `CostRounding`).
  Not in Domain: orchestration/transactions (receiving, reservation, fulfillment, debt collection), cross-aggregate
  checks, debt/payment reversal, refunds, version increments (AGRI-12), fulfilled-value calculation (open question).
- Docs — AGRI-10 sync: recorded decisions made during AGRI-10 implementation that weren't part of the
  pre-implementation plan — Delivery cancellation outcome (`BUSINESS_RULES.md` #54, `DATABASE_DESIGN.md` §35.5),
  debt action sign convention (§35.6), and the ledger-owning-aggregate mutation pattern
  (`BACKEND_CODING_RULES.md` #63) — plus the `CostRounding.cs` file and feature-level shared-record
  convention missing from `BACKEND_ARCHITECTURE.md` §9.2. All added to `DECISIONS.md`. Ahead of AGRI-11.
- Done — AGRI-11 Return, AI & System Domain model (tables 52–67): 16 entities + 15 enums under
  `Domain/Features/{Returns,Diagnosis,Content,Notifications,Audit}` — all 67 Domain entities now exist.
  Aggregates: SalesReturn (lines + Refunds; lifecycle, return limit, return value, settlement), DiagnosisCase
  (images, immutable inferences, superseding reviews, recommendations), plus master Disease/DiseaseTreatment,
  AiModel/AiPolicyConfig, Article, ContactRequest, Notification; AuditLog is an immutable `BaseEntity`.
  Decisions recorded before coding: `DATABASE_DESIGN.md` §35.9–35.13, `BUSINESS_RULES.md` #55–61,
  `DECISIONS.md`, `BACKEND_CODING_RULES.md` #61 (return_value rounding exception, `CostRounding.RoundMoney`),
  `BACKEND_ARCHITECTURE.md` §9.2 (`Audit/`).
  Not in Domain: RETURN_IN posting, RETURN debt transaction (needs a DebtAccount method), "already returned"
  totals, refund execution, can_review_ai checks, policy overlap, class_labels validation, AI calls, audit writing.
- Done — AGRI-12 EF Core persistence model (67 tables): `Infrastructure/Persistence/AgriSageDbContext` + full
  `IAgriSageDbContext` (67 DbSets, `SaveChangesAsync`, `BeginTransactionAsync`; Application now references the
  `Microsoft.EntityFrameworkCore` abstractions package only), 67 `IEntityTypeConfiguration` under
  `Configurations/<Feature>/`, model-wide `Conventions/` (snake_case columns, enum ↔ UPPER_SNAKE converter + `IN (...)`
  CHECK, Restrict/NoAction FKs, lowercase pk/fk/ix/ux/ck names; EF `ForeignKeyIndexConvention` replaced so actor FKs
  to users are not indexed), documented precision/types/defaults, indexes, partial unique indexes and CHECKs
  (`DATABASE_DESIGN.md` §35.14). DI registers the DbContext (no connection at startup).
  Domain change (decision §35.10): `SalesReturn.AddItem` now takes the `Order` and requires exactly one fulfillment
  source matching `FulfillmentType`. Model verified offline by `PersistenceModelTests`.
  Deferred — AGRI-13: audit/soft-delete interceptors, `deleted_by` from current user, global query filters,
  `version` as concurrency token. AGRI-14: InitialCreate migration (must add the expression indexes in
  `PostgreSqlRawIndexes` via `migrationBuilder.Sql`), design-time factory, Supabase apply. No migration exists yet.
- Done — AGRI-13 Soft delete, audit & concurrency (no schema change): `Persistence/Interceptors/`
  (`SoftDeleteInterceptor` → `AuditableEntityInterceptor` → `ConcurrencyVersionInterceptor`, shared
  `EntityStateInterceptor` base) registered in that order; `Authentication/CurrentUserService` (JWT `sub`);
  soft-delete query filter on all 66 soft-deletable entities and `version` concurrency tokens on the 4 designed
  tables via `PersistenceConventions`; `AgriSageDbContext` defers EF cascade fix-up to SaveChanges.
  EF `Remove()` → Domain `MarkDeleted` + Deleted→Modified keeping original values; audit_logs append-only
  enforced; bulk `ExecuteDelete*` forbidden and `ExecuteUpdate*` review-only (source test). Rules:
  `DATABASE_DESIGN.md` §35.15, `BACKEND_CODING_RULES.md` #25/#64, `DECISIONS.md`.
  Deferred — AGRI-14: real `DbUpdateConcurrencyException` / query-filter SQL tests against PostgreSQL.
  Later: map concurrency conflicts to 409/retry per use case, business Audit Log writing, Auth issuing `sub`,
  root version bump on child-only changes and soft-delete cascade to children (both undefined by the design).
- Done — AGRI-14 Initial migration: DB configuration refactored (`Database` section in
  `appsettings.Development.json` → Supabase `agrisage-dev` through the **Session Pooler**
  (`aws-0-ap-southeast-1.pooler.supabase.com:5432`, user `postgres.<project-ref>`; the Direct host is IPv6-only),
  secret `Database:Password` only; `DatabaseOptions`), `dotnet-ef` 10.0.12 local tool,
  `Persistence/Migrations/20260929092546_InitialCreate` + snapshot. **Applied to `agrisage-dev` (PostgreSQL 17.6)**
  and verified against the live database: 67 business tables + `__EFMigrationsHistory`, 1008 columns, 263 FKs
  (117 Restrict / 146 No Action / 0 Cascade), 163 CHECKs, 212 non-PK indexes (24 partial) incl. the 2 raw expression
  indexes (`NULLS NOT DISTINCT` present), `version` on the 4 designed tables, 0 rows (no seed data yet).
  Real-PostgreSQL tests: `RealDatabaseTests` (opt-in, `AGRISAGE_DB_TESTS=1`, every test rolled back) cover soft delete +
  query filter, `DbUpdateConcurrencyException`, unique/partial/raw indexes, CHECKs, Restrict FK and rollback cleanup.
  Open: Supabase Data API / RLS (tables carry default `anon`/`authenticated` grants, RLS off — the Data API must stay
  disabled for `public`); reference seed data is a separate task.

- Done — Reference data seed (`docs/agent/requests/SEED_REFERENCE_DATA.md`):
  `Infrastructure/Persistence/Seed/` has Role/Unit/Disease/Store seeders, `DatabaseSeeder`, validated Store options,
  read-only approved catalogs and safe seed errors. Api `--seed` runs then exits before HTTP startup; no auto-seed,
  new package, Domain/EF configuration change or migration. Natural-key lookups include soft-deleted rows;
  deleted matches fail, live rows are never overwritten, a different ACTIVE store blocks the operation.
  One owned transaction and one SaveChanges; rollback-only tests supply their transaction and use a savepoint
  (no nested transaction or outer commit). Dev Store values are explicitly approved TEMPORARY data:
  AGRISAGE-DEV / AgriSage Dev Store / Dev address - to be replaced / Can Tho; optional fields NULL.
  Verified 2026-09-30: restore succeeded; build 0 warnings/0 errors; offline 233 unit passed and
  102 integration passed / 23 real tests skipped; final full run with `AGRISAGE_DB_TESTS=1`:
  233 unit + 125 integration passed, 0 failed/0 skipped (including 10 new seed PostgreSQL tests).
  Tests verify 5 roles / 9 units / 5 diseases / 1 store, second run adds 0, preserved values/IDs/timestamps,
  all four deleted-code conflicts, ACTIVE-store conflict, failure after SaveChanges rollback, and test cleanup.
  **Applied to `agrisage-dev`** (user-approved): first `--seed` run added roles=5, units=9, diseases=5, stores=1;
  the second run added 0 (idempotent). Read-only verification: 5 roles (ADMIN, DELIVERY_STAFF, FARMER, SALES_STAFF,
  STORE_OWNER), 9 units, 5 diseases (exactly one `is_healthy_class`), 1 store `AGRISAGE-DEV` ACTIVE, 0 soft-deleted
  rows, 0 rows in every other business table. The reference seed tests roll back all data.

- Done — Auth: `POST /api/auth/register|login`, `GET /api/auth/me`
  (`Api/Features/Auth`), `Application/Features/Auth` (service, DTOs, validators, `ContactNormalizer`,
  `AuthenticationFailedException`), Infrastructure `PasswordHashService` (PBKDF2), `AccessTokenService` (JWT),
  `NpgsqlErrorClassifier`, `AgriSageClaimTypes`; Api `GlobalExceptionHandler`, `ValidationFilter`, rate limiting,
  `MaintenanceCommands` (`--seed`, `--create-admin`). No schema change, no migration, no new package version.
  Decisions: see `DECISIONS.md` (Auth rows). Not implemented: refresh token, OTP/verify, password reset, lockout,
  user management API. Verified: build 0 warnings/0 errors; 285 unit + 162 integration passed (23+9 PostgreSQL tests
  run with `AGRISAGE_DB_TESTS=1`, all rolled back).
  **First Admin created on `agrisage-dev`** (user-approved `--create-admin`): 1 user, role ADMIN, ACTIVE, PBKDF2 hash;
  a second run changed nothing. The password lives only in the developer's User Secrets
  (`AdminBootstrap:Password`); there is no change-password feature yet.

- Done (code + tests, not committed yet) — Staff management: `Application/Features/Staff` (`StaffPolicy`,
  `StaffService`, DTOs, validators), `Api/Features/Staff/StaffController`, `POST /api/auth/change-password`,
  `UserAccessValidator` + JWT `OnTokenValidated` (locked accounts rejected immediately), `ICurrentUserService.Role`.
  No schema change, no migration, no new package. Verified: build 0 warnings/0 errors; 321 unit + 190 integration
  passed with `AGRISAGE_DB_TESTS=1` (45 PostgreSQL tests, all rolled back). No staff created on `agrisage-dev`
  (still 1 user: the Admin). Still deferred to the mentor decision: OTP / phone and email verification,
  forgot-password, refresh token, lockout after failed attempts.

- Done (code + tests, branch `feature/catalog`, not pushed) — Catalog: `Application/Features/Products` (category,
  brand, active ingredient, unit, product with packagings and ingredients, store product, public catalog services,
  `CatalogRules`), `Api/Features/Products` (staff controllers + `CatalogController`, anonymous, rate limit `public`),
  Domain `SoftDeletableEntity.Restore` + `ProductActiveIngredient.Reinstate` + `PackagingStatus`.
  No schema change, no migration, no new package. Decisions: `DECISIONS.md` (Catalog rows), `DATABASE_DESIGN.md` §35.16.
  Verified: build 0 warnings/0 errors; 346 unit + 277 integration passed with `AGRISAGE_DB_TESTS=1` (59 PostgreSQL
  tests, all rolled back). No catalog data created on `agrisage-dev`. Not included: image upload (Supabase Storage),
  prices, stock.

- Done (branch `feature/storage`, not pushed) — Image upload:
  `Application/Features/Files` (`ProductImageService`, `ImageRules`), `Infrastructure/Storage`
  (`SupabaseFileStorageService`, `StorageOptions`), `Api/Features/Files/FilesController`, `StorageUnavailableException`
  → 503, rate limit `upload`. No schema change, no migration, no new package. Verified offline: build 0 warnings/0 errors;
  371 unit + 253 integration passed (60 real-DB/real-storage tests skipped without their flags). The real Supabase test
  (`AGRISAGE_STORAGE_TESTS=1`) passed against `agrisage-dev`: upload, public read, delete (the bucket was left empty).
  Supabase reports a missing object as HTTP 400 `not_found` (handled as "already deleted"); public URLs are served
  through a CDN, so a deleted image can stay visible briefly — names are unique GUIDs, so nothing is ever overwritten.

- Done (branch `feature/goods-receipts`, not committed) — Suppliers + Goods Receiving:
  `Application/Features/Suppliers`, `GoodsReceipts` (`GoodsReceiptService`, `GoodsReceiptConfirmer`, `ReceiptItemRules`),
  `Inventory` (lots, stock movements, lot status), `Common` (`BusinessCalendar`, `DocumentNumbers`, `EnumText`,
  `IRowLockService`), `Infrastructure/Persistence/RowLockService`, Api controllers `Suppliers`, `GoodsReceipts`,
  `Inventory`, `ApiRoles.Operate`. Domain: `GoodsReceipt.Confirm` takes the lot per item; `Product` rejects expiry
  without lot tracking. No schema change, no migration, no new package. Verified: build 0 warnings/0 errors; 408 unit +
  385 integration passed with `AGRISAGE_DB_TESTS=1` (all real-DB tests rolled back; 1 skipped = Supabase Storage).
  Decisions: `DECISIONS.md` (Receiving rows), `DATABASE_DESIGN.md` §35.17. Not included: Excel import, reversal of a
  confirmed receipt, stock-take/adjustment, reservations.
- Done (branch `feature/delivery-proof-upload`, not pushed) — Delivery photo upload:
  `POST/DELETE /api/files/delivery-proofs`, `IDeliveryProofService`, shared `ImageUploader`, `StorageArea` on
  `IFileStorageService`, `StorageOptions.DeliveryProofBucket`. No schema change, no migration, no new package.
  Offline tests pass; the real Supabase test needs the public bucket `delivery-proofs` to be created first (not created yet).
- Done (branch `feature/payment-order-link-refunds`) — Schema change C-D1/C-D2:
  migration `20261002155111_PaymentOrderLinkAndOrderRefunds` (`payments.order_id`, cancelled-order refunds;
  design §35.18), `Payment` requires/limits its order, `Order.RequestCancellationRefund` + complete/fail/cancel,
  EF mapping, migration review tests, rolled-back real-DB tests (run only after the migration is applied to
  `agrisage-dev`).
- Done (branch `feature/stocktake-snapshot-time`) — C-D6 exact stale detection: migration
  `20261003001732_StocktakeItemSnapshotTime` (`stocktake_items.snapshot_at`), `Stocktake.AddItem` takes the
  snapshot time, `Stocktake.RefreshItem` re-snapshots one stale line; design §35.19; migration review tests.
- Done — Cross-module skeleton: the 8 interfaces of the API contracts in
  `Application/Features/{Pricing,Credit,Debt,Payments}`, their `Common/Placeholders/Temporary*` implementations
  registered in `Application/DependencyInjection.cs` (one line per interface, tagged with the owner task),
  `DocumentNumbers` prefixes OD/DL/PM/ST/RT/RF/DE + generic `NextAsync`.
- Done (branch `docs/api-flows`, task F0.1) — Work re-split by flow after the mentor review: the three module
  contracts were replaced by `docs/reference/api-flows/` (README + FLOW_1..FLOW_4); all 150 routes and every DTO
  kept, 19 routes added (quick counter sale, stock summary/alerts/expire-due, Excel receipt import, stock card,
  Farmer delivery tracking, group credit tier, Farmer allocation preview, sales/delivery/debt/inventory reports).
  DI tags and interface comments now name the owner tasks (F1.1, F1.3, F1.6, F3.3–F3.5). Design §35.20 (debt term by
  customer type) written; its migration `CustomerGroupDefaultCreditTier` is task F0.2. No code behaviour change.
- Done (branch `feature/customer-group-credit-tier`, task F0.2) — migration
  `20261003085951_CustomerGroupDefaultCreditTier`: `customer_groups.default_credit_tier_id` uuid NULL, index, FK
  `credit_tiers` NO ACTION (design §35.20; still 67 tables). Domain `CustomerGroup.DefaultCreditTierId` +
  `SetDefaultCreditTier`; migration review test, reviewed migration list, rolled-back real-DB tests (FK, no physical
  delete of a used tier). **Applied to `agrisage-dev`** (lead-approved, 2026-10-03); read-only check: column uuid
  NULL, FK NO ACTION, index present, still 67 tables, 0 rows in `customer_groups` / `credit_tiers`. Verified: build
  0 warnings; 432 unit + 427 integration passed with `AGRISAGE_DB_TESTS=1` (2 skipped = real Supabase Storage).
- Done (branch `feature/f4-3-receipt-import`, task F4.3, by the lead) — Goods receipt Excel import:
  `GET /api/goods-receipts/import-template`, `POST /api/goods-receipts/import/preview`, `POST /api/goods-receipts/import`
  (FLOW_4 §5). Application `Features/GoodsReceipts/Import` (`IReceiptSpreadsheet`, `GoodsReceiptImportService`,
  `ReceiptImportValues`, validator); Infrastructure `Spreadsheets/ClosedXmlReceiptSpreadsheet` (new package ClosedXML
  0.105.1, MIT, approved; no known vulnerabilities). `ReceiptItemRules.Check` now names the field of a violation;
  `GoodsReceiptService` creates drafts with a source type/file name and loads all lines in two queries;
  `BusinessRuleException` can carry `errors` (422 problem details `errors`). No schema change, no migration.
- Done (branch `feature/f1-1-price-lists`, task F1.1) — Price lists: 10 routes of FLOW_1 §3
  (`Api/Features/Pricing/PriceListsController`, `Application/Features/Pricing/PriceListService`), real `PriceResolver`
  (registered for `IPriceResolver`; `TemporaryPriceResolver` deleted), catalog prices (`PublicPackaging.Price`,
  `PublicProductListItem.FromPrice`), shared `Application/Common/AuditTrail` and `Common/Validators/MoneyRules`
  (`MustBeMoney`, moved from GoodsReceipts). Domain: `PriceListItem.Reinstate`. No schema change, no migration.
- Done (branch `feature/staff-audit`) — Staff audit: `StaffService` (create, update, lock, unlock, reset password, remove) and `AuthService.ChangePasswordAsync` write `audit_logs` through `AuditTrail` (no passwords or hashes). Seed test no longer requires an empty `products` table (compares before/after). No schema change, no migration.
- Done (branch `feature/f1-2-counter-orders`, task F1.2) — Staff counter orders: 9 routes of FLOW_1 §4 (`Api/Features/Orders/OrdersController`, `Application/Features/Orders`: `OrderService`, `OrderBuilder` shared step, `OrderQueries`, validators). Walk-in name optional ("Khách lẻ"); price override audited (`PRICE_OVERRIDE`); lines added later use the order's own price list. Domain: `Order.ChangeNote`. No schema change, no migration.
- Done (branch `feature/f1-3-cash-payments`, task F1.3) — Cash payments: 7 routes of FLOW_1 §5 (`Api/Features/Payments/PaymentsController` and `MePaymentsController`, `Application/Features/Payments`: `PaymentService`, `MyPaymentService`, `PaymentQueries`, `PaymentAllocator` shared step, real `OrderPrepaymentLedger` replacing `TemporaryOrderPrepaymentLedger`). `IRowLockService` gained `LockOrderAsync` and `LockPaymentAsync`; `ApiRoles.Farmer`; shared `RefundResponse`. DEBT_REPAYMENT still refused until F3.5. No schema change, no migration.
- Done (branch `feature/f1-4-confirm-reserve`, task F1.4) — Confirmation and reservation: 5 routes of FLOW_1 §6 (`Api/Features/Orders/OrderConfirmationController`, `Application/Features/Orders`: `OrderConfirmationService`, `OrderConfirmer` shared step, pure `FefoAllocator`). `IRowLockService.LockLotBalancesAsync`. Stock is reserved (`inventory_reservations`, lot `quantity_reserved`) at confirmation; nothing leaves stock until pickup (F1.5). No schema change, no migration.
- Done (branch `feature/f1-5-pickup`, task F1.5) — Pickup: `POST /api/orders/{id}/pickup` and `POST /api/orders/{id}/items/{itemId}/cancel-remaining` (FLOW_1 §7; `OrderPickupService`, shared `FulfillmentPostingService`). A pickup posts a SALE stock movement with the weighted-average cost snapshot and consumes the reservation. Test infrastructure: the real-DB test classes that switch off the store's ACTIVE walk-in price lists share the xUnit collection `RealDb.WalkInPriceListCollection`, because they lock the same rows (otherwise they wait for each other and exhaust the pooler's 15 connections whenever `agrisage-dev` has an active list). No schema change, no migration.
- Done (branch `feature/f1-5-pickup`, M2 review) — `Order.CancelItemRemaining` keeps the reason; confirmation re-checks line sellability; a pickup from another lot moves the reservation there so it ends CONSUMED (`InventoryReservation.ReserveMore`); `Domain/Common/TimeOrderedId` replaces `Guid.CreateVersion7` for entity ids (increasing in creation order even inside one millisecond, so rows saved together — e.g. order lines — keep their order when sorted by `created_at, id`).
- Done (branch `feature/f1-6-cancel-orders`, task F1.6) — `POST /api/orders/{id}/cancel` (FLOW_1 §8; `OrderCancellationService`, shared `OrderCanceller`) and the real `IOrderPaymentCancellation` (`OrderPaymentCancellation`, replaces `TemporaryOrderPaymentCancellation`; also used by cancel-remaining). Domain: `Payment.ReleaseUnconsumedPrepayment` / `PaymentAllocation.ReleaseUnconsumed` (design §35.21). No schema change, no migration.
- Done (branch `feature/f1-7-counter-sales`, task F1.7) — Quick counter sale: `POST /api/counter-sales/preview` and `POST /api/counter-sales` (FLOW_1 §9; `CounterSaleService`, shared `CashPayments`, `FefoProposals`). The order, cash payment, reservation and pickup are one atomic operation. Fixed on the way: the whole-package check of a pickup is on the line total, not per lot (FEFO splits a line at any base quantity). `TemporaryFulfillmentFinancialPosting` now consumes prepayment for FULL_PAYMENT. No schema change, no migration.
- Done (branch `feature/f1-8-sales-report`, task F1.8) — Sales report: `GET /api/reports/sales` (FLOW_1 §10; `Api/Features/Reports/ReportsController`, `Application/Features/Reports/SalesReportService`). Flow L1 (F1.1–F1.8) is complete. No schema change, no migration.
- Done (branch `feature/f2-1-farmer-profile-addresses`, task F2.1 remaining part) — Farmer profile and addresses: 7 routes `/api/me/profile`, `/api/me/addresses` (FLOW_2 §3.1; `Api/Features/Customers/MeProfileController`, `Application/Features/Customers/MyProfileService`, shared `CustomerAddresses` read) and `GET /api/customers/{farmerProfileId}/addresses`. First address = default; `isDefault: true` / `set-default` move the flag in one transaction (`IUserAddressDefaultSwitcher` pre-clears the old flag like `ICustomerGroupDefaultSwitcher`); `isDefault: false` never removes it; deleting the default leaves none; max 10 active addresses (422). No schema change, no migration. The staff customer routes of §3.2 came from the Customer management PR (`CUSTOMER_MANAGEMENT.md`), whose create contract differs from FLOW_2 §3.2 (password required, no store-managed account without a usable password).
- Done (same branch, tasks F2.2–F2.7) — Flow L2 complete except the F2.1 staff-contract difference above:
  F2.2 cart (`Api/Features/Carts/MeCartController`, `Application/Features/Carts/CartService`; prices recalculated through
  `IPriceResolver` on every read, lines flagged NOT_SELLABLE / NO_PRICE), F2.3 checkout and my orders
  (`MeOrdersController`, `MeOrderService`: L1's `OrderBuilder` without overrides, cart CONVERTED in the same transaction,
  Farmer cancel only while PENDING_CONFIRMATION through `OrderCanceller`), shared `Customers/CurrentFarmer`.
  F2.4 payOS: `IPaymentGateway` reshaped (async webhook verification, status query, cancel, whole-VND amounts, QR code,
  expiry), adapter `Infrastructure/Payments/PayOsPaymentGateway` (official SDK `payOS` 2.1.0, decision C-D7, no known
  vulnerabilities; `PayOsOptions`, 503 `PaymentGatewayUnavailableException` when not configured), `PayOsPaymentService`
  (payOS never called inside an open DB transaction; webhook idempotent under the order → payment locks; outcomes logged by the
  controller with the order code only), Domain `Payment.AssignProviderOrderCode` (code stored before the link is requested),
  `OrderPaymentCancellation` now closes a PENDING payOS link at payOS instead of refusing (F2.4 part of F1.6).
  F2.5–F2.7 deliveries (`Application/Features/Deliveries`, `Api/Features/Deliveries`): notes allocated FEFO from the
  order's reservation minus what other notes hold, planned limit across active notes, lot change keeps the released allocation
  and moves the reservation (new lots reserved before the old ones are released), DELIVERY_STAFF scope (`DeliveryAccess`),
  attempts post through L1's `FulfillmentPostingService`, proof photos accepted only from the `delivery-proofs` bucket
  (`IFileStorageService.KeyFromPublicUrl`) and protected from deletion (`IProofPhotoUsage`, decision C-D3), incidents link only
  posted ADJUSTMENT movements, Farmer tracking, `Reports/DeliveryReportService`. `IRowLockService.LockDeliveryAsync` added.
  No schema change, no migration. Verified with the real-DB suite (`AGRISAGE_DB_TESTS=1`).
- Done (branch `feature/cors-allowed-origins`) — CORS for the web client: `Api/Extensions/CorsExtensions` (`Cors:AllowedOrigins`, validated at start-up), `UseCors` first in `Program.cs`, local dev origins in `appsettings.Development.json`. Set the production origin(s) with environment variables `Cors__AllowedOrigins__0` … when the web app is deployed. No schema change.

## Not Yet Implied by Foundation Completion

Foundation completion does not mean these business APIs are complete:

```text
Auth/Register/Login flows
Product CRUD
Pricing workflows
Goods Receipt API
Inventory operations API
Order checkout
payOS end-to-end payment
Delivery operations
Credit/debt collection
Return/refund UI/API
AI diagnosis API
Notifications/reports
```

These belong to later feature Epics.

## Current Approved Documents

- `docs/reference/BACKEND_ARCHITECTURE.md`
- `docs/reference/BACKEND_CODING_RULES.md`
- `docs/reference/DATABASE_DESIGN.md`

## Implementation Strategy

Database was designed first at the specification level.
Runtime implementation uses EF Core Code First migrations:

```text
approved DB design
→ code model/configuration
→ reviewed migration
→ PostgreSQL
```

Do not switch to EF Database First scaffolding unless the team explicitly changes the workflow.
