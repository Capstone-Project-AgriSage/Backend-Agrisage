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
Routes, bodies and cross-module interfaces of orders/fulfillment/delivery (group A): `docs/reference/API_CONTRACT_SALES.md`.
Routes, bodies and interfaces of customers/addresses/groups/price lists/credit/debt (group B): `docs/reference/API_CONTRACT_CUSTOMERS_CREDIT.md`.
Routes, bodies and interfaces of payments/payOS/stocktake/adjustments/returns/refunds (group C): `docs/reference/API_CONTRACT_PAYMENTS_INVENTORY_RETURNS.md`.
