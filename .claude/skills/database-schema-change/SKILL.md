---
name: database-schema-change
description: Implement or review an AgriSage EF Core schema change, entity mapping, constraint, index, or migration against the approved database design.
---

# Database Schema Change

Use for Domain entity mapping, EF configuration, migrations, indexes, FK, constraints, or schema review.

## Procedure

1. Identify the affected table(s) and business workflow.
2. Open the exact table section in `docs/reference/DATABASE_DESIGN.md`.
3. Also inspect:
   - global DB conventions;
   - relevant posting/invariant section;
   - migration relationship/FK policy;
   - index plan when applicable.
4. Update Domain entity only for domain/persistence state that belongs there.
5. Update `IEntityTypeConfiguration<T>` with exact PostgreSQL mapping.
6. Preserve:
   - precision/nullability;
   - partial unique indexes;
   - check constraints;
   - concurrency;
   - `Restrict/NoAction`;
   - soft delete behavior.
7. Generate a migration only when the code model is ready.
8. Read the migration before applying it.
9. Check for unexpected drops, destructive changes, cascade deletes, wrong defaults,
   lost indexes/constraints, and provider-specific SQL issues.
10. Run migration/integration tests when environment permits.

## Never

- manually alter the shared schema in DBeaver as the implementation method;
- edit an already-applied shared migration without an explicit team decision;
- invent a missing business rule to make the migration compile.
