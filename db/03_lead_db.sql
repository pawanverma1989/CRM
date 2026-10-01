-- =====================================================================
--  LEAD SERVICE  ->  database: lead_db
--  Owns: leads, lead sources, web forms, and the lead-conversion saga.
--  Publishes: lead.created, lead.assigned, lead.status_changed,
--             lead.converted, lead.deleted, web_form.submitted
--  Consumes:  contact.merged / company.merged (re-point converted ids),
--             deal.deleted (clear converted_deal_id), dsr.erasure_requested
--  Calls (sync, during conversion): customer-service POST /companies,
--             POST /contacts; sales-service POST /deals
-- =====================================================================
--  Target: PostgreSQL 14+. Run while connected to lead_db.
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

CREATE TABLE lead_sources (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    name            TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    UNIQUE (organization_id, name)
);

CREATE TABLE web_forms (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    name            TEXT        NOT NULL,
    public_key      TEXT        NOT NULL UNIQUE DEFAULT encode(gen_random_bytes(16), 'hex'),
    fields          JSONB       NOT NULL,
    lead_source_id  UUID        REFERENCES lead_sources(id) ON DELETE SET NULL,
    default_owner_id UUID,                           -- identity.users.id
    consent_text    TEXT,                            -- shown on form; sent with web_form.submitted
    success_message TEXT,
    redirect_url    TEXT,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE leads (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    owner_id        UUID,
    first_name      TEXT,
    last_name       TEXT,
    email           CITEXT,
    phone           TEXT,
    phone_normalized TEXT,
    company_name    TEXT,
    job_title       TEXT,
    status          TEXT        NOT NULL DEFAULT 'new'
                    CHECK (status IN ('new','contacted','qualified','disqualified','converted')),
    disqualify_reason TEXT,
    lead_source_id  UUID        REFERENCES lead_sources(id) ON DELETE SET NULL,
    web_form_id     UUID        REFERENCES web_forms(id) ON DELETE SET NULL,
    utm_source      TEXT,
    utm_medium      TEXT,
    utm_campaign    TEXT,
    notes           TEXT,
    tags            TEXT[]      NOT NULL DEFAULT '{}',
    custom_fields   JSONB       NOT NULL DEFAULT '{}'::jsonb,
    converted_at         TIMESTAMPTZ,
    converted_by         UUID,
    converted_contact_id UUID,                       -- customer.contacts.id  (no FK: other DB)
    converted_company_id UUID,                       -- customer.companies.id (no FK: other DB)
    converted_deal_id    UUID,                       -- sales.deals.id        (no FK: other DB)
    version         INT         NOT NULL DEFAULT 1,
    created_by      UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ,
    CHECK (email IS NOT NULL OR phone IS NOT NULL),
    CHECK (status <> 'converted' OR converted_at IS NOT NULL),
    CHECK (status <> 'disqualified' OR disqualify_reason IS NOT NULL)
);
CREATE INDEX idx_leads_org_status ON leads (organization_id, status) WHERE deleted_at IS NULL;
CREATE INDEX idx_leads_owner      ON leads (owner_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_leads_email      ON leads (organization_id, email);
CREATE INDEX idx_leads_phone      ON leads (organization_id, phone_normalized);
CREATE INDEX idx_leads_tags       ON leads USING GIN (tags);

CREATE TABLE custom_field_definitions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    entity_type     TEXT        NOT NULL DEFAULT 'lead' CHECK (entity_type = 'lead'),
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

-- Lead conversion saga: creating a company, contact and deal spans three
-- databases, so progress is recorded step by step. A crashed conversion is
-- resumed from its last completed step; each call sends the lead id as an
-- idempotency key so a retried step never creates duplicates.
CREATE TABLE lead_conversions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    lead_id         UUID        NOT NULL UNIQUE REFERENCES leads(id),
    requested_by    UUID,
    request         JSONB       NOT NULL,            -- options: existing company?, create deal?, deal name/amount/pipeline
    status          TEXT        NOT NULL DEFAULT 'started'
                    CHECK (status IN ('started','company_done','contact_done','deal_done',
                                      'completed','failed','compensating','compensated')),
    company_id      UUID,
    contact_id      UUID,
    deal_id         UUID,
    last_error      TEXT,
    attempts        INT         NOT NULL DEFAULT 0,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_conversions_pending ON lead_conversions (updated_at)
    WHERE status NOT IN ('completed','compensated');

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
