START TRANSACTION;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    ALTER TABLE users ADD security_version bigint NOT NULL DEFAULT 0;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    ALTER TABLE payments ADD last_reconciliation_attempt_at timestamp with time zone;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    ALTER TABLE notifications ADD deduplication_key character varying(200);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE TABLE auth_challenges (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        user_id uuid NOT NULL,
        purpose character varying(30) NOT NULL,
        channel character varying(20) NOT NULL,
        destination character varying(255) NOT NULL,
        token_hash character varying(64) NOT NULL,
        security_version bigint NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        consumed_at timestamp with time zone,
        failed_attempts integer NOT NULL DEFAULT 0,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        deleted_at timestamp with time zone,
        deleted_by uuid,
        CONSTRAINT pk_auth_challenges PRIMARY KEY (id),
        CONSTRAINT ck_auth_challenges_channel CHECK (channel IN ('EMAIL', 'SMS')),
        CONSTRAINT ck_auth_challenges_failed_attempts CHECK (failed_attempts BETWEEN 0 AND 5),
        CONSTRAINT ck_auth_challenges_purpose CHECK (purpose IN ('PASSWORD_RESET', 'EMAIL_VERIFICATION', 'PHONE_VERIFICATION')),
        CONSTRAINT ck_auth_challenges_purpose_channel CHECK (purpose = 'PASSWORD_RESET' OR (purpose = 'EMAIL_VERIFICATION' AND channel = 'EMAIL') OR (purpose = 'PHONE_VERIFICATION' AND channel = 'SMS')),
        CONSTRAINT ck_auth_challenges_security_version CHECK (security_version >= 0),
        CONSTRAINT fk_auth_challenges_deleted_by FOREIGN KEY (deleted_by) REFERENCES users (id),
        CONSTRAINT fk_auth_challenges_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE TABLE auth_sessions (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        user_id uuid NOT NULL,
        security_version bigint NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        last_used_at timestamp with time zone NOT NULL,
        revoked_at timestamp with time zone,
        revocation_reason character varying(100),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        deleted_at timestamp with time zone,
        deleted_by uuid,
        CONSTRAINT pk_auth_sessions PRIMARY KEY (id),
        CONSTRAINT ck_auth_sessions_security_version CHECK (security_version >= 0),
        CONSTRAINT fk_auth_sessions_deleted_by FOREIGN KEY (deleted_by) REFERENCES users (id),
        CONSTRAINT fk_auth_sessions_user_id FOREIGN KEY (user_id) REFERENCES users (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE TABLE notification_outbox (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        audit_log_id uuid NOT NULL,
        available_at timestamp with time zone NOT NULL,
        processed_at timestamp with time zone,
        attempts integer NOT NULL DEFAULT 0,
        last_error_code character varying(100),
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        deleted_at timestamp with time zone,
        deleted_by uuid,
        CONSTRAINT pk_notification_outbox PRIMARY KEY (id),
        CONSTRAINT ck_notification_outbox_attempts CHECK (attempts >= 0),
        CONSTRAINT fk_notification_outbox_audit_log_id FOREIGN KEY (audit_log_id) REFERENCES audit_logs (id) ON DELETE RESTRICT,
        CONSTRAINT fk_notification_outbox_deleted_by FOREIGN KEY (deleted_by) REFERENCES users (id)
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE TABLE refresh_tokens (
        id uuid NOT NULL DEFAULT (gen_random_uuid()),
        session_id uuid NOT NULL,
        token_hash character varying(64) NOT NULL,
        expires_at timestamp with time zone NOT NULL,
        consumed_at timestamp with time zone,
        created_at timestamp with time zone NOT NULL,
        updated_at timestamp with time zone NOT NULL,
        deleted_at timestamp with time zone,
        deleted_by uuid,
        CONSTRAINT pk_refresh_tokens PRIMARY KEY (id),
        CONSTRAINT fk_refresh_tokens_deleted_by FOREIGN KEY (deleted_by) REFERENCES users (id),
        CONSTRAINT fk_refresh_tokens_session_id FOREIGN KEY (session_id) REFERENCES auth_sessions (id) ON DELETE RESTRICT
    );
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    ALTER TABLE users ADD CONSTRAINT ck_users_security_version CHECK (security_version >= 0);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE INDEX ix_payments_last_reconciliation_attempt_at ON payments (last_reconciliation_attempt_at) WHERE payment_method = 'PAYOS' AND status = 'PENDING' AND deleted_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE UNIQUE INDEX ux_notifications_user_id_deduplication_key ON notifications (user_id, deduplication_key) WHERE deduplication_key IS NOT NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE INDEX ix_auth_challenges_user_id_purpose_created_at ON auth_challenges (user_id, purpose, created_at DESC);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE INDEX ix_auth_sessions_user_id_expires_at ON auth_sessions (user_id, expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE INDEX ix_notification_outbox_available_at ON notification_outbox (available_at) WHERE processed_at IS NULL AND deleted_at IS NULL;
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE UNIQUE INDEX ux_notification_outbox_audit_log_id ON notification_outbox (audit_log_id);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE INDEX ix_refresh_tokens_session_id_expires_at ON refresh_tokens (session_id, expires_at);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    CREATE UNIQUE INDEX ux_refresh_tokens_token_hash ON refresh_tokens (token_hash);
    END IF;
END $EF$;

DO $EF$
BEGIN
    IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory" WHERE "MigrationId" = '20261007062351_AuthSessionsNotificationsOperations') THEN
    INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
    VALUES ('20261007062351_AuthSessionsNotificationsOperations', '10.0.12');
    END IF;
END $EF$;
COMMIT;

