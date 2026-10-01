-- =====================================================================
--  CUSTOMER SERVICE (companies & contacts)  ->  database: customer_db
--  Owns: companies, contacts, their custom-field definitions, duplicate
--        detection and merging.
--  Publishes: company.created/updated/deleted/restored/merged,
--             contact.created/updated/deleted/restored/merged,
--             contact.reassigned, company.reassigned
--  Consumes:  user.deactivated (flag records for reassignment),
--             dsr.erasure_requested (hard-delete a person's data)
--  Cross-service ids stored WITHOUT foreign keys: owner_id, created_by
--  (identity service users).
-- =====================================================================
CREATE EXTENSION IF NOT EXISTS pg_trgm;    -- fuzzy name matching for duplicates
--  Target: PostgreSQL 14+. Run while connected to customer_db.
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

CREATE TABLE companies (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    owner_id        UUID,                            -- identity.users.id
    name            TEXT        NOT NULL,
    domain          CITEXT,
    industry        TEXT,
    employee_count  INT         CHECK (employee_count >= 0),
    annual_revenue  NUMERIC(16,2),
    phone           TEXT,
    website         TEXT,
    address_line1   TEXT,
    address_line2   TEXT,
    city            TEXT,
    state           TEXT,
    postal_code     TEXT,
    country         TEXT,
    gstin           TEXT,
    tags            TEXT[]      NOT NULL DEFAULT '{}',   -- lower-case labels
    custom_fields   JSONB       NOT NULL DEFAULT '{}'::jsonb,
    merged_into_id  UUID        REFERENCES companies(id),
    version         INT         NOT NULL DEFAULT 1,      -- optimistic locking + event ordering
    created_by      UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ
);
CREATE INDEX idx_companies_org_owner ON companies (organization_id, owner_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_companies_name_trgm ON companies USING GIN (name gin_trgm_ops);
CREATE INDEX idx_companies_tags      ON companies USING GIN (tags);
CREATE INDEX idx_companies_custom    ON companies USING GIN (custom_fields);
CREATE UNIQUE INDEX uq_companies_domain ON companies (organization_id, domain)
    WHERE domain IS NOT NULL AND deleted_at IS NULL AND merged_into_id IS NULL;

CREATE TABLE contacts (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    company_id      UUID        REFERENCES companies(id) ON DELETE SET NULL,  -- same DB: real FK
    owner_id        UUID,
    first_name      TEXT        NOT NULL,
    last_name       TEXT,
    email           CITEXT,
    phone           TEXT,
    phone_normalized TEXT,                           -- E.164, set by the service
    mobile          TEXT,
    job_title       TEXT,
    address_line1   TEXT,
    address_line2   TEXT,
    city            TEXT,
    state           TEXT,
    postal_code     TEXT,
    country         TEXT,
    source          TEXT,
    source_lead_id  UUID,                            -- lead.leads.id when created by conversion
    tags            TEXT[]      NOT NULL DEFAULT '{}',
    custom_fields   JSONB       NOT NULL DEFAULT '{}'::jsonb,
    merged_into_id  UUID        REFERENCES contacts(id),
    version         INT         NOT NULL DEFAULT 1,
    created_by      UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ
);
CREATE INDEX idx_contacts_org_owner ON contacts (organization_id, owner_id) WHERE deleted_at IS NULL;
CREATE INDEX idx_contacts_company   ON contacts (company_id);
CREATE INDEX idx_contacts_name_trgm ON contacts USING GIN ((first_name || ' ' || coalesce(last_name,'')) gin_trgm_ops);
CREATE INDEX idx_contacts_phone     ON contacts (organization_id, phone_normalized);
CREATE INDEX idx_contacts_tags      ON contacts USING GIN (tags);
CREATE INDEX idx_contacts_custom    ON contacts USING GIN (custom_fields);
CREATE UNIQUE INDEX uq_contacts_email ON contacts (organization_id, email)
    WHERE email IS NOT NULL AND deleted_at IS NULL AND merged_into_id IS NULL;
-- Idempotent lead conversion: one contact per converted lead.
CREATE UNIQUE INDEX uq_contacts_source_lead ON contacts (source_lead_id) WHERE source_lead_id IS NOT NULL;

CREATE TABLE custom_field_definitions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('contact','company')),
    field_key       TEXT        NOT NULL CHECK (field_key ~ '^[a-z][a-z0-9_]*$'),
    label           TEXT        NOT NULL,
    field_type      TEXT        NOT NULL CHECK (field_type IN
                        ('text','textarea','number','currency','date','datetime',
                         'boolean','select','multiselect','email','phone','url','user')),
    options         JSONB,
    is_required     BOOLEAN     NOT NULL DEFAULT false,
    sort_order      INT         NOT NULL DEFAULT 0,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (organization_id, entity_type, field_key)
);

-- History of merges (the event contact.merged tells other services to
-- re-point their references from loser_id to survivor_id).
CREATE TABLE merge_history (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('contact','company')),
    survivor_id     UUID        NOT NULL,
    loser_id        UUID        NOT NULL,
    field_choices   JSONB,                           -- which values were kept from which record
    merged_by       UUID,
    merged_at       TIMESTAMPTZ NOT NULL DEFAULT now()
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
