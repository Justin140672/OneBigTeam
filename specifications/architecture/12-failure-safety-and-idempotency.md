# 11. Failure Safety & Idempotency (NFR-08)

## Purpose

This document inventories the platform's **critical multi-step workflows**, records their
transaction boundaries and failure points, and defines the idempotency / reconciliation
guarantees for each. It is the reference for NFR-08 ("Add failure-safety and idempotency
verification for critical workflows").

It is a living register. When a new cross-module workflow is added, add a row to the
inventory and state its transaction boundary, its duplication risk, and its recovery path.

---

## Mechanisms already in place

| Mechanism | Where | What it guarantees |
|---|---|---|
| **Audit outbox** (`audit.audit_pending_items` staging table + `AuditPendingItemPromotionJob`) | `HR.Infrastructure` | An audit event is written to a durable staging row by the publisher; a Hangfire job promotes it to `audit.audit_events`. Promotion is idempotent via a **unique index on `event_id`**. A promotion crash leaves the pending row for retry; a double promotion is rejected by the unique index. Audit-write failure is logged and never fails or corrupts the business operation. |
| **In-process integration event dispatch** (`IntegrationEventPublisher`) | `HR.SharedKernel` | Handlers for an integration event run synchronously in the publishing request. Each handler is isolated in its own `try/catch`: one handler failing is logged and **does not** abort the others or propagate to the caller. There is **no automatic redelivery** — a failed handler's work is dropped until a reconciliation job or a later event re-triggers it. |
| **Consumer natural-key idempotency** | Individual `IIntegrationEventHandler` implementations | Consumers check for an existing effect before creating one, e.g. `ProbationRecords.AnyAsync(company+employee)`, `INotificationWriter.ExistsAsync(recipient, sourceEntity, type)`. Protects against redelivery / replay / reconciliation overlap. |
| **Reconciliation jobs** | `OffboardingPlanCreationReconciliationJob`, `OffboardingCancellationReconciliationJob`, `ProcessLeavingEmployeesJob`, `GenerateDueProbationReviewsJob` | Daily Hangfire sweeps that detect and repair "committed upstream, downstream side effect missing" states. Each is idempotent and safe to run repeatedly. |
| **Filtered unique indexes as backstops** | e.g. `offboarding_plans (company_id, employee_id) WHERE status not terminal`, `employees (company_id, employee_number) WHERE <> ''` | Make a duplicate physically impossible even under a race between the check-then-insert and a concurrent creation path. |
| **Hangfire automatic retry** | All background jobs | Transient job failures retry with back-off. Jobs must therefore be idempotent. |

### NFR-08 addition

| Mechanism | Where | What it guarantees |
|---|---|---|
| **Provisioning idempotency key** — `employees.source_reference` + filtered unique index `ix_employees_company_id_source_reference` (`WHERE source_reference IS NOT NULL`) | `HR.Modules.Employees` (`CreateEmployeeHandler`, `EmployeeProvisioningService`) | An automated workflow that provisions an employee as a side effect passes a stable `SourceReference` (`"<source>:<entity>:<id>"`). If the workflow is retried after a partial failure, `CreateEmployeeHandler` returns the already-provisioned employee instead of creating a duplicate, and does **not** re-publish `EmployeeCreatedIntegrationEvent`. The unique index is the race backstop; a `DbUpdateException` from it is resolved by re-reading and returning the existing row. |

---

## Critical workflow inventory

Legend for **Boundary**: `TX` = single DB transaction; `TX + post-commit fan-out` = one business
commit followed by best-effort event/audit/notification publication; `multi-TX` = more than one
independent commit (often across module DbContexts) with no distributed transaction.

### 1. Employee creation (`CreateEmployee`)

| | |
|---|---|
| Handler | `HR.Modules.Employees.Features.CreateEmployee.Handler` |
| Saves | one `employees.employees` row (single `SaveChangesAsync`) |
| Then publishes | `EmployeeCreatedIntegrationEvent` (in-process, post-commit) |
| Downstream consumers | Leave (`InitialiseEmployeeLeave`), Probation (`CreateProbationOnEmployeeCreated`), Onboarding (`EmployeeCreated` → onboarding plan + tasks), Documents (required-document requests), Assets (required-asset tasks), Identity (user profile link), Employees (timeline projection), Notifications (`NotifyOnEmployeeCreated`) |
| Boundary | `TX + post-commit fan-out` |
| Failure points | (a) crash after employee commit, before event publish → downstream side effects never created; (b) one consumer throws → only that consumer's work dropped; (c) redelivery/replay of the event |
| Duplication risk | **Low for the employee row** (single TX). Medium for downstream: each consumer is individually responsible for not double-creating. |
| Current mitigation | Consumer natural-key checks (Probation `AnyAsync`, Notifications `ExistsAsync`, Onboarding plan uniqueness, Leave balance existence check). Reconciliation for onboarding-plan gaps is **partial** — see open questions. |
| Residual risk | A crash between the employee commit and the event publish is only recovered if the operator re-triggers, or a reconciliation job exists for that consumer. Onboarding plan creation has no dedicated "missing plan" reconciliation sweep (offboarding does). |

### 2. Candidate hire (`HireCandidate`) — **HARDENED (NFR-08)**

| | |
|---|---|
| Handler | `HR.Modules.Recruitment.Features.HireCandidate.Handler` |
| Step 1 | `IEmployeeProvisioningService.CreateFromCandidateAsync` → `CreateEmployeeHandler` commits an `employees.employees` row **in the Employees DbContext** and publishes `EmployeeCreatedIntegrationEvent` (triggers workflow #1's entire fan-out) |
| Step 2 | Back in Recruitment: `application.RecordHire(...)`, `candidate.LinkToEmployee(employeeId)`, stage-history entry — committed **in the Recruitment DbContext** |
| Step 3 | Publish `CandidateHiredIntegrationEvent`; publish stage-changed events; publish `CandidateHiredAuditEvent` |
| Boundary | `multi-TX` (Employees commit, then Recruitment commit, then post-commit fan-out) — **no distributed transaction** |
| Failure point | Crash / DB error **between step 1 and step 2**: employee exists and is fully provisioned, but the candidate is not linked (`candidate.EmployeeId` still null). A naive retry re-enters step 1 and **creates a second employee** → second `EmployeeCreated` → duplicate onboarding plan, probation record, leave init, notifications. |
| Mitigation (NFR-08) | Step 1 now receives `SourceReference = "recruitment:application:{applicationId}"`. On retry, `CreateEmployeeHandler` finds the existing employee for that source reference and returns it without creating a row or publishing an event. Step 2 then links the candidate to the same employee id and converges. The filtered unique index `ix_employees_company_id_source_reference` is the backstop against a concurrent double-hire. |
| Residual risk | If step 2 committed but step 3 (event/audit publish) was lost, `CandidateHired`-driven side effects (HR-inbox task in Tasks, manager notification) are dropped with no reconciliation sweep. Low impact (informational). A fully-successful hire that is retried returns a `conflict` ("candidate already linked") — acceptable. |

### 3. Leave approval (`ApproveLeaveRequest` / auto-approve at submission)

| | |
|---|---|
| Handler | `HR.Modules.Leave.Features.ApproveLeaveRequest.Handler` → `LeaveApprovalEffectsService` |
| Saves | balance/TOIL-ledger mutation + `leave_request.Approve(...)` in **one** `SaveChangesAsync` (`ApplyBalanceEffectsAndApproveAsync` only stages; caller commits) |
| Then (`PublishApprovalOutcomeAsync`, post-commit) | writes `LeaveApproved` notification (`INotificationWriter`, separate Notifications DbContext), publishes `LeaveApprovedAuditEvent`, publishes `LeaveApprovedIntegrationEvent` |
| Boundary | `TX + post-commit fan-out` |
| Failure points | (a) crash after approval commit, before notification/audit/event → those are lost; (b) notification write is a separate DbContext commit; (c) redelivery of `LeaveApproved` integration event to Reporting / other consumers |
| Duplication risk | **None for the balance deduction** (single TX, `RecordUsage` is part of the approve TX; a retry of a fully-approved request is rejected by leave-request state). Notification: `NotificationWriter` keys should prevent a duplicate on retry (see open questions — confirm `WriteTemplatedAsync` upserts / dedupes on `(recipient, sourceEntity, type)`). Audit: unique `event_id` prevents duplicate audit rows. |
| Residual risk | A crash between commit and fan-out means the employee is not notified of an approval that did happen. No reconciliation sweep; manual recovery = re-run approval is blocked (already approved), so notification would need manual re-issue. Low severity. |

### 4. Compensation changes (`CreateCompensation` / employment-details salary edits)

| | |
|---|---|
| Handler | `HR.Modules.Employees` compensation features |
| Saves | `employees.compensations` row(s) in one TX; `CompensationChangedIntegrationEvent` published post-commit |
| Boundary | `TX + post-commit fan-out` |
| Duplication risk | Low — a retried create makes a distinct row only if the caller retries with a new id; the UI does not auto-retry. No natural idempotency key today. |
| Residual risk | If a client double-submits, two effective-dated compensation rows can result. Sensitive-data note: salary must never appear in audit payloads (enforced by `AuditPayloadRedactionGuard`). |
| Status | **Documented only.** Recommend a client-supplied idempotency key or a `(employee_id, effective_from, reason)` uniqueness rule in a future ticket. |

### 5. Onboarding (plan creation on `EmployeeCreated`)

| | |
|---|---|
| Handler | `HR.Modules.Onboarding.Features.EmployeeCreated.Handler` |
| Saves | `onboarding` plan + tasks; syncs task items into the Tasks module (separate DbContext) |
| Boundary | `multi-TX` (Onboarding commit, then Tasks sync) |
| Duplication risk | Redelivery → check for an existing plan for `(company, employee)` before creating. Task sync is the partial-failure gap (Onboarding rows committed, Tasks items missing). |
| Current mitigation | Plan existence check on redelivery. |
| Residual risk | **No dedicated reconciliation sweep for missing onboarding plans or unsynced onboarding task items** (offboarding has one — `OffboardingPlanCreationReconciliationJob` — onboarding does not). Recommend mirroring that job for onboarding. |
| Status | **Documented only** — recommended follow-up: `OnboardingPlanCreationReconciliationJob`. |

### 6. Offboarding (plan creation on leaving process start)

| | |
|---|---|
| Handler | `StartLeavingProcessHandler` → `IOffboardingPlanCoordinator.StartAsync` → `StartOffboardingHandler`; recovery via `OffboardingPlanCreationReconciliationJob` |
| Boundary | `multi-TX` (Employees leaving-process commit, then Offboarding plan commit, then Tasks sync) |
| Duplication risk | Fully mitigated: filtered unique index `(company_id, employee_id)` on active plans; reconciliation job treats a `conflict` as already-done; task sync retried by the same job; duplicate-active-plan groups are logged for manual investigation. |
| Residual risk | Low. This is the reference implementation other workflows should follow. |

### 7. Document publication & acknowledgement

| | |
|---|---|
| Handlers | `HR.Modules.Documents` publish / `UploadEmployeeDocumentVersion`; acknowledgement via task action; `SharedCompanyDocumentAcknowledgedIntegrationEvent` |
| Boundary | `TX + post-commit fan-out` for publication; acknowledgement is a `TX` plus event |
| Duplication risk | Acknowledgement: a redelivered `...Acknowledged` event or a double task-completion could double-count. Confirm an acknowledgement row uniqueness constraint on `(document_id, employee_id)` exists. |
| Residual risk | Medium if no uniqueness constraint on the acknowledgement row. |
| Status | **Documented only** — audit: confirm `(document_id, employee_id)` unique on the acknowledgement table; add if missing in a future ticket. |

### 8. Subscription & deletion operations (candidate purge, archived-document purge, role-override expiry)

| | |
|---|---|
| Handlers | `PurgeEligibleCandidates`, `PurgeEligibleArchivedEmployeeDocuments`, `ExpireEmployeeRoleOverridesJob`, `ToilExpiryService` |
| Boundary | `TX` per batch, Hangfire-scheduled |
| Duplication risk | Deletion is naturally idempotent (a second run finds nothing to delete). Storage-blob deletion + DB row deletion is `multi-TX` — a crash between them orphans a blob or a DB row. |
| Residual risk | Orphaned storage blobs after a partial purge. Low cost; a re-run of the purge re-attempts blob deletion for rows still present, but a blob whose DB row was already deleted is never revisited. |
| Status | **Documented only** — recommended follow-up: a storage-orphan sweep, or delete the blob first then the row. |

---

## Intentional eventual-consistency register

These are deliberate design choices, not defects. They must not be "fixed" by forcing synchronous
cross-module transactions.

| Situation | Why it is acceptable | Convergence path |
|---|---|---|
| `EmployeeCreated` fan-out (leave init, probation, onboarding, documents, assets, notifications) runs after the employee row is committed | Cross-module synchronous transactions are forbidden (02, 04). A new employee is usable immediately; downstream artefacts appear within the same request normally, or shortly after. | In-process handlers (same request); consumer idempotency on redelivery; reconciliation sweeps where they exist. |
| Audit events are promoted asynchronously by a background job | Audit must never fail or slow a business operation (AUD-01). | `AuditPendingItemPromotionJob` (Hangfire, retried); unique `event_id`. |
| Notifications and emails are delivered asynchronously | Delivery is best-effort and must not block the triggering action (04). | `EmailDeliveryJob` retry; in-app notification row persisted synchronously. |
| `CandidateHired` / `LeaveApproved` informational side effects (HR-inbox task, manager notification) | Informational only; losing one does not corrupt state. | Consumer idempotency; would require a new reconciliation sweep for full durability (not currently justified). |
| Offboarding plan created after the leaving process is committed | Same cross-module rule; the leaving process is the source of truth. | `OffboardingPlanCreationReconciliationJob` (daily). |

---

## Manual reconciliation runbook (unrecoverable failures)

For a failure that no automatic sweep repairs:

1. **Duplicate employee from a hire** — should now be impossible (NFR-08 idempotency key). If one is
   observed: identify the two `employees.employees` rows sharing a person; keep the one linked from
   `recruitment.candidates.employee_id`; soft-delete the orphan; manually cancel any onboarding plan /
   probation record / leave balances created for the orphan (query each module by the orphan employee id).
   File a regression test.
2. **Missing onboarding plan** (employee created, no plan) — until a reconciliation job exists, an HR
   admin re-runs onboarding plan creation from the employee's page, or an operator invokes
   `EmployeeCreated` replay for that employee id. Verify no partial plan exists first.
3. **Missing offboarding plan** — wait for `OffboardingPlanCreationReconciliationJob` (daily) or trigger
   it manually from Hangfire (`/hangfire`).
4. **Audit pending item stuck in `failed`** — inspect `audit.audit_pending_items WHERE status = 'failed'`;
   fix the underlying cause; call `ResetForRetry()` (moves it back to `pending`); the promotion job
   picks it up on its next run.
5. **Lost notification after a committed approval/hire** — the business state is correct; re-issue the
   notification manually if the recipient must be informed. Do not re-run the business operation.
6. **Orphaned storage blob after a partial purge** — record the blob key from logs; delete via the
   storage admin tooling. Non-urgent.

---

## Operational visibility

| Signal | Where to look |
|---|---|
| Failed background jobs (incl. audit promotion, reconciliation sweeps) | `/hangfire` dashboard; `/health/background-jobs` endpoint (`status: degraded` when `Failed > 0`) |
| Stuck audit events | `audit.audit_pending_items WHERE status IN ('failed','processing')` and `AttemptCount` |
| Integration-event handler failures | Structured log: `"Integration event handler {HandlerType} failed while handling {EventType}"` (from `IntegrationEventPublisher`) |
| Duplicate active offboarding plans | Structured log (error): `"Detected {Count} active offboarding plans for employee ..."` from `OffboardingPlanCreationReconciliationJob` |
| Duplicate provisioned employee (should be zero) | Query: `SELECT company_id, source_reference, count(*) FROM employees.employees WHERE source_reference IS NOT NULL GROUP BY 1,2 HAVING count(*) > 1` — the unique index makes a non-empty result impossible |

### Recommended follow-up alerts (not in this ticket)

- Alert when `audit.audit_pending_items` has any row `failed` for > 1 hour.
- Alert when any Hangfire recurring job has not succeeded within 2× its interval.
- Metric: count of `IntegrationEventPublisher` handler failures per hour.

---

## Open questions / recommended follow-up tickets

1. **Onboarding reconciliation** — add an `OnboardingPlanCreationReconciliationJob` mirroring the
   offboarding one (missing plan + unsynced task-item recovery). Highest-value remaining gap.
2. **Compensation idempotency** — decide on a client idempotency key or a
   `(employee_id, effective_from, reason)` uniqueness rule.
3. **Document acknowledgement uniqueness** — confirm / add a `(document_id, employee_id)` unique
   constraint on the acknowledgement table.
4. **Storage-orphan sweep** — for purge workflows that delete a DB row and a blob in separate steps.
5. **`LeaveApproved` / `CandidateHired` informational side effects** — decide whether durable
   delivery (outbox or reconciliation) is warranted, or accept the documented eventual-consistency.
