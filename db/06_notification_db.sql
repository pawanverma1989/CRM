-- =====================================================================
--  NOTIFICATION SERVICE  ->  database: notification_db
--  Owns: in-app notifications, per-user preferences, email delivery log.
--  Publishes: nothing required (optionally notification.delivered)
--  Consumes:  task.assigned, task.reminder_due, task.overdue, lead.assigned,
--             deal.reassigned, contact.reassigned, mention.created,
--             import.completed, export.completed, user.created/updated
--             (keeps user_contacts for email delivery)
-- =====================================================================
--  Target: PostgreSQL 14+. Run while connected to notification_db.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Common building blocks (identical in every service database)
-- ---------------------------------------------------------------------
CREATE EXTENSION IF NOT EXISTS pgcrypto;   -- gen_random_uuid(), gen_random_bytes()
CREATE EXTENSION IF NOT EXISTS citext;     -- case-insensitive text (emails, names)

CREATE OR REPLACE FUNCTION set_updated_at() RETURNS trigger AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Transactional outbox: write the event in the SAME transaction as the data
-- change; a relay process publishes unpublished rows to the message broker
-- (Kafka / RabbitMQ / NATS) and sets published_at. No lost or phantom events.
CREATE TABLE outbox_events (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),   -- becomes the event_id
    organization_id UUID        NOT NULL,
    aggregate_type  TEXT        NOT NULL,          -- e.g. 'contact', 'deal'
    aggregate_id    UUID        NOT NULL,
    event_type      TEXT        NOT NULL,          -- e.g. 'contact.updated'
    payload         JSONB       NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at    TIMESTAMPTZ,
    publish_attempts INT        NOT NULL DEFAULT 0
);
CREATE INDEX idx_outbox_unpublished ON outbox_events (occurred_at) WHERE published_at IS NULL;

-- Inbox / idempotency: consumers record each event they processed so a
-- redelivered event is ignored (brokers deliver at-least-once).
CREATE TABLE processed_events (
    event_id        UUID PRIMARY KEY,
    event_type      TEXT        NOT NULL,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Local copy of who to email (from identity events).
CREATE TABLE user_contacts (
    user_id         UUID PRIMARY KEY,
    organization_id UUID        NOT NULL,
    email           CITEXT      NOT NULL,
    first_name      TEXT,
    timezone        TEXT,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE notifications (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    user_id         UUID        NOT NULL,            -- recipient
    notification_type TEXT      NOT NULL CHECK (notification_type IN
                        ('task_reminder','task_assigned','task_overdue','lead_assigned',
                         'deal_assigned','record_reassigned','mention','import_finished',
                         'export_ready','system')),
    title           TEXT        NOT NULL,
    body            TEXT,
    entity_type     TEXT,
    entity_id       UUID,
    link_url        TEXT,                            -- deep link into the app
    actor_id        UUID,
    source_event_id UUID        UNIQUE,              -- one notification per event (idempotent)
    read_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_notifications_unread ON notifications (user_id, created_at DESC) WHERE read_at IS NULL;

CREATE TABLE notification_preferences (
    user_id         UUID        NOT NULL,
    notification_type TEXT      NOT NULL,
    in_app          BOOLEAN     NOT NULL DEFAULT true,
    email           BOOLEAN     NOT NULL DEFAULT true,
    PRIMARY KEY (user_id, notification_type)
);

CREATE TABLE email_deliveries (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    notification_id UUID        NOT NULL REFERENCES notifications(id) ON DELETE CASCADE,
    to_email        CITEXT      NOT NULL,
    status          TEXT        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','sent','failed','bounced')),
    provider_message_id TEXT,
    attempts        INT         NOT NULL DEFAULT 0,
    last_error      TEXT,
    sent_at         TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_email_pending ON email_deliveries (created_at) WHERE status = 'pending';

-- ---------------------------------------------------------------------
-- updated_at triggers for every table that has the column
-- ---------------------------------------------------------------------
DO $$
DECLARE t TEXT;
BEGIN
    FOR t IN
        SELECT c.table_name FROM information_schema.columns c
        JOIN information_schema.tables tb
          ON tb.table_schema = c.table_schema AND tb.table_name = c.table_name
        WHERE c.table_schema = current_schema() AND c.column_name = 'updated_at'
          AND tb.table_type = 'BASE TABLE'
    LOOP
        EXECUTE format(
            'CREATE TRIGGER trg_%1$s_updated_at BEFORE UPDATE ON %1$I
             FOR EACH ROW EXECUTE FUNCTION set_updated_at()', t);
    END LOOP;
END $$;
