-- =====================================================================
--  SALES SERVICE  ->  database: sales_db  (V2 — requirements additions)
--  Implements schema changes from Requirements §7.
-- =====================================================================

-- ── 1. Enforce exactly one Won and one Lost stage per pipeline (PIP-3) ─────────
CREATE UNIQUE INDEX uq_pipeline_won_stage
    ON pipeline_stages (pipeline_id) WHERE stage_type = 'won';

CREATE UNIQUE INDEX uq_pipeline_lost_stage
    ON pipeline_stages (pipeline_id) WHERE stage_type = 'lost';

-- ── 2. deal_contacts.is_primary — at most one per deal (DL-2) ──────────────────
ALTER TABLE deal_contacts ADD COLUMN is_primary BOOLEAN NOT NULL DEFAULT false;

CREATE UNIQUE INDEX uq_deal_contacts_primary
    ON deal_contacts (deal_id) WHERE is_primary;

-- ── 3. user_refs — local copy of owner display names (OWN-2) ───────────────────
-- Mirrors the pattern from customer_db.user_refs.
CREATE TABLE user_refs (
    user_id         UUID        PRIMARY KEY,
    organization_id UUID        NOT NULL,
    display_name    TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    source_version  INT         NOT NULL DEFAULT 0,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_user_refs_org ON user_refs (organization_id);

-- ── 4. reassignment_queue — open deals of deactivated users (OWN-2) ───────────
CREATE TABLE reassignment_queue (
    id                  UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id     UUID        NOT NULL,
    deal_id             UUID        NOT NULL REFERENCES deals(id) ON DELETE CASCADE,
    deactivated_user_id UUID        NOT NULL,
    queued_at           TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (deal_id)
);
CREATE INDEX idx_reassignment_queue_org
    ON reassignment_queue (organization_id, deactivated_user_id);

-- ── 5. Clear probability override when stage changes (DL-4) ───────────────────
-- Replace deals_track_stage to also null out the probability override.
CREATE OR REPLACE FUNCTION deals_track_stage() RETURNS trigger AS $$
DECLARE v_type TEXT;
BEGIN
    IF TG_OP = 'INSERT' OR NEW.stage_id IS DISTINCT FROM OLD.stage_id THEN
        SELECT stage_type INTO v_type FROM pipeline_stages WHERE id = NEW.stage_id;
        NEW.stage_entered_at := now();
        NEW.status := v_type;

        -- DL-4: clear the per-deal probability override on any stage change.
        IF TG_OP = 'UPDATE' THEN
            NEW.probability := NULL;
        END IF;

        IF v_type IN ('won','lost') THEN
            -- WL-1: honour app-supplied past close date; default to now().
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

-- ── 6. Publish deal.reopened when closed → open (WL-3) ────────────────────────
-- Replace deals_log_stage to detect reopened deals.
CREATE OR REPLACE FUNCTION deals_log_stage() RETURNS trigger AS $$
DECLARE
    v_user      UUID := nullif(current_setting('app.current_user_id', true), '')::uuid;
    v_event_type TEXT;
BEGIN
    IF TG_OP = 'INSERT' OR NEW.stage_id IS DISTINCT FROM OLD.stage_id THEN

        v_event_type :=
            CASE
                WHEN TG_OP = 'INSERT'                                        THEN 'deal.created'
                WHEN NEW.status = 'won'                                      THEN 'deal.won'
                WHEN NEW.status = 'lost'                                     THEN 'deal.lost'
                WHEN TG_OP = 'UPDATE'
                     AND OLD.status IN ('won','lost')
                     AND NEW.status = 'open'                                  THEN 'deal.reopened'
                ELSE 'deal.stage_changed'
            END;

        INSERT INTO deal_stage_history
            (deal_id, from_stage_id, to_stage_id, amount_at_change, changed_by)
        VALUES (
            NEW.id,
            CASE WHEN TG_OP = 'UPDATE' THEN OLD.stage_id END,
            NEW.stage_id,
            NEW.amount,
            v_user
        );

        INSERT INTO outbox_events
            (organization_id, aggregate_type, aggregate_id, event_type, payload)
        VALUES (
            NEW.organization_id, 'deal', NEW.id, v_event_type,
            jsonb_build_object(
                'deal_id',       NEW.id,
                'version',       NEW.version,
                'pipeline_id',   NEW.pipeline_id,
                'from_stage_id', CASE WHEN TG_OP = 'UPDATE' THEN OLD.stage_id END,
                'to_stage_id',   NEW.stage_id,
                'status',        NEW.status,
                'amount',        NEW.amount,
                'currency',      NEW.currency,
                'owner_id',      NEW.owner_id,
                'changed_by',    v_user,
                'closed_at',     NEW.closed_at,
                'loss_reason_id', NEW.loss_reason_id
            )
        );
    END IF;
    RETURN NULL;
END;
$$ LANGUAGE plpgsql;

-- ── 7. Default pipeline seed data (PIP-1 / AC-1) ─────────────────────────────
-- Inserted for the single organization convention used in this MVP.
-- In a multi-tenant setup this would run per-organization via an API call;
-- here we seed the single org's pipeline so AC-1 passes on a fresh install.
-- The organization_id nil UUID is used as a placeholder — real org IDs are
-- assigned by the Identity service and this seed is overridden by the
-- application's startup seed logic (POST /pipelines in the API).
-- NOTE: the API's SalesSeeder creates the default pipeline on first start.
-- This migration is left intentionally empty for the seed so the seeder owns it.
-- (Seed data does not belong in migrations; this comment is left to explain why
--  the migration exists but contains no INSERT statements for the pipeline.)

-- updated_at trigger for user_refs
CREATE TRIGGER trg_user_refs_updated_at
    BEFORE UPDATE ON user_refs
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
