-- =====================================================================
--  IDENTITY SERVICE  ->  V4 users.version
--  Users are a mutable aggregate, so they carry a version (CLAUDE.md
--  database conventions). It is bumped by trigger on EVERY update
--  (including login stamps), so it only ever increases. Every user.*
--  event payload carries the row's version as committed; consumers
--  keeping a user_refs copy apply an event only when its version is
--  newer than their source_version.
--  Existing rows start at 1.
--  Apply as: identity_svc role, connected to identity_db
-- =====================================================================

ALTER TABLE users
    ADD COLUMN version INT NOT NULL DEFAULT 1;

CREATE OR REPLACE FUNCTION bump_version() RETURNS trigger AS $$
BEGIN
    -- Always derived from the stored row: a client-supplied version is ignored.
    NEW.version := OLD.version + 1;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_users_version
    BEFORE UPDATE ON users
    FOR EACH ROW EXECUTE FUNCTION bump_version();
