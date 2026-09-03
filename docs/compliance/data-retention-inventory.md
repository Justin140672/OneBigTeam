# Data retention inventory (NFR-07)

Owner: Crazy Cat Software Limited (trading as One Big Team)
Status: **Draft for operator / DPO sign-off.** The retention *rules* below restate the commitments
already made to customers in the Privacy Policy and Data Processing Agreement (DPA); the
*enforcement* column records what the platform does automatically today.
Review frequency: at least annually and after any change to the customer-facing privacy documents.

This document supports:

- `src/HR.Marketing/Documents/privacy-policy.md` ("Retention" section)
- `src/HR.Marketing/Documents/data-processing-agreement.md` (§10, Schedule 1 Duration)
- `docs/compliance/data-protection-operations.md` ("Customer cancellation, return and deletion")
- `docs/runbooks/backup-and-disaster-recovery.md` (§5.5 reapplication of deletion obligations)
- `specifications/architecture/12-failure-safety-and-idempotency.md` (§8 deletion workflows,
  storage-orphan finding)

---

## 1. Key principle — who controls retention

For **HR record content** (employees, leave, documents, sickness, recruitment, tasks, ...) the
**customer is the data controller** and One Big Team is the processor. The customer decides how long
those records are kept and is responsible for applying appropriate retention periods
(privacy-policy.md: *"The customer controls retention of HR records while its account remains active
and is responsible for applying appropriate retention periods to those records."*).

Consequences:

- The platform does **not** impose, and must not silently enforce, a maximum age on customer HR
  content. Automatically destroying a customer's employee or document records on a timer would be a
  data-loss regression, not a feature (see the explicit notes in
  `PurgeEligibleArchivedEmployeeDocumentsHandler` and `PurgeEligibleCandidatesHandler`).
- Configurable retention is therefore offered **only** where (a) the data is genuinely transient
  platform-managed state, or (b) the customer has an existing, documented lever. Today that is a
  very short list (section 3).
- The one hard, product-level control is **deletion on cancellation**: at the end of the 30-day
  recovery period the tenant is deleted per `data-protection-operations.md`. That is a manual,
  operator-run, per-store procedure — the `ExecuteCustomerDeletion` admin action only revokes
  access and records lifecycle state (documented limitation, unchanged by this ticket).

---

## 2. Inventory

| # | Data class | Where | Lawful / default retention rule | Customer-configurable? | Automated enforcement today |
|---|------------|-------|----------------------------------|------------------------|------------------------------|
| 1 | **Employee records** (employees, departments, positions, compensation, employment history) | `employees` schema | Controller-set. Kept for the life of the account; after cancellation deleted at recovery-period expiry via the per-store deletion procedure. UK guidance: core employment and financial records are typically retained ~6 years after employment ends, but this is the **controller's** call. | **No** — controller responsibility. Deletion of an individual employee is a controller action in-app (soft-delete / offboarding), not a platform timer. | None (by design). Tenant-wide deletion is the manual procedure in `data-protection-operations.md` step 4. |
| 2 | **Recruitment records** (vacancies, candidates, applications, interviews, offers) | `recruitment` schema | Controller-set. Unsuccessful-candidate personal data commonly retained 6–12 months to defend discrimination claims, then minimised. | **Yes (limited)** — `CompanySettings.CandidateRetentionDays` (default 365, range enforced by `UpdateRecruitmentSettingsValidator`). Changing it never destroys data on its own. | **Operator-triggered, not scheduled.** `PurgeEligibleCandidates` (company-administrator only) redacts candidates older than the window that are not hired and have no open application. Now **skipped when the company is under a legal hold** (this ticket). |
| 3 | **Documents & file versions** (employee documents, shared company documents, version history + the stored blobs) | `documents` schema + Supabase Storage | Controller-set. A document version is immutable; superseded/expired documents are archived, not deleted, until the controller decides. | **No** configurable period. Archive is a controller action. | **Operator-triggered, not scheduled.** `PurgeEligibleArchivedEmployeeDocuments` (company-administrator only) hard-deletes the row **and the blob** for documents archived ≥ `MinimumRetentionDays` (90). This ticket: (a) **skipped under legal hold**; (b) blob-delete failure after row-delete is now logged with the storage key for operator cleanup instead of silently orphaning (NFR-08 storage-orphan finding). |
| 4 | **Audit records** (`audit.audit_events`, `audit.audit_pending_items`) | `audit` schema | Privacy Policy: *"Customer audit records [retained] for the subscription term and controlled deletion period"*; platform security records ~12 months. Audit is a **compliance and security control** — it must outlive the events it records and support incident investigation and claims. | **No.** | **Retained indefinitely within the account lifetime — intentionally no scheduled deletion.** Rationale: (i) audit is the evidence base for security-incident, breach, complaint and rights-request investigations (`data-protection-operations.md`); (ii) audit payloads are already redacted of sensitive content (`AuditPayloadRedactionGuard`, NFR-01 scrubber) so age-based minimisation adds little; (iii) audit rows are deleted only as part of full tenant deletion. A future ticket could add an age cap aligned to the "subscription term + controlled deletion period" wording if a DPO requires it — deliberately not done here to avoid weakening the security control on the last ticket of the run. |
| 5 | **Notifications** (in-app `notifications`, `email_deliveries`) | `notifications` schema | No lawful-basis retention requirement — read in-app notifications are transient UI state. Email delivery metadata is operational. Postmark itself retains message content up to 45 days (subprocessor list). | **Partly** — `Notifications:Retention:RetentionDays` (default 365) and `Notifications:Retention:Enabled` (default `false` = dry-run). Platform-level config, not per-tenant UI, because there is no customer-facing requirement to tune it. | **Scheduled (this ticket).** `PurgeExpiredReadNotificationsJob` — daily, **dry-run by default**, deletes only `IsRead` notifications older than the window, **per company**, **skips companies under legal hold**, audits aggregate counts only. Unread notifications are never touched. `email_deliveries` retention: documented follow-up. |
| 6 | **Imports & generated exports** (staged import files, report export artifacts) | `dataimport` schema + Supabase Storage; `reporting` exports | Privacy Policy: *"Customer exports generated for immediate download are not intentionally retained… Where an assisted export must be staged temporarily, it is deleted within seven days."* Transient by definition. | **No** — the 7-day rule is fixed and lawful-basis-driven (data minimisation). | **Partial / documented follow-up.** Immediate-download exports are streamed, not persisted. Staged import files and any staged export artifacts should be swept on a 7-day timer — recommended as the next scheduled retention job, modelled on `PurgeExpiredReadNotificationsJob` (same dry-run + legal-hold pattern). Not built in this ticket to keep blast radius small. |
| 7 | **Support records** (support requests, responses, attachments) | `support` schema + Supabase Storage | Privacy Policy: *"Support correspondence is normally retained for the subscription term and up to 24 months afterwards, unless a longer period is needed for security, a complaint or a legal claim."* | **No.** | None scheduled. Deleted as part of full tenant deletion (`data-protection-operations.md` step 4 lists support attachments explicitly). A 24-month-post-cancellation sweep is an operator process, not a platform timer, because it depends on the off-platform "legal claim / complaint" exception. |
| 8 | **Backups** (Supabase PITR + daily DB snapshots; independent file backup once live) | Provider-managed | DPA §10: residual copies remain *"until the applicable backup retention cycle expires"*. Proposed: DB PITR 7 days + daily snapshots 30 days; files 30 days (`backup-and-disaster-recovery.md` §1, pending sign-off). | **No** — DPA §10: *"One Big Team will provide the applicable maximum backup retention period on request and will not extend it."* | Provider lifecycle rules. **Deletion obligations (including legal holds) are reapplied after any restore** — `backup-and-disaster-recovery.md` §5.5, extended by this ticket to cover legal holds and the resumption of scheduled retention processing. |

---

## 3. What was made configurable, and why most was not

| Made configurable in this ticket | Not made configurable | Why |
|---|---|---|
| `Notifications:Retention:RetentionDays` + `Notifications:Retention:Enabled` (platform config; default = dry-run, 365 days). | Employee, document, audit, support, backup retention. | Rows 1, 3, 4, 7, 8 are either **controller-owned** (the customer already controls them and must not have the platform override them) or **lawful-basis-fixed / compliance controls** (audit, backup retention, the 7-day export rule). Offering a knob would imply the platform can lawfully shorten periods it does not own. |
| (pre-existing) `CompanySettings.CandidateRetentionDays` — legal-hold enforcement added. | — | Recruitment already had a legitimate controller lever (defend-a-claim window for unsuccessful candidates). This ticket only makes the existing purge respect legal holds. |

Net: **one new platform-level setting**, deliberately shipped in dry-run mode. Everything else is
either already controller-controlled or fixed by lawful basis.

---

## 4. Legal hold

A **company-wide legal hold** (`customer_subscriptions.legal_hold_placed_at/_by/_reason`, set via the
platform-admin `PlaceCompanyLegalHold` / `LiftCompanyLegalHold` endpoints) suspends **all** retention
deletion for that tenant:

- `PurgeExpiredReadNotificationsJob` skips the company and audits the skip.
- `PurgeEligibleArchivedEmployeeDocuments` and `PurgeEligibleCandidates` return `409 Conflict`.
- `ExecuteCustomerDeletion` returns `409 Conflict` until the hold is lifted.
- Cross-module enforcement is via `ILegalHoldStatusReader` (in `HR.Infrastructure.Abstractions`),
  implemented by the Companies module — no module references Companies directly.
- Legal holds are listed on the admin deletion-queue response and must be reapplied after a backup
  restore (`backup-and-disaster-recovery.md` §5.5).

It is intentionally coarse (whole tenant, not per-entity): a hold is rare, operator-driven, and
"preserve everything for this customer" is the safe default while a matter is live.

---

## 5. Audit approach

Retention-policy changes and deletion outcomes are audited via the existing `IAuditEventPublisher`
with **no deleted content** recorded:

| Event | Records | Never records |
|---|---|---|
| `subscription.legal-hold-placed` / `-lifted` | company, actor, timestamp, operator reason | — |
| `notifications.retention-run` (one per company per run) | company, dry-run flag, window, **count** deleted/matched, legal-hold-skip flag | notification titles, bodies, recipients |
| `EmployeeDocumentPurgedAuditEvent` (pre-existing) | company, document id, type, archived date, actor | file contents |
| `CandidatesPurgedAuditEvent` (pre-existing) | company, candidate ids, actor | candidate personal data |

Job failures raise an **administrative alert** (`AdministrativeAlertCategory.Compliance`) into the
ADM-03 alerts inbox, and structured error logs, so administrators get failure visibility.

---

## 6. Open questions / recommended follow-up tickets

1. **Staged import / export file sweep** (row 6) — implement a 7-day `PurgeStagedImportFilesJob`
   mirroring `PurgeExpiredReadNotificationsJob` (dry-run + legal-hold). Highest-value remaining gap.
2. **Audit age cap** (row 4) — decide with a DPO whether to cap audit at "subscription term +
   controlled deletion period"; currently retained for the account lifetime by design.
3. **`email_deliveries` retention** (row 5) — extend the notifications job, or a sibling, to prune
   old delivery metadata.
4. **Post-cancellation sweeps** (rows 2, 7) — the 24-month support / candidate windows after
   cancellation are operator procedures; consider tooling once the per-store deletion procedure is
   automated.
5. **Admin UI** — a read-only "last retention run + failures" panel in the Admin portal. For now:
   structured logs + `notifications.retention-run` audit events + the ADM-03 alerts inbox.
