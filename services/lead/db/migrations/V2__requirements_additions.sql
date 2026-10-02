-- =====================================================================
--  LEAD SERVICE  ->  lead_db  (V2 — requirements additions)
--  Implements schema changes listed in requirements §7.
-- =====================================================================

-- 1. Admin-managed disqualify reasons list (STA-3).
CREATE TABLE disqualify_reasons (
    id              UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    organization_id UUID        NOT NULL,
    name            TEXT        NOT NULL,
    is_active       BOOLEAN     NOT NULL DEFAULT true,
    UNIQUE (organization_id, name)
);

-- Link leads to the managed list.  The free-text column stays for audit
-- continuity; the FK is the authoritative reference from here on.
ALTER TABLE leads
    ADD COLUMN disqualify_reason_id UUID REFERENCES disqualify_reasons(id) ON DELETE RESTRICT;

-- 2. CAPTCHA and required-field config on web forms (WEB-1, WEB-4).
ALTER TABLE web_forms
    ADD COLUMN captcha_enabled  BOOLEAN NOT NULL DEFAULT true,
    ADD COLUMN required_fields  JSONB   NOT NULL DEFAULT '[]'::jsonb;

-- 3. Submission log for rate-limiting and repeat-submission detection (WEB-4, LDU-2).
CREATE TABLE web_form_submissions (
    id           UUID PRIMARY KEY DEFAULT gen_random_uuid(),
    web_form_id  UUID        NOT NULL REFERENCES web_forms(id) ON DELETE CASCADE,
    lead_id      UUID        REFERENCES leads(id) ON DELETE SET NULL,
    ip_address   INET        NOT NULL,
    submitted_at TIMESTAMPTZ NOT NULL DEFAULT now()
);
-- Rate-limit index: count submissions per IP per form in a sliding window.
CREATE INDEX idx_wfs_ip_form_time ON web_form_submissions (ip_address, web_form_id, submitted_at);
-- Repeat-submission detection: find latest submission for a lead's email quickly.
CREATE INDEX idx_wfs_lead ON web_form_submissions (lead_id, submitted_at);

-- 4. Retry scheduling on conversion saga (CNV-5).
ALTER TABLE lead_conversions
    ADD COLUMN next_retry_at TIMESTAMPTZ;

-- Update the pending-conversions index to include next_retry_at for the retry worker.
DROP INDEX IF EXISTS idx_conversions_pending;
CREATE INDEX idx_conversions_pending ON lead_conversions (next_retry_at NULLS FIRST)
    WHERE status NOT IN ('completed','compensated','failed');
