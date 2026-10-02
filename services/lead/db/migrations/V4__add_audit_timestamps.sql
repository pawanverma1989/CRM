-- =====================================================================
--  LEAD SERVICE  ->  lead_db  (V4 — add missing audit timestamps)
--  CLAUDE.md: "Every business table has created_at, updated_at (trigger-maintained)."
--  lead_sources and disqualify_reasons were created without them in V1/V2.
-- =====================================================================

ALTER TABLE lead_sources
    ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    ADD COLUMN updated_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE TRIGGER trg_lead_sources_updated_at
    BEFORE UPDATE ON lead_sources
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

ALTER TABLE disqualify_reasons
    ADD COLUMN created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    ADD COLUMN updated_at TIMESTAMPTZ NOT NULL DEFAULT now();

CREATE TRIGGER trg_disqualify_reasons_updated_at
    BEFORE UPDATE ON disqualify_reasons
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
