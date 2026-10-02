-- =====================================================================
--  Seed: add a user to an EXISTING organization (and optionally team).
--  Run as a superuser (or identity_svc role) against identity_db.
--
--  Required variables:
--    org_name        – exact name of the existing organization
--    user_email      – new user's e-mail address
--    user_password   – plain-text password (hashed here, never stored)
--    user_first_name – first name
--    user_role       – 'admin' | 'manager' | 'sales_rep'
--
--  Optional variable (omit or pass empty string to skip):
--    user_last_name  – last name
--    team_name       – assign user to this existing team in the same org
--
--  Usage:
--    psql -U postgres -d identity_db                       \
--         -v org_name="'Acme Corp'"                        \
--         -v user_email="'jane@acme.com'"                  \
--         -v user_password="'ChangeMe123!'"                \
--         -v user_first_name="'Jane'"                      \
--         -v user_last_name="'Doe'"                        \
--         -v user_role="'admin'"                           \
--         -v team_name="'Sales East'"                      \
--         -f db/seed_admin_user.sql
-- =====================================================================

\set ON_ERROR_STOP on

DO $$
DECLARE
    v_org_name       TEXT   := :'org_name';
    v_email          CITEXT := :'user_email';
    v_password       TEXT   := :'user_password';
    v_first_name     TEXT   := :'user_first_name';
    v_last_name      TEXT   := nullif(:'user_last_name', '');
    v_role           TEXT   := :'user_role';
    v_team_name      TEXT   := nullif(:'team_name', '');

    v_org_id         UUID;
    v_team_id        UUID;
    v_user_id        UUID;
    v_password_hash  TEXT;
BEGIN
    -- ----------------------------------------------------------------
    -- 1. Resolve organization — must already exist
    -- ----------------------------------------------------------------
    SELECT id INTO v_org_id
    FROM   organizations
    WHERE  name = v_org_name AND is_active
    LIMIT  1;

    IF v_org_id IS NULL THEN
        RAISE EXCEPTION 'Organization "%" not found or is inactive.', v_org_name;
    END IF;

    RAISE NOTICE 'Using organization "%" → %', v_org_name, v_org_id;

    -- ----------------------------------------------------------------
    -- 2. Optionally resolve team within this org
    -- ----------------------------------------------------------------
    IF v_team_name IS NOT NULL THEN
        SELECT id INTO v_team_id
        FROM   teams
        WHERE  organization_id = v_org_id AND name = v_team_name
        LIMIT  1;

        IF v_team_id IS NULL THEN
            RAISE EXCEPTION 'Team "%" not found in organization "%".', v_team_name, v_org_name;
        END IF;

        RAISE NOTICE 'Assigning to team "%" → %', v_team_name, v_team_id;
    END IF;

    -- ----------------------------------------------------------------
    -- 3. Guard: abort if this e-mail already exists
    -- ----------------------------------------------------------------
    IF EXISTS (SELECT 1 FROM users WHERE email = v_email) THEN
        RAISE EXCEPTION 'A user with e-mail "%" already exists.', v_email;
    END IF;

    -- ----------------------------------------------------------------
    -- 4. Hash the password with bcrypt (cost 12)
    -- ----------------------------------------------------------------
    v_password_hash := crypt(v_password, gen_salt('bf', 12));

    -- ----------------------------------------------------------------
    -- 5. Insert the user
    -- ----------------------------------------------------------------
    INSERT INTO users
        (organization_id, team_id, email, password_hash, first_name, last_name, role)
    VALUES
        (v_org_id, v_team_id, v_email, v_password_hash, v_first_name, v_last_name, v_role)
    RETURNING id INTO v_user_id;

    RAISE NOTICE 'Created % user "% %" <%> → %',
        v_role, v_first_name, coalesce(v_last_name, ''), v_email, v_user_id;

    -- ----------------------------------------------------------------
    -- 6. Outbox event (same transaction — no lost events)
    -- ----------------------------------------------------------------
    INSERT INTO outbox_events
        (organization_id, aggregate_type, aggregate_id, event_type, payload)
    VALUES (
        v_org_id, 'user', v_user_id, 'user.created',
        jsonb_build_object(
            'user_id',         v_user_id,
            'organization_id', v_org_id,
            'team_id',         v_team_id,
            'email',           v_email,
            'first_name',      v_first_name,
            'last_name',       v_last_name,
            'role',            v_role,
            'version',         1
        )
    );

    RAISE NOTICE 'Done. Login with e-mail: %', v_email;
END $$;
