# Agent Context Index

Use progressive disclosure. Open only what the task needs.

| File | Read when |
|---|---|
| `PROJECT_OVERVIEW.md` | Understanding product scope, actors, current backend phase |
| `BUSINESS_RULES.md` | Implementing or reviewing business behavior |
| `BACKEND_MAP.md` | Deciding where code belongs / layer boundaries |
| `DATABASE_MAP.md` | Locating tables and schema sections |
| `WORKFLOW_MAP.md` | Implementing cross-module business flows |
| `DECISIONS.md` | Checking frozen architectural/technical decisions |
| `CURRENT_STATE.md` | Planning current implementation work |

For exact details, use authoritative files in `docs/reference/`.
Business API contracts are split by flow in `docs/reference/api-flows/`:
- `README.md` — conventions, shared response shapes, cross-flow interfaces, shared steps, folder ownership, old → new task ids.
- `FLOW_1_COUNTER_SALE.md` (L1) — price lists, counter orders, cash payments, confirmation/FEFO, pickup, cancellation, quick counter sale, sales report.
- `FLOW_2_ONLINE_ORDER_DELIVERY.md` (L2) — Farmer profile/addresses/customers, cart, online checkout, payOS, delivery notes, attempts/incidents, delivery report.
- `FLOW_3_CREDIT_DEBT.md` (L3) — customer groups, group price lists and credit tier, credit profiles, settlement guard, debt ledger, repayment, debt reports.
- `FLOW_4_INVENTORY_RETURNS.md` (L4) — stock summary/alerts, stocktake/adjustments, Excel receipt import, returns, refunds, stock card and inventory reports.
