-- =====================================================================
--  AUDIT & COMPLIANCE SERVICE  ->  database: compliance_db
--  Owns: the central audit trail, consent records, and data subject
--        requests (access / erasure) under GDPR and India's DPDP Act.
--  Publishes: dsr.erasure_requested, dsr.access_requested, consent.changed
--  Consumes:  every domain event (written to audit_log),
--             user.logged_in / user.login_failed,
--             web_form.submitted (records consent), lead.converted
--             (copies lead consents to the new contact),
--             dsr.erasure_completed / dsr.access_completed (from each service)
-- =====================================================================
--  Target: PostgreSQL 14+. Run while connected to compliance_db.
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

-- Append-only. Partition by month once it grows (PARTITION BY RANGE (created_at)).
CREATE TABLE audit_log (
    id              BIGSERIAL PRIMARY KEY,
    source_event_id UUID        UNIQUE,              -- idempotent ingestion
    organization_id UUID        NOT NULL,
    service         TEXT        NOT NULL,            -- which service reported it
    user_id         UUID,
    entity_type     TEXT        NOT NULL,
    entity_id       UUID        NOT NULL,
    action          TEXT        NOT NULL CHECK (action IN
                        ('create','update','delete','restore','merge','convert',
                         'reassign','export','import','login','login_failed','erase')),
    changes         JSONB,                           -- {"amount":{"old":50000,"new":75000}}
    ip_address      INET,
    user_agent      TEXT,
    occurred_at     TIMESTAMPTZ NOT NULL,            -- when it happened in the source service
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_audit_entity ON audit_log (entity_type, entity_id, occurred_at DESC);
CREATE INDEX idx_audit_org    ON audit_log (organization_id, occurred_at DESC);
CREATE INDEX idx_audit_user   ON audit_log (user_id, occurred_at DESC);

CREATE OR REPLACE FUNCTION audit_log_immutable() RETURNS trigger AS $$
BEGIN
    RAISE EXCEPTION 'audit_log is append-only';
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_audit_log_immutable
    BEFORE UPDATE OR DELETE ON audit_log
    FOR EACH ROW EXECUTE FUNCTION audit_log_immutable();

-- Consent history: never update; insert a new row for each change.
CREATE TABLE consents (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    subject_type    TEXT        NOT NULL CHECK (subject_type IN ('contact','lead')),
    subject_id      UUID        NOT NULL,            -- customer.contacts.id or lead.leads.id
    subject_email   CITEXT,                          -- lets a DSR find the person by email
    purpose         TEXT        NOT NULL CHECK (purpose IN ('marketing','sales_contact','data_processing','third_party_sharing')),
    channel         TEXT        NOT NULL CHECK (channel IN ('email','phone','sms','whatsapp','any')),
    status          TEXT        NOT NULL CHECK (status IN ('granted','withdrawn')),
    source          TEXT        NOT NULL,
    evidence        TEXT,
    recorded_by     UUID,
    recorded_at     TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_consents_subject ON consents (subject_type, subject_id, purpose, channel, recorded_at DESC);
CREATE INDEX idx_consents_email   ON consents (organization_id, subject_email);

-- Current consent per subject/purpose/channel (latest row wins).
CREATE VIEW v_current_consent AS
SELECT DISTINCT ON (subject_type, subject_id, purpose, channel)
       organization_id, subject_type, subject_id, purpose, channel, status, recorded_at
FROM consents
ORDER BY subject_type, subject_id, purpose, channel, recorded_at DESC;

CREATE TABLE data_subject_requests (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    request_type    TEXT        NOT NULL CHECK (request_type IN ('access','erasure','correction','withdraw_consent')),
    requester_email CITEXT      NOT NULL,
    subject_refs    JSONB       NOT NULL DEFAULT '[]'::jsonb,  -- [{"type":"contact","id":"..."}, ...]
    status          TEXT        NOT NULL DEFAULT 'received'
                    CHECK (status IN ('received','verifying','in_progress','completed','rejected')),
    due_at          TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    handled_by      UUID,
    result_file_url TEXT,                            -- access requests: the compiled export
    notes           TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_dsr_open ON data_subject_requests (organization_id, due_at)
    WHERE status NOT IN ('completed','rejected');

-- A request is complete only when every service holding personal data confirms.
CREATE TABLE dsr_service_tasks (
    dsr_id          UUID        NOT NULL REFERENCES data_subject_requests(id) ON DELETE CASCADE,
    service         TEXT        NOT NULL CHECK (service IN
                        ('customer','lead','sales','activity','notification','search',
                         'reporting','data_transfer')),
    status          TEXT        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','completed','failed')),
    records_affected INT,
    detail          JSONB,
    completed_at    TIMESTAMPTZ,
    PRIMARY KEY (dsr_id, service)
);

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
