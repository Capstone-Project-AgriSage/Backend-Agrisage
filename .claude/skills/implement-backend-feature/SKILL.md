---
name: implement-backend-feature
description: Implement an AgriSage backend feature or Jira task while preserving 4-layer boundaries, business rules, tests, and database invariants.
---

# Implement Backend Feature

Use when asked to implement a backend feature, endpoint, service, or Jira task.

## Procedure

1. Read the task/acceptance criteria.
2. Inspect existing feature files and call sites before editing. The reference implementation to copy is
   the Suppliers / GoodsReceipts / Inventory features (Application service + validators + contracts,
   controller, `GoodsReceiptConfirmer` for a transaction owner) and their unit, HTTP and real-DB tests.
3. Read `AGENTS.md`, then the section of your task in the matching `docs/reference/API_CONTRACT_*.md`:
   use its routes, DTO names/fields, roles and error codes exactly; implement cross-module interfaces with
   the signatures given there (and their temporary implementation until the owner delivers the real one).
   If the contract is wrong or incomplete, stop and propose a change to the contract file first.
4. Load only relevant context:
   - architecture placement → `docs/agent/context/BACKEND_MAP.md`
   - business behavior → `docs/agent/context/BUSINESS_RULES.md`
   - cross-module flow → `docs/agent/context/WORKFLOW_MAP.md`
   - schema needs → `docs/agent/context/DATABASE_MAP.md` then exact `DATABASE_DESIGN.md` section
5. Identify the use-case owner and transaction boundary.
6. Implement in the correct layers:
   - API: HTTP/controller only
   - Application: DTO/validator/service/orchestration
   - Domain: invariant/state behavior
   - Infrastructure: EF/provider implementation
7. Avoid unrelated refactors.
8. Add/update tests at the appropriate level.
9. Run relevant build/tests.
10. Review the diff against coding rules.

## Definition of Done

- Acceptance criteria are implemented.
- Architecture dependencies remain valid.
- No Entity is returned directly by API.
- No client-supplied business calculation is trusted.
- Inventory/debt changes preserve their audit ledgers.
- Transaction scope is atomic where required.
- Relevant tests/build pass, or unverified items are clearly reported.
