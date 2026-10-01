-- =====================================================================
--  IDENTITY SERVICE  ->  V2 Schema changes
--  Implements requirements §6 of Identity Service Requirements doc:
--    1. users.status + email_verified_at (replaces is_active)
--    2. password_reset_tokens -> user_tokens with purpose column
--    3. user_sessions.replaced_by_id + last_used_at
--    4. service_clients table
-- =====================================================================

-- -----------------------------------------------------------------
-- 1. users: add status, email_verified_at, pending_email
-- -----------------------------------------------------------------
ALTER TABLE users
    ADD COLUMN status           TEXT        NOT NULL DEFAULT 'active'
                                CHECK (status IN ('invited', 'active', 'deactivated')),
    ADD COLUMN email_verified_at TIMESTAMPTZ,
    -- holds the unverified new address while awaiting confirmation (USR-9)
    ADD COLUMN pending_email    CITEXT;

-- drop the view before altering the column it depends on
DROP VIEW v_user_visibility;

-- back-fill existing rows from is_active
UPDATE users SET status = CASE WHEN is_active THEN 'active' ELSE 'deactivated' END;

ALTER TABLE users DROP COLUMN is_active;
CREATE VIEW v_user_visibility AS
SELECT u.id AS user_id, u.organization_id, u.role,
       CASE
         WHEN u.role = 'admin' THEN NULL
         WHEN u.role = 'manager' THEN
              array(SELECT DISTINCT m.id FROM users m
                    LEFT JOIN teams t ON t.id = m.team_id
                    WHERE (m.id = u.id OR t.manager_id = u.id)
                      AND m.status = 'active')
         ELSE ARRAY[u.id]
       END AS visible_owner_ids
FROM users u
WHERE u.status = 'active';

-- -----------------------------------------------------------------
-- 2. Rename password_reset_tokens -> user_tokens, add purpose
-- -----------------------------------------------------------------
ALTER TABLE password_reset_tokens RENAME TO user_tokens;
ALTER INDEX idx_pwreset_user RENAME TO idx_user_tokens_user;

ALTER TABLE user_tokens
    ADD COLUMN purpose TEXT NOT NULL DEFAULT 'password_reset'
                       CHECK (purpose IN ('password_reset', 'invitation', 'email_verification'));

-- reset rate-limiting: track how many reset tokens were issued per user per window
ALTER TABLE user_tokens
    ADD COLUMN metadata JSONB;   -- e.g. {"reset_count_1h": 2}

-- -----------------------------------------------------------------
-- 3. user_sessions: replaced_by_id + last_used_at (AUTH-2, AUTH-7)
-- -----------------------------------------------------------------
ALTER TABLE user_sessions
    ADD COLUMN replaced_by_id UUID REFERENCES user_sessions(id) ON DELETE SET NULL,
    ADD COLUMN last_used_at   TIMESTAMPTZ;

CREATE INDEX idx_sessions_replaced_by ON user_sessions (replaced_by_id)
    WHERE replaced_by_id IS NOT NULL;

-- -----------------------------------------------------------------
-- 4. service_clients (TOK-5)
-- -----------------------------------------------------------------
CREATE TABLE service_clients (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL REFERENCES organizations(id) ON DELETE CASCADE,
    client_id       TEXT        NOT NULL UNIQUE,
    secret_hash     TEXT        NOT NULL,    -- hashed secret; never plain text
    service_name    TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now()
);
CREATE INDEX idx_service_clients_org ON service_clients (organization_id);

CREATE TRIGGER trg_service_clients_updated_at
    BEFORE UPDATE ON service_clients
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();
