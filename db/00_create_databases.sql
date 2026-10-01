-- =====================================================================
--  Creates one database and one login role per microservice.
--  Run once as a superuser (psql -f 00_create_databases.sql), then run each
--  NN_<name>_db.sql file while connected to its own database.
--  Each service gets credentials ONLY for its own database, so no service
--  can read or write another service's tables.
--  Replace every 'change_me' with a secret from your vault before running.
--  In production these can be separate servers; for development, one
--  PostgreSQL server holding all databases is fine.
-- =====================================================================

CREATE ROLE identity_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE identity_db OWNER identity_svc;
REVOKE ALL ON DATABASE identity_db FROM PUBLIC;

CREATE ROLE customer_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE customer_db OWNER customer_svc;
REVOKE ALL ON DATABASE customer_db FROM PUBLIC;

CREATE ROLE lead_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE lead_db OWNER lead_svc;
REVOKE ALL ON DATABASE lead_db FROM PUBLIC;

CREATE ROLE sales_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE sales_db OWNER sales_svc;
REVOKE ALL ON DATABASE sales_db FROM PUBLIC;

CREATE ROLE activity_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE activity_db OWNER activity_svc;
REVOKE ALL ON DATABASE activity_db FROM PUBLIC;

CREATE ROLE notification_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE notification_db OWNER notification_svc;
REVOKE ALL ON DATABASE notification_db FROM PUBLIC;

CREATE ROLE search_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE search_db OWNER search_svc;
REVOKE ALL ON DATABASE search_db FROM PUBLIC;

CREATE ROLE reporting_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE reporting_db OWNER reporting_svc;
REVOKE ALL ON DATABASE reporting_db FROM PUBLIC;

CREATE ROLE data_transfer_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE data_transfer_db OWNER data_transfer_svc;
REVOKE ALL ON DATABASE data_transfer_db FROM PUBLIC;

CREATE ROLE compliance_svc LOGIN PASSWORD 'change_me';
CREATE DATABASE compliance_db OWNER compliance_svc;
REVOKE ALL ON DATABASE compliance_db FROM PUBLIC;
