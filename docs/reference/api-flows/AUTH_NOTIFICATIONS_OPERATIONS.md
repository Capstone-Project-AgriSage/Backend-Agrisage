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
| POST | /api/auth/reset-password | Anonymous; `{ token, newPassword }` for the long token an email carries, or `{ identifier, code, newPassword }` (phone + the 6-digit code an SMS carries); exactly one proof, single use; 204 |
| POST | /api/auth/reset-password/verify-code | Anonymous; `{ identifier, code }` (phone + the 6-digit code an SMS carries); checks the code **without using it**: 204 if right, 422 if wrong/expired/unknown phone. A wrong code spends one of the code's 5 attempts, shared with `reset-password`. Lets a client confirm the code before it asks for the new password; the client then sends the same `{ identifier, code, newPassword }` to `reset-password`, which checks again and is the call that decides. Not used for the email token |
| POST | /api/auth/email-verification/request | Authenticated; current email only; 202 |
| POST | /api/auth/email-verification/confirm | Authenticated; `{ token }`; 204 |
| POST | /api/auth/phone-verification/request | Authenticated; current phone only; 202 |
| POST | /api/auth/phone-verification/confirm | Authenticated; `{ code }` (6 digits); 204 |

Refresh tokens: 256 random bits, hashes only, single-use rotation, absolute session lifetime (default
30 days). Reusing a consumed token revokes its entire session, including successor access tokens.
Password change/reset, staff password reset, account status/role/contact changes advance the user's
security version. A lock followed by unlock never revives old sessions. Contact changes clear the
corresponding verified flag. Concurrent security mutations use row locks or optimistic concurrency.

Email confirmation and password-reset tokens include a challenge id
and 256-bit secret. Everything delivered by SMS (phone verification and password reset) uses
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

SMS providers (`MessageDelivery:SmsProvider`): `Generic` (default) posts `{to, message}` with a Bearer key to an
HTTPS gateway. `AndroidGateway` uses the open-source "SMS Gateway for Android" app on a phone, which sends through its
own SIM: `POST {SmsGatewayUrl}` (the app's `/message` address) with Basic auth (`SmsUsername` + `SmsApiKey`) and
`{ "textMessage": { "text": … }, "phoneNumbers": ["+84…"] }`; plain HTTP is accepted only for private-network
addresses (LAN, loopback), anything else needs HTTPS. SMS text is ASCII, no timestamp, so a 6-digit OTP fits one
segment. Cost guards for real SMS: `SmsAllowedRecipients` (when not empty, any other number is refused without
sending) and `SmsDailyLimit` (per Vietnam day, in memory per API process, 0 = unlimited); a refused or capped send is
the same "delivery unavailable" outcome as a provider failure and is logged without the number or the code.

`Simulated` is for demos and tests that must not cost money: no SMS leaves the machine, the allow-list and the daily limit
do not apply, and each message (destination, purpose, the 6-digit code, the text) goes to an in-memory outbox (the last 50,
empty again when the API restarts) and to the log. It is allowed only in the Development environment; the API refuses to
start with it anywhere else. Two anonymous routes exist only in this mode and answer 404 otherwise:
`GET /api/dev/sms-outbox` (JSON, newest first) and `GET /api/dev/sms-outbox/view` (a self-refreshing page that looks like a
phone's inbox, for a second screen). It replaces SMS only: email is unchanged and still needs SMTP.

An SMS carries a 6-digit code for both phone verification and password reset (2026-10-14, lead decision); an email still
carries the long token. For an SMS reset the code is only valid together with the phone number (`identifier`), is checked
against that account's newest unused SMS reset challenge, and shares the challenge rules: single use, 10 minutes, 5
failed attempts then the challenge is dead, 60-second resend cooldown. An unknown phone, no live challenge and a wrong
code are the same 422. Residual risk: a 6-digit code is guessable in principle (5 guesses per challenge, a new
challenge at most every 60 seconds, auth rate limit per IP), so an attacker who keeps requesting resets for one
victim gets a small but non-zero chance over many hours; a per-account failed-attempt lockout would close it and is
the deferred lockout item.

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
| ~~Current authorized human review / approved recommendations~~ (no Human Review since 2026-10-21) | — | — | — |

Recipients must have active accounts and active, non-deleted primary roles. Staff must be active
members of the event's store; delivery assignments still target the assigned active member. Missing,
deleted or mismatched entities never disclose an event to another store. Delivery outcome is taken
from the immutable audit snapshot, so a later successful retry does not hide an earlier failure.
Metadata may include `orderId` for navigation; it is resolved on the server, never client supplied.

The notification worker also reads committed posted stock movements and persisted diagnosis results
in bounded batches. Stable per-record keys survive retries/archive/soft deletion and allow existing
records without a notification to be delivered. It never posts stock, invokes AI or approves reviews.
These alerts require real persisted results. The diagnosis APIs exist (`AI_DIAGNOSIS.md`); an alert
carries `entityType = DIAGNOSIS_CASE` and the case id so a client can open `GET /api/me/diagnosis-cases/{id}`.
The diagnosis use cases do not write notifications themselves, only this worker does (one alert per decision).
Since 2026-10-21 (`AI_DIAGNOSIS.md` 2.0) there is no Human Review: the only diagnosis alert is
`AI_DIAGNOSIS_COMPLETED`, one per case once a successful inference exists; review and recommendation alerts
are no longer sent.
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

### Audit history enrichment (user-requested, 2026-10-09)

- Preserve existing fields/routes; add `actorName`, `actorEmail`, `actorRole` and `status` to list/detail responses.
  Actor information comes from the current user/role records, including soft-deleted accounts for audit purposes;
  it is not a historical identity snapshot. A missing actor remains null for system/anonymous events.
- Add optional `search` (max 200 characters: actor name/email, action, entity type, reason or exact resource/user UUID),
  `actorRole` (ADMIN, STORE_OWNER, SALES_STAFF, DELIVERY_STAFF, FARMER), and `status` (SUCCESS, FAILURE) filters.
  Apply every filter and pagination in SQL after restricting the viewer's store scope.
- Status describes recorded events: actions ending in `_FAILED` and `AUTH_REFRESH_REUSE` are FAILURE;
  other committed audit events are SUCCESS. A cancelled/rejected business record is a successfully recorded
  decision, not a failed HTTP request. This is not a history of every unsuccessful request; no status column or
  schema migration is introduced. Existing events cannot reconstruct unrecorded failures.
- Anonymous successful login/registration records use the server-verified user as actor through
  `AuditTrail.RecordAuthentication`; never accept an actor from request data.
- Login failures write `AUTH_LOGIN_FAILED` atomically before returning the existing generic 401/403;
  actor remains the JWT user or null, target is the known account or null, reason is INVALID_CREDENTIALS or
  INACTIVE_ACCOUNT. Never store the submitted identifier or password; an attempted account is not a verified actor.
- Recursively redact credential fields in old/new JSON on write and again on read for older records;
  never serialize full identity entities, passwords, hashes, tokens, OTPs or authorization headers.
- Management web shows human-readable actions, actor details, resource references, field-level before/after
  changes, reason, Vietnam timestamps, IP and browser; CSV follows the same applied server filters and redaction.
  Owner's `/agent/activity-log` uses this API instead of mock data. Admin views all events; Owner sees their
  active store only. Sales/Delivery/Farmer cannot inspect audit history.
- Extend business audit coverage to product/catalog changes and committed inventory/receiving operations;
  each owner adds the audit to its existing unit of work before the existing save/commit, with no extra save.

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

## Event-driven realtime extension (2026-10-27, current user request)

Existing REST routes/DTOs are unchanged. Authenticated SignalR hub `/api/realtime` emits `Changed` with
`{eventId, topic}`. Topics: `notifications`, `permissions`, `inventory`, `diagnosis`, `resync`.
Messages are invalidations, not business snapshots. Clients re-read the appropriate existing API, debounce bursts,
deduplicate event ids and synchronize all active views on initial connect/reconnect/focus/online. No periodic
permission or notification HTTP polling. Initial connection failure also retries with bounded exponential backoff.
Browser JWT `access_token` is accepted only under the hub path; REST still requires Authorization headers.
The hub closes at authentication expiration. User identity comes from `sub`; there are no arbitrary join/store methods.
Inventory/diagnosis audiences use live role grants, member overrides, store membership, Owner ceiling and ownership.
Permission invalidations include disabled users so their next REST read detects revoked sessions.

Backend: existing notification dispatcher/policies/unique keys remain. PostgreSQL triggers enqueue transport events
inside business transactions and notify after commit. Targeted stock/diagnosis notification collectors replace 15s
full scans. A separate outbox BackgroundService drains bounded batches with row locks, exponential retry and a
60s indexed queue recovery check (`Realtime:RecoverySeconds`, configurable 5–300); time-based expiry/reminder/payment
maintenance remains in OperationsWorker. `BackgroundJobs:Enabled` controls both worker families. PollSeconds and
NotificationSeconds remain accepted legacy settings but no longer schedule notification scans.

Deployment: review/apply EventDrivenRealtime before the updated API; no auto-migration. LISTEN requires a direct or
session-pooled PostgreSQL connection, not a transaction pooler. Allow WebSocket upgrades for `/api/realtime` and never
log query-string bearer tokens at the reverse proxy. Each API instance uses its own dedicated LISTEN connection;
PostgreSQL broadcasts invalidations to all instances, no Redis/backplane needed. If LISTEN is unavailable, worker
recovery still processes durable notifications, but realtime delivery waits for listener recovery; reconnect/focus
REST snapshots repair missed hints. Committed event rows are retained for investigation; schedule reviewed archival
outside hot request paths when volume requires it. Do not silently drop failed/unprocessed rows.

Client implementation and verification: Management-Webs and Farmer-Web use the official @microsoft/signalr client;
Farmer-Mobile uses signalr_netcore and the existing serialized Dio token refresh. Their notification/permission polling
intervals are removed; mobile notification/case polling is also removed. Mobile resumes and socket reconnects invalidate
REST snapshots, and logout disposes the connection/retry timers. Event ids are deduplicated and bursts coalesced.
Management inventory overview/ledger/card/reports and diagnosis queue/detail refresh in place while retaining filters.
Farmer-Web's AI Doctor/history are still demo/mock data in the existing source; this transport change does not replace
that separate data integration. Actual diagnosis invalidations refresh the Management screens and Farmer-Mobile.
Delivery-Mobile and AI-Service have no matching 15s/30s permission/notification polling to migrate.

Verification uses a disposable local PostgreSQL database for migration/trigger rollback, coalescing, competing workers,
retry, multi-instance fan-out, store/role/override/Owner-ceiling checks and Farmer ownership. HTTP/WebSocket tests verify
JWT authentication, hub-only query tokens and per-user isolation. Browser mocks exercise actual SignalR callbacks,
reconnect resynchronization, access revocation, inventory/AI changes and zero idle 15s/30s HTTP polling. Web builds,
Flutter analyzer/widget tests and Android debug build are verified separately; shared Supabase is not migrated by tests.

### Supabase session-connection budget (2026-10-27)

The reported EMAXCONNSESSION error is Supavisor's session pool reaching its configured 15-client limit.
Database options now bound each API process's normal Npgsql pool to 4 sessions by default, minimum 0,
idle lifetime 30 seconds, pruning interval 5 seconds, connection/pool wait timeout 15 seconds.
Configuration keys: Database:MaximumPoolSize, MinimumPoolSize, ConnectionIdleLifetimeSeconds,
ConnectionPruningIntervalSeconds, ConnectionTimeoutSeconds. These are non-secret and can be overridden per deployment.
The LISTEN service uses one additional unpooled session; the sequential operations scheduler can temporarily hold
one additional advisory-lock session. Thus the default upper budget is about 6 sessions per API process, not 4 total.
Add every API process/connection-string pool plus DBeaver, migrations and other tools when sizing against the
project's session pool limit. A separate process cannot enforce a global project-wide limit.

Restart old API processes after changing this configuration; their existing Npgsql pools are not resized at runtime.
Do not switch the entire API to the transaction pooler just to hide this error: LISTEN and session advisory locks
need a direct/session connection. Do not terminate other clients' sessions or change Supabase quotas automatically.
Known DB capacity/connectivity failures now return 503 with Retry-After: 5 and sanitized problem details, including
when JWT validation cannot query the database. They never become an authentication rejection; unrelated database
errors retain existing handling. No write is automatically replayed. Listener reconnect uses capped backoff (5–60s).
