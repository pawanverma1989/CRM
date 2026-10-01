-- =====================================================================
--  SEARCH SERVICE  ->  database: search_db
--  Owns: the global search index across contacts, companies, leads and
--        deals, and users' saved views (saved filters).
--  Publishes: nothing
--  Consumes:  contact.*, company.*, lead.*, deal.* (upsert / delete index docs)
--  Starts on PostgreSQL full-text search; the same table shape can move
--  to OpenSearch/Elasticsearch later without changing other services.
-- =====================================================================
CREATE EXTENSION IF NOT EXISTS pg_trgm;
--  Target: PostgreSQL 14+. Run while connected to search_db.
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

CREATE TABLE search_documents (
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('contact','company','lead','deal')),
    entity_id       UUID        NOT NULL,
    organization_id UUID        NOT NULL,
    owner_id        UUID,                            -- for permission filtering
    title           TEXT        NOT NULL,            -- "Priya Sharma", "Tata Widgets", "Big order"
    subtitle        TEXT,                            -- email / company / stage, shown under the title
    keywords        TEXT,                            -- email, phone, domain, tags... joined
    search_vector   TSVECTOR GENERATED ALWAYS AS (
                        setweight(to_tsvector('simple', coalesce(title,'')), 'A') ||
                        setweight(to_tsvector('simple', coalesce(subtitle,'')), 'B') ||
                        setweight(to_tsvector('simple', coalesce(keywords,'')), 'C')
                    ) STORED,
    source_version  INT         NOT NULL,            -- ignore older events arriving late
    is_deleted      BOOLEAN     NOT NULL DEFAULT false,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (entity_type, entity_id)
);
CREATE INDEX idx_search_vector ON search_documents USING GIN (search_vector);
CREATE INDEX idx_search_title_trgm ON search_documents USING GIN (title gin_trgm_ops);
CREATE INDEX idx_search_org ON search_documents (organization_id, owner_id) WHERE NOT is_deleted;

CREATE TABLE saved_views (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    user_id         UUID        NOT NULL,
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('contact','company','lead','deal','task','activity')),
    name            TEXT        NOT NULL,
    filters         JSONB       NOT NULL DEFAULT '{}'::jsonb,  -- translated into a query on the owning service
    columns         JSONB,
    sort            JSONB,
    visibility      TEXT        NOT NULL DEFAULT 'private' CHECK (visibility IN ('private','team','organization')),
    is_default      BOOLEAN     NOT NULL DEFAULT false,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (user_id, entity_type, name)
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
