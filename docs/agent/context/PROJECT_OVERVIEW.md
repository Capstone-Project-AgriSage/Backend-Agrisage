# Project Overview — Agent Context

## Product

AgriSage is an agricultural-input store management and advisory system combining:
- product/catalog and customer pricing;
- supplier receiving and lot/expiry inventory;
- retail/counter and Farmer orders;
- payment, credit, accounts receivable, delivery and debt collection;
- return/refund tracking;
- AI-assisted rice disease diagnosis with Human Review.

## Human Roles

Primary roles:
1. FARMER
2. STORE_OWNER
3. SALES_STAFF
4. DELIVERY_STAFF
5. ADMIN

AI Reviewer is not a sixth role.
Review capability is granted to eligible Store Members through `can_review_ai`.

## Main Applications

```text
Farmer Web        → React
Store/Admin Web   → React
Farmer Mobile     → Flutter
Staff Mobile      → Flutter
Backend           → ASP.NET Core + EF Core
Database          → PostgreSQL / Supabase
AI Service        → Python + FastAPI + PyTorch
Payment           → payOS
```

## Store Scope

The current product operates one physical/operational agricultural-input store.
The `stores` table remains in the schema and Store ID is never hard-coded.

## Current Delivery Phase

Current backend phase: `Business APIs — split by flow` (see `CURRENT_STATE.md` and
`docs/reference/api-flows/`). The foundation phase below is complete.

Result of the foundation phase:
- four-layer solution;
- common foundations;
- 67 Domain entities;
- EF Core mappings;
- soft delete/audit/concurrency foundation;
- reviewed `InitialCreate`;
- Supabase schema;
- seed reference data;
- DB foundation integration tests.

Business APIs are implemented in later feature Epics.

## Authoritative References

- `docs/reference/DATABASE_DESIGN.md`
- `docs/reference/BACKEND_ARCHITECTURE.md`
- `docs/reference/BACKEND_CODING_RULES.md`

This file is a summary only.
