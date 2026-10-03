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
| Auth tokens | JWT access token only (HS256, claims `sub` = users.id, `role` = role code, `jti`); no refresh token (no table in the 67-table baseline) |
| Registration | Farmers self-register (`POST /api/auth/register`, role forced to FARMER, FarmerProfile created in the same SaveChanges); staff accounts are created by Admin/Store Owner; Admin/Store Owner/Sales may also create a store-managed Farmer account (`POST /api/customers`, phone required, no usable password until the farmer claims it through the future OTP/forgot-password flow; decision B-D1) |
| Phone/email normalization | Vietnamese mobile `0(3|5|7|8|9)xxxxxxxx` stored as `0…` (`+84`/`84` accepted); email trimmed + lower-case; duplicates checked on normalized values (unique indexes compare the stored text) |
| Password policy | 8–128 characters, no composition rule (length over complexity); hashing = ASP.NET Core PBKDF2 `PasswordHasher`, rehash on login when needed |
| Login behavior | Wrong password and unknown account give the same 401 message and the same work (dummy hash); non-ACTIVE account → 403 only after the password verified; success records `last_login_at` |
| Phone verification | No OTP yet: `phone_verified = false`; trust comes from the store approving customers/credit. OTP (Zalo ZBS/ZNS or SMS) is a later task behind a sender abstraction |
| Auth rate limit | Fixed window 10 requests/minute per client IP on register/login (429); behind a proxy the forwarded IP must be configured at deployment |
| Exception → HTTP | `GlobalExceptionHandler` (RFC 7807 + `traceId`): Validation 400, AuthenticationFailed 401, Forbidden 403, NotFound 404, Conflict / concurrency / unique violation 409, BusinessRule / Domain 422, other 500 with no details |
| Request validation | `ValidationFilter` runs the FluentValidation validator of every action argument before the action |
| Operator commands | `--seed` and `--create-admin` run one-shot against the configured DB and exit before HTTP (`MaintenanceCommands`); output is counts/safe messages only |
| First Admin | `--create-admin` with `AdminBootstrap:Email` / `:Password` (secrets, never appsettings); idempotent: any existing Admin → no change |
| Staff management API | `api/staff` for Admin and Store Owner; staff = Store Owner / Sales / Delivery members of the single ACTIVE store (StoreMember created automatically, store taken from the active Store, never hard-coded); Admin accounts are not staff |
| Staff permission matrix | Admin manages Store Owner, Sales, Delivery; Store Owner manages Sales and Delivery only (never Store Owner or Admin); the role is read from the JWT, enforced in `StaffPolicy` + `StaffService` (HTTP `[Authorize(Roles)]` is only the first gate) |
| Staff removal | `DELETE /api/staff/{id}` = StoreMember → LEFT + user LOCKED, not a soft delete (a soft-deleted user would disappear from history queries through the query filter); unlock re-activates the member |
| Initial staff password | Chosen by the creator and handed over; no forced change on first login (no column); reset by Admin/Owner; users change their own with `POST /api/auth/change-password` |
| Immediate lock | Every authenticated request checks the account is ACTIVE and not deleted (`IUserAccessValidator` in `JwtBearerEvents.OnTokenValidated`), so a lock does not wait for the token to expire; costs one small query per request |
| Catalog permissions | Read the internal catalog (`api/categories`, `brands`, `active-ingredients`, `units`, `products`, `store-products`): Admin, Store Owner, Sales, Delivery; write: Admin and Store Owner; the public catalog (`api/catalog/*`) needs no sign-in (rate limited 120/min per IP) |
| Public catalog content | Only the store's ACTIVE and sellable Store Products of ACTIVE Products; id = store-product id; no stock, minimum stock, store SKU, lot/expiry flags, internal statuses, audit data; prices come with the price lists |
| Packaging status | `ACTIVE` / `INACTIVE` (database design left it open); conversion and the base flag are fixed after creation; the base packaging stays ACTIVE while others are |
| Sellable condition | Store Product creation and `mark-sellable` need an ACTIVE Product with an ACTIVE base packaging and at least one ACTIVE sale packaging |
| Category code | Not unique in the schema; Application rejects duplicates (case-insensitive) among non-deleted categories; tree cycles are rejected |
| Catalog deletes | Product DELETE = DISCONTINUED + store products deactivated (no soft delete: SKU and history stay); categories, brands and ingredients are soft deleted only when unused; a packaging used by prices, receipts, carts or orders cannot be deleted (set INACTIVE) |
| Ingredient re-add | Adding a removed ingredient revives the soft-deleted link (`ProductActiveIngredient.Reinstate`, protected `SoftDeletableEntity.Restore`) because the unique index counts deleted rows |
| Product images | `image_url` / `logo_url` accept absolute https URLs only; uploading to Supabase Storage is a later task behind `IFileStorageService` |
| Image storage | Supabase Storage through `IFileStorageService` (`SupabaseFileStorageService`, REST + server-side secret key); public bucket `product-images`; the database keeps only the URL (`image_url` / `logo_url`) |
| Storage configuration | `Storage:Url` and `Storage:Bucket` committed per environment; `Storage:SecretKey` only in User Secrets / `Storage__SecretKey`; new `sb_secret_` keys go in the `apikey` header only, legacy JWT keys also as bearer; not configured → HTTP 503 on use, never at startup |
| Receiving numbers | Receipt `GR-yyyyMMdd-NNNN`, stock movement `SM-yyyyMMdd-NNNN`; day = Vietnam day (UTC+7); number = highest of the day + 1; a clash on the unique receipt number answers 409 "try again"; `movement_number` has no unique index, so two simultaneous confirmations could share a number (documented limit, no migration) |
| Receiving permissions | Admin, Store Owner and Sales create/edit/cancel/delete DRAFT receipts and read stock; Sales may also confirm receipts (changed after review: confirmation is daily counter work); only Admin and Store Owner change lot status; suppliers: Admin/Owner write, Sales read; Delivery and Farmer have no access |
| Confirm goods receipt | One transaction owner (`GoodsReceiptConfirmer`): `SELECT … FOR UPDATE` on the receipt through `IRowLockService`, revalidate, find or create logical lots, `ReceiveStock`, STOCK_IN movement POSTED, receipt CONFIRMED, one SaveChanges. Version conflicts and unique-index races answer 409 |
| Receiving lot rules | Logical lot = store product + lot number (trimmed, case-insensitive) + expiry; products without lot tracking share one no-lot bucket. DEPLETED lot becomes ACTIVE on receiving; QUARANTINED/BLOCKED/EXPIRED keep their status. Expired goods, DISCONTINUED products and non-purchase packagings are refused; `requires_expiry_date` implies `requires_lot_tracking` |
| Receiving scope | Manual entry only; Excel import and reversal of a confirmed receipt are separate tasks |
| Image upload rules | Admin and Store Owner only; JPEG/PNG/WebP decided by magic bytes (not name or Content-Type); max 3 MB; server-generated name `yyyy/MM/<guid>.<ext>`; only generated keys can be deleted; 30 uploads/min per IP; errors map to 400 (bad file) or 503 (storage) without provider details |
| Delivery photo upload | `POST /api/files/delivery-proofs` for every staff role (delivery staff take the photos), `DELETE` only Admin/Store Owner; separate public bucket `delivery-proofs` (`Storage:DeliveryProofBucket`); same rules as product images but max 5 MB; `IFileStorageService` takes a `StorageArea` (ProductImages / DeliveryProofs). The URL is saved later as `delivery_attempts.proof_image_url` / `delivery_incidents.evidence_image_url`; the delivery use cases must refuse to delete a photo that an attempt or incident references |
| Local secrets file | Optional `src/AgriSage.Api/appsettings.Local.json` (template `appsettings.Local.example.json`, empty values): gitignored, excluded from build output and publish, read only in Development and added last (overrides User Secrets and environment variables); servers use environment variables only. `CommittedConfigurationTests` skips the real file and requires the template to be empty |
| Sales API contract | Orders, fulfillment and delivery routes, bodies, roles and the interfaces `IPriceResolver`, `IOrderSettlementGuard`, `IFulfillmentFinancialPosting` are fixed in `docs/reference/API_CONTRACT_SALES.md`; order confirmation and pickup = Admin/Owner/Sales; delivery staff use `/api/deliveries` scoped to their assignments; numbers `OD-yyyyMMdd-NNNN` / `DL-yyyyMMdd-NNNN`; PREPARING optional. Settled there (2026-10-02): FULL_PAYMENT orders are confirmed only when PAID order payments cover the total; deliveries assigned by staff user id; price override by Admin/Owner/Sales with reason; FEFO = earliest expiry, undated lots last, then oldest lot. Contract changes go through a PR on that file first |
| Customers/credit API contract | Group B routes and the real implementations of `IPriceResolver`, `IOrderSettlementGuard`, `IFulfillmentFinancialPosting`, plus `ICreditReservationAdjuster`, `IDebtRepaymentPosting`, `IDebtReturnPosting` (for C) and `IOrderPrepaymentLedger` (from C) are in `docs/reference/API_CONTRACT_CUSTOMERS_CREDIT.md`; customer id = `farmerProfileId`; debt entries `DE-yyyyMMdd-NNNN`; all its decisions B-D1..B-D6 are settled (2026-10-03) |
| Payments/inventory/returns API contract | Group C routes and `IOrderPrepaymentLedger`, `IOrderPaymentCancellation` are in `docs/reference/API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md`; C1 (cash payments) is done first because FULL_PAYMENT confirmation needs it; numbers PM-/ST-/RT-/RF-; all its decisions C-D1..C-D9 are settled (C-D5: stocktake completion and manual adjustments stay Admin/Store Owner because they can decrease stock; C-D6: only stale stocktake lines are refreshed and recounted) |
| payOS integration | Official SDK `payOS` 2.1.0 only inside `Infrastructure/Payments/PayOsPaymentGateway` (approved new package); webhook verified by the SDK, idempotent under a row lock, exact amount only; a missed webhook is recovered by `POST /api/payments/{id}/sync` (status query → applied like the webhook, `confirmedVia = STATUS_QUERY`); payOS amounts are whole VND (fraction paid in cash); every link expires (`PayOS:LinkExpiryMinutes`, default 30); secrets only in User Secrets / environment |
| Payment target and cancelled-order refunds | Migration `PaymentOrderLinkAndOrderRefunds` (first schema change after InitialCreate, still 67 tables, design §35.18): `payments.order_id` (ORDER_PAYMENT ⇒ NOT NULL, DEBT_REPAYMENT ⇒ NULL; an ORDER payment is allocated only to its order); `refunds.sales_return_id` nullable + `refunds.order_id`, exactly one source, a cancelled-order refund needs its original payment and is owned by the Order aggregate. payOS debt repayments are allocated oldest due first. Every new migration must be added to the reviewed list in `InitialCreateMigrationTests` |
| Stocktake stale lines | Migration `StocktakeItemSnapshotTime` (design §35.19): `stocktake_items.snapshot_at` (back-filled from `created_at`, no default). A line is stale when a POSTED movement for its lot has `posted_at` in (snapshot_at − 5 min, counted_at]; this catches offsetting movements, movements after the count do not count. Stale lines block completion and are re-snapshotted alone (`Stocktake.RefreshItem`) |
| Bulk EF APIs | `ExecuteDelete*` forbidden for business entities; `ExecuteUpdate*` forbidden for normal business mutations unless reviewed (they bypass the interceptors) |
| Reference seed scope | Only 5 English-named roles, 9 approved units (BOTTLE/BOX/CARTON/BAG/PACK/KG/GRAM/LITER/ML), 5 Rice disease classes (HEALTHY alone is healthy; knowledge fields NULL), one configured Store; no users or business transactions |
| Reference seed activation | Api `--seed` runs once and exits before HTTP startup; never automatic startup seed or migration seed; Supabase seed execution requires separate explicit approval |
| Reference seed persistence | Infrastructure `Persistence/Seed/DatabaseSeeder` owns one transaction and one SaveChanges; existing transaction uses an isolated savepoint without commit/nested transaction (rollback-only RealDb tests); natural-code lookup includes soft-deleted rows, fails on deleted matches, preserves live rows |
| Initial dev Store seed | `Seed:Store` holds approved temporary development data: AGRISAGE-DEV / AgriSage Dev Store / Dev address - to be replaced / Can Tho; optional fields NULL; not production information and no hard-coded Store ID; a different ACTIVE store blocks seed |

Full rationale for the rows above: `docs/reference/DATABASE_DESIGN.md` §XXXV,
`docs/reference/BACKEND_CODING_RULES.md` #61–#64.
Reference seed requirements: `docs/agent/requests/SEED_REFERENCE_DATA.md`; approved units, English role
names, temporary dev Store values and `--seed` activation were confirmed by the team on 2026-09-30.

If a task proposes changing one of these decisions, treat it as an architecture/business decision,
not a normal implementation detail.
