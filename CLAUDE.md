# CRM Platform

A customer relationship management (CRM) system built as microservices, each with its own database.
The current scope is the MVP ("must-have") feature set: contacts & companies, leads, deals pipeline,
activities & tasks, search, users & permissions, reports, import/export, notifications, audit &
compliance.

## Tech stack

<!-- TODO: fill in once decided. Until then, ask before introducing a language, framework or library. -->
- Backend: .NET 10
- Database: PostgreSQL 14+ (one database per service)
- Message broker: RabbitMQ
- Frontend: Latest version of React

## Repository layout

```
CRM
  Docker/                                  combined docker compose file
  NGINX/                                   nginx config
  db/
    00_create_databases.sql                creates every database + one login role per service
    NN_<service>_db.sql                    starting schema per service (seed into first migration)
  docs/
    architecture.md                        service map, event catalogue, sagas (source of truth)
  services/
    identity/
      frontend/                            React app — users, roles, org settings UI
      db/
        migrations/                        versioned schema for identity_db
    customer/
      frontend/                            React app — companies, contacts, custom fields UI
      db/
        migrations/                        versioned schema for customer_db
    lead/
      frontend/                            React app — lead list, web forms, conversion UI
      db/
        migrations/                        versioned schema for lead_db
    sales/
      frontend/                            React app — pipeline board, deals, stage history UI
      db/
        migrations/                        versioned schema for sales_db
    activity/
      frontend/                            React app — calls, emails, meetings, tasks UI
      db/
        migrations/                        versioned schema for activity_db
    notification/
      frontend/                            React app — notification inbox, preferences UI
      db/
        migrations/                        versioned schema for notification_db
    search/
      frontend/                            React app — global search, saved views UI
      db/
        migrations/                        versioned schema for search_db
    reporting/
      frontend/                            React app — report builder, dashboards UI
      db/
        migrations/                        versioned schema for reporting_db
    data-transfer/
      frontend/                            React app — import/export job management UI
      db/
        migrations/                        versioned schema for data_transfer_db
    compliance/
      frontend/                            React app — audit log, consent, DSR UI
      db/
        migrations/                        versioned schema for compliance_db
```

Each service owns exactly two sub-trees: `frontend/` for its React application and `db/` for its
PostgreSQL schema. The starting schemas are in `db/NN_<service>_db.sql`; turn each one into that
service's first migration under `services/<service>/db/migrations/`. After that, change a schema
only through a new migration in that service's `db/migrations/` folder.

## Architecture rules (do not break these)

1. **A service only touches its own database.** Never query, join or write another service's
   tables, and never share a DB login between services. Need data from another service? Use its
   API, or keep a local read-only copy updated from its events.
2. **No foreign keys across services.** Columns holding another service's id (e.g. `deals.company_id`,
   `activities.contact_id`) are plain `UUID` with a comment naming the owner. Foreign keys are
   used freely *within* a service's database.
3. **Publish events through the outbox.** Write the data change and its `outbox_events` row in the
   same transaction. Never publish to the broker directly from request code. Every service that
   writes `outbox_events` must also run an outbox relay (`Workers/OutboxRelay.cs`) that publishes the
   full envelope with `event_id` = the outbox row id — a missing relay fails silently (consumers'
   local copies just stay empty).
4. **Consumers are idempotent.** Record every handled `event_id` in `processed_events` and skip
   duplicates. For local copies (`customer_refs`, `record_refs`, `search_documents`, `fact_*`),
   apply an event only if its `version` is newer than `source_version`.
5. **Synchronous calls are the exception.** Allowed: JWT key lookup (Identity), lead conversion
   (Lead → Customer, Sales), bulk import/export (Data transfer → Customer/Lead/Sales), global search.
   Anything else should be an event. Ask before adding a new sync dependency.
6. **Permissions come from the JWT.** Claims: `user_id`, `organization_id`, `role`,
   `visible_owner_ids`. Every query filters by `organization_id` and, unless the role is admin,
   by `owner_id = ANY(visible_owner_ids)`. Never trust an `organization_id` sent in a request body.
7. **Multi-step writes across services are sagas** with persisted state and idempotency keys
   (see `lead_conversions` and the `source_lead_id` unique indexes). No distributed transactions.

## Database conventions

- Primary keys: `UUID DEFAULT gen_random_uuid()`.
- Every business table has `organization_id` (multi-tenant from day one).
- `created_at`, `updated_at` (trigger-maintained), and `deleted_at` for soft delete. Default
  queries exclude `deleted_at IS NOT NULL`. Hard delete only for erasure requests and recycle-bin purge.
- Mutable aggregates carry `version INT`, bumped on every update. Use it for optimistic locking
  and put it in every event payload.
- Small fixed value lists: `TEXT` + `CHECK`, not PostgreSQL `ENUM`.
- Custom fields: definitions in `custom_field_definitions`, values in a `custom_fields JSONB`
  column validated by the service before saving.
- Tags: `tags TEXT[]`, stored lower-case, GIN-indexed.
- Emails are `CITEXT`; phones also stored normalised to E.164 in `phone_normalized`.
- Money: `NUMERIC(16,2)` + `currency CHAR(3)` (default `INR`). Never floats.
- Timestamps: `TIMESTAMPTZ`, stored in UTC; convert to the org/user timezone only in the UI.
- Index every foreign key and every column used in list filters; use partial indexes
  `WHERE deleted_at IS NULL` for list queries.

## Business rules enforced in the database (keep them)

- One live contact per email per organization; one live company per domain per organization.
- A lead needs an email or a phone; disqualifying needs a reason; converting sets `converted_at`.
- A deal's `status` and `closed_at` follow its stage (trigger); a lost deal must have `loss_reason_id`.
- Every deal stage change writes `deal_stage_history` and a `deal.*` outbox event (trigger).
- Every activity must be linked to at least one of lead / contact / company / deal.
- `audit_log` is append-only (trigger blocks UPDATE/DELETE).
- Consents are history: insert a new row for each change, never update.

## Events

Name events `<aggregate>.<past_tense_verb>`: `contact.created`, `deal.stage_changed`, `lead.converted`.
Envelope: `event_id`, `event_type`, `organization_id`, `aggregate_id`, `version`, `occurred_at`,
`actor_id`, `payload`. Adding a payload field is fine; removing or renaming one needs a new
event version (`deal.stage_changed.v2`). The full catalogue lives in `docs/architecture.md`; update
it whenever you add or change an event.

Each publishing service has its own topic exchange `crm.<service>.events` (routing key = event
type). Consumers bind the routing keys they handle in `RabbitMq:ConsumeRoutingKeys`; a new event a
consumer must react to (e.g. `user.reactivated`) needs a binding there as well as a handler.

### User data in other services

- Services that show or assign owners keep a local `user_refs` copy fed by Identity's `user.*`
  events and serve it via their own `GET /owners`. Do not add a synchronous user-list call to
  Identity (decision D17 in `docs/architecture.md` §7.7).
- `user_refs` holds only `user_id`, `organization_id`, `display_name`, `is_active`,
  `source_version`, `updated_at` — never email, phone or role, and never fall back to the email
  for the display name.
- `users.version` is bumped by trigger; every `user.*` payload carries it.
- Backfill or repair: admin resync `POST /api/identity/v1/users/resync-events` (also a button on the
  Identity Users page) re-sends every user as `user.updated` with `resync: true`. Safe to repeat.
- New users appear in other services after a few seconds. Owner pickers use the per-app
  `useOwners({ fresh: true })` hook and `OwnerSelectHint` (refetch on open + Refresh button); reuse
  them for any new owner picker.

### Health endpoints

- `/health` = liveness only (database); the Docker healthcheck uses it. Never add broker or event
  checks to it.
- `/health/events` = checks tagged `events`: outbox lag (Degraded when the oldest unpublished row is
  older than `RabbitMq:OutboxLagWarningSeconds`, default 60) and, for consumers, dead-letter queue
  depth (`<queue>.dead`). Degraded/Unhealthy return 503. New services must expose both.

## Security & compliance

- Passwords: argon2id or bcrypt. Reset and session tokens are stored only as hashes.
- Every create/update/delete/merge/convert/export emits an event the compliance service writes
  to `audit_log`, including field-level `changes` for updates.
- Erasure requests: each service that stores personal data must handle `dsr.erasure_requested`,
  hard-delete that person's data (including local copies and search documents) and reply
  `dsr.erasure_completed`.
- Never log personal data (emails, phones, names) in application logs; log ids.
- Target compliance: GDPR and India's DPDP Act.

## Working in this repo

- Before changing a service, read its schema and that service's section in `docs/architecture.md`.
- Keep changes inside one service where possible. If a change needs a new event or API between
  services, say so and update the architecture doc in the same change.
- Every schema change ships as a new migration under `services/<service>/db/migrations/`, tested
  by applying it to a fresh database created by `db/00_create_databases.sql`, running as that
  service's own role (not a superuser).
- Tests cover the database rules above and event handler idempotency (the same event delivered twice).
- To diagnose an empty owner list or other missing local copy, check in this order: the publisher's
  `outbox_events` for rows with `published_at IS NULL`, its `/health/events`, the consumer's
  dead-letter queue, then the consumer's `processed_events` and local copy table.
- Features outside the MVP list (email sync, workflow automation, WhatsApp, AI features, quotes,
  mobile apps, SSO) are out of scope unless explicitly requested.


## Git & Docker notes

- `services/*/db/data/` is the PostgreSQL on-disk data directory created when a service's DB
  container first starts. It is binary, large, and changes on every write — **never commit it**.
  It is excluded via `.gitignore`; the schema is captured in `db/NN_<service>_db.sql` and applied
  fresh on `docker compose up`.
- `Docker/.env` contains secrets and is also excluded via `.gitignore`. Use `Docker/.env.example`
  as the template.

## Subagents (always use them)

**Always delegate work to the matching subagent below instead of doing it inline.** This is a
standing instruction from the user: you do not need to ask before spawning these agents. When a
task spans several areas (e.g. a new backend endpoint plus its UI), split it and hand each part to
its agent (run independent parts in parallel). Only do the work directly when no agent below fits
(e.g. editing docs, SQL migrations, or a quick read-only question).

| Agent | Use it for |
|---|---|
| `dotnet-backend-developer-agent` | Any .NET backend work: APIs, controllers, services, EF Core, workers (outbox relay, consumers), backend tests |
| `react-developer-agent` | Any frontend work in `services/*/frontend/`: components, hooks, pages, styling, frontend tests |
| `docker-config-agent` | Docker Compose, Dockerfiles, NGINX and other container/infra configuration |
| `git-automation-agent-dotnet-react` | Commits, branches, check-ins. **Always ask the user for the run mode first.** |

When briefing an agent, pass along the relevant rules from this file (architecture rules, database
conventions, event envelope) and the service's section of `docs/architecture.md`, since the agent
starts without this conversation's context.

