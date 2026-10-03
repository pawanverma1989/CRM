-- =====================================================================
--  SALES SERVICE  ->  database: sales_db  (V1 — initial schema)
--  Seeded from db/04_sales_db.sql.  All subsequent changes go in V2+.
-- =====================================================================
--  Run as sales_svc (not a superuser).
-- =====================================================================

CREATE EXTENSION IF NOT EXISTS pgcrypto;
CREATE EXTENSION IF NOT EXISTS citext;

CREATE OR REPLACE FUNCTION set_updated_at() RETURNS trigger AS $$
BEGIN
    NEW.updated_at := now();
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

-- Transactional outbox (architecture §4.1).
CREATE TABLE outbox_events (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    aggregate_type  TEXT        NOT NULL,
    aggregate_id    UUID        NOT NULL,
    event_type      TEXT        NOT NULL,
    payload         JSONB       NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    published_at    TIMESTAMPTZ,
    publish_attempts INT        NOT NULL DEFAULT 0
);
CREATE INDEX idx_outbox_unpublished ON outbox_events (occurred_at) WHERE published_at IS NULL;

-- Idempotency inbox (architecture §4.2).
CREATE TABLE processed_events (
    event_id        UUID PRIMARY KEY,
    event_type      TEXT        NOT NULL,
    processed_at    TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE pipelines (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    name            TEXT        NOT NULL,
    is_default      BOOLEAN     NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (organization_id, name)
);
CREATE UNIQUE INDEX uq_pipelines_default ON pipelines (organization_id) WHERE is_default;

CREATE TABLE pipeline_stages (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    pipeline_id     UUID        NOT NULL REFERENCES pipelines(id) ON DELETE CASCADE,
    name            TEXT        NOT NULL,
    sort_order      INT         NOT NULL,
    probability     SMALLINT    NOT NULL DEFAULT 0 CHECK (probability BETWEEN 0 AND 100),
    stage_type      TEXT        NOT NULL DEFAULT 'open' CHECK (stage_type IN ('open','won','lost')),
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    UNIQUE (pipeline_id, name),
    UNIQUE (pipeline_id, sort_order) DEFERRABLE INITIALLY DEFERRED
);

CREATE TABLE loss_reasons (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    name            TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    UNIQUE (organization_id, name)
);

-- Local read-only copy of company/contact names from Customer service events.
-- Lets the Kanban board render without calling another service (NFR-4).
CREATE TABLE customer_refs (
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('company','contact')),
    id              UUID        NOT NULL,
    organization_id UUID        NOT NULL,
    display_name    TEXT        NOT NULL,
    email           CITEXT,
    is_deleted      BOOLEAN     NOT NULL DEFAULT false,
    source_version  INT         NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (entity_type, id)
);

CREATE TABLE deals (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    pipeline_id     UUID        NOT NULL REFERENCES pipelines(id),
    stage_id        UUID        NOT NULL REFERENCES pipeline_stages(id),
    owner_id        UUID,                            -- identity.users.id
    company_id      UUID,                            -- customer.companies.id
    primary_contact_id UUID,                         -- customer.contacts.id
    name            TEXT        NOT NULL,
    amount          NUMERIC(16,2) NOT NULL DEFAULT 0 CHECK (amount >= 0),
    currency        CHAR(3)     NOT NULL DEFAULT 'INR',
    probability     SMALLINT    CHECK (probability BETWEEN 0 AND 100),
    expected_close_date DATE,
    status          TEXT        NOT NULL DEFAULT 'open' CHECK (status IN ('open','won','lost')),
    closed_at       TIMESTAMPTZ,
    loss_reason_id  UUID        REFERENCES loss_reasons(id),
    loss_notes      TEXT,
    source_lead_id  UUID,                            -- lead.leads.id
    stage_entered_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    last_activity_at TIMESTAMPTZ,
    tags            TEXT[]      NOT NULL DEFAULT '{}',
    custom_fields   JSONB       NOT NULL DEFAULT '{}'::jsonb,
    version         INT         NOT NULL DEFAULT 1,
    created_by      UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ,
    CHECK (status = 'open' OR closed_at IS NOT NULL),
    CHECK (status <> 'lost' OR loss_reason_id IS NOT NULL)
);
CREATE INDEX idx_deals_board     ON deals (pipeline_id, stage_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_deals_owner     ON deals (organization_id, owner_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_deals_close     ON deals (organization_id, expected_close_date) WHERE status = 'open' AND deleted_at IS NULL;
CREATE INDEX idx_deals_company   ON deals (company_id);
CREATE INDEX idx_deals_contact   ON deals (primary_contact_id);
CREATE INDEX idx_deals_tags      ON deals USING GIN (tags);
CREATE UNIQUE INDEX uq_deals_source_lead ON deals (source_lead_id) WHERE source_lead_id IS NOT NULL;

CREATE TABLE deal_contacts (
    deal_id         UUID        NOT NULL REFERENCES deals(id) ON DELETE CASCADE,
    contact_id      UUID        NOT NULL,            -- customer.contacts.id
    role            TEXT,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (deal_id, contact_id)
);
CREATE INDEX idx_deal_contacts_contact ON deal_contacts (contact_id);

CREATE TABLE deal_stage_history (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    deal_id         UUID        NOT NULL REFERENCES deals(id) ON DELETE CASCADE,
    from_stage_id   UUID        REFERENCES pipeline_stages(id),
    to_stage_id     UUID        NOT NULL REFERENCES pipeline_stages(id),
    amount_at_change NUMERIC(16,2),
    changed_by      UUID,
    changed_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_stage_history_deal ON deal_stage_history (deal_id, changed_at);

CREATE TABLE custom_field_definitions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    entity_type     TEXT        NOT NULL DEFAULT 'deal' CHECK (entity_type = 'deal'),
    field_key       TEXT        NOT NULL CHECK (field_key ~ '^[a-z][a-z0-9_]*$'),
    label           TEXT        NOT NULL,
    field_type      TEXT        NOT NULL,
    options         JSONB,
    is_required     BOOLEAN     NOT NULL DEFAULT false,
    sort_order      INT         NOT NULL DEFAULT 0,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (organization_id, field_key)
);

-- Stage changes: set status/closed_at/stage_entered_at, bump version.
-- closed_at uses coalesce so an app-supplied past date is honoured (WL-1).
CREATE OR REPLACE FUNCTION deals_track_stage() RETURNS trigger AS $$
DECLARE v_type TEXT;
BEGIN
    IF TG_OP = 'INSERT' OR NEW.stage_id IS DISTINCT FROM OLD.stage_id THEN
        SELECT stage_type INTO v_type FROM pipeline_stages WHERE id = NEW.stage_id;
        NEW.stage_entered_at := now();
        NEW.status := v_type;
        IF v_type IN ('won','lost') THEN
            NEW.closed_at := coalesce(NEW.closed_at, now());
        ELSE
            NEW.closed_at := NULL;
            NEW.loss_reason_id := NULL;
        END IF;
    END IF;
    IF TG_OP = 'UPDATE' THEN
        NEW.version := OLD.version + 1;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_deals_track_stage
    BEFORE INSERT OR UPDATE ON deals
    FOR EACH ROW EXECUTE FUNCTION deals_track_stage();

-- Write deal_stage_history and the outbox event on every stage move.
CREATE OR REPLACE FUNCTION deals_log_stage() RETURNS trigger AS $$
DECLARE v_user UUID := nullif(current_setting('app.current_user_id', true), '')::uuid;
BEGIN
    IF TG_OP = 'INSERT' OR NEW.stage_id IS DISTINCT FROM OLD.stage_id THEN
        INSERT INTO deal_stage_history (deal_id, from_stage_id, to_stage_id, amount_at_change, changed_by)
        VALUES (NEW.id, CASE WHEN TG_OP = 'UPDATE' THEN OLD.stage_id END,
                NEW.stage_id, NEW.amount, v_user);

        INSERT INTO outbox_events (organization_id, aggregate_type, aggregate_id, event_type, payload)
        VALUES (NEW.organization_id, 'deal', NEW.id,
                CASE WHEN TG_OP = 'INSERT' THEN 'deal.created'
                     WHEN NEW.status = 'won'  THEN 'deal.won'
                     WHEN NEW.status = 'lost' THEN 'deal.lost'
                     ELSE 'deal.stage_changed' END,
                jsonb_build_object(
                    'deal_id', NEW.id, 'version', NEW.version,
                    'pipeline_id', NEW.pipeline_id,
                    'from_stage_id', CASE WHEN TG_OP = 'UPDATE' THEN OLD.stage_id END,
                    'to_stage_id', NEW.stage_id, 'status', NEW.status,
                    'amount', NEW.amount, 'currency', NEW.currency,
                    'owner_id', NEW.owner_id, 'changed_by', v_user));
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_deals_log_stage
    AFTER INSERT OR UPDATE OF stage_id ON deals
    FOR EACH ROW EXECUTE FUNCTION deals_log_stage();

-- Board view: open deals with company names from the local copy.
CREATE VIEW v_pipeline_board AS
SELECT d.id, d.organization_id, d.pipeline_id, d.stage_id, s.name AS stage_name, s.sort_order,
       d.name, d.amount, d.currency, d.owner_id, d.expected_close_date,
       coalesce(d.probability, s.probability) AS probability,
       c.display_name AS company_name,
       now() - d.stage_entered_at AS time_in_stage,
       d.last_activity_at
FROM deals d
JOIN pipeline_stages s ON s.id = d.stage_id
LEFT JOIN customer_refs c ON c.entity_type = 'company' AND c.id = d.company_id
WHERE d.status = 'open' AND d.deleted_at IS NULL;

-- updated_at triggers for every table with the column
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
