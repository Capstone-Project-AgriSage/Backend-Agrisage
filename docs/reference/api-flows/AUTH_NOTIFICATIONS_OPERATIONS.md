# Auth, notifications and operation history (2026-10-07)

User-requested extension; Google login, dynamic permissions, AI and report exports are excluded.
Existing roles, ownership checks and business ledgers remain authoritative.

## Authentication contract

Register/login keep their routes and existing response fields. They additionally return `refreshToken`,
`refreshExpiresAt` and `sessionId`. Access JWTs carry `sid` and `sv` (security version). Every authenticated
request checks the live user, role and session; old JWTs without a session are invalid after deployment.
Passwords, refresh tokens, verification tokens and OTPs never appear in audit/logs.

| Method | Route | Authorization / response |
|---|---|---|
| POST | /api/auth/refresh | Anonymous; `{ refreshToken }`; rotated AuthResponse |
| POST | /api/auth/logout | Authenticated; revoke current session; 204 |
| POST | /api/auth/logout-all | Authenticated; revoke all sessions; 204 |
| GET | /api/auth/sessions | Authenticated; live own sessions only |
| DELETE | /api/auth/sessions/{id} | Authenticated; revoke own session; 204, other's id = 404 |
| POST | /api/auth/forgot-password | Anonymous; `{ identifier }`; uniform 202 |
| POST | /api/auth/reset-password | Anonymous; `{ token, newPassword }`; single-use token; 204 |
| POST | /api/auth/email-verification/request | Authenticated; current email only; 202 |
| POST | /api/auth/email-verification/confirm | Authenticated; `{ token }`; 204 |
| POST | /api/auth/phone-verification/request | Authenticated; current phone only; 202 |
| POST | /api/auth/phone-verification/confirm | Authenticated; `{ code }` (6 digits); 204 |

Refresh tokens: 256 random bits, hashes only, single-use rotation, absolute session lifetime (default
30 days). Reusing a consumed token revokes its entire session, including successor access tokens.
Password change/reset, staff password reset, account status/role/contact changes advance the user's
security version. A lock followed by unlock never revives old sessions. Contact changes clear the
corresponding verified flag. Concurrent security mutations use row locks or optimistic concurrency.

Email confirmation and password-reset tokens (including future SMS resets) include a challenge id
and 256-bit secret. SMS phone verification uses
6-digit OTPs, keyed hashes, 5 attempts, a 10-minute lifetime and a 60-second resend cooldown. All
challenge types are single-use, bound to purpose, destination and security version. Reset requests
are generic for unknown/inactive accounts; delivered messages carry the actual token. Reissuing a
challenge invalidates its predecessor. Expired/used tokens cannot be accepted.

Email uses configured SMTP with TLS. SMS uses a configured HTTPS gateway and can be configured later.
Missing delivery configuration returns 503; it never returns a token or silently reports delivery.
Anonymous reset requests always acknowledge with 202 even if a configured provider fails; failures
are audited, the unsent challenge is invalidated, and no account-existence signal is returned.
All anonymous and challenge endpoints use the existing auth rate limiter. No account is forced to
verify contact just to use existing workflows in this release.

## In-app notifications

All roles may access their own notifications; the user id comes from the current-user abstraction.

| Method | Route | Contract |
|---|---|---|
| GET | /api/me/notifications | Pagination; optional `status`; excludes archived by default |
| GET | /api/me/notifications/unread-count | `{ count }` |
| POST | /api/me/notifications/{id}/read | Idempotent; own notification; 204 |
| POST | /api/me/notifications/read-all | Mark own unread notifications; 204 |
| DELETE | /api/me/notifications/{id} | Semantic archive; 204 |

Response: id, notificationType, title, message, data (JSON metadata), status, readAt, createdAt.
Other users' ids return 404. Archive is terminal; reading twice preserves the initial read time.
No public arbitrary-send endpoint. Business audit events create a notification outbox row in the same
SaveChanges/transaction. The worker delivers order status, confirmed payment, delivery assignment/
completion and credit-limit changes. Outbox retries and deduplication prevent duplicate notifications.

Successful Farmer checkout (`POST /api/me/orders`, Web or Mobile) records `ORDER_PLACED` with its
outbox in the same transaction as the order and cart conversion. It delivers an `ORDER_PLACED`
notification, "Đơn hàng mới chờ xác nhận", to active STORE_OWNER and SALES_STAFF members of that
order's store whose accounts and primary roles are active. Farmers, delivery staff and members of
other stores do not receive this staff alert. Failed checkout creates no event; retries deliver at
most one notification per recipient/event. Existing order-confirmed events still notify the Farmer.
Orders created before this event was added are not backfilled automatically. No schema migration is needed.

### Recipient coverage (user-approved extension, 2026-10-09)

| Trigger | Owner | Sale | Farmer |
|---|---|---|---|
| Farmer checkout / pending confirmation | Yes | Yes | — |
| Staff-created pending order | Yes | Yes | — |
| Completed counter sale / cancellation of remaining items | Yes | Yes | Own order |
| Order cancellation | Yes | Yes | Own order |
| Order confirmation, preparation, ready, pickup | — | Yes | Own order |
| Delivery created / needs delivery | — | Yes | — |
| Dispatch, successful/partial/failed delivery attempt, cancellation | — | Yes | Own order |
| Successful order payment / failed or rejected payment | — | Yes | Own payment |
| Confirmed debt repayment | Yes | Yes | Own payment |
| New debt / manual debit | — | — | Own debt |
| Farmer debt dispute | Yes | Yes | — |
| Debt due soon / overdue (daily) | Yes | Yes | Own debt |
| Low stock / out of stock (daily) | Yes | Yes | — |
| Expiring / expired lot (daily) | Yes | — | — |
| Posted receipt, sale issue, return receipt, adjustment, reversal | Yes | — | — |
| Return or refund requested | Yes | Yes | — |
| Return approved, rejected, cancelled, inspected; refund completed, failed, cancelled | — | — | Own order |
| Credit limit changed | Yes | — | Own credit profile |
| Persisted successful AI result | — | — | Own diagnosis |
| Current authorized human review / approved recommendations | — | — | Own diagnosis |

Recipients must have active accounts and active, non-deleted primary roles. Staff must be active
members of the event's store; delivery assignments still target the assigned active member. Missing,
deleted or mismatched entities never disclose an event to another store. Delivery outcome is taken
from the immutable audit snapshot, so a later successful retry does not hide an earlier failure.
Metadata may include `orderId` for navigation; it is resolved on the server, never client supplied.

The notification worker also reads committed posted stock movements and persisted diagnosis results
in bounded batches. Stable per-record keys survive retries/archive/soft deletion and allow existing
records without a notification to be delivered. It never posts stock, invokes AI or approves reviews.
AI APIs/provider processing are not implemented yet; these alerts require real persisted results.
The Farmer diagnosis-history page still uses demo data. Diagnosis alerts show their notification
content without linking to that demo page until a real owned diagnosis read API/UI is implemented.
Recommendations require a current verified review by an active member with `can_review_ai`, and
approval by an active authorized reviewer. Raw AI output never creates product recommendations.
Debt reminders and inventory alerts must be enabled; they do not mutate inventory/debt ledgers.

## Background operations

Infrastructure hosts the timer only; Application owns each operation. PostgreSQL advisory locks keep
one execution per task across application replicas. Jobs are configurable and disabled by default
until the migration/configuration is deployed. Runs use bounded batches and fresh dependency scopes.

- Dispatch notification outbox, retry transient failures with bounded backoff.
- Mark due lots expired through the existing inventory use case; no physical quantity changes.
- Notify Farmers and Owner/Sale staff of due/overdue outstanding debt; Owner/Sale of low/out-of-stock products;
  Owners of expiring/expired lots. Read committed stock movements and diagnosis results for missing notifications.
  Daily deduplication uses the Vietnam business date. Debt reminders never change balances/status/ledgers.
- Reconcile pending payOS payments through the existing settlement path (same checks/locks/idempotency
  as webhook/manual sync); external provider calls stay outside DB transactions.
- Semantically expire auth sessions/challenges; no physical deletion.

## Operation history

GET `/api/audit-logs` and `/api/audit-logs/{id}`: ADMIN or STORE_OWNER only. Pagination and optional
action/entityType/entityId/actorUserId/from/to filters. ADMIN can inspect all events; STORE_OWNER can
inspect only the active store's events while being an active store member, excluding global authentication
events. Responses expose the existing audit metadata/JSON, never credentials. No update/delete APIs.

## Deployment and verification

One reviewed migration expands the 67-table baseline to 71 tables. Update the database design first.
Do not auto-migrate/seed at startup. Configure SMTP secrets via User Secrets/environment variables,
apply the reviewed migration through the normal team process, then enable jobs. New schema features
require migration/model, Domain, HTTP authorization/validation, provider and transactional tests.

### Configure email first

Set these through User Secrets or deployment environment variables. The committed local template
contains empty placeholders only; do not put provider credentials into versioned settings.

| Environment variable | Value |
|---|---|
| MessageDelivery__SmtpHost | SMTP provider hostname |
| MessageDelivery__SmtpPort | STARTTLS port, usually 587 |
| MessageDelivery__SmtpUsername | SMTP account, if provider requires authentication |
| MessageDelivery__SmtpPassword | SMTP password/app password from secret storage |
| MessageDelivery__FromEmail | Provider-approved sender address |
| MessageDelivery__FromName | Optional display name; defaults to AgriSage |

SMTP always requires TLS. Emails currently contain a one-time confirmation/reset code; the client
submits the complete code to the corresponding endpoint. No frontend callback URL is assumed.
Leave `MessageDelivery__SmsGatewayUrl` and `MessageDelivery__SmsApiKey` unset for this phase.
Phone verification/reset by phone then returns 503. A later gateway must accept HTTPS POST JSON
`{ "to": "...", "message": "..." }` with Bearer authentication; adapt the Infrastructure sender
if the selected provider has another contract.

### Deploy and enable jobs

1. Configure database and JWT settings using the existing setup procedure. Keep the JWT signing key
   stable: it also protects keyed challenge hashes. A key rotation invalidates outstanding challenges.
2. Review and apply `20261007062351_AuthSessionsNotificationsOperations` with the normal EF deployment
   process. The application does not apply this migration automatically.
3. Deploy API/client changes together. Existing JWTs lack `sid`/`sv` and require a new login. Store the
   new refresh token securely and serialize refresh calls per session; concurrent token reuse revokes
   the whole session. After password reset/change or logout-all, return the client to login.
4. Set `BackgroundJobs__Enabled=true` after migration. Defaults: notification dispatch every 15s,
   lot expiration every 10min, debt/inventory alerts every hour, payOS reconciliation every 2min,
   auth maintenance every hour. `BackgroundJobs__BatchSize` is 1–100; job timeout defaults to 300s.
   Optional switches: `ExpireLots`, `DebtReminders`, `InventoryAlerts`, `ReconcilePayments`,
   `ExpireAuthentication` under `BackgroundJobs`. Notification dispatch always runs when enabled.
5. Configure payOS normally before enabling reconciliation; disable `BackgroundJobs__ReconcilePayments`
   if its provider is not configured yet. Workers do not need an ADMIN login or a stored system token.

### Verification scope

Tests cover Domain security transitions, HTTP authorization/validation, crypto and gateway adapters,
real PostgreSQL migration/constraints/transactions, notification delivery/retries/ownership/deduplication,
debt and inventory reminder invariants, payment reconciliation, and concurrent auth operations using
independent database connections. Real-DB tests require `AGRISAGE_DB_TESTS=1` and a migrated, reference-
seeded test database with a test ADMIN. Concurrency/credit tests use a separate disposable schema with
`AGRISAGE_CREDIT_TEST_CONNECTION_STRING`. Run `dotnet restore`, `dotnet build`, `dotnet test AgriSage.sln`.
Use an isolated local database for this suite. SMTP delivery, the selected future SMS provider and
live payOS calls require configured credentials and are not covered by the fake-provider tests.

Verified on 2026-10-07 with .NET SDK 10.0.401 and isolated Docker PostgreSQL 17.6:
restore succeeded; build succeeded with 0 warnings/errors; 616 unit tests and 1,072 integration tests
passed, 0 failed. Two live Supabase Storage tests were skipped because storage credentials were not
configured. EF reported no pending model changes. The new migration and reference/test-admin seeding
were applied only to the disposable local test database, which was removed after verification.

Deployment on 2026-10-07: after explicit user authorization, applied only the pending
`20261007062351_AuthSessionsNotificationsOperations` migration to the project's configured Supabase
database. All four CREATE TABLE operations, additive columns/constraints/indexes and the EF history
insert succeeded. A subsequent `dotnet ef migrations list` confirmed all six migrations are applied.
No seeding or background-job configuration change was performed during this deployment.
