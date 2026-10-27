namespace AgriSage.Infrastructure.Persistence;

// Frozen SQL for EventDrivenRealtime. After deployment, change triggers through a new reviewed migration.
// Schema-local functions (no public search_path assumptions), safe in isolated migration test schemas too.
public static class RealtimeOutboxSql
{
    public const string Install = """
        CREATE FUNCTION agrisage_enqueue(topic_name text, scope_name text, subject uuid) RETURNS void
        LANGUAGE plpgsql AS $$ BEGIN
          INSERT INTO realtime_outbox
            (id, topic, scope, subject_id, transaction_id, created_at, updated_at, available_at, attempts)
          VALUES (gen_random_uuid(), topic_name, scope_name, subject, txid_current(), now(), now(), now(), 0)
          ON CONFLICT (transaction_id, topic, scope, subject_id) DO NOTHING;
        END $$;

        CREATE FUNCTION agrisage_wake_worker() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN
          PERFORM pg_notify('agrisage_work', ''); RETURN NEW;
        END $$;
        CREATE TRIGGER realtime_outbox_wake AFTER INSERT ON realtime_outbox
          FOR EACH ROW EXECUTE FUNCTION agrisage_wake_worker();
        CREATE TRIGGER notification_outbox_wake AFTER INSERT ON notification_outbox
          FOR EACH ROW EXECUTE FUNCTION agrisage_wake_worker();

        CREATE FUNCTION agrisage_capture_change() RETURNS trigger LANGUAGE plpgsql AS $$
        DECLARE store uuid; subject uuid;
        BEGIN
          IF TG_OP = 'UPDATE' AND NEW IS NOT DISTINCT FROM OLD THEN RETURN NEW; END IF;
          CASE TG_TABLE_NAME
          WHEN 'notifications' THEN
            PERFORM agrisage_enqueue('notifications', 'user', NEW.user_id);
          WHEN 'roles' THEN
            PERFORM agrisage_enqueue('permissions', 'role', NEW.id);
          WHEN 'role_permissions' THEN
            PERFORM agrisage_enqueue('permissions', 'role', NEW.role_id);
          WHEN 'store_member_permissions' THEN
            PERFORM agrisage_enqueue('permissions', 'member', NEW.store_member_id);
          WHEN 'users', 'auth_sessions' THEN
            IF TG_TABLE_NAME = 'users' THEN subject := NEW.id; ELSE subject := NEW.user_id; END IF;
            PERFORM agrisage_enqueue('permissions', 'user', subject);
          WHEN 'store_members' THEN
            PERFORM agrisage_enqueue('permissions', 'user', NEW.user_id);
          WHEN 'stores' THEN
            PERFORM agrisage_enqueue('permissions', 'store', NEW.id);
          WHEN 'permissions' THEN
            PERFORM agrisage_enqueue('permissions', 'all', '00000000-0000-0000-0000-000000000000');
          WHEN 'diagnosis_cases' THEN
            PERFORM agrisage_enqueue('diagnosis', 'case', NEW.id);
          WHEN 'ai_inferences' THEN
            PERFORM agrisage_enqueue('diagnosis', 'case', NEW.diagnosis_case_id);
          WHEN 'stock_movements' THEN
            IF NEW.status IN ('POSTED', 'REVERSED') THEN
              PERFORM agrisage_enqueue('inventory', 'store', NEW.store_id);
              PERFORM agrisage_enqueue('stock-posted', 'movement', NEW.id);
            END IF;
          WHEN 'inventory_reservations' THEN
            PERFORM agrisage_enqueue('inventory', 'store', NEW.store_id);
          WHEN 'inventory_lots' THEN
            SELECT store_id INTO store FROM store_products WHERE id = NEW.store_product_id;
            IF store IS NOT NULL THEN PERFORM agrisage_enqueue('inventory', 'store', store); END IF;
          WHEN 'inventory_lot_balances' THEN
            SELECT p.store_id INTO store FROM inventory_lots l
              JOIN store_products p ON p.id = l.store_product_id WHERE l.id = NEW.inventory_lot_id;
            IF store IS NOT NULL THEN PERFORM agrisage_enqueue('inventory', 'store', store); END IF;
          ELSE NULL;
          END CASE;
          RETURN NEW;
        END $$;
        DO $$ DECLARE table_name text; BEGIN
          FOREACH table_name IN ARRAY ARRAY['notifications', 'roles', 'role_permissions', 'store_member_permissions',
            'users', 'auth_sessions', 'store_members', 'stores', 'permissions', 'diagnosis_cases', 'ai_inferences',
            'stock_movements', 'inventory_reservations', 'inventory_lots', 'inventory_lot_balances'] LOOP
            EXECUTE format('CREATE TRIGGER agrisage_changed AFTER INSERT OR UPDATE ON %I FOR EACH ROW EXECUTE FUNCTION agrisage_capture_change()', table_name);
          END LOOP;
        END $$;

        -- One-time catch-up replaces the former periodic full-domain scans. Existing deduplication keys stay valid.
        SELECT agrisage_enqueue('stock-posted', 'movement', id) FROM stock_movements
          WHERE status IN ('POSTED', 'REVERSED') AND posted_at IS NOT NULL AND deleted_at IS NULL;
        SELECT agrisage_enqueue('diagnosis', 'case', id) FROM diagnosis_cases
          WHERE status IN ('AI_COMPLETED', 'UNDER_REVIEW', 'VERIFIED', 'INCONCLUSIVE') AND deleted_at IS NULL;
        """;

    public const string Uninstall = """
        DO $$ DECLARE table_name text; BEGIN
          FOREACH table_name IN ARRAY ARRAY['notifications', 'roles', 'role_permissions', 'store_member_permissions',
            'users', 'auth_sessions', 'store_members', 'stores', 'permissions', 'diagnosis_cases', 'ai_inferences',
            'stock_movements', 'inventory_reservations', 'inventory_lots', 'inventory_lot_balances'] LOOP
            EXECUTE format('DROP TRIGGER IF EXISTS agrisage_changed ON %I', table_name);
          END LOOP;
        END $$;
        DROP TRIGGER IF EXISTS notification_outbox_wake ON notification_outbox;
        DROP TRIGGER IF EXISTS realtime_outbox_wake ON realtime_outbox;
        DROP FUNCTION agrisage_capture_change();
        DROP FUNCTION agrisage_wake_worker();
        DROP FUNCTION agrisage_enqueue(text, text, uuid);
        """;
}
