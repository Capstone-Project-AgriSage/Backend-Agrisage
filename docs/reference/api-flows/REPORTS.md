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

## Counter sales today — 2026-10-11

`GET /api/reports/counter-sales-today`, no query parameters. `Operate` roles plus `COUNTER_SALES.SELL`
(since 2026-11-16, was `REPORTS.READ`: whoever sells at the counter sees the counter's figures of the day;
every other report keeps `REPORTS.READ`), with the same active-store membership scope as other reports. Read-only; no schema change.
`CounterSalesTodayResponse`: `date` (Vietnam calendar day), `asOf` (UTC), `paidSalesRevenue`,
`completedOrderCount`, `receivedAmount`, `creditSalesAmount`. Empty activity returns zero for all four metrics.

- All four metrics refer to `COUNTER` orders of the active store, including counter orders fulfilled by delivery.
- `paidSalesRevenue`: gross fulfilled value of today's POSTED SALE movements for orders paid in full as of
  the report. Reuse design §35.22's movement/product rounding. Full payment means active ORDER allocations
  plus active DEBT allocations to that order's debt entries cover its total. Only confirmed payments count;
  include PAID, PARTIALLY_REFUNDED and REFUNDED statuses, as in the existing payment ledger. Returns are
  separate outflows and are not subtracted from this gross sales metric. Prepayment without fulfillment is not revenue.
- `completedOrderCount`: orders currently COMPLETED whose `CompletedAt` falls today, regardless of payment
  or settlement type; count each order once. Partial fulfillment is not a completed order.
- `receivedAmount`: confirmed ORDER_PAYMENT amounts received today for counter orders, plus the active DEBT
  allocation amounts from payments confirmed today attributed to those orders. Include CASH, BANK_TRANSFER
  and PAYOS. Count only the counter share of a debt payment split across counter and online orders. Receipts
  include subsequently refunded payments; refunds are separate outflows. Order creation date does not filter receipts.
- `creditSalesAmount`: positive POSTED CREDIT_SALE `DebtTransaction.AmountDelta` occurring today, joined
  through the DebtEntry to CREDIT counter orders. Exclude reservations, manual adjustments and subsequent
  repayments; this is debt originated today, not the current outstanding balance.

The backend clock defines today and the reporting cutoff. The frontend shows exactly four cards above the
counter workspace, supports refresh, and reloads after successful relevant business writes.

## Advisory and diagnosis — 2026-11-23

`GET /api/reports/advisory-diagnosis?fromDate&toDate`: `Manage` roles plus `REPORTS.READ`, the same active-store scope
and period rules as the other reports. Read-only; no schema change. It feeds the owner dashboard's
"Tư vấn & chẩn đoán AI" row.

`AdvisoryDiagnosisReportResponse`: `fromDate`, `toDate`, `attributionDays` (7), `advisory`, `topProblems`, `diagnosis`.

- `advisory`: `sessionCount`, `farmerCount`, `textSessionCount`, `diagnosisSessionCount`, `sessionsWithProducts`,
  `estimatedValue`, `convertedSessions`, `conversionPercent`, `convertedOrderValue`.
  - Sessions are the store's live `advisory_sessions` whose `created_at` falls on the Vietnam days of the period.
    `farmerCount` counts distinct Farmers; the two source counts split by `source` (TEXT, DIAGNOSIS_CASE).
  - `sessionsWithProducts` = sessions with at least one `advisory_session_items` row; `estimatedValue` = the sum of
    their `estimated_amount` (a missing amount counts 0). This is what the advice priced, not money received.
  - A session is **converted** when the same Farmer created an order of the store that is not CANCELLED, at or after
    the session time and within `attributionDays` × 24 h of it, with at least one order line for a store product the
    session suggested: the `boughtWithin7Days` rule of the staff session list (ADVISORY.md section 7). Nothing links an order to an advisory session, so this is an attribution, not proof that the
    advice caused the order; the dashboard says so. A partially cancelled order still counts. An order of a session
    near the end of the period may arrive after `toDate`, so recent figures can still grow.
  - `convertedOrderValue` = the sum of `line_total_amount` of those matching lines (suggested products only), each
    order line counted once even when several sessions match it. Order value, not recognized revenue.
  - `conversionPercent` = convertedSessions / sessionsWithProducts × 100 (2 decimals), null when no session had products.
- `topProblems`: at most 5 rows `diseaseId`, `code`, `name`, `cropType`, `sessionCount`; most sessions first, then code.
- `diagnosis`: `caseCount` and `byCrop` rows `cropType`, `cropName` (catalog name), `caseCount`, `confidentCount`,
  `uncertainCount`, `verifiedCount`, `noResultCount`.
  - Cases are `diagnosis_cases` whose `submitted_at` falls on the Vietnam days of the period. Diagnosis cases carry
    no store (one operational store, as in the staff case list), so every case counts.
  - The certainty is the one the Farmer reads (`DiagnosisResultPolicy.CertaintyOf` with the latest SUCCESS
    inference's `passed_policy`): CONFIDENT, UNCERTAIN, or VERIFIED for a case confirmed by the store before
    2026-10-21. `noResultCount` = everything else (submitted, processing, failed, cancelled, inconclusive).
  - Rows follow the crop catalog order and list only crops with a case; a crop code outside the catalog keeps its
    code as `cropName` and comes last.

## My sales — 2026-11-23

`GET /api/reports/my-sales?fromDate&toDate`: `Operate` roles plus `COUNTER_SALES.SELL` (like
`/counter-sales-today`, decision 2026-11-23), the same active-store scope and period rules. It answers only for the
signed-in user (from the token; no user id is accepted). Read-only; no schema change. It feeds the sales staff
dashboard's "Kết quả của tôi".

`MySalesReportResponse`: `fromDate`, `toDate`, `orders`, `sales`, `payments`.

- `orders`: `createdCount`, `completedCount`, `cancelledCount`, `openCount` for the store's orders with
  `created_by` = me and `created_at` on the Vietnam days of the period, by current status. CANCELLED and
  PARTIALLY_CANCELLED count as cancelled, COMPLETED as completed, every other status as open.
- `sales`: `orderCount`, `fulfilledValue`, `returnValue`, `netSales`, `averageOrderValue`, with the `/revenue`
  definitions filtered to orders I created (`staffUserId` = me). **No cost of goods, gross profit or margin**: sale
  staff do not see cost.
- `payments`: `paymentCount`, `receivedAmount`, `orderPaymentAmount`, `debtRepaymentAmount` for the store's payments
  with `confirmed_by` = me, confirmed on the Vietnam days of the period, status PAID, PARTIALLY_REFUNDED or REFUNDED
  (as `/payments`). Gross receipts; refunds are not subtracted. payOS payments confirmed by the webhook have no
  confirming person and are not anyone's.

## Calling the APIs

Bearer authentication is required. For example:

```http
GET /api/reports/revenue?fromDate=2026-10-01&toDate=2026-10-31&groupBy=DAY&page=1&pageSize=100
GET /api/reports/revenue-summary?fromDate=2026-10-01&toDate=2026-10-31
GET /api/reports/revenue?fromDate=2026-10-01&toDate=2026-10-31&groupBy=PRODUCT&source=COUNTER
GET /api/reports/payments?fromDate=2026-10-01&toDate=2026-10-31&groupBy=METHOD
GET /api/reports/purchases?fromDate=2026-10-01&toDate=2026-10-31&groupBy=SUPPLIER
GET /api/reports/credit-exposure
GET /api/reports/advisory-diagnosis?fromDate=2026-11-01&toDate=2026-11-30
GET /api/reports/my-sales?fromDate=2026-11-01&toDate=2026-11-30
```

The existing reports remain available: `/sales`, `/deliveries`, `/debt-aging`, `/debt-collections`,
`/debt-by-customer-group`, `/inventory-movement`, `/inventory-valuation`. Together with the eight new routes,
`/counter-sales-today`, `/advisory-diagnosis` and `/my-sales`, there are 18 report endpoints. Interactive schemas and query parameters are in `/swagger` in Development.
