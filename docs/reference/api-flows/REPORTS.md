# Reporting API extension — 2026-10-09

User-requested completion of operational reports; revenue is the first priority. This adds routes without changing
the existing sales, debt, inventory or delivery report contracts. No database/schema change.

All routes are read-only `GET /api/reports/...`, `Manage` (ADMIN, STORE_OWNER). Resolve the active operational store;
an Owner must be an ACTIVE member of that store. Never accept a client-supplied store id. Dates are inclusive
Vietnam calendar days (UTC+7); date ranges are required, ordered and at most 366 days. Reject `9999-12-31`
because the exclusive next-day boundary cannot be represented; reject `0001-01-01` because its Vietnam midnight
cannot be represented in UTC. Unknown grouping/enum and empty UUID filters → 400.

| Route | Query | Purpose |
|---|---|---|
| `/revenue` | `fromDate`, `toDate`, `groupBy` = DAY (default), WEEK, MONTH, PRODUCT, CATEGORY, STAFF, CUSTOMER, CUSTOMER_GROUP, SOURCE, SETTLEMENT; optional `storeProductId`, `categoryId`, `staffUserId`, `farmerProfileId`, `customerGroupId`, `source`, `settlementType`; `page`=1, `pageSize`=20 (max 100) | Revenue trend and breakdown, including zero-value time buckets |
| `/revenue-summary` | Same date range and filters, without grouping/pagination | KPI totals and immediately preceding period of the same length; `netSalesChangePercent` is null when preceding net sales is zero |
| `/orders` | Date range; `groupBy`=STATUS (default), SOURCE, SETTLEMENT | Orders created in the period grouped by their current state/channel/settlement; order value is **not** recognized revenue |
| `/payments` | Date range; `groupBy`=DAY (default), METHOD, CONTEXT, STAFF | Confirmed receipts, including subsequently partially/fully refunded payments; completed refunds are separate cash outflows |
| `/purchases` | Date range; `groupBy`=DAY (default), SUPPLIER | CONFIRMED receipts by confirmation day; procurement value, not cash paid to suppliers (no supplier-payment ledger exists) |
| `/returns` | Date range; `groupBy`=DAY (default), CUSTOMER | COMPLETED returns by completion day; return value, debt reduction and requested refund value |
| `/refunds` | Date range; `groupBy`=DAY (default), METHOD, SOURCE | COMPLETED refunds by completion day, including return and cancelled-order sources |
| `/credit-exposure` | none | Current credit policy/status, outstanding debt, ACTIVE reservation remainder, total exposure and available credit per customer |

## Revenue response and definitions

`RevenueReportResponse`: `fromDate`, `toDate`, normalized `groupBy`, `rows`, `page`, `pageSize`, `totalCount`,
`totalPages`, `totals`. Each row has `key`, `label` plus all KPI fields below. Totals cover the entire filtered
report, independent of pagination. Empty result: zero totals, no dimension rows; time reports retain zero buckets.

KPI fields: `orderCount`, `fulfilledValue`, `costOfGoods`, `grossProfit`, `returnValue`, `netSales`,
`averageOrderValue`, `grossMarginPercent`. Recognize revenue on POSTED SALE movement posting day, never order
creation/confirmation/payment day; use design §35.22's weighted product price and round each movement/product line
before aggregation. Returns use COMPLETED return line values on completion day. `grossProfit` and margin are
before returns, as in `/sales`; they do not represent accounting net profit (operating expenses are not modeled).
`averageOrderValue` = fulfilledValue / distinct fulfilled orders, 0 when no sales. Margin = grossProfit /
fulfilledValue × 100, null when fulfilledValue is zero. Counts distinguish orders across all rows; summing row
order counts can overcount an order fulfilled in multiple periods or with multiple products.

DAY key = yyyy-MM-dd; WEEK key = Monday yyyy-MM-dd (edge weeks clipped to requested range); MONTH key = yyyy-MM.
Dimension keys are UUIDs or UPPER_SNAKE enum codes. Walk-in customer/group key = WALK_IN; missing group = UNGROUPED.
Customer/staff/product/category/group labels include archived reference names. Time rows ascend; dimension rows
sort by fulfilledValue descending then stable key. All rows are paged (including time rows).

`RevenueSummaryResponse`: dates, `current` KPIs, `previousFromDate`, `previousToDate`, `previous` KPIs,
`netSalesChangePercent` (2 decimal places) = (current net sales - previous net sales) / abs(previous net sales) × 100.
Reject a range whose immediately preceding equal-length period would begin at/before DateOnly.MinValue.
All revenue filters also apply to the preceding period.

## Operational response shapes

Period reports return `fromDate`, `toDate`, normalized `groupBy`, `rows` and `totals`.

- Orders rows: `key`, `label`, `orderCount`, `orderValue`; totals: `orderCount`, `orderValue`.
- Payments rows: `key`, `label`, `paymentCount`, `receivedAmount`; totals additionally include
  `orderPaymentAmount`, `debtRepaymentAmount`, `refundedAmount`, `netReceivedAmount` (received minus completed refunds
  in the period). Refunds may belong to payments received earlier; net receipts may be negative.
- Purchases rows: `key`, `label`, `receiptCount`, `purchaseAmount`; totals: receiptCount, purchaseAmount.
- Returns rows: `key`, `label`, `returnCount`, `returnAmount`, `debtAdjustmentAmount`, `refundAmount`; matching totals.
  `refundAmount` is the return's refund requirement, not proof that cash was refunded in this period.
- Refunds rows: `key`, `label`, `refundCount`, `refundedAmount`; matching totals. SOURCE = SALES_RETURN or ORDER.
- Credit response: `asOf` UTC timestamp, rows (`farmerProfileId`, `fullName`, `status`, `creditLimit`, `outstanding`,
  `reservedCredit`, `exposure`, `availableCredit`, `utilizationPercent`) and additive totals of the monetary fields.
  `exposure` = outstanding + sum(amountReserved - amountConsumed - amountReleased) of ACTIVE/PARTIALLY_CONSUMED reservations;
  available is max(limit - exposure, 0) only for ACTIVE profiles, otherwise 0; utilization is null for zero limit.

All grouping and monetary aggregation runs in SQL; only reference labels, date bucket formatting and KPI ratios
are computed after aggregation. No edits, ledger writes or external-provider calls occur in reporting.

## Calling the APIs

Bearer authentication is required. For example:

```http
GET /api/reports/revenue?fromDate=2026-10-01&toDate=2026-10-31&groupBy=DAY&page=1&pageSize=100
GET /api/reports/revenue-summary?fromDate=2026-10-01&toDate=2026-10-31
GET /api/reports/revenue?fromDate=2026-10-01&toDate=2026-10-31&groupBy=PRODUCT&source=COUNTER
GET /api/reports/payments?fromDate=2026-10-01&toDate=2026-10-31&groupBy=METHOD
GET /api/reports/purchases?fromDate=2026-10-01&toDate=2026-10-31&groupBy=SUPPLIER
GET /api/reports/credit-exposure
```

The existing reports remain available: `/sales`, `/deliveries`, `/debt-aging`, `/debt-collections`,
`/debt-by-customer-group`, `/inventory-movement`, `/inventory-valuation`. Together with the eight new routes,
there are 15 report endpoints. Interactive schemas and query parameters are in `/swagger` in Development.
