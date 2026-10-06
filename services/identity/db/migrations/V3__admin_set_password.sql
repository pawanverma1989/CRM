-- =====================================================================
--  IDENTITY SERVICE  ->  V3 Admin-set initial password
--  Adds a second way to add a user besides invitation:
--    1. An admin creates the user directly with an initial password
--       (POST /api/identity/v1/users). The user is 'active' at once.
--    2. users.must_change_password marks such users. The UI suggests
--       (does not force) a password change on every login until the
--       user changes or resets the password, which clears the flag.
--  Invited users and existing rows keep the default (false).
-- =====================================================================

ALTER TABLE users
    ADD COLUMN must_change_password BOOLEAN NOT NULL DEFAULT false;
