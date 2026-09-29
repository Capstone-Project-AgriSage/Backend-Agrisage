# Key Technical Decisions — Agent Context

These are stable decisions currently frozen for backend implementation.

| Decision | Current choice |
|---|---|
| Backend architecture | 4 layers + feature folders |
| Persistence implementation | EF Core + Npgsql |
| Schema workflow | Design First → EF Code First Migrations |
| Database | PostgreSQL / Supabase |
| DB inspection | DBeaver |
| Generic repository | Not used by default |
| App persistence abstraction | `IAgriSageDbContext` |
| Controller responsibility | HTTP only |
| Business orchestration | Application Services |
| Entity invariants | Domain |
| External integrations | Application interfaces + Infrastructure implementations |
| Delete behavior | Soft delete / semantic reversal |
| Business FK physical delete | Restrict / NoAction by default |
| Inventory quantities | Base-unit `bigint` |
| Costing | Weighted Average Cost per logical Lot |
| Online payment | payOS |
| Automated payOS refund | Out of MVP |
| AI service | Python FastAPI + PyTorch |
| AI authority | Human Review required before recommendation |
| Promotions | Removed |
| Store scope | One active store, real Store table retained |
| Reporting tables | None initially; query transaction tables |
| Monetary rounding | Unit costs (numeric(20,6)) round AwayFromZero to 6 decimals; money (numeric(18,2)) input beyond 2 decimals is rejected, not rounded |
| Last-unit Lot cost | Final issuance that empties a Lot takes the exact remaining `total_cost_value` as COGS |
| Aggregate child mutation | Child-of-aggregate entities (e.g. Goods Receipt Item) cannot be soft-deleted/mutated directly; only the aggregate root may do so |
| Reservation status | Inventory/Credit Reservation status is derived from reserved/consumed/released quantities, not set independently |
| Order/line terminal state | `PARTIALLY_CANCELLED` is terminal once a line has both fulfilled and cancelled quantity |
| Debt Entry adjustment | `ADJUST` only decreases outstanding; increasing receivable requires a new `MANUAL_ADJUSTMENT` Debt Entry |
| Payment allocation binding | `allocation_type` is bound to the Payment's `payment_context` (ORDER_PAYMENT→ORDER, DEBT_REPAYMENT→DEBT) |
| Stocktake completion | `Complete()` requires every line to have a counted quantity |
| Delivery cancellation outcome | Nothing delivered → Delivery CANCELLED; partly delivered → Delivery DELIVERED, remainder shown as PARTIALLY_CANCELLED on its Delivery Items |
| Debt action sign convention | `debt_entry_actions.adjustment_amount` follows the ledger sign convention (negative = decrease); NULL for DISPUTE/KEEP/CHANGE_DUE_DATE |
| Ledger-owning aggregate | An entity with its own identity (e.g. Debt Entry) can still have its balance-affecting methods restricted to `internal`, callable only by the aggregate that owns the ledger (e.g. Debt Account) |
| Sales Return lifecycle | Sequential REQUESTED → … → INSPECTED; PARTIALLY_RESOLVED = some resolution recorded; COMPLETED = all RESTOCK lines have RETURN_IN and completed refunds = refund total; Reject changes status only |
| Return quantity limit | "Already returned" counts all other non-rejected/non-cancelled returns; each line references exactly one fulfillment source matching the Order's fulfillment type (DELIVERY → lot allocation, PICKUP → original stock movement item) |
| Return value rounding | `round2(qty × unit_price ÷ conversion, AwayFromZero)` — the only exception to money-is-never-rounded |
| Return settlement | Debt adjustment + refund = total return value; Refund is a child of Sales Return; FAILED refund changes status only |
| Diagnosis review | Allowed after AI completes, after AI fails (manual), and as re-review (supersedes); no CONFIRMED without a SUCCESS inference |
| Recommendation eligibility | VERIFIED case only (never bypassed by `requires_human_review`); Healthy → treatment guidance only |
| Content/Notification lifecycle | Only documented invariants (published_at / resolved_at / read_at); no transition graph |
| Audit Log | Append-only `BaseEntity`, immutable in Domain; separate `Audit` feature folder |
| Attempt item DB CHECK | DB: `delivered + failed <= attempted` (each ≥ 0); `= attempted` is enforced by the Domain when the attempt completes |
| User contact CHECK | `NULLIF(BTRIM(email),'') IS NOT NULL OR NULLIF(BTRIM(phone_number),'') IS NOT NULL` — blank values do not count |
| Expression indexes | `ux_inventory_lots_logical_lot` and `ux_users_email_lower` are raw SQL in the InitialCreate migration (not EF-modelled) |
| Actor FK indexes | No index on FKs to users that only record an actor (`deleted_by`, `*_by`, `author_id`) unless §XXX lists one |
| Extra DB rules | One open reservation per Order, one default address/customer group, single-row source/status consistency CHECKs (DATABASE_DESIGN §35.14) |
| Application EF dependency | Application references `Microsoft.EntityFrameworkCore` (abstractions: `DbSet<T>`, transactions) only; never Npgsql/Relational/Infrastructure |
| Enum persistence | UPPER_SNAKE strings via a central converter; exceptions `PayOs` → `PAYOS`, `PayOsWebhook` → `PAYOS_WEBHOOK` |
| Persistence bookkeeping | Timestamps, soft delete and `version` are set by SaveChanges interceptors (order: soft delete → audit timestamps → version); business code never assigns them |
| EF `Remove()` of soft-deletable rows | Converted to an UPDATE via Domain `MarkDeleted()` keeping EF original values; aggregate children, non-deletable states and already-deleted rows → `DomainException` |
| Domain-path soft delete | `MarkDeleted` / `root.RemoveItem` keep the `deleted_by` / `deleted_at` passed by the Application (sourced from `ICurrentUserService` / `IDateTimeProvider`) |
| `deleted_by` source | JWT `sub` claim via `ICurrentUserService`; NULL when unauthenticated (no system user id); authenticated without a valid `sub` → error |
| Soft delete of a root | Does not cascade to its children (not defined by the design) |
| EF cascade timing | `CascadeDeleteTiming` / `DeleteOrphansTiming` = `OnSaveChanges`, so `Remove()` never nulls tracked dependents' FKs before the soft-delete conversion |
| Audit Log persistence | Append-only enforced: EF update/delete of `audit_logs` is rejected |
| Concurrency version | Only the 4 `version` tables; insert = 0; each persisted row update (incl. soft delete) = original + 1; no change → no increment; child-only changes do not bump the root; no automatic retry |
| Database configuration | Non-secret `Database` section (Host/Port/Database/Username/SslMode) committed per environment; only `Database:Password` is secret (User Secrets / `Database__Password`); `DatabaseOptions` builds the connection string with `NpgsqlConnectionStringBuilder` |
| EF CLI | `dotnet-ef` pinned as a local tool (`dotnet-tools.json`) at the EF Core version (10.0.12); no design-time factory — tools use the Api host |
| Raw SQL in migrations | InitialCreate calls `migrationBuilder.Sql(PostgreSqlRawIndexes.…)`; those constants are frozen — changes need new constants + a new migration |
| Constraint names | Configured names must be ≤ 63 chars so EF never truncates them (`~`); enforced by `PersistenceModelTests` |
| Bulk EF APIs | `ExecuteDelete*` forbidden for business entities; `ExecuteUpdate*` forbidden for normal business mutations unless reviewed (they bypass the interceptors) |

Full rationale for the rows above: `docs/reference/DATABASE_DESIGN.md` §XXXV,
`docs/reference/BACKEND_CODING_RULES.md` #61–#64.

If a task proposes changing one of these decisions, treat it as an architecture/business decision,
not a normal implementation detail.
