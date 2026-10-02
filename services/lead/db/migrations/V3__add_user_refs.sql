-- =====================================================================
--  LEAD SERVICE  ->  lead_db  (V3 — add user_refs)
--  Local read-only copy of Identity users for owner names in lead lists.
--  Fed by user.created / user.updated / user.deactivated events.
--  An event is applied only when its version beats source_version (CLAUDE.md rule 4).
-- =====================================================================

CREATE TABLE user_refs (
    user_id         UUID        PRIMARY KEY,         -- identity.users.id, no FK across services
    organization_id UUID        NOT NULL,
    display_name    TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    source_version  INT         NOT NULL DEFAULT 0,
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE INDEX idx_user_refs_org ON user_refs (organization_id) WHERE is_active;

CREATE TRIGGER trg_user_refs_updated_at
    BEFORE UPDATE ON user_refs
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
