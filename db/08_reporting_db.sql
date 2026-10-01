-- =====================================================================
--  REPORTING SERVICE  ->  database: reporting_db
--  Owns: a read-only, denormalised copy of the facts needed for dashboards.
--        Reports never query other services' databases; they read these
--        tables, which are filled from events (seconds behind live data).
--  Publishes: nothing
--  Consumes:  user.*, team.updated, pipeline.updated, deal.*, lead.*,
--             activity.logged, activity.deleted
-- =====================================================================
--  Target: PostgreSQL 14+. Run while connected to reporting_db.
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

CREATE TABLE dim_users (
    user_id         UUID PRIMARY KEY,
    organization_id UUID        NOT NULL,
    full_name       TEXT        NOT NULL,
    team_id         UUID,
    team_name       TEXT,
    role            TEXT,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE dim_pipeline_stages (
    stage_id        UUID PRIMARY KEY,
    organization_id UUID        NOT NULL,
    pipeline_id     UUID        NOT NULL,
    pipeline_name   TEXT        NOT NULL,
    stage_name      TEXT        NOT NULL,
    sort_order      INT         NOT NULL,
    probability     SMALLINT    NOT NULL,
    stage_type      TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE fact_deals (
    deal_id         UUID PRIMARY KEY,
    organization_id UUID        NOT NULL,
    pipeline_id     UUID        NOT NULL,
    stage_id        UUID        NOT NULL,
    owner_id        UUID,
    company_id      UUID,
    amount          NUMERIC(16,2) NOT NULL,
    currency        CHAR(3)     NOT NULL,
    probability     SMALLINT,                        -- NULL = stage default
    expected_close_date DATE,
    status          TEXT        NOT NULL,
    closed_at       TIMESTAMPTZ,
    loss_reason     TEXT,
    lead_source     TEXT,
    created_at      TIMESTAMPTZ NOT NULL,
    is_deleted      BOOLEAN     NOT NULL DEFAULT false,
    source_version  INT         NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_fact_deals_org ON fact_deals (organization_id, status, owner_id);
CREATE INDEX idx_fact_deals_closed ON fact_deals (organization_id, closed_at) WHERE status <> 'open';

CREATE TABLE fact_deal_stage_changes (
    id              UUID PRIMARY KEY,                -- the event id
    deal_id         UUID        NOT NULL,
    organization_id UUID        NOT NULL,
    from_stage_id   UUID,
    to_stage_id     UUID        NOT NULL,
    changed_at      TIMESTAMPTZ NOT NULL
);
CREATE INDEX idx_fact_stage_changes ON fact_deal_stage_changes (organization_id, changed_at);

CREATE TABLE fact_leads (
    lead_id         UUID PRIMARY KEY,
    organization_id UUID        NOT NULL,
    owner_id        UUID,
    lead_source     TEXT,
    status          TEXT        NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL,
    converted_at    TIMESTAMPTZ,
    is_deleted      BOOLEAN     NOT NULL DEFAULT false,
    source_version  INT         NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_fact_leads_org ON fact_leads (organization_id, created_at);

CREATE TABLE fact_activities (
    activity_id     UUID PRIMARY KEY,
    organization_id UUID        NOT NULL,
    owner_id        UUID,
    activity_type   TEXT        NOT NULL,
    occurred_at     TIMESTAMPTZ NOT NULL,
    deal_id         UUID,
    is_deleted      BOOLEAN     NOT NULL DEFAULT false
);
CREATE INDEX idx_fact_activities ON fact_activities (organization_id, owner_id, occurred_at);

-- ---------------------------------------------------------------- views
CREATE VIEW v_pipeline_by_stage AS
SELECT s.organization_id, s.pipeline_id, s.pipeline_name, s.stage_id, s.stage_name, s.sort_order,
       count(d.deal_id)                AS deal_count,
       coalesce(sum(d.amount), 0)      AS total_amount,
       round(coalesce(sum(d.amount * coalesce(d.probability, s.probability) / 100.0), 0), 2) AS weighted_amount
FROM dim_pipeline_stages s
LEFT JOIN fact_deals d ON d.stage_id = s.stage_id AND d.status = 'open' AND NOT d.is_deleted
WHERE s.stage_type = 'open' AND s.is_active
GROUP BY s.organization_id, s.pipeline_id, s.pipeline_name, s.stage_id, s.stage_name, s.sort_order;

CREATE VIEW v_sales_forecast AS
SELECT d.organization_id, d.owner_id, u.full_name AS owner_name,
       date_trunc('month', d.expected_close_date)::date AS close_month,
       count(*) AS deal_count, sum(d.amount) AS total_amount,
       round(sum(d.amount * coalesce(d.probability, s.probability) / 100.0), 2) AS weighted_amount
FROM fact_deals d
JOIN dim_pipeline_stages s ON s.stage_id = d.stage_id
LEFT JOIN dim_users u ON u.user_id = d.owner_id
WHERE d.status = 'open' AND NOT d.is_deleted AND d.expected_close_date IS NOT NULL
GROUP BY d.organization_id, d.owner_id, u.full_name, date_trunc('month', d.expected_close_date);

CREATE VIEW v_won_lost AS
SELECT d.organization_id, d.owner_id, u.full_name AS owner_name,
       date_trunc('month', d.closed_at)::date AS close_month,
       count(*) FILTER (WHERE d.status = 'won')                    AS won_count,
       coalesce(sum(d.amount) FILTER (WHERE d.status = 'won'), 0)  AS won_amount,
       count(*) FILTER (WHERE d.status = 'lost')                   AS lost_count,
       coalesce(sum(d.amount) FILTER (WHERE d.status = 'lost'), 0) AS lost_amount,
       round(100.0 * count(*) FILTER (WHERE d.status = 'won') / nullif(count(*), 0), 1) AS win_rate_pct
FROM fact_deals d
LEFT JOIN dim_users u ON u.user_id = d.owner_id
WHERE d.status IN ('won','lost') AND NOT d.is_deleted
GROUP BY d.organization_id, d.owner_id, u.full_name, date_trunc('month', d.closed_at);

CREATE VIEW v_loss_reasons AS
SELECT organization_id, loss_reason, count(*) AS deal_count, sum(amount) AS lost_amount
FROM fact_deals
WHERE status = 'lost' AND NOT is_deleted
GROUP BY organization_id, loss_reason;

CREATE VIEW v_activity_by_user AS
SELECT a.organization_id, a.owner_id, u.full_name AS owner_name,
       date_trunc('day', a.occurred_at)::date AS activity_date,
       count(*) FILTER (WHERE a.activity_type = 'call')    AS calls,
       count(*) FILTER (WHERE a.activity_type = 'email')   AS emails,
       count(*) FILTER (WHERE a.activity_type = 'meeting') AS meetings,
       count(*) FILTER (WHERE a.activity_type = 'note')    AS notes,
       count(*) AS total
FROM fact_activities a
LEFT JOIN dim_users u ON u.user_id = a.owner_id
WHERE NOT a.is_deleted
GROUP BY a.organization_id, a.owner_id, u.full_name, date_trunc('day', a.occurred_at);

CREATE VIEW v_lead_source_performance AS
SELECT organization_id, coalesce(lead_source, 'Unknown') AS lead_source,
       count(*) AS leads,
       count(*) FILTER (WHERE status = 'converted') AS converted,
       round(100.0 * count(*) FILTER (WHERE status = 'converted') / nullif(count(*), 0), 1) AS conversion_pct
FROM fact_leads
WHERE NOT is_deleted
GROUP BY organization_id, coalesce(lead_source, 'Unknown');

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
