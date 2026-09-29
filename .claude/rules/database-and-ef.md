# Database and EF Core Rules

- `docs/reference/DATABASE_DESIGN.md` is authoritative for initial schema design.
- Implement schema through Domain entities + EF configurations + reviewed migrations.
- After `InitialMigration`, migrations are schema source of truth.
- Do not evolve schema manually in DBeaver.
- Match PostgreSQL types, precision, nullability, FK, checks, indexes, and partial indexes.
- Use `Restrict` / `NoAction` for business FK physical delete behavior unless explicitly approved.
- Use Global Query Filters for applicable soft-deletable entities.
- Timestamps, soft delete and `version` are set by the SaveChanges interceptors; business code never
  assigns them (DB design §35.15, coding rule #64).
- Never use `ExecuteDelete` / `ExecuteDeleteAsync` on business entities, and do not use
  `ExecuteUpdate` / `ExecuteUpdateAsync` for normal business mutations without explicit review:
  bulk APIs bypass the interceptors (timestamps, soft delete, version, audit).
- Physical inventory mutations require Stock Movement + Stock Movement Item.
- Accounts Receivable mutations require Debt Transaction.
- Confirmed/posted records are corrected with semantic cancellation/reversal/adjustment.
- Read-only EF queries use `AsNoTracking()`.
- Prefer SQL-side projection rather than materializing large graphs.
- Do not expose `IQueryable` to API.
- For schema work, open only the exact table/flow sections needed from the DB design.
- Review every generated migration before applying it.
- For migration review check destructive operations, FK delete actions, indexes, constraints,
  defaults, precision, provider-specific SQL, and data-loss risk.
