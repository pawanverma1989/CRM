# CRM microservices: database-per-service design

Ten services, each with its **own PostgreSQL database and its own login**. A service can only
touch its own tables; anything it needs from another service arrives through an **API call** or an
**event**.

## Files

| File | Database | Service |
|---|---|---|
| `00_create_databases.sql` | all | Creates the 10 databases and 10 login roles (one per service) |
| `01_identity_db.sql` | identity_db | Identity |
| `02_customer_db.sql` | customer_db | Customer (companies & contacts) |
| `03_lead_db.sql` | lead_db | Lead |
| `04_sales_db.sql` | sales_db | Sales (pipelines & deals) |
| `05_activity_db.sql` | activity_db | Activity (timeline & tasks) |
| `06_notification_db.sql` | notification_db | Notification |
| `07_search_db.sql` | search_db | Search |
| `08_reporting_db.sql` | reporting_db | Reporting |
| `09_data_transfer_db.sql` | data_transfer_db | Data transfer (import/export) |
| `10_compliance_db.sql` | compliance_db | Audit & compliance |

Setup: `psql -f 00_create_databases.sql` as a superuser, then run each `NN_*.sql` connected to its
own database (as its own role). In production, run them through each service's migration tool
(Flyway, Liquibase, Prisma, Alembic…) so every service versions its own schema.

## Service map

| # | Service | Owns (tables) | Why it is its own service |
|---|---|---|---|
| 1 | **Identity** | organizations, teams, users, sessions, password resets | Security boundary; every other service trusts its tokens |
| 2 | **Customer** | companies, contacts, custom fields, merge history | Core master data; the people and businesses you sell to |
| 3 | **Lead** | leads, lead sources, web forms, lead conversions | Different lifecycle (unqualified → converted), public form endpoint |
| 4 | **Sales** | pipelines, stages, deals, deal contacts, stage history, loss reasons | The revenue engine; heaviest business rules |
| 5 | **Activity** | activities, participants, mentions, tasks | Highest write volume; attaches to records in 3 other services |
| 6 | **Notification** | notifications, preferences, email deliveries | Pure consumer of events; scales and fails independently |
| 7 | **Search** | search documents, saved views | Combines data from 3 services into one index |
| 8 | **Reporting** | dim_*/fact_* read-model tables + report views | Heavy read queries never slow down the live services |
| 9 | **Data transfer** | import/export jobs, row errors, undo records | Long-running batch work kept away from request traffic |
| 10 | **Audit & compliance** | audit log, consents, data subject requests | Legal record must be tamper-proof and central |

## The rules that make separate databases work

1. **No foreign keys across databases.** Columns like `deals.company_id` or `activities.contact_id`
   hold an id from another service, commented in each file. Integrity is kept by events (e.g. on
   `company.deleted`, Sales and Activity update their rows).
2. **Every change publishes an event via the outbox.** Each database has `outbox_events`: the service
   writes its data change *and* the event in one transaction; a relay publishes to the message broker
   (Kafka, RabbitMQ or NATS). Events are never lost or sent for rolled-back changes.
3. **Every consumer is idempotent.** Each database has `processed_events`; brokers can deliver twice.
4. **Local read-only copies instead of cross-service joins.** Sales keeps `customer_refs` (company and
   contact names for deal cards); Activity keeps `record_refs` (names of leads/contacts/companies/deals
   for task lists); Notification keeps `user_contacts`. Each row has `source_version` so an older event
   arriving late never overwrites newer data.
5. **Permissions travel in the token.** Identity issues a JWT with `user_id`, `organization_id`,
   `role` and `visible_owner_ids` (see `v_user_visibility`). Each service filters
   `owner_id = ANY(visible_owner_ids)` without calling Identity.

## Event catalogue

| Publisher | Events | Main consumers |
|---|---|---|
| Identity | `user.created/updated/deactivated`, `team.updated`, `user.logged_in/login_failed` | Notification, Reporting, Customer/Lead/Sales (reassignment), Compliance |
| Customer | `company.*`, `contact.*` incl. `.merged`, `.reassigned`, `.deleted` | Sales, Activity, Lead, Search, Compliance |
| Lead | `lead.created/assigned/status_changed/converted/deleted`, `web_form.submitted` | Activity, Search, Reporting, Notification, Compliance |
| Sales | `deal.created/updated/stage_changed/won/lost/reassigned/deleted`, `pipeline.updated` | Activity, Search, Reporting, Notification, Lead, Compliance |
| Activity | `activity.logged/deleted`, `task.assigned/reminder_due/overdue/completed`, `mention.created` | Sales (`last_activity_at`), Reporting, Notification, Compliance |
| Data transfer | `import.completed/failed`, `export.completed` | Notification, Compliance |
| Compliance | `dsr.erasure_requested`, `dsr.access_requested`, `consent.changed` | Every service holding personal data |

Standard event envelope: `event_id`, `event_type`, `organization_id`, `aggregate_id`, `version`,
`occurred_at`, `actor_id`, `payload`.

## Synchronous API calls (the only ones)

| Caller → Callee | When |
|---|---|
| Any service → Identity (JWKS endpoint) | Verify JWT signatures (cached) |
| Lead → Customer, Sales | Lead conversion (below) |
| Data transfer → Customer / Lead / Sales | Bulk create/update during import; paged list during export |
| Sales / Activity → Customer (fallback) | Only if a record is missing from the local copy |
| API gateway / frontend → Search | Global search box; then fetches full records from the owning service |

## Workflows that span services

**Lead conversion (saga, run by the Lead service, tracked in `lead_conversions`)**
1. Create or reuse the company → Customer service
2. Create the contact → Customer service (`source_lead_id` unique = safe retry)
3. Optionally create the deal → Sales service (`source_lead_id` unique = safe retry)
4. Mark the lead converted, publish `lead.converted`.
   Activity re-links the lead's timeline to the contact/deal; Compliance copies consents.
If a step fails it is retried from the last completed step; nothing is duplicated.

**Merging duplicate contacts:** Customer publishes `contact.merged {survivor_id, loser_id}`;
Sales, Activity, Lead and Search re-point their references.

**Erasure request (right to be forgotten):** Compliance creates the request and one
`dsr_service_tasks` row per service, publishes `dsr.erasure_requested`; each service hard-deletes that
person's data and replies `dsr.erasure_completed`. The request closes when every task is completed.

**Reports:** Reporting fills `dim_*`/`fact_*` tables from events. Dashboards are seconds behind live
data, which is normal for reporting.

## Practical advice for building it

- **Start smaller if the team is small.** Ten services is the target shape, not a day-one requirement.
  A sensible first deployment is 5 services: Identity · Customer+Lead · Sales+Activity ·
  Notification · Platform (Search+Reporting+Data transfer+Compliance). Because every database is
  already separate, splitting later is a deployment change, not a data migration.
- One PostgreSQL server holding all databases is fine for development and early production. Move a
  database to its own server only when its load demands it (Activity and Reporting usually first).
- Encryption at rest, TLS, automated backups and point-in-time recovery must be set up **per database**.
