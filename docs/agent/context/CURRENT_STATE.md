# Current State — Agent Context

> Update this file when the implementation phase materially changes.
> It is mutable project state, not an architectural source of truth.

## Current Phase

`BE Foundation & Database Initialization`

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
