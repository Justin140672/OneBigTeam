# Design: Customer Database Assignment & Manual Provisioning

Owner: One Big Team engineering
Status: **Design — approved shape, implementation split into sized sub-tickets (see section 10).**
Related: `specifications/architecture/01-solution-structure.md`, `05-database-standards.md`,
`08-deployment-architecture.md`, `docs/runbooks/backup-and-disaster-recovery.md`,
`docs/compliance/data-protection-operations.md`.

This document covers **design only**. No connection-routing behaviour changes as a result of merging
it. Automatic database creation and any self-service migration UI are explicitly out of scope.

---

## 1. Problem & goals

Today every customer (company) shares one PostgreSQL database (Supabase), isolated by a
`company_id` column on every tenant-owned table and a global EF query filter. Some customers will
contractually require physical isolation in a **dedicated database**. We need:

- A single authoritative record of which database each customer uses.
- Dedicated databases that are **manually provisioned** by an operator (no automation in v1).
- Existing and newly-signed customers to **default to the shared database** with no behaviour change.
- A dedicated database that is unreachable to **fail loudly** — never silently fall back to shared
  (that would leak or split a tenant's data across two stores).

Non-goals for v1: automatic provisioning, live/zero-downtime migration, per-customer schema drift,
customer self-service, sharding the shared database.

---

## 2. Core concept: the customer database assignment record

A new **central, non-tenant** table owned by the **Companies module** (it already owns the customer
/ subscription / platform-settings records and is the module a request hits first for tenant
resolution concerns):

`companies.customer_database_assignments`

| column | type | notes |
|---|---|---|
| `id` | uuid PK | |
| `company_id` | uuid NOT NULL, unique | FK-in-spirit to `companies.companies`; one assignment per customer |
| `mode` | text NOT NULL | `shared` \| `dedicated` |
| `database_key` | text NULL | required when `mode = dedicated`; **logical name only**, e.g. `cust-acme-eu1`. Never a connection string. |
| `status` | text NOT NULL | `active` \| `provisioning` \| `migrating` \| `suspended` |
| `assigned_at` | timestamptz NOT NULL | |
| `assigned_by_user_id` | uuid NULL | platform admin who set it |
| `notes` | text NULL | operator free-text (ticket refs etc.) |
| `created_at` / `updated_at` | timestamptz NOT NULL | |

Rules:
- Every customer has exactly one row. Absence of a row is treated as `shared` by the resolver, but a
  backfill migration inserts an explicit `shared` row for every existing company so the table is the
  complete source of truth.
- `mode = dedicated` requires a non-null `database_key` **and** `status = active` before the
  application will route to it. `provisioning` / `migrating` mean "not ready" — see section 6.
- This table is a documented global/system exception to the `company_id`-filter rule (same category
  as `platform_settings`): it *is* keyed by `company_id` but is read by infrastructure before a
  tenant context exists, so it is queried without the global filter.

### Credentials live in configuration, not the database

`database_key` maps to a connection string resolved from secure configuration at runtime:

```
CustomerDatabases:cust-acme-eu1:ConnectionString   (env var / Railway secret / Key Vault)
```

The resolver builds a map `database_key -> connection string` from configuration on startup. A
`dedicated` assignment whose `database_key` has **no** matching configuration entry is a hard
startup/first-request error for that tenant (section 6), never a fallback to shared.

---

## 3. What stays central vs per-customer

"Central" = always in the shared/platform database, one copy for the whole platform. "Per-customer"
= lives in whichever database the customer is assigned to (shared DB for shared customers, the
dedicated DB for dedicated customers).

| Module / DbContext | Schema | Placement | Reason |
|---|---|---|---|
| **Companies** — `companies.companies`, `company_settings`, `company_addresses`, `public_holidays`, `customer_subscriptions`, `customer_database_assignments`, `platform_settings`, `platform_metrics_snapshot` | `companies` | **Central** (routing/assignment, billing, platform config) **+ per-customer** (company profile/settings/addresses/holidays) — **split DbContext responsibilities, see note** | Routing & billing must be queryable platform-wide and before tenant resolution; company profile/settings are tenant data |
| **Identity** — `identity.user_profiles`, `role_assignments`, `platform_administrators`, Supabase auth mapping | `identity` | **Central** | Authentication/tenant-resolution must work before we know the customer's database; a user's `company_id` is the input to routing. Platform admins are global. |
| **Employees, Leave, Sickness, Recruitment, Tasks, Documents, Assets, Onboarding, Offboarding, Probation, CompanyOnboarding, DataImport, Support, Notifications** | own schema each | **Per-customer** | Pure tenant-owned business data; the entire point of a dedicated DB |
| **Reporting** — projections, saved views, favourites, exports | `reporting` | **Per-customer** | Projections are built from that customer's events/data and must live with it |
| **Audit** (`HR.Infrastructure`, `audit` schema) | `audit` | **Per-customer for tenant events; central for platform-admin events** | Tenant audit entries (`company_id` set) follow the customer's data so a dedicated customer's audit trail is fully isolated and restorable with their DB. Platform-admin events (`company_id = Guid.Empty`, e.g. `platform-settings.updated`, `marketing-feature.updated`) stay central. |
| **Marketing** (`HR.Modules.Marketing`, `marketing` schema) | `marketing` | **Central** | Public product/marketing content — not tenant data at all |
| **Hangfire** job storage | `hangfire` | **Central** (one queue) with per-customer connection passed as job argument — see section 4 | Operating two dozen Hangfire schemas is unacceptable operational load |

### Companies module split

The Companies module currently has one `CompaniesDbContext` spanning both central (billing, platform
settings, assignment) and per-customer (company profile/settings/addresses/holidays) tables. Options,
in order of preference:

1. **Keep one `CompaniesDbContext` but route it per-request** like every other tenant DbContext, and
   move the three genuinely-central tables (`customer_database_assignments`, `platform_settings`,
   `platform_metrics_snapshot`, `customer_subscriptions`, `platform_administrators` already in
   Identity) into a small dedicated **`HR.Modules.Companies` "platform" DbContext**
   (`PlatformDbContext`) that is always bound to the central connection string.
   → Cleanest boundary; one migration to carve out the platform tables; the assignment resolver
   depends only on `PlatformDbContext`.
2. Leave everything in `CompaniesDbContext` bound to central, and accept that a dedicated customer's
   company profile/settings physically live in the shared DB while their employees etc. live in the
   dedicated DB. → Rejected: violates "a dedicated customer's data is isolated", complicates
   restore/delete, and creates a cross-database dependency for every feature that joins company
   settings.

**Decision: option 1.** Sub-ticket (a) includes carving out `PlatformDbContext`.

### Cross-module / cross-database dependencies to sever

- **Local projections only.** Modules already copy the employee fields they need (name, manager)
  into their own schema via integration events — this continues to work unchanged inside a single
  customer database. No module queries another module's schema, so there is no cross-database join.
- **`IActiveCompanyDirectory` / `ICompanyProvisioner` / settings readers** (Companies contracts
  consumed by other modules): these run inside a request that already has a tenant context, so they
  resolve against the same customer database as the caller. The provisioner (signup) runs against
  central for the assignment row + company row, then the per-customer schema create/migrate.
- **Reporting** must never aggregate across customers, so it never needs more than one customer DB
  at a time. Platform-wide admin metrics come from `platform_metrics_snapshot` (central), fed by a
  job that iterates customers (section 4).

---

## 4. Impact review

### 4.1 Request routing / connection resolution

- New `ICustomerDatabaseResolver` in `HR.Infrastructure` (technical concern, not business logic):
  `Task<CustomerDatabaseConnection> ResolveAsync(Guid companyId, CancellationToken)`.
- Resolution order: (1) look up assignment in `PlatformDbContext` (cached in `IMemoryCache`,
  short TTL ~60s, explicit invalidation on assignment change); (2) `shared` → the shared
  connection string; (3) `dedicated` + `status=active` + `database_key` present + configuration
  entry present → that connection string; (4) anything else → throw
  `CustomerDatabaseUnavailableException` (maps to HTTP 503 with a generic body; logged with
  `company_id` + `database_key`, no credentials).
- Every tenant DbContext is registered so its `UseNpgsql` connection string comes from the resolver
  for the current `ICurrentTenant.CompanyId`, instead of a single fixed string. Implemented once via
  a shared `AddTenantDbContext<TContext>()` helper in `HR.Infrastructure` so all ~15 modules change
  identically.
- Middleware order: tenant resolution (Identity, central) → database resolution → module pipeline.
  `ReadOnlyModeMiddleware` (Companies) stays after both.

### 4.2 Background jobs (Hangfire)

- Hangfire storage stays central (one `hangfire` schema).
- Every tenant-scoped job already takes `companyId` as an argument. Job bodies must resolve the
  customer database via `ICustomerDatabaseResolver` at execution time (they run outside a request,
  so there is no ambient tenant/database context).
- A job whose customer is `dedicated` + not `active` (mid-migration / suspended) must **fail and
  retry with backoff**, not run against shared. Hangfire's retry handles the transient
  provisioning/migration window.
- Platform-wide sweeper jobs (retention purge, metrics snapshot, deletion queue) iterate the
  customer list from central and open each customer DB in turn; a single customer's unreachable DB
  is logged and skipped for that run (does not fail the whole sweep) — except deletion/legal-hold
  jobs, which must hard-fail for the affected customer so the obligation is not silently missed.

### 4.3 Reporting & exports

- Run entirely within one customer database — no code change beyond getting their connection from
  the resolver.
- Export files land in Supabase Storage keyed by `company_id` as today; storage is not per-customer
  in v1 (documented; a follow-up if a customer contractually requires isolated object storage).

### 4.4 EF Core migrations across all module DbContexts

- Each module keeps its own migrations. The migration *set* is identical for every database; only
  the target connection differs.
- New operator tooling (`HR.MigrationRunner` console, sub-ticket (c)) applies **all module
  migrations to one target database** given a `database_key` or explicit connection string. The
  shared DB continues to migrate on API startup (dev) / via pipeline (prod) exactly as now.
- A dedicated database is only ever considered `active` when its migration history matches the
  application's expected set. The runner records/verifies this; startup logs a warning (not a
  crash) if a dedicated DB is behind, and the resolver treats "schema behind" as unavailable.
- Schema-version skew across customers is the main new operational risk — see section 8.

### 4.5 Deletion & data-subject requests

- Deleting a dedicated customer = drop/retire their database (operator step in the deletion runbook)
  **plus** delete their central rows (assignment, company, subscription, user profiles, platform
  audit references). The deletion queue job must branch on `mode`.
- Legal hold (`ILegalHoldStatusReader`) is read from central and already blocks purge jobs; it must
  additionally block the "retire dedicated DB" operator step (enforced by runbook + a pre-flight
  check in the tooling).
- Restore of a dedicated customer is independent of other customers — a point in favour of the
  design; captured in the DR runbook update (sub-ticket (c)).

### 4.6 Cross-module DB dependencies

None are cross-*database* because no module queries another module's schema today. The only true
central dependencies are tenant resolution (Identity) and assignment lookup (Companies platform),
both of which are central by design. Confirmed by the existing architecture tests.

---

## 5. Tenant-isolation testing (both modes)

Add to `HR.Integration.Tests` a mode-parameterised harness:

- **Shared mode (existing, keep):** two companies in one DB; every endpoint proves `company_id`
  filtering; cross-tenant id access returns 404/403; global query filter cannot be bypassed.
- **Dedicated mode (new):** spin a second Postgres database in the test host; assign company B to it;
  assert:
  - company B's business rows physically exist only in DB B, company A's only in the shared DB;
  - an authenticated company-B user's requests hit DB B (assert via a row written in the request
    then read directly from DB B);
  - a company-A user can never read company-B data even though they are in different databases;
  - removing/breaking DB B's connection string makes company-B requests return 503 and **zero**
    company-B queries fall through to the shared DB (assert shared DB untouched);
  - a background job for company B runs against DB B; with DB B `status = migrating` the job retries
    and does not touch shared;
  - migrating company B shared→dedicated (copy + flip assignment) preserves row counts and
    `company_id` values.
- **Architecture test:** every tenant DbContext is registered through `AddTenantDbContext<T>()` (no
  module binds a hard-coded connection string), and `PlatformDbContext` is the only context bound to
  the central string.

---

## 6. Failure semantics (explicit)

| Situation | Behaviour |
|---|---|
| No assignment row | Treat as `shared` (backfill makes this not occur in practice) |
| `dedicated`, `status != active` | Requests: 503. Jobs: retry with backoff. Never shared. |
| `dedicated`, `database_key` set, no config entry | Hard error at startup (fail the deploy) if detectable; otherwise 503 on first request for that tenant + alert. Never shared. |
| Dedicated DB reachable but schema behind expected migrations | Treated as unavailable (503 / job retry) + alert |
| Dedicated DB connection drops mid-request | Request fails (500/503); no retry against shared |
| Shared DB down | Existing behaviour (platform-wide outage) |

Rationale: a dedicated customer choosing physical isolation must never have data written to, or read
from, the shared store. Loud failure is strictly safer than a fallback.

---

## 7. Manual provisioning & migration runbook (summary — full runbook in sub-ticket (c))

**Provision a new dedicated database for customer X:**
1. Operator creates the database (Supabase project/branch or Postgres instance). Record the host.
2. Add `CustomerDatabases:<database_key>:ConnectionString` to the environment's secret store. Deploy
   config (no code deploy).
3. Set assignment: `mode=dedicated`, `database_key=<key>`, `status=provisioning` (platform admin UI
   or SQL).
4. Run `HR.MigrationRunner --database-key <key>` to create schemas + apply all module migrations.
5. Runner verifies migration history == expected set; marks internal check pass.
6. **New customer:** set `status=active`. Done — signup/first login now routes there.

**Migrate an existing customer from shared → dedicated:**
1–5 as above (`status=provisioning`).
2a. Announce a maintenance window to the customer.
6. Set `status=migrating`. This makes the resolver return 503 for their users and their jobs retry —
   i.e. **pause customer writes and jobs**.
7. Wait for in-flight Hangfire jobs for that `company_id` to drain (dashboard filter) or let them
   fail into retry.
8. Copy data: `pg_dump` the shared DB filtered to `company_id = X` for every tenant schema →
   restore into the dedicated DB. (Tooling provides a `--company-id` filtered copy command.)
9. Validate: per-schema row counts match; `SELECT count(*) WHERE company_id <> X` is 0 in the
   dedicated DB; spot-check key aggregates (employee count, open leave requests) via a read-only
   app instance pointed at the dedicated DB.
10. Flip: `status=active`. Invalidate the resolver cache (deploy or admin action).
11. Verify: log in as a test user for customer X, exercise create/read on 2–3 modules, confirm rows
    land in the dedicated DB.
12. Retain the customer's rows in the shared DB for the backup-retention window, then delete
    `company_id = X` rows from the shared schemas (separate cleanup job, gated on `status=active`
    for ≥ N days and no rollback flag).

**Rollback (any point before step 12 cleanup):**
- Set `status=active` + `mode=shared`, `database_key=null`. Invalidate cache. Users are back on the
  shared DB with their original (untouched) data. Discard/park the dedicated DB.
- After step 12 cleanup, rollback requires restoring `company_id = X` into shared from backup —
  treat as a DR event.

---

## 8. Risks & mitigations

| Risk | Mitigation |
|---|---|
| Schema-version skew across N databases | Migration runner verifies history; dedicated DB "behind" = unavailable + alert; CI blocks release if any known dedicated DB is unmigrated; keep dedicated customer count low and migrate them promptly |
| Silent fallback to shared (data leak/split) | No fallback path exists in code; dedicated tests assert shared DB untouched on failure |
| Operational load of manual steps | Tooling automates copy/verify/migrate; runbook is explicit; v1 deliberately limits dedicated customers |
| Credentials sprawl | Connection strings only in the environment secret store, keyed by `database_key`; never in DB, logs, audit, or health output |
| Connection pool exhaustion (many pools) | Bounded pool size per customer; idle pool eviction; monitor open connections per `database_key` |
| Cross-database consistency during migration window | `status=migrating` hard-stops writes + jobs for that customer; window is short and announced |
| Backup/restore complexity | DR runbook updated per-mode; dedicated customers are independently restorable (net positive) |

---

## 9. Decisions taken in this design

1. Assignment record lives in the **Companies module**, in a new central **`PlatformDbContext`**
   (carved out from `CompaniesDbContext`).
2. Credentials in **configuration only**, keyed by a logical `database_key`.
3. **No fallback** to shared, ever. Unavailable dedicated DB → 503 / job retry.
4. Hangfire storage stays **central**; job bodies resolve the customer DB at execution time.
5. Tenant audit follows the customer's DB; platform-admin audit stays central.
6. Backfill inserts an explicit `shared` row for every existing customer.
7. **The foundational record is NOT implemented in this change.** Reasons: adding the table +
   carving out `PlatformDbContext` requires a migration against the shared production database and a
   model-snapshot change with architecture/integration-test churn, for zero behavioural value until
   the resolver exists. Reliability-first: ship it together with sub-ticket (a) behind its tests.

---

## 10. Sub-tickets

### (a) Connection routing / resolution — *M/L*
- Carve `PlatformDbContext` out of `CompaniesDbContext` (move `platform_settings`,
  `platform_metrics_snapshot`, add `customer_database_assignments`); migration + backfill.
- `ICustomerDatabaseResolver` + config-map loader + `CustomerDatabaseUnavailableException` → 503.
- `AddTenantDbContext<T>()` helper; convert all tenant module DbContext registrations to use it.
- Database-resolution middleware after tenant resolution.
- Resolver cache + invalidation hook.
- Platform-admin API + Admin.Web screen to view/set a customer's assignment (`shared` default,
  `dedicated` with key, status transitions), audited.
- Tests: shared-mode unchanged; dedicated-mode isolation suite (section 5); architecture test that
  no module hard-codes a connection string.
- **No customer is dedicated yet** — default path is identical to today.

### (b) Background processing — *M*
- Job base/helper that resolves the customer DB from `companyId` at execution time.
- Retry-not-fallback behaviour for `status != active`.
- Platform sweeper jobs iterate customers and open each DB; skip-and-log vs hard-fail policy per
  job class (section 4.2).
- Tests: job runs against dedicated DB; job retries during `migrating`; sweeper tolerates one
  unreachable customer DB (except deletion/legal-hold).

### (c) Operational tooling & runbooks — *M*
- `HR.MigrationRunner` console: apply all module migrations to a target DB by `database_key` /
  connection string; verify migration history == expected set.
- Filtered copy command (`--company-id`) + row-count / stray-`company_id` validation.
- Full provisioning + shared→dedicated migration runbook (expand section 7) with
  `[OPERATOR]` / `[SIGN-OFF]` markers, matching the DR runbook style.
- DR runbook update: per-mode restore, independent dedicated-customer restore, deletion of a
  dedicated customer (drop DB + central rows), legal-hold interaction.
- Post-cleanup job: delete `company_id = X` from shared schemas, gated on `status=active` ≥ N days
  and no rollback flag.

---

## 11. Out of scope (v1)

Automatic database creation/provisioning; self-service or UI-driven migration; zero-downtime live
migration; per-customer object storage; sharding the shared database; cross-customer reporting.
