-- =====================================================================
--  DATA TRANSFER SERVICE (import & export)  ->  database: data_transfer_db
--  Owns: CSV import jobs (mapping, progress, row errors, undo) and export jobs.
--  Never writes into another service's database: imports call the owning
--  service's bulk API (customer / lead / sales), exports call their list APIs.
--  Publishes: import.completed, import.failed, export.completed
--  Consumes:  nothing required
-- =====================================================================
--  Target: PostgreSQL 14+. Run while connected to data_transfer_db.
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

CREATE TABLE import_jobs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    user_id         UUID,
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('contact','company','lead','deal')),
    target_service  TEXT        NOT NULL CHECK (target_service IN ('customer','lead','sales')),
    file_name       TEXT        NOT NULL,
    file_url        TEXT        NOT NULL,            -- object storage (S3 / GCS / MinIO)
    field_mapping   JSONB       NOT NULL DEFAULT '{}'::jsonb,
    duplicate_strategy TEXT     NOT NULL DEFAULT 'skip' CHECK (duplicate_strategy IN ('skip','update','create')),
    default_owner_id UUID,
    status          TEXT        NOT NULL DEFAULT 'pending'
                    CHECK (status IN ('pending','mapping','processing','completed','failed','cancelled','undone')),
    total_rows      INT         NOT NULL DEFAULT 0,
    processed_rows  INT         NOT NULL DEFAULT 0,  -- resume point after a crash
    created_count   INT         NOT NULL DEFAULT 0,
    updated_count   INT         NOT NULL DEFAULT 0,
    skipped_count   INT         NOT NULL DEFAULT 0,
    error_count     INT         NOT NULL DEFAULT 0,
    started_at      TIMESTAMPTZ,
    finished_at     TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_import_jobs_org ON import_jobs (organization_id, created_at DESC);

CREATE TABLE import_job_errors (
    id              BIGSERIAL PRIMARY KEY,
    import_job_id   UUID        NOT NULL REFERENCES import_jobs(id) ON DELETE CASCADE,
    row_number      INT         NOT NULL,
    raw_row         JSONB,
    error_message   TEXT        NOT NULL
);
CREATE INDEX idx_import_errors_job ON import_job_errors (import_job_id, row_number);

CREATE TABLE import_job_records (                   -- enables "undo import"
    import_job_id   UUID        NOT NULL REFERENCES import_jobs(id) ON DELETE CASCADE,
    row_number      INT         NOT NULL,
    entity_id       UUID        NOT NULL,            -- id returned by the owning service
    action          TEXT        NOT NULL CHECK (action IN ('created','updated')),
    PRIMARY KEY (import_job_id, row_number)
);

CREATE TABLE export_jobs (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    user_id         UUID,
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('contact','company','lead','deal','activity','task')),
    filters         JSONB       NOT NULL DEFAULT '{}'::jsonb,
    status          TEXT        NOT NULL DEFAULT 'pending' CHECK (status IN ('pending','processing','completed','failed')),
    row_count       INT,
    file_url        TEXT,
    expires_at      TIMESTAMPTZ,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    finished_at     TIMESTAMPTZ
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
