# Backup and Disaster Recovery Runbook (NFR-04)

Owner: Crazy Cat Software Limited (trading as One Big Team)
Status: **Draft for operator sign-off.** Every value marked _[SIGN-OFF]_ must be confirmed by an
accountable operator. Every step marked _[OPERATOR]_ requires access to the Railway and/or
Supabase consoles and cannot be completed from the source repository.
Review frequency: at least annually and after any material change to hosting provider, data model,
or the customer-facing security/DPA commitments.

This runbook supports the Data Processing Agreement (`src/HR.Marketing/Documents/data-processing-agreement.md`),
the Security page (`src/HR.Marketing/Documents/security.md`), the deployment architecture
(`specifications/architecture/08-deployment-architecture.md`), and the data-protection operating
procedures (`docs/compliance/data-protection-operations.md`).

---

## 1. Recovery objectives (proposed — _[SIGN-OFF]_)

The 99.5% monthly availability target (`specifications/product-specifications/31-non-functional-requirements.md`)
permits ~3h 39m of downtime per month. The customer documents promise "provider-managed backups",
"an independent file backup once implemented and tested", and "periodically tested restoration".
The following objectives are consistent with those constraints and are proposed for sign-off:

| Objective | Proposed value | Rationale |
|-----------|----------------|-----------|
| **RPO — database** | **5 minutes** | Supabase Point-in-Time Recovery (PITR) provides WAL-based recovery to any second within the retention window. 5 min is a safe conservative commitment allowing for WAL shipping lag. |
| **RPO — private files (Supabase Storage)** | **24 hours** | Independent file backup runs daily (section 4). Files are write-once/immutable per document version, so a 24h window loses at most one day of newly-uploaded documents, which customers can re-upload. |
| **RTO — full production restore** | **4 hours** | Below the monthly downtime budget for a single incident. Covers: provision new Supabase project/branch, restore DB, restore/repoint Storage, recover secrets, redeploy 5 Railway services, run validation. |
| **RTO — database-only rollback (logical error, no infra loss)** | **1 hour** | PITR restore into a new branch/project plus cutover. |
| **Maximum tolerable data loss (MTDL)** | Equal to RPO per data class above | — |
| **Backup retention — database** | **PITR window 7 days + daily snapshots 30 days** _[SIGN-OFF]_ | Must equal the retention cycle disclosed to customers under DPA clause 10 ("residual copies … until the applicable backup retention cycle expires"). If retention changes, update the DPA and the deletion runbook. |
| **Backup retention — private files** | **30 days** _[SIGN-OFF]_ | Aligns with the DB daily-snapshot window so a mutually consistent restore point always exists. |

**Sign-off block**

| Field | Value |
|-------|-------|
| Objectives approved by | __________________ |
| Role | __________________ |
| Date | __________________ |
| Next review due | __________________ |

---

## 2. Database backups

| Attribute | Detail |
|-----------|--------|
| System of record | Supabase PostgreSQL (single logical database, one schema per module). |
| Mechanism | Supabase automated daily backups **plus** Point-in-Time Recovery (WAL). |
| Required Supabase plan | **Pro plan or higher** — PITR is not available on the Free plan. PITR add-on must be enabled for the production project. _[OPERATOR]_ |
| Schedule | Continuous WAL archiving; daily full snapshot taken by Supabase (time controlled by provider). |
| Retention | Per section 1 _[SIGN-OFF]_. Confirm the configured PITR window in Supabase → Database → Backups matches the DPA-disclosed figure. |
| Encryption | At rest: AES-256 by the provider (Supabase/AWS). In transit: TLS. Backups inherit provider-managed encryption; no customer-managed keys in v1. |
| Ownership | Accountable owner: _[SIGN-OFF]_ (named operator). Backup configuration changes are a two-person change. |
| Monitoring | Add a monthly manual check (or Supabase status webhook) that the most recent successful backup timestamp is < 25h old. Record in the assurance register (`docs/compliance/data-protection-operations.md` → "Records and assurance"). |
| Off-provider copy | _[SIGN-OFF, OPTIONAL]_ Weekly `pg_dump` (custom format, compressed) written to an independent object store (different provider/account) with 90-day retention, for provider-loss resilience. If adopted, the dump job runs from a scheduled GitHub Actions workflow or a Railway cron service using a read-only database role; the artifact is client-side encrypted (age/gpg) before upload. |

### Access control (database backups)

- Supabase project access limited to named operators with MFA enforced. _[OPERATOR]_
- Restore/download of a backup is restricted to the project Owner role. _[OPERATOR]_
- All backup/restore actions are logged in the Supabase organisation audit log; export and file
  monthly into the assurance evidence store. _[OPERATOR]_
- The off-provider dump bucket (if used) is a dedicated bucket, private, with its own credentials
  stored only in the CI/cron secret store, never in source control.

---

## 3. Private file storage backups (Supabase Storage)

Buckets in scope (all private, signed-URL access only):

| Content | Service | Bucket (configured) |
|---------|---------|---------------------|
| Employee documents | `HR.Modules.Documents` `SupabaseDocumentStorageService` | _[OPERATOR: record bucket name]_ |
| Recruitment / candidate documents | `HR.Modules.Recruitment` candidate document storage | _[OPERATOR]_ |
| Profile photos | `HR.Infrastructure` `SupabaseProfilePhotoStorageService` | _[OPERATOR]_ |
| Branding assets | branding storage | _[OPERATOR]_ |
| Support-session attachments | `HR.Infrastructure` `SupabaseSupportAttachmentStorageService` | _[OPERATOR]_ |
| Staged import files | `HR.Modules.DataImport` | _[OPERATOR]_ |

Supabase does **not** include Storage objects in its database PITR/backups. An **independent copy is
required** and is currently claimed-but-not-implemented in the customer docs (see reconciliation,
section 8). Until it is implemented and restoration-tested, the Security page and DPA Schedule 2
must continue to describe independent file backup as *not yet active*.

### 3a. Approved approach (proposed — _[SIGN-OFF]_)

**Daily server-side bucket-to-bucket copy into an independent Supabase project**, plus optional
off-provider mirror.

Design (sketch — not yet built; deferred to a dedicated implementation story):

- A recurring background job `PrivateFileBackupJob` in `HR.Infrastructure/BackgroundJobs`
  (registered via the existing `IRecurringJobRegistrar`), scheduled daily.
- For each in-scope bucket it lists objects modified since the last successful run (high-water mark
  persisted in an infrastructure table `infra.file_backup_runs`) and streams each object to the
  backup destination bucket under the same key, preserving `company_id`-prefixed paths.
- Destination: a **separate Supabase project** (isolined credentials) — or an S3-compatible bucket
  on a different provider. Objects are server-side encrypted at rest by the destination; optionally
  client-side encrypted for the off-provider case.
- The job records per-run: start/end, objects copied, bytes, failures, and the resulting
  high-water mark. Failures raise a monitored alert (same channel as Hangfire failures).
- Retention on the destination: 30 days (lifecycle rule on the destination bucket) _[SIGN-OFF]_,
  matching the DB daily-snapshot window so a consistent restore point always exists.
- The job uses a dedicated read-only source credential and a write-only destination credential.

Interim manual control until the job exists: weekly operator-run
`supabase storage` CLI sync (or `rclone`) of every private bucket to the backup project, logged in
the assurance register. _[OPERATOR]_

### Access control (file backups)

- Destination project/bucket access limited to named operators, MFA enforced.
- Destination credentials live only in the job's secret store.
- Destination is never wired to the application runtime — it is restore-only.
- Access + restore actions logged and reviewed monthly.

---

## 4. Mutually consistent restore point (DB + files)

Files and DB are backed up by independent mechanisms, so "consistent" here means **choose a restore
target time `T` such that every backup class has coverage at or after `T`, then restore each class
to the newest state at-or-before `T`**.

Procedure:

1. Decide `T` — normally "just before the incident". `T` must be:
   - within the DB PITR window, **and**
   - on or after the oldest retained daily file-backup snapshot.
2. Restore the database to `T` using PITR (section 5).
3. Restore Storage objects to the last daily file-backup **at or before `T`**. Because document
   rows in the DB reference storage keys, and documents are immutable per version, the only
   inconsistency window is: documents whose DB row exists at `T` but whose file copy is from an
   earlier daily snapshot (up to 24h older). Detect these with the orphan-check query in
   `scripts/nfr-04-restore-drill-validation.sql` (section "DB rows referencing missing files").
4. For any detected orphaned reference: the file was uploaded in the last <24h before `T`. Options:
   (a) recover it from the source production bucket if that still exists; (b) accept the loss and
   notify the affected customer(s) — the DB row can be soft-flagged / the document marked as
   needing re-upload. Record the decision.
5. Never restore files to a state *newer* than the DB target `T` (would produce files with no
   owning row).

---

## 5. Recovery runbook

Pre-req: incident declared, comms started, an operator with Supabase Owner + Railway admin + secret
store access is on the call.

### 5.1 Database restoration _[OPERATOR]_

1. In Supabase → Database → Backups, choose **Restore to a new project/branch** at time `T`
   (do not restore in place — keep the damaged instance for forensics).
2. Wait for the restore to complete; note the new connection string.
3. Run `scripts/nfr-04-restore-drill-validation.sql` against the restored DB. All assertions must
   pass (row counts sane, no cross-tenant leakage, schema-per-module intact, migration history
   matches the deployed app version).
4. Confirm the EF migration history table (`__EFMigrationsHistory` per module schema, or the
   infrastructure equivalent) matches the migrations compiled into the release being redeployed.
   If the app is ahead, the startup migrator will apply the gap on boot — verify via the
   `deployment-health-check` workflow's startup-migrations endpoint.

### 5.2 Private file restoration _[OPERATOR]_

1. Identify the daily file-backup snapshot at-or-before `T`.
2. Recreate/point production Storage buckets and sync objects from the backup destination back into
   the production project (CLI/`rclone` copy, preserving keys).
3. Run the orphan-check and the "files with no DB row" check from the validation script.
4. Resolve orphans per section 4 step 4.

### 5.3 Secret and configuration recovery _[OPERATOR]_

Secrets are **not** in source control (`specifications/architecture/08-deployment-architecture.md`
→ Secrets Management). Recovery source of truth: the password/secret manager vault entry
"OneBigTeam — Production" _[SIGN-OFF: name the vault]_.

Required secrets to restore across the 5 Railway services (Marketing, App, API, Admin, Admin API):

- Supabase: project URL, anon key, service-role key, JWT secret, DB connection string
- Supabase Auth configuration (redirect URLs, SMTP/Postmark relay)
- Postmark server token(s)
- Stripe: secret key, webhook signing secret, price IDs
- Cloudflare: API token (if used for cache purge / DNS automation)
- `PlatformAdmin:AllowedEmails` allow-list
- Any data-protection encryption keys for sensitive fields (if introduced)

Steps:

1. Recreate each Railway service from the repo (IaC / `railway.json` or dashboard) _[OPERATOR]_.
2. Populate environment variables per service from the vault.
3. Update DNS / custom domains in Cloudflare to the new service URLs if endpoints changed.
4. Rotate any secret that may have been exposed during the incident; update Stripe/Postmark/
   Supabase dashboards and the vault together.

### 5.4 Service validation _[OPERATOR]_

1. Trigger `.github/workflows/deployment-health-check.yml` against the recovered API base URL —
   startup-migrations endpoint must report healthy.
2. Manually verify health checks: DB connectivity, Supabase availability, Storage availability,
   Hangfire availability.
3. Smoke test per service:
   - Marketing site loads.
   - App: log in as a seed/test tenant, view an employee, open an employee document (signed URL
     resolves — proves DB↔Storage consistency), book leave.
   - API: authenticated request returns tenant-scoped data; anonymous request returns 401.
   - Admin App + Admin API: platform admin can load the customer list and the deletion queue.
4. Confirm background jobs are running (Hangfire dashboard shows recurring jobs scheduled).
5. Confirm audit history is intact and writable (perform one audited action, see it appear).

### 5.5 Reapplication of pending deletion obligations _[OPERATOR]_ — **mandatory**

A restore can resurrect a tenant that was mid-deletion or already deletion-executed. Per DPA
clause 10 and `docs/compliance/data-protection-operations.md` step 6, still-valid deletion
instructions must be reapplied **before** the service returns to ordinary use.

1. From the restored `CustomerSubscription` data, list every company where:
   - `DeletionExecutedAt` is not null, **or**
   - `HasPendingDeletion` is true (`DeletionScheduledAt` set, not cancelled, not executed).
   The validation script includes this query.
2. Cross-check against the **out-of-band deletion register** (`docs/compliance` process) — the
   authoritative list of deletion obligations, because it survives a DB rollback. Any obligation in
   the register but not reflected in the restored DB must be re-created.
3. For each deletion-executed company: re-run `ExecuteCustomerDeletion` (or re-stamp
   `DeletionExecutedAt` + `AdminForcedReadOnly`) so access stays revoked. Note: this action is a
   status transition only; the separate per-store hard-deletion (DPA step 4) must also be
   re-verified against every module schema, Supabase private objects, and auth profiles.
4. For each pending-deletion company: confirm `DeletionScheduledAt` survived; if the countdown
   date passed during the outage, re-evaluate and re-schedule or execute per the register.
5. Record per-company reapplication outcome; a second authorised person verifies before ordinary
   access is restored.

### 5.6 Close-out

- Declare recovery complete; update status page.
- Notify affected customers per the breach/incident procedure if data loss occurred (RPO exceeded).
- Complete a lessons-learned review; file all evidence (restore logs, validation output, sign-offs)
  in the assurance store.

---

## 6. Restoration testing (drill)

Because production cannot be restored on demand from this repository, NFR-04 is proven by a
**scripted local restore drill** plus a **recurring production restore exercise**.

- **Local drill (repeatable now):** `docs/runbooks/restore-drill.md` +
  `scripts/nfr-04-restore-drill-validation.sql`. Restores a `pg_dump` into a scratch database and
  runs the validation query set (row counts, tenant isolation, schema-per-module, migration
  history, DB↔file reference integrity, pending-deletion list).
- **Recurring production exercise (_[OPERATOR]_):** at least every 6 months, restore the latest
  production backup into a throwaway Supabase branch/project, run the same validation script,
  perform section 5.4 smoke tests, and record results using the template in `restore-drill.md`.
  File results in the assurance register. A failed or skipped exercise is a production-readiness
  gate failure.

---

## 7. Operator action checklist (nothing in this list can be done from the repo)

- [ ] Approve recovery objectives and retention figures (section 1 sign-off block).
- [ ] Confirm Supabase production project is on Pro+ with PITR enabled; record the window.
- [ ] Record all private bucket names in section 3.
- [ ] Stand up the independent file-backup destination project/bucket; wire the interim manual sync.
- [ ] Implement `PrivateFileBackupJob` (separate story) and restoration-test it.
- [ ] Create/populate the production secret vault entry and document its name here.
- [ ] Create the out-of-band deletion obligations register (or confirm existing).
- [ ] Enforce MFA + least privilege on Supabase org, Railway, Cloudflare, Stripe, Postmark.
- [ ] Enable and route backup/restore audit-log export to the assurance store.
- [ ] Schedule the 6-monthly production restore exercise; run the first one.
- [ ] Update the Security page and DPA Schedule 2 once independent file backup is live and tested.

---

## 8. Reconciliation: customer-facing claims vs. documented procedure

| # | Source | Claim | Backed by | Status |
|---|--------|-------|-----------|--------|
| 1 | DPA §4 / Sch 2 | "appropriate technical and organisational measures … against accidental or unlawful destruction, loss" | This runbook (backup + DR end-to-end) | **Documented**; execution pending operator |
| 2 | DPA Sch 2 "Availability, backup and recovery" | "Production database backups are maintained through the database hosting provider once configured and verified" | Section 2 | **Documented**; _[OPERATOR]_ must configure & verify PITR/retention |
| 3 | DPA Sch 2 | "Any independent Customer-file backup service must be implemented, contractually approved and restoration-tested before it is described as an active production control" | Section 3 (approach + sketch only) | **Gap** — approach documented, **not implemented, not tested**. Docs correctly still say "not active". |
| 4 | DPA Sch 2 | "Backup retention is configured according to the documented production recovery and deletion schedule" | Section 1 retention rows | **Documented**; figures need _[SIGN-OFF]_ and must match DPA §10 disclosure |
| 5 | DPA Sch 2 | "Backup access is restricted and backups are used only for disaster recovery" | Sections 2, 3 access-control subsections | **Documented**; _[OPERATOR]_ must enforce MFA/roles and enable audit-log export |
| 6 | DPA Sch 2 / Security page | "Recovery procedures are documented and restoration is tested periodically" | Section 5 (runbook) + Section 6 (drill + 6-monthly exercise) | **Documented**; first production exercise **not yet run** |
| 7 | DPA §10 / Schedule 1 Duration | "Residual copies may remain in encrypted, access-restricted provider backups until the applicable backup retention cycle expires" | Section 1 retention; encryption note in Section 2 | **Documented**; retention value must be finalised and kept in sync |
| 8 | DPA §10 | "If a backup is restored, deletion instructions that remain applicable will be reapplied" | Section 5.5 (mandatory reapplication step) + validation-script query + out-of-band register requirement | **Documented**; depends on _[OPERATOR]_ creating the out-of-band deletion register |
| 9 | DPA §10 | "One Big Team will provide the applicable maximum backup retention period on request and will not extend it" | Section 1; operator checklist item to keep DPA in sync | **Documented** |
| 10 | Security page "Backups" | "Provider-managed backups are access-restricted, used only for disaster recovery and retained only for the configured recovery period" | Sections 1, 2 | **Documented**; _[OPERATOR]_ enforcement outstanding |
| 11 | Security page "Encryption" | "encrypted … by the production hosting providers at rest" (applies to backups) | Section 2 encryption row | **Documented** (provider-managed AES-256); no customer-managed keys in v1 |
| 12 | `data-protection-operations.md` launch gate | "database backup retention and restoration are tested" | Sections 2, 5, 6 | **Documented**; test evidence pending |
| 13 | `data-protection-operations.md` launch gate | "any advertised independent file backup is implemented and tested" | Section 3 | **Gap** — same as #3 |
| 14 | `deployment-architecture.md` Disaster Recovery AC 9 | "Recovery procedures are documented" | This runbook | **Met** (documentation); execution pending |

### Open gaps requiring action (not resolvable in-repo)

- **G1 (from #3, #13):** Independent private-file backup is **not implemented**. Approach + job
  sketch provided; needs a dedicated implementation story and a restoration test before any doc
  describes it as active.
- **G2:** Recovery objectives and retention figures are **proposed, not signed off**.
- **G3:** No production restore exercise has been performed; 6-monthly schedule not yet established.
- **G4:** Out-of-band deletion obligations register (needed for DPA §10 reapplication after
  restore) must be created/confirmed by operations.
- **G5:** MFA/least-privilege enforcement and backup-system audit-log export on Supabase, Railway,
  Cloudflare, Stripe and Postmark must be verified by an operator.
