-- =====================================================================
--  CUSTOMER SERVICE  ->  V2 Schema changes
--  Implements §7 "Schema changes required" of Customer Service — Requirements:
--    1. contacts: CHECK that an email or a phone is present            (CON-1)
--    2. companies/contacts: version bumped by a trigger on every update (COM-3, CON-6)
--    3. New table picklists (company industry, contact source)         (§4)
--    4. New table reassignment_queue                                   (OWN-3)
--    5. New table user_refs (owner names, fed by Identity events)       (§6.2)
--    6. contacts.source: free text -> a value from picklists
--    7. companies.industry: free text -> a value from picklists.
--       NOT in the document's numbered list, but §4.1 requires Industry to be
--       "a single select from an admin-managed list"; the same picklists table
--       and a real FK (same database) enforce it the way change 6 does.
--  Also adds the field-level validation and the list/sort/quick-search indexes
--  §4, §3.4 and §3.8 call for.
--  Apply as: customer_svc role, connected to customer_db
-- =====================================================================

-- -----------------------------------------------------------------
-- 1. contacts: an email or a phone is required (CON-1 / AC-2)
-- -----------------------------------------------------------------
ALTER TABLE contacts
    ADD CONSTRAINT chk_contacts_email_or_phone
        CHECK (email IS NOT NULL OR phone IS NOT NULL);

-- -----------------------------------------------------------------
-- 2. version bump trigger (COM-3, CON-6, AC-6)
--    Only bumps when the caller left version untouched, so an application
--    doing optimistic locking (UPDATE ... SET version = n+1 WHERE version = n)
--    stays authoritative and its in-memory copy is not left stale. Raw SQL
--    updates still get a correct version for event ordering.
-- -----------------------------------------------------------------
CREATE OR REPLACE FUNCTION bump_version() RETURNS trigger AS $$
BEGIN
    IF NEW.version = OLD.version THEN
        NEW.version := OLD.version + 1;
    END IF;
    RETURN NEW;
END;
$$ LANGUAGE plpgsql;

CREATE TRIGGER trg_companies_version
    BEFORE UPDATE ON companies
    FOR EACH ROW EXECUTE FUNCTION bump_version();

CREATE TRIGGER trg_contacts_version
    BEFORE UPDATE ON contacts
    FOR EACH ROW EXECUTE FUNCTION bump_version();

-- -----------------------------------------------------------------
-- 3. picklists — admin-managed value lists (§4)
-- -----------------------------------------------------------------
CREATE TABLE picklists (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    list_type       TEXT        NOT NULL
                                CHECK (list_type IN ('company_industry','contact_source')),
    value           TEXT        NOT NULL CHECK (btrim(value) <> '' AND char_length(value) <= 100),
    sort_order      INT         NOT NULL DEFAULT 0,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    created_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at      TIMESTAMPTZ NOT NULL DEFAULT now(),
    UNIQUE (organization_id, list_type, value)
);
CREATE INDEX idx_picklists_org_type ON picklists (organization_id, list_type, sort_order)
    WHERE is_active;

CREATE TRIGGER trg_picklists_updated_at
    BEFORE UPDATE ON picklists
    FOR EACH ROW EXECUTE FUNCTION set_updated_at();

-- -----------------------------------------------------------------
-- 4. reassignment_queue — records of deactivated users (OWN-3)
--    The partial unique index makes a redelivered user.deactivated event a
--    no-op: a record can only sit in the queue once while unresolved (AC-12).
-- -----------------------------------------------------------------
CREATE TABLE reassignment_queue (
    id                UUID        PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id   UUID        NOT NULL,
    record_type       TEXT        NOT NULL CHECK (record_type IN ('contact','company')),
    record_id         UUID        NOT NULL,
    previous_owner_id UUID        NOT NULL,          -- identity.users.id, no FK
    new_owner_id      UUID,                          -- identity.users.id, no FK
    queued_at         TIMESTAMPTZ NOT NULL DEFAULT now(),
    resolved_at       TIMESTAMPTZ,
    resolved_by       UUID                           -- identity.users.id, no FK
);
CREATE UNIQUE INDEX uq_reassignment_open
    ON reassignment_queue (record_type, record_id) WHERE resolved_at IS NULL;
CREATE INDEX idx_reassignment_org_open
    ON reassignment_queue (organization_id, queued_at) WHERE resolved_at IS NULL;

-- -----------------------------------------------------------------
-- 5. user_refs — local read-only copy of Identity users, for owner names
--    in lists. Fed by user.created / user.updated / user.deactivated;
--    an event is applied only when its version beats source_version
--    (CLAUDE.md architecture rule 4).
-- -----------------------------------------------------------------
CREATE TABLE user_refs (
    user_id         UUID        PRIMARY KEY,         -- identity.users.id, no FK
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

-- -----------------------------------------------------------------
-- 6. contacts.source -> picklists.id
-- -----------------------------------------------------------------
ALTER TABLE contacts ADD COLUMN source_id UUID REFERENCES picklists(id);

INSERT INTO picklists (organization_id, list_type, value)
SELECT DISTINCT organization_id, 'contact_source', btrim(source)
FROM   contacts
WHERE  source IS NOT NULL AND btrim(source) <> ''
ON CONFLICT (organization_id, list_type, value) DO NOTHING;

UPDATE contacts c
SET    source_id = p.id
FROM   picklists p
WHERE  p.organization_id = c.organization_id
  AND  p.list_type = 'contact_source'
  AND  p.value = btrim(c.source);

ALTER TABLE contacts DROP COLUMN source;

CREATE INDEX idx_contacts_source ON contacts (organization_id, source_id)
    WHERE deleted_at IS NULL;

-- -----------------------------------------------------------------
-- 7. companies.industry -> picklists.id
-- -----------------------------------------------------------------
ALTER TABLE companies ADD COLUMN industry_id UUID REFERENCES picklists(id);

INSERT INTO picklists (organization_id, list_type, value)
SELECT DISTINCT organization_id, 'company_industry', btrim(industry)
FROM   companies
WHERE  industry IS NOT NULL AND btrim(industry) <> ''
ON CONFLICT (organization_id, list_type, value) DO NOTHING;

UPDATE companies c
SET    industry_id = p.id
FROM   picklists p
WHERE  p.organization_id = c.organization_id
  AND  p.list_type = 'company_industry'
  AND  p.value = btrim(c.industry);

ALTER TABLE companies DROP COLUMN industry;

CREATE INDEX idx_companies_industry ON companies (organization_id, industry_id)
    WHERE deleted_at IS NULL;

-- -----------------------------------------------------------------
-- 8. Tag rules enforced in the database (TAG-2)
--    Lower-case, trimmed, non-empty, <= 40 characters each, <= 20 per record.
--    A CHECK cannot contain a subquery, so an IMMUTABLE helper carries it.
-- -----------------------------------------------------------------
CREATE OR REPLACE FUNCTION tags_are_valid(t TEXT[]) RETURNS boolean
    LANGUAGE sql IMMUTABLE AS $$
    SELECT coalesce(array_length(t, 1), 0) <= 20
       AND NOT EXISTS (
           SELECT 1 FROM unnest(t) AS x
           WHERE char_length(x) > 40
              OR x <> lower(btrim(x))
              OR btrim(x) = ''
       );
$$;

ALTER TABLE companies ADD CONSTRAINT chk_companies_tags CHECK (tags_are_valid(tags));
ALTER TABLE contacts  ADD CONSTRAINT chk_contacts_tags  CHECK (tags_are_valid(tags));

-- -----------------------------------------------------------------
-- 9. Standard-field validation (§4.1, §4.2)
-- -----------------------------------------------------------------
ALTER TABLE companies
    ALTER COLUMN country SET DEFAULT 'IN',
    ADD CONSTRAINT chk_companies_name      CHECK (btrim(name) <> '' AND char_length(name) <= 200),
    ADD CONSTRAINT chk_companies_domain    CHECK (domain IS NULL OR char_length(domain) <= 253),
    ADD CONSTRAINT chk_companies_revenue   CHECK (annual_revenue IS NULL OR annual_revenue >= 0),
    ADD CONSTRAINT chk_companies_phone     CHECK (phone IS NULL OR char_length(phone) <= 30),
    ADD CONSTRAINT chk_companies_website   CHECK (website IS NULL OR website ~* '^https?://'),
    ADD CONSTRAINT chk_companies_address   CHECK (
        (address_line1 IS NULL OR char_length(address_line1) <= 200) AND
        (address_line2 IS NULL OR char_length(address_line2) <= 200) AND
        (city          IS NULL OR char_length(city)          <= 100) AND
        (state         IS NULL OR char_length(state)         <= 100) AND
        (postal_code   IS NULL OR char_length(postal_code)   <= 20)),
    ADD CONSTRAINT chk_companies_country   CHECK (country IS NULL OR country ~ '^[A-Z]{2}$'),
    ADD CONSTRAINT chk_companies_gstin     CHECK (
        gstin IS NULL OR gstin ~ '^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z][0-9A-Z]Z[0-9A-Z]$');

ALTER TABLE contacts
    ALTER COLUMN country SET DEFAULT 'IN',
    ADD CONSTRAINT chk_contacts_first_name CHECK (btrim(first_name) <> '' AND char_length(first_name) <= 100),
    ADD CONSTRAINT chk_contacts_last_name  CHECK (last_name IS NULL OR char_length(last_name) <= 100),
    ADD CONSTRAINT chk_contacts_email      CHECK (email IS NULL OR char_length(email) <= 254),
    ADD CONSTRAINT chk_contacts_phones     CHECK (
        (phone  IS NULL OR char_length(phone)  <= 30) AND
        (mobile IS NULL OR char_length(mobile) <= 30)),
    ADD CONSTRAINT chk_contacts_job_title  CHECK (job_title IS NULL OR char_length(job_title) <= 150),
    ADD CONSTRAINT chk_contacts_address    CHECK (
        (address_line1 IS NULL OR char_length(address_line1) <= 200) AND
        (address_line2 IS NULL OR char_length(address_line2) <= 200) AND
        (city          IS NULL OR char_length(city)          <= 100) AND
        (state         IS NULL OR char_length(state)         <= 100) AND
        (postal_code   IS NULL OR char_length(postal_code)   <= 20)),
    ADD CONSTRAINT chk_contacts_country    CHECK (country IS NULL OR country ~ '^[A-Z]{2}$');

-- -----------------------------------------------------------------
-- 10. List, sort, quick-search and recycle-bin indexes (§3.7, §3.8, NFR-2)
-- -----------------------------------------------------------------
CREATE INDEX idx_companies_org_name    ON companies (organization_id, name)       WHERE deleted_at IS NULL;
CREATE INDEX idx_companies_org_created ON companies (organization_id, created_at) WHERE deleted_at IS NULL;
CREATE INDEX idx_companies_org_updated ON companies (organization_id, updated_at) WHERE deleted_at IS NULL;
CREATE INDEX idx_companies_org_city    ON companies (organization_id, city)       WHERE deleted_at IS NULL;
CREATE INDEX idx_companies_org_country ON companies (organization_id, country)    WHERE deleted_at IS NULL;
CREATE INDEX idx_companies_domain_trgm ON companies USING GIN ((domain::text) gin_trgm_ops);
CREATE INDEX idx_companies_recycle_bin ON companies (organization_id, deleted_at) WHERE deleted_at IS NOT NULL;

CREATE INDEX idx_contacts_org_name     ON contacts (organization_id, first_name, last_name) WHERE deleted_at IS NULL;
CREATE INDEX idx_contacts_org_created  ON contacts (organization_id, created_at) WHERE deleted_at IS NULL;
CREATE INDEX idx_contacts_org_updated  ON contacts (organization_id, updated_at) WHERE deleted_at IS NULL;
CREATE INDEX idx_contacts_org_city     ON contacts (organization_id, city)       WHERE deleted_at IS NULL;
CREATE INDEX idx_contacts_email_trgm   ON contacts USING GIN ((email::text) gin_trgm_ops);
CREATE INDEX idx_contacts_phone_trgm   ON contacts USING GIN (phone_normalized gin_trgm_ops);
CREATE INDEX idx_contacts_recycle_bin  ON contacts (organization_id, deleted_at) WHERE deleted_at IS NOT NULL;

-- merge_history lookups by either side of a merge (DUP-5, DSR-1)
CREATE INDEX idx_merge_history_survivor ON merge_history (organization_id, survivor_id);
CREATE INDEX idx_merge_history_loser    ON merge_history (organization_id, loser_id);

-- custom field definitions are read on every form load (CF-1)
CREATE INDEX idx_custom_fields_org_entity
    ON custom_field_definitions (organization_id, entity_type, sort_order) WHERE is_active;
