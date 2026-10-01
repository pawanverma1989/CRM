-- =====================================================================
--  ACTIVITY SERVICE (timeline & tasks)  ->  database: activity_db
--  Owns: activities (calls, emails, meetings, notes), participants,
--        tasks & reminders, @mentions.
--  Publishes: activity.logged, activity.updated, activity.deleted,
--             task.created, task.assigned, task.completed,
--             task.reminder_due, task.overdue, mention.created
--  Consumes:  lead.* / contact.* / company.* / deal.* (keeps record_refs
--             current; soft-deletes timeline when a parent is deleted),
--             *.merged (re-point links), lead.converted (copy lead
--             timeline links to the new contact/deal), dsr.erasure_requested
-- =====================================================================
--  Target: PostgreSQL 14+. Run while connected to activity_db.
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

-- Local copy of the records activities can be attached to (name, owner,
-- deleted flag). Used to validate links and show names in task lists.
CREATE TABLE record_refs (
    entity_type     TEXT        NOT NULL CHECK (entity_type IN ('lead','contact','company','deal')),
    id              UUID        NOT NULL,
    organization_id UUID        NOT NULL,
    display_name    TEXT        NOT NULL,
    owner_id        UUID,
    is_deleted      BOOLEAN     NOT NULL DEFAULT false,
    source_version  INT         NOT NULL,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    PRIMARY KEY (entity_type, id)
);

CREATE TABLE activities (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    activity_type   TEXT        NOT NULL CHECK (activity_type IN ('call','email','meeting','note')),
    subject         TEXT,
    body            TEXT,
    direction       TEXT        CHECK (direction IN ('inbound','outbound')),
    outcome         TEXT,
    occurred_at     TIMESTAMPTZ NOT NULL DEFAULT now(),
    duration_minutes INT        CHECK (duration_minutes >= 0),
    location        TEXT,
    -- Links to records in other services (ids only, no FKs)
    lead_id         UUID,
    contact_id      UUID,
    company_id      UUID,
    deal_id         UUID,
    owner_id        UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ,
    CHECK (num_nonnulls(lead_id, contact_id, company_id, deal_id) >= 1)
);
CREATE INDEX idx_activities_lead    ON activities (lead_id, occurred_at DESC)    WHERE lead_id IS NOT NULL;
CREATE INDEX idx_activities_contact ON activities (contact_id, occurred_at DESC) WHERE contact_id IS NOT NULL;
CREATE INDEX idx_activities_company ON activities (company_id, occurred_at DESC) WHERE company_id IS NOT NULL;
CREATE INDEX idx_activities_deal    ON activities (deal_id, occurred_at DESC)    WHERE deal_id IS NOT NULL;
CREATE INDEX idx_activities_owner   ON activities (organization_id, owner_id, occurred_at DESC);

CREATE TABLE activity_participants (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    activity_id     UUID        NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
    user_id         UUID,
    contact_id      UUID,
    CHECK (num_nonnulls(user_id, contact_id) = 1)
);
CREATE INDEX idx_activity_participants ON activity_participants (activity_id);

CREATE TABLE mentions (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    activity_id     UUID        NOT NULL REFERENCES activities(id) ON DELETE CASCADE,
    mentioned_user_id UUID      NOT NULL,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (activity_id, mentioned_user_id)
);

CREATE TABLE tasks (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    title           TEXT        NOT NULL,
    description     TEXT,
    task_type       TEXT        NOT NULL DEFAULT 'todo'
                    CHECK (task_type IN ('todo','call','email','meeting','follow_up')),
    priority        TEXT        NOT NULL DEFAULT 'normal' CHECK (priority IN ('low','normal','high')),
    status          TEXT        NOT NULL DEFAULT 'open' CHECK (status IN ('open','completed','cancelled')),
    due_at          TIMESTAMPTZ,
    remind_at       TIMESTAMPTZ,
    reminder_sent_at TIMESTAMPTZ,
    completed_at    TIMESTAMPTZ,
    assigned_to     UUID,
    lead_id         UUID,
    contact_id      UUID,
    company_id      UUID,
    deal_id         UUID,
    completed_activity_id UUID  REFERENCES activities(id) ON DELETE SET NULL,
    created_by      UUID,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    deleted_at      TIMESTAMPTZ,
    CHECK (status <> 'completed' OR completed_at IS NOT NULL)
);
CREATE INDEX idx_tasks_my_open   ON tasks (assigned_to, due_at) WHERE status = 'open' AND deleted_at IS NULL;
CREATE INDEX idx_tasks_reminders ON tasks (remind_at) WHERE status = 'open' AND reminder_sent_at IS NULL;
CREATE INDEX idx_tasks_lead      ON tasks (lead_id)    WHERE lead_id IS NOT NULL;
CREATE INDEX idx_tasks_contact   ON tasks (contact_id) WHERE contact_id IS NOT NULL;
CREATE INDEX idx_tasks_company   ON tasks (company_id) WHERE company_id IS NOT NULL;
CREATE INDEX idx_tasks_deal      ON tasks (deal_id)    WHERE deal_id IS NOT NULL;

-- Every new activity is announced (sales-service uses it for last_activity_at,
-- reporting-service for activity counts).
CREATE OR REPLACE FUNCTION activities_publish() RETURNS trigger AS $$
BEGIN
    INSERT INTO outbox_events (organization_id, aggregate_type, aggregate_id, event_type, payload)
    VALUES (NEW.organization_id, 'activity', NEW.id, 'activity.logged',
            jsonb_build_object('activity_id', NEW.id, 'activity_type', NEW.activity_type,
                               'occurred_at', NEW.occurred_at, 'owner_id', NEW.owner_id,
                               'lead_id', NEW.lead_id, 'contact_id', NEW.contact_id,
                               'company_id', NEW.company_id, 'deal_id', NEW.deal_id));
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_activities_publish
    AFTER INSERT ON activities
    FOR EACH ROW EXECUTE FUNCTION activities_publish();

-- "My tasks": today / overdue, with record names from the local copy.
CREATE VIEW v_open_tasks AS
SELECT t.id, t.organization_id, t.assigned_to, t.title, t.task_type, t.priority, t.due_at,
       (t.due_at < now()) AS is_overdue,
       coalesce(rd.display_name, rc.display_name, rco.display_name, rl.display_name) AS related_to
FROM tasks t
LEFT JOIN record_refs rd  ON rd.entity_type  = 'deal'    AND rd.id  = t.deal_id
LEFT JOIN record_refs rc  ON rc.entity_type  = 'contact' AND rc.id  = t.contact_id
LEFT JOIN record_refs rco ON rco.entity_type = 'company' AND rco.id = t.company_id
LEFT JOIN record_refs rl  ON rl.entity_type  = 'lead'    AND rl.id  = t.lead_id
WHERE t.status = 'open' AND t.deleted_at IS NULL;

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
