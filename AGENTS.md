# AGENTS.md — AgriSage Repository Guide

## Purpose
Short, always-loaded briefing for AI coding agents.
Detailed context: `docs/agent/context/`.
Authoritative specs: `docs/reference/`.
Keep this file high-signal; it is a map, not the project encyclopedia.

## Project Snapshot
- AgriSage: agricultural-input store management + AI advisory.
- Backend: ASP.NET Core + EF Core.
- DB: PostgreSQL/Supabase; DBeaver is inspection/debug only.
- Web: React. Mobile: Flutter.
- AI: Python + FastAPI + PyTorch.
- Payment: payOS.
- Current phase: **Business APIs, split by flow** — L1 counter sale, L2 online order & delivery, L3 credit & debt, L4 inventory & returns, in parallel; details in `docs/agent/context/CURRENT_STATE.md`.
- DB baseline: **67 tables**.
- One active operational Store; never hard-code Store ID.

## Sources of Truth
Use this precedence:
1. Current user/Jira task + approved acceptance criteria.
2. `docs/reference/DATABASE_DESIGN.md`.
3. `docs/reference/BACKEND_ARCHITECTURE.md`.
4. `docs/reference/BACKEND_CODING_RULES.md`.
4b. `docs/reference/api-flows/` — routes, DTOs, roles, error codes, cross-flow interfaces and file ownership of the business APIs (the design wins on conflict; change a contract by PR on the file first).
5. Accepted existing code/tests.
6. `docs/agent/context/*` summaries.

If authoritative sources materially conflict, report the conflict instead of silently choosing.

## Backend Shape

```text
src/
├── AgriSage.Api
├── AgriSage.Application
├── AgriSage.Domain
└── AgriSage.Infrastructure

tests/
├── AgriSage.UnitTests
└── AgriSage.IntegrationTests
```

Dependencies:

```text
Domain
↑
Application
↑
Api

Infrastructure → Application + Domain
Api → Infrastructure only for composition/DI
```

Never add reverse dependencies.

## Feature Organization
Organize each layer by business feature:

```text
Auth, Users, Stores, Customers,
Products, Pricing,
Suppliers, GoodsReceipts, Inventory,
Orders, Payments, Deliveries,
Credit, Debt, Returns,
Diagnosis, Articles, Notifications, Reports
```

A feature is a business area, not one table.

## Layer Responsibilities

### API
HTTP boundary only:
- receive route/query/body;
- authorize;
- call Application;
- return HTTP response.

Never query DbContext, calculate business values, own transactions,
mutate entities directly, or call provider SDKs.

### Application
Owns use-case orchestration:
- load data;
- validate business state/ownership;
- coordinate Domain entities;
- call external abstractions;
- own cross-feature transaction boundaries;
- return response DTOs.

### Domain
Owns entities, enums, invariants, state transitions, pure domain behavior.
Must not know DTOs, HTTP, EF implementation, PostgreSQL, payOS, FastAPI, or storage SDKs.

### Infrastructure
Owns EF Core, mappings, migrations, interceptors, seeders,
JWT/password implementation, and external-provider adapters.
It does not own business policy.

## DTO Contract

```text
Controller → Request DTO → Application Service → Domain/DB
Domain/DB → Application Service → Response DTO → Controller
```

Never return Domain entities directly.
Request/response DTOs are normally separate.
DTOs contain data, not business calculations.

## Database Workflow

```text
DATABASE_DESIGN.md
→ Domain Entities
→ EF Configurations
→ reviewed EF Migration
→ Supabase PostgreSQL
```

After `InitialMigration`, migrations are schema source of truth.
Do not manually evolve schema in DBeaver.
One migration at a time across the team (announce it to the lead first); every new migration is added to the
reviewed list in `InitialCreateMigrationTests` and gets its own review test.
For DB work, open only exact relevant sections of the full design.

## Non-Negotiable Invariants
- DELETE API = soft delete or semantic cancel/reverse.
- Physical stock changes require `StockMovement` + `StockMovementItem`.
- AR/debt changes require `DebtTransaction`.
- Confirmed/posted financial/inventory records are not silently edited.
- Inventory core uses base-unit quantities.
- Expired/blocked/quarantined Lots cannot be reserved/allocated/sold.
- FEFO suggests Lots; staff confirms actual physical Lots.
- Costing = Weighted Average Cost per logical Inventory Lot.
- Walk-in cannot use individual credit/debt.
- Credit exposure includes outstanding AR + active credit reservations.
- Debt is created only for unpaid value after successful credit fulfillment.
- Payment↔Debt allocation uses `payment_allocations`.
- payOS webhook processing is idempotent.
- AI recommendation requires authorized Human Review.
- AI reviewer = `can_review_ai` permission, not a primary role.
- Production AI classes: Leaf Blast, Bacterial Leaf Blight, Brown Spot, Sheath Blight, Healthy.
- Promotions are out of scope.

Full rules: `docs/agent/context/BUSINESS_RULES.md`.

## Transaction Ownership

```text
Confirm Goods Receipt → GoodsReceipt use case
Confirm Delivery      → Delivery/Fulfillment use case
Collect Debt Payment  → Debt/Payment use case
Complete Return       → Return use case
```

One cross-feature operation has one transaction owner.
No independent nested transactions or midway saves inside one atomic operation.

## Persistence
- Read-only EF queries use `AsNoTracking()`.
- Prefer SQL-side projection to DTOs.
- Do not expose `IQueryable` outside Application.
- No generic repository that merely wraps EF CRUD.
- Normal abstraction: `IAgriSageDbContext`.
- Business FK delete behavior defaults to `Restrict` / `NoAction`.
- Respect exact DB precision, indexes, partial indexes, checks, and concurrency rules.

## External Integrations

```text
payOS          → IPaymentGateway
FastAPI AI     → IAiDiagnosisClient
Object Storage → IFileStorageService
Clock          → IDateTimeProvider
Current user   → ICurrentUserService
```

Application depends on interfaces, never provider concrete classes.

## Validation / Security
Use three levels:
1. DTO/request validation.
2. Application/Domain business validation.
3. DB structural constraints.

Never trust client-supplied price, totals, role, identity, stock, credit, or debt.
Identity comes from JWT/current-user abstraction.
Authorization checks role + ownership + special permissions.
Never commit or log secrets.

## Verification
Run relevant checks when implementation exists:

```bash
dotnet restore
dotnet build
dotnet test
```

For schema work, inspect generated migration before applying it.
Never claim verification that was not run.

## Progressive Context Loading
Do not read every document before every task.

```text
Scope/product      → PROJECT_OVERVIEW.md
Business behavior  → BUSINESS_RULES.md
Layer placement    → BACKEND_MAP.md
Tables/schema map  → DATABASE_MAP.md
Cross-module flow  → WORKFLOW_MAP.md
Frozen decisions   → DECISIONS.md
Current phase      → CURRENT_STATE.md
API routes/DTOs    → docs/reference/api-flows/README.md (conventions, interfaces, who owns what), then only
                     your flow file FLOW_1..FLOW_4 and only your task's section (task ids F1.1..F4.6)
Code pattern       → Suppliers / GoodsReceipts / Inventory features and their tests (reference feature)
Exact details      → docs/reference/*
```

All context files are under `docs/agent/context/`.

## Execution Protocol
Before editing:
1. Read task + acceptance criteria.
2. Inspect relevant code.
3. Load only relevant context/rules.
4. Open exact DB sections for schema-sensitive work.
5. Identify transaction owner and unresolved decisions.

While editing:
1. Keep scope surgical.
2. Follow feature structure.
3. Preserve boundaries/invariants.
4. Reuse existing patterns before adding abstractions.
5. Do not redesign unrelated code.

Before finishing:
1. Review diff.
2. Run relevant build/tests.
3. Check architecture/rule compliance.
4. Report DB/migration impact.
5. Summarize changes and verification.

## Skills / Rules
Canonical:
- rules: `.agents/rules/`
- skills: `.agents/skills/`

Initial skills:
- `implement-backend-feature`
- `database-schema-change`
- `review-backend-change`

Claude-native mirrors live in `.claude/`; Codex skill mirrors in `.codex/skills/`.
Use `scripts/sync-agent-assets.ps1` after editing canonical rules/skills.

## Keep This File Healthy
Keep it compact and project-specific.
Do not paste full architecture/database docs or generic C# knowledge.
Put detailed knowledge in context/reference docs.
Put repeatable procedures in skills.
Promote permanent rules only when stable and repeatedly useful.
