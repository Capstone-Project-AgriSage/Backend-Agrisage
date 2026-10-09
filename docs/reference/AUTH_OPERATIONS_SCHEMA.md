# Authentication and operations schema extension — 2026-10-07

This extends DATABASE_DESIGN.md for the user-requested Auth P0, notifications and background jobs.
The reviewed migration is the deployable schema; no shared database is altered automatically.

## Existing tables

- `users.security_version bigint NOT NULL DEFAULT 0`, CHECK >= 0; optimistic concurrency token.
  Domain increments for password, role, status and contact changes. Verification alone does not increment.
- `notifications.deduplication_key varchar(200) NULL`; unique `(user_id, deduplication_key)` where key
  is not null, including archived/soft-deleted rows. Never reissue an already-delivered event.
- `payments.last_reconciliation_attempt_at timestamptz NULL`; technical scheduling metadata only.
  Partial index for pending payOS payments. Attempts are recorded in a short transaction before
  provider I/O, followed by settlement in a fresh scope. This keeps failing payments from starving others.

## New tables (68–71)

All have uuid `id` and the standard created_at/updated_at/deleted_at/deleted_by columns, query filters
and Restrict/NoAction FKs. Security secrets are stored only as keyed/cryptographic digests.

### 68. auth_sessions

`user_id uuid NOT NULL FK users`, `security_version bigint NOT NULL`, `expires_at timestamptz NOT NULL`,
`last_used_at timestamptz NOT NULL`, `revoked_at timestamptz NULL`, `revocation_reason varchar(100) NULL`.
CHECK security_version >= 0. Index `(user_id, expires_at)`. Session expiry is absolute, not sliding.

### 69. refresh_tokens

`session_id uuid NOT NULL FK auth_sessions`, `token_hash varchar(64) NOT NULL`,
`expires_at timestamptz NOT NULL`, `consumed_at timestamptz NULL`.
Unique token_hash (including consumed/deleted tokens); index `(session_id, expires_at)`.
Consumed hashes are retained to detect replay; revocation is recorded on the session.

### 70. auth_challenges

`user_id uuid NOT NULL FK users`, `purpose varchar(30) NOT NULL` (PASSWORD_RESET,
EMAIL_VERIFICATION, PHONE_VERIFICATION), `channel varchar(20) NOT NULL` (EMAIL, SMS),
`destination varchar(255) NOT NULL`, `token_hash varchar(64) NOT NULL`, `security_version bigint NOT NULL`,
`expires_at timestamptz NOT NULL`, `consumed_at timestamptz NULL`, `failed_attempts integer NOT NULL DEFAULT 0`.
CHECK security_version >= 0, failed_attempts between 0 and 5; CHECK channel matches verification purpose.
Index `(user_id, purpose, created_at DESC)`; purpose/channel enum CHECKs. No token is stored in plaintext.
Challenge confirmation checks current contact and security version after locking the user.

### 71. notification_outbox

`audit_log_id uuid NOT NULL FK audit_logs`, `available_at timestamptz NOT NULL`,
`processed_at timestamptz NULL`, `attempts integer NOT NULL DEFAULT 0`, `last_error_code varchar(100) NULL`.
Unique audit_log_id. Partial index available_at WHERE processed_at IS NULL AND deleted_at IS NULL.
CHECK attempts >= 0. Written atomically with the business audit; dispatch saves notifications and
processed_at atomically. Failure codes contain no provider payload, credentials or token values.

## Locking and compatibility

Auth use cases lock user first, then session/challenge. User security version concurrency guards staff
and profile changes that already use other transaction owners. Outbox dispatch locks its row before
re-reading it. Background task coordination uses advisory locks on dedicated PostgreSQL connections;
each business use case retains its own transaction. No stock/debt amount is changed by reminders.

The 67-table baseline remains the historical InitialCreate. The new current model has 71 tables.
Existing JWTs without session claims are rejected; clients must log in after migration/deployment.
