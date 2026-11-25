# Exchanges (2026-11-25 user-requested extension)

Exchange owns one existing SalesReturn and replacement snapshots. The original order and invoice remain immutable.
Only COMPLETED orders are eligible. There is no additional time window in the current return policy.
Return quantities include all active returns and exchanges, serialized on the original order.

Staff routes (`ApiRoles.Operate`, dynamic EXCHANGES permissions):
- GET/POST `/api/exchanges`; GET `/api/exchanges/{id}`.
- POST `/{id}/approve`, `/reject`, `/cancel`, `/receive`, `/complete-inspection`, `/complete`.
- PUT `/{id}/items/{itemId}/inspection` uses the existing ReturnInspectionRequest.
- GET `/api/orders/{id}/exchangeable` uses ReturnableResponse.

Farmer routes (FARMER, JWT ownership, MY_EXCHANGES permissions):
- GET/POST `/api/me/exchanges`; GET `/{id}`; POST `/{id}/cancel`.
- GET `/api/me/orders/{id}/exchangeable`.

Create: `{ orderId, requestKey: uuid, reason, items: ReturnItemRequest[], replacements: [{ storeProductId, productPackagingId, quantity }] }`.
requestKey is unique per store and requester; retries return the existing exchange; a different payload conflicts.
Prices and conversion are resolved and snapshotted by the server, with no client price override.
Lists use pagination, optional orderId/status/search. List responses retain the existing pagination fields and add
`summary: { receivedAmount, refundedAmount, netReceivedAmount }`. This summary covers all records matching the
same store, ownership and search/status/order filters, before pagination. Only COMPLETED exchanges contribute:
positive difference = cash received, negative difference = cash refunded, net = received − refunded.
Pending settlement and EXCHANGE_CREDIT offsets are excluded. Empty results have zero totals.
 Details include linked return, replacement lines,
replacement order id, signed difference and append-only audit history. Farmer views redact internal costs and history actor identifiers.

States: REQUESTED → APPROVED → RECEIVED → READY_TO_COMPLETE → COMPLETED.
REQUESTED → REJECTED or CANCELLED; APPROVED → CANCELLED. Farmers cancel only REQUESTED.
Approval, rejection, inspection completion and settlement default to owner/admin; sales staff may receive/inspect.
Approval checks replacement availability without reserving. Completion requires staff-confirmed physical lots,
revalidates availability under locks, creates and fulfills the replacement order atomically.

Inspection posts the whole return value once to the existing idempotent debt step, including multi-line requests.
Inspection reuses Return processing: RESELLABLE restocks at original cost; DAMAGED/EXPIRED/UNUSABLE
are recorded as WRITE_OFF and never enter sellable stock. Original unpaid debt is reduced first.
Available exchange credit = totalRefundAmount. Difference = replacement value − exchange credit.
Positive = cash received; negative = cash refunded; zero = equal exchange. Staff confirms exact signed amount
and a settlement reference in Complete: `{ confirmedDifference, settlementReference, lots: [{ replacementItemId, inventoryLotId, baseQuantity }] }`.
The exchanged value is paired EXCHANGE_CREDIT Refund/Payment records (noncash), with separate CASH difference records.
Cashflow reports exclude EXCHANGE_CREDIT. Generic return/refund mutations cannot change an exchange's return.
Each mutation owns one transaction and one SaveChanges; locks original order → return → farmer → lot balances.
Repeated completion returns the completed result and never posts again.

Documents: RT return, RF offset/refund, PM offset/payment, SM RETURN_IN/SALE, OD replacement order.
The existing invoice API can issue an invoice for the completed replacement order. The original invoice is preserved;
this extension does not send or issue a legal adjustment invoice automatically.
Errors follow existing 400/401/403/404/409/422 contracts. New schema is additive and applied through reviewed EF migration.


## Implementation and rollout

- Migration: `20261125060116_SalesExchanges`, following the local SalesInvoices migration.
- Adds `sales_exchanges`, `exchange_items`, 12 permissions and their role defaults; expands only the two method CHECKs.
  No original-order or existing invoice rows are rewritten. Downgrade refuses to delete recorded exchanges or their financial records.
- Applied to the configured Supabase development DB on 2026-11-25 at 13:33 (Asia/Saigon), following the user's
  explicit migration request. EF Core applied only `20261125060116_SalesExchanges` in a transaction.
- The database's existing `20261124232200_DebtFollowUps` history entry is absent from this checkout and was preserved.
  Preflight confirmed that only Exchange was pending locally, the new tables/permissions did not already exist,
  and both method CHECKs matched the reviewed baseline. Do not regenerate or erase the database-only history.
- Post-apply checks passed: both Exchange tables query through EF, all 12 permissions are active, default grants
  are ADMIN 9 / STORE_OWNER 9 / SALES_STAFF 5 / FARMER 3, the unique indexes and EXCHANGE_CREDIT CHECKs exist,
  and no local migration remains pending. Existing business rows were not rewritten by this migration.
- The reviewed SQL and deployment verification report are in the workspace `outputs/exchange-deploy` directory.
  Other environments still use the existing EF migration workflow after their own preflight checks.
  Deploy the API and both clients together so the new permissions and EXCHANGE_CREDIT values are understood.
- The API process currently running from the default bin directory is preserved. Verification uses the ignored
  `tests/AgriSage.IntegrationTests/bin/exchange-artifacts` directory to avoid its DLL locks.

Management entry: `/agent/exchanges` (Admin/Owner) or `/sales/exchanges` (Sales); completed orders also have
an Exchange row action. Farmer entry: the Exchange panel under the details of a completed order.
Cash collection/refund is confirmed by staff with a receipt reference; no payOS link or automatic external transfer
is created by this workflow. Replacement invoices use the existing invoice modal/API. Legal adjustment-invoice
submission is not automated by the existing invoice integration.

## Verification

```powershell
dotnet test --artifacts-path tests/AgriSage.IntegrationTests/bin/exchange-artifacts
# PostgreSQL test: new transaction-local schema, all migrations, all fixture data rolled back.
$env:AGRISAGE_DB_TESTS = '1'
dotnet test tests/AgriSage.IntegrationTests --no-build --artifacts-path tests/AgriSage.IntegrationTests/bin/exchange-artifacts --filter FullyQualifiedName~ExchangeDatabaseTests
Remove-Item Env:AGRISAGE_DB_TESTS
```

Both clients: TypeScript build, Vite build and oxlint. Browser regression:
`node scripts/run-browser-tests.mjs scripts/test-exchanges.mjs` in Management-Webs, with Playwright available
(or UI_PLAYWRIGHT_MODULE pointing to its index.mjs). The test starts a separate Farmer Vite instance on port 5287,
intercepts every API call, checks owner lifecycle, retry key, permission gating and Farmer create/cancel.
Screenshots go to the workspace outputs/exchange-ui folder. No browser test posts to the real backend.

Verified results for this change: 859 unit tests passed, 1,208 offline integration tests passed
(493 opt-in tests skipped); the isolated PostgreSQL exchange test passed separately with the real connection.
The latter covers multi-line debt settlement, additional cash, equal exchange, cash refund, stock disposition,
quarantined-lot refusal, ownership, rejected/cancelled quantity release, repeated requests/completion,
rollback on invalid picks and cashflow-report exclusion of exchange credit.
Both client builds/type checks passed; repository lint has existing warnings, and the new Exchange files pass targeted lint.
The mocked browser workflow passes with no page errors. Screenshot fixtures contain test data only.
