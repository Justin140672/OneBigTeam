# SPEC-OFF-01 — Leaving and Offboarding

## Status and authority

| Field | Value |
|---|---|
| Status | Current product contract |
| Effective date | 23 August 2026 |
| Owner | Product |
| Applies to | Employee Lifecycle, Offboarding, Tasks, Assets, Documents, Identity, Leave, Notifications, Reporting and Audit |

This specification is the authoritative end-to-end contract for employees leaving a company. It supersedes conflicting leaving, termination and offboarding behaviour in earlier product specifications, including the generic termination workflow in `12-employee-lifecycle.md` and offboarding fragments in `14-leave-management.md`, `18-assets-management.md`, `20-tasks-workflow.md`, `21-documents-file-management.md`, `22-notifications-communication.md`, `23-reporting-exports.md`, `26-permissions-access-ux.md` and `27-audit-activity-history.md`.

Those specifications remain authoritative for their general module behaviour. If there is a conflict about a leaver or an offboarding process, this specification wins. The terms **leaving process** and **departure** replace the ambiguous generic term **termination** except where `Terminated` is a retained technical or historical status.

## Product outcomes

The product shall:

- record every supported route by which employment ends;
- distinguish the legal employment end date from the employee's final day at work;
- create a controlled, owned and auditable offboarding plan;
- finish employment on time even when operational offboarding is incomplete;
- preserve the former employee's history without retaining product access by accident;
- coordinate modules without one module writing another module's data;
- make retries safe and expose recoverable failures;
- provide definitions that map directly to automated tests.

## Terminology and date rules

| Term | Definition |
|---|---|
| Resignation received date | The date the company received notice. For employer-led or agreed departures, this is the date the leaving decision was formally recorded or communicated. The stored field may retain its existing technical name. |
| Calculated leaving date | `resignation received date + effective notice period`, calculated in calendar units. It is a proposal shown to HR, not an independently persisted lifecycle state. |
| Leaving date | The inclusive final date of employment for status, access, leave proration, headcount and leaver reporting. |
| Last working day | The final date on which work is expected. It may be earlier than the leaving date because of garden leave, paid leave, sickness, non-working days or another agreed arrangement. |
| Departure finalisation | The transition that marks employment ended on the leaving date and performs the associated identity, timeline and reporting updates. |
| Offboarding | The operational checklist for assets, documents, access, handover and notifications. It is related to, but does not control, departure finalisation. |
| Company today | The calendar date in the company's configured time zone. All due-date and backdating decisions use this date, not server UTC. |

Date invariants:

1. `resignation received date <= leaving date`.
2. `last working day <= leaving date`.
3. Dates are inclusive `DateOnly` values; time of day is not user-configurable in this contract.
4. Weekends and public holidays are not silently removed from notice calculations. A user may explicitly choose an earlier last working day.
5. A leaving date equal to company today is not backdated. It is finalised by the due-departure process on that date; an implementation may finalise immediately if it uses the same idempotent finaliser.
6. A leaving date before company today is backdated and requires explicit confirmation.

## Leaving routes

The required controlled reasons are:

| Reason | Route | Notice default |
|---|---|---|
| Resignation | Employee-led | Effective notice period |
| Redundancy | Employer-led | Effective notice period, editable by HR |
| Dismissal | Employer-led | Effective notice period, editable to the legally agreed date |
| Retirement | Employee-led/agreed | Effective notice period |
| End of contract | Contractual | Effective notice period, editable to the contract end date |
| Mutual agreement | Agreed | Effective notice period, editable to the agreed date |
| Other | Exceptional | Effective notice period; explanatory notes are mandatory |

The route affects reporting and wording only. It does not weaken authorization, audit, offboarding, access removal or finalisation rules. HR Administrator is the only standard role permitted to start, amend or cancel a leaving process. The employee cannot self-initiate a formal process in MVP.

## Notice-period decision

The effective notice period is resolved when the leaving process starts in this strict order:

1. employee override, when both unit and length are present;
2. position profile override, when both unit and length are present;
3. company default;
4. platform fallback of one month only when company settings are absent for legacy data.

Supported units are days, weeks and months. Length is a positive whole number. A partial override is invalid at write boundaries and is ignored defensively during resolution.

Calendar arithmetic applies: days are calendar days, weeks are seven calendar days, and months preserve the day where possible or use the final valid day of the destination month. The UI shall show the calculated leaving date, the period and its source before confirmation. HR may override the proposed leaving date and must provide an amendment/override reason. The stored notice period and source are an immutable snapshot for that leaving process; later employee, position or company setting changes do not alter it.

Amending a leaving date does not recalculate the notice snapshot. It records the new explicit date, the actor, timestamp and reason.

## Aggregate ownership

### Leaving process

The Employees module owns one leaving-process record per attempt, with:

- employee and company identifiers;
- leaving route/reason and optional explanatory notes;
- received/decision date, leaving date and last working day;
- notice unit, length and source snapshot;
- status, start/amend/cancel/finalise timestamps and actors;
- cancellation or amendment reason where applicable.

Only one leaving process may be `InProgress` for an employee. Completed and cancelled attempts are retained permanently as employment history.

### Offboarding plan

The Offboarding module owns an offboarding plan and its checklist. Each plan is linked to exactly one leaving-process attempt, not merely to the employee. This prevents a later departure attempt from reusing a cancelled plan.

An offboarding plan snapshots its last working day and template version. It contains independently identified obligations whose status can be reconciled with Tasks and owning modules.

## State models

### Employee employment state

| From | Trigger | To | Rule |
|---|---|---|---|
| Active or On Leave | Start leaving | Leaving | Employment continues and normal historical records remain available. |
| Suspended | Start leaving | Leaving | Suspension restrictions remain effective until changed or departure is finalised. |
| Leaving | Cancel leaving | Prior employed state | Restore the state captured at start; do not always force `Active`. |
| Leaving | Leaving date becomes due | Former Employee | Finalise regardless of checklist completion. |
| Former Employee | Corrective reactivation | Active | A separate authorised reactivation workflow is required; cancellation is not allowed after finalisation. |

Draft employees cannot start leaving. Former employees cannot start another leaving process until reactivated. `On Leave` is an employment presentation state and does not suspend the leaving date.

### Leaving-process state

| State | Allowed transition | Outcome |
|---|---|---|
| InProgress | Amend | Remains `InProgress`; dates/reason change and dependent work is reconciled. |
| InProgress | Cancel | `Cancelled`; outstanding generated work is cancelled and prior employee state restored. |
| InProgress | Finalise | `Completed`; employee becomes a former employee. |
| Cancelled | None | Terminal and immutable except append-only notes/audit corrections. |
| Completed | None | Terminal; corrections use a separate reactivation/correction workflow. |

Start is rejected when an in-progress process exists. Amend and cancel are rejected after completion or cancellation. Repeating the same command with the same idempotency key returns the original outcome; a materially different payload with that key is a conflict.

### Offboarding-plan state

| State | Allowed transition | Outcome |
|---|---|---|
| NotStarted | Start | `InProgress` and instantiate checklist. |
| NotStarted | Cancel | `Cancelled`. |
| InProgress | Complete | `Completed` only when every mandatory obligation is satisfied. |
| InProgress | Cancel | `Cancelled`; open generated tasks are cancelled. |
| Completed | None | Terminal. Departure may still await its leaving date. |
| Cancelled | None | Terminal. A new leaving attempt creates a new plan. |

Plan creation and start may occur as one operation. An implementation shall not expose a stranded `NotStarted` plan after a successful start response.

### Offboarding-obligation state

| State | Allowed transitions | Meaning |
|---|---|---|
| Pending | Start, Complete, Waive, Cancel | Not yet actioned. |
| InProgress | Complete, Waive, Cancel | Action has begun. |
| Completed | None | Required outcome was completed. |
| Waived | None | Authorised HR user decided the outcome is not required; waiver reason is mandatory. |
| Cancelled | None | Parent leaving process was cancelled; not treated as completion. |

`Completed`, `Waived` and `Cancelled` are terminal. Historical `Skipped` maps to `Waived` only when it has an actor and reason; otherwise it remains an incomplete migration exception until reconciled.

## Start workflow

Starting a leaving process shall atomically establish the Employees record and a durable request to create the offboarding plan. The visible outcome is:

1. validate tenant, employee, role, state and date invariants;
2. resolve and snapshot notice;
3. save the leaving process and set employee state to `Leaving`;
4. create exactly one linked offboarding plan from the selected effective template;
5. create obligation/task projections without duplicates;
6. notify the employee, manager where present, HR owner and other assigned owners;
7. append audit and employee timeline entries;
8. if backdated and confirmed, invoke departure finalisation after the plan request is durable.

If a downstream module is unavailable, the leaving process remains valid and visibly shows `Setup incomplete`; retry/reconciliation completes the missing side effects. The API must not create a second process or plan in response to a retry.

## Templates, obligations and ownership

Companies may maintain versioned offboarding templates. A company has one active default template; HR may select another active template at start. Editing a template never mutates existing plans.

Each template item defines:

- stable item identifier and version;
- title and description;
- owner type: Employee, Manager, HR, Finance, IT/Access, named employee or named role;
- due-date offset relative to last working day or leaving date;
- mandatory or optional classification;
- linked action type and source module where applicable;
- escalation route.

The system baseline template contains:

| Obligation | Default owner | Required | Completion source |
|---|---|---|---|
| Return each assigned asset | Employee, overseen by HR | Mandatory | Assets confirms Returned, Lost or authorised write-off |
| Review outstanding employee document requests and retention | HR | Mandatory | Documents/HR confirmation |
| Revoke system and third-party access | IT/Access, falling back to HR | Mandatory | Identity confirms product access action; external access is attested |
| Notify Finance of final employment facts and leave balance | Finance, falling back to HR | Mandatory | Acknowledgement/hand-off only |
| Reassign direct reports and approval responsibilities | Manager, falling back to HR | Mandatory when reports/responsibilities exist |
| Handover and knowledge transfer | Manager, falling back to HR | Mandatory |
| Exit interview | Manager | Optional |

Optional items do not block plan completion. A mandatory item blocks completion until `Completed` or `Waived`. Only HR may waive a mandatory item, and a reason is required. A system-confirmed action must not be manually completed while its owning module reports it unresolved.

If an owner is missing, inactive, is the leaver, or loses access before completing the item, ownership falls back to the company HR work queue. The plan remains valid. Missing ownership is never a reason to omit an obligation.

The Tasks module owns task presentation, assignment, reminders and its task status. Offboarding owns obligation meaning and plan completion. Task completion invokes an idempotent completion action; Offboarding then verifies any source-module condition and updates the obligation. Status drift is reconciled from the owning obligation, with audit visibility.

## Cross-module hand-offs

| Concern | Authoritative owner | Contract at start/amend/finalise |
|---|---|---|
| Employment state, dates, reason, manager history | Employees | Owns leaving attempt and finalisation. Publishes/replays lifecycle facts. |
| Plan and obligation completion | Offboarding | Instantiates template, tracks mandatory outcomes and publishes plan state. |
| Work item assignment and UI | Tasks | Projects obligations as tasks; cancels/reassigns idempotently on instruction. |
| Physical assets | Assets | Returns current assigned assets at start and owns return/lost/write-off status. One obligation per active assignment. |
| Employee documents | Documents | Owns files, requests, retention and access. Departure never deletes documents automatically. |
| Product account and permissions | Identity/Permissions | Disables product access at finalisation when configured and removes operational scopes; preserves identity/audit linkage. |
| External/system access attestation | Offboarding task owner | Records confirmation only; no claim of automated external revocation without an integration. |
| Leave entitlement and requests | Leave | Calculates final-date proration, resolves future requests and supplies a final balance statement. |
| Finance | External hand-off | Receives an auditable hand-off; One Big Team does not calculate pay, tax, deductions or settlement. |
| Notifications | Notifications | Owns delivery and delivery history; business modules own recipient and trigger decisions. |
| Reports | Reporting | Reads purpose-specific contracts; never writes operational records. |
| Audit and timeline | Audit/Employees | Audit is immutable technical/business evidence; employee timeline is a curated lifecycle view. |

## Amendments

HR may amend an `InProgress` process's leaving date, last working day, route and explanatory notes. Every amendment requires a reason and a before/after audit record.

On date amendment:

- validate the date invariants and require confirmation if newly backdated;
- update all pending/in-progress obligation due dates derived from the changed date;
- update linked open Tasks due dates;
- do not alter completed or waived obligations;
- re-evaluate leave proration and future leave requests;
- notify the employee and current owners of material date changes;
- retain the original notice snapshot;
- immediately invoke idempotent finalisation if the new leaving date is in the past;
- do not reverse a finalised departure if the new date is later, because completed processes cannot be amended.

Asset and document obligations are reconciled after an amendment. Newly assigned assets or newly outstanding document obligations are added exactly once; resolved items are completed from source truth, not deleted.

## Cancellation

Cancellation is permitted only while the leaving process is `InProgress`. HR must confirm and provide a reason.

Cancellation shall:

- mark the leaving process and its linked plan `Cancelled`;
- restore the employee state captured when the process started;
- cancel every pending/in-progress obligation and generated Task;
- leave completed/waived obligations and their evidence unchanged;
- reverse only reservations or future changes explicitly created by the leaving workflow;
- not undo an asset already returned, a document already archived under an independent retention decision, or an access change already performed;
- tell HR which completed side effects require manual restoration;
- notify the employee and all current task owners;
- append audit and timeline entries.

Cancellation does not delete records. A later departure creates a new leaving process and plan. Cancellation after `Completed` is rejected; HR must use a separately audited reactivation/correction workflow.

## Backdated departures

A backdated start or amendment is allowed for authorised historical correction only and requires explicit confirmation. The UI shall state that employment will be finalised immediately and access may be removed.

The system shall create the same leaving process, plan, obligations, leave calculation, hand-offs, audit and reporting facts as an on-time departure. Due dates in the past become immediately overdue. Notifications are phrased as a historical/corrective departure and are not sent to an employee whose product access is already disabled unless an authorised personal-email channel is explicitly configured.

Departure finalisation runs immediately through the normal idempotent finaliser. Incomplete obligations remain open and escalated; backdating never silently marks them complete.

## Manager and approval reassignment

Before departure, Employees shall identify:

- direct reports;
- open manager-owned approvals and Tasks;
- workflows that name the employee as an approver or owner.

The selected successor must be an active employee in the same company and cannot be the leaver or one of their subordinates where that would create a hierarchy cycle. HR may choose different successors for reporting lines and workload.

If no successor is selected by the leaving date:

- direct reports become managerless, not reassigned to an inferred manager;
- open approvals and Tasks move to the HR work queue;
- access checks cease to grant the former manager hierarchy scope;
- a mandatory high-priority reassignment obligation remains open and appears in workload/reporting.

Historical records retain the manager that applied at the time; reassignment changes current ownership only.

## Leave entitlement and outstanding balance

Leave owns all calculations in days, consistent with `00-current-product-decisions.md`.

When a process starts or its leaving date changes, Leave shall provide a provisional statement containing:

- policy and leave year;
- full-year entitlement;
- leaving-date prorated entitlement according to the employee's policy;
- carried and manual adjustments;
- approved/taken usage through the leaving date;
- provisional positive or negative balance.

Requests wholly after the leaving date are cancelled by the system with an audit reason and the requester/approver notified. A request spanning the leaving date is shortened to the leaving date and recalculated; if the leave model cannot safely split it, it is moved to HR review and does not block departure. Requests on or before the leaving date retain their normal state and approval rules.

At finalisation, Leave freezes a final balance statement using facts effective on the leaving date. The value is supplied to the Finance hand-off and reporting. One Big Team does not decide whether a positive balance is paid or a negative balance is deducted; HR or Finance records the external outcome. Cancelling before departure removes the provisional leaver proration and restores affected future requests where safe; otherwise it creates an HR review item with an audit trail.

## Finalisation and incomplete offboarding

The leaving date controls employment finalisation. The last working day and offboarding completion do not postpone it.

At the beginning of the leaving date in company-local date processing, the idempotent finaliser shall:

1. change employee state from `Leaving` to `Former Employee`;
2. complete the leaving process;
3. close current employment history with the leaving date;
4. obtain/freeze the final leave balance statement;
5. apply the company's product-access policy (default: disable access);
6. remove current manager/operational permission effects and re-home unresolved work;
7. record final audit and timeline facts;
8. update source contracts used by leaver/headcount reporting;
9. evaluate, but not force-complete, the offboarding plan.

If all mandatory obligations are completed or waived, Offboarding completes the plan independently, whether before or after departure.

If mandatory obligations remain at departure:

- employment still finalises;
- the plan remains `InProgress` and is labelled `Overdue after departure`;
- employee-owned open items move to the HR work queue because the employee may lose access;
- HR and each unresolved item's escalation owner receive a high-priority notification;
- reminders continue on the configured cadence;
- reports show the former employee and the outstanding obligations until resolution;
- completion remains possible after departure.

## Access and former-employee visibility

`AutoDisableAccessOnLeavingDate` defaults to true. When true, product sign-in/access is disabled at departure finalisation. When false, authentication may remain enabled only for deliberately granted former-employee self-service access.

A former employee with retained access may see a minimal self-service record containing their identity, employment dates, permitted personal documents and the final leave statement. They cannot see company directory data, manager/team data, operational Tasks, notifications intended for staff, or any resource gained from former position/manager roles. HR controls document availability and retention.

When access is removed, the employee record remains visible to authorised HR and in historical reports. Managers retain access only if current scope or an explicit historical-report permission grants it; the old reporting line alone does not grant continuing detailed-record access.

Disabling access must not delete the authentication identity, employee record, documents, audit records or task evidence. Re-enabling access is a separate audited action.

## Notifications, reminders and escalation

| Event | Required recipients | Priority/timing |
|---|---|---|
| Leaving started | Employee, manager if present, HR owner, assigned obligation owners | Once after durable creation |
| Dates/reason amended | Employee, HR owner and owners whose due dates changed | Once per material amendment |
| Leaving cancelled | Employee, HR owner and current owners | Once after cancellation |
| Obligation assigned/reassigned | New owner | At assignment |
| Obligation approaching due | Owner | Configurable; baseline 7 days before due |
| Obligation overdue | Owner and escalation owner | Daily evaluation; one notification per recipient/obligation/escalation stage |
| Departure with incomplete plan | HR and escalation owners | High priority at finalisation |
| Plan completed | HR and manager where active | Once |
| Cross-module setup/recovery failure | HR work queue | Visible until recovered |

Missing managers suppress only the manager-specific notification. They do not suppress the workflow, employee notification, HR notification or task creation. Delivery failure does not roll back a business transition; Notifications retries delivery and exposes failure history.

## Audit, timeline and reporting

Audit records are append-only and company scoped. Each event includes actor (or `System`), timestamp, company, employee, leaving-process ID, plan ID where applicable, idempotency/correlation ID, and redacted before/after metadata.

Required audit events are:

- leaving process started, amended, cancelled and completed;
- backdating confirmed;
- offboarding plan created, started, completed and cancelled;
- obligation created, assigned, reassigned, completed, waived and cancelled;
- asset/document/access/leave/Finance hand-off requested, succeeded and failed;
- employment finalised and access enabled/disabled;
- recovery retry attempted, succeeded or exhausted.

The employee timeline shows the human-readable milestones: leaving announced, dates amended, leaving cancelled, offboarding started/completed and employment ended. It does not show noisy delivery retries or contain sensitive reason notes. Timeline visibility is `AuthorisedInternal` except a deliberately safe employee-facing event.

Reporting definitions:

| Report/metric | Definition |
|---|---|
| Current leaver | Employee with an `InProgress` leaving process. |
| Former employee/leaver | Employee whose leaving process is `Completed`; reported leaving date is the employment end date. |
| Leaving in period | Completed leaving process whose leaving date falls inclusively within the filter range. Future planned leavers are separate. |
| Headcount | Includes `Leaving` employees through their leaving date; excludes `Former Employee` after that date. |
| Offboarding progress | Latest non-cancelled plan per leaving attempt, with mandatory and total completed-or-waived counts shown separately. |
| Offboarding complete | Every mandatory obligation is `Completed` or `Waived`; optional open items do not block. |
| Overdue after departure | Former employee with an `InProgress` plan containing at least one mandatory open obligation. |
| Outstanding assets | Active asset assignments whose return outcome is unresolved, regardless of task state. |
| Final leave balance | Frozen balance supplied by Leave at finalisation, not a settlement calculation. |

Cancelled attempts are excluded from default leaver/offboarding metrics but are available to authorised audit/history views. Report filters and exports use the same tenant and permission rules as interactive views.

## Idempotency, ordering and recovery

All start, amend, cancel, finalise, plan-generation, task-projection, notification, completion-action and reconciliation operations must be idempotent.

Required identities and uniqueness rules:

- at most one `InProgress` leaving process per company/employee;
- at most one plan per leaving-process ID;
- at most one generated obligation per plan/template-item/source-entity tuple;
- at most one Task projection per obligation;
- at most one notification per event/recipient/channel/escalation stage;
- at most one employment-finalised fact per leaving process.

Events carry company ID, leaving-process ID, aggregate version and correlation/idempotency ID. Consumers ignore an already-applied event and reject cross-company references. Out-of-order events are retried or held until prerequisites exist; they must not invent placeholder business records.

A scheduled reconciler shall detect and repair:

- a leaving process without its plan;
- obligations without Task projections or Tasks whose obligation state has advanced;
- missing asset/document/leave/access hand-offs;
- a due in-progress departure not finalised;
- a completed set of mandatory obligations whose plan is not completed;
- a cancelled process with open generated work;
- notifications that exhausted normal delivery retries.

Recovery is bounded, observable and audited. Exhausted retries create an HR workload action with the failed step and safe retry option. Business history is never deleted to force convergence.

## Security and validation

- Every command and read is authenticated, tenant-scoped and server-authorised.
- Employee self-visibility and manager visibility follow the current access decision, narrowed after departure as defined here.
- Leaving reasons, notes and dismissal/redundancy details are sensitive HR data and must not appear in general notifications, task titles, logs or broad reports.
- Audit metadata must not contain credentials, bank details, tax identifiers, medical facts or settlement amounts.
- The API, not only the UI, enforces state, ownership and date invariants.

## Automated acceptance contract

Each criterion below is independently automatable. Tests should use the identifier in their name or trace metadata.

### Initiation and dates

- **OFF-AC-001** Given an active employee and authorised HR user, when each supported leaving reason is submitted with valid dates, then one in-progress leaving process and one linked plan are created.
- **OFF-AC-002** Given `Other`, when explanatory notes are absent, then start is rejected without mutation.
- **OFF-AC-003** Given received date after leaving date or last working day after leaving date, then start/amend is rejected without mutation.
- **OFF-AC-004** Given employee, position and company notice settings, then resolution uses employee, then position, then company, and snapshots the selected source.
- **OFF-AC-005** Given a month-end received date, then month notice arithmetic uses the last valid destination-month day.
- **OFF-AC-006** Given an existing in-progress attempt, then another start is a conflict and creates no plan/tasks.
- **OFF-AC-007** Given a missing manager, then start succeeds, manager obligations fall back to HR, and non-manager notifications still exist.

### Amendment, cancellation and backdating

- **OFF-AC-008** Given an in-progress process, when dates are amended, then derived open due dates and leave projections update while the notice snapshot and terminal obligations remain unchanged.
- **OFF-AC-009** Given offboarding already started, when HR amends it, then the amendment succeeds, is audited and owners receive material-change notifications.
- **OFF-AC-010** Given a past leaving date without explicit confirmation, then start/amend is rejected without mutation.
- **OFF-AC-011** Given a confirmed backdated date, then the standard plan is created, departure finalises immediately and unresolved obligations remain overdue.
- **OFF-AC-012** Given an in-progress process, when cancelled with a reason, then the captured prior employee state returns, the plan/open generated work is cancelled and completed evidence remains.
- **OFF-AC-013** Given a completed or cancelled process, then amend/cancel is rejected without mutation.
- **OFF-AC-014** Given a cancellation followed by another departure, then a new attempt and plan are created and historical records remain distinct.

### Templates, tasks and hand-offs

- **OFF-AC-015** Given a selected template version, then its items are snapshotted and later template edits do not change the plan.
- **OFF-AC-016** Given assigned assets, then exactly one mandatory return obligation is created per active assignment and completion follows Assets source truth.
- **OFF-AC-017** Given no assigned assets, then no asset-return obligation is fabricated.
- **OFF-AC-018** Given document, access, Finance, reassignment and handover needs, then the baseline mandatory obligations are created with explicit owners/fallbacks.
- **OFF-AC-019** Given an optional open item and all mandatory items satisfied, then the plan can complete.
- **OFF-AC-020** Given a mandatory open item, then plan completion is rejected; when HR waives it with a reason, completion may proceed.
- **OFF-AC-021** Given an inactive/missing owner or leaver-owned item at access removal, then ownership moves to HR and the obligation is not omitted.
- **OFF-AC-022** Given completion of a projected Task more than once, then the obligation completes once and the plan-completed event is emitted at most once.
- **OFF-AC-023** Given a system-confirmed obligation whose source condition is unresolved, then manual completion is rejected.

### Leave, managers and finalisation

- **OFF-AC-024** Given a leaving date, then Leave returns a provisional day-based prorated statement and cancels requests wholly after that date.
- **OFF-AC-025** Given a request spanning the leaving date, then it is shortened/recalculated or routed visibly to HR review.
- **OFF-AC-026** Given cancellation before departure, then provisional proration is removed and future requests are restored or visibly routed to review.
- **OFF-AC-027** Given direct reports and a valid successor, then current reporting lines and open workload reassign without changing historical manager facts.
- **OFF-AC-028** Given no successor at departure, then reports become managerless, workload moves to HR and a mandatory escalation remains open.
- **OFF-AC-029** Given company today equals leaving date, then finalisation occurs once regardless of last working day or plan status.
- **OFF-AC-030** Given incomplete mandatory obligations at departure, then employment completes, the plan remains in progress/overdue, employee work moves to HR and high-priority escalation is created.
- **OFF-AC-031** Given all mandatory obligations satisfied before departure, then the plan completes but employment remains `Leaving` until the leaving date.
- **OFF-AC-032** Given auto-disable enabled, then access is disabled at finalisation without deleting identity/history; when disabled, only deliberately granted former-employee permissions remain.
- **OFF-AC-033** Given finalisation, then Leave freezes the final balance and Finance receives a hand-off without the product calculating settlement.

### Visibility, audit, reporting and recovery

- **OFF-AC-034** Given a former employee with retained access, then only the defined minimal self-service resources are returned and former operational roles/scopes grant nothing.
- **OFF-AC-035** Given each state transition and hand-off outcome, then the required redacted audit event exists with actor/system, tenant, correlation and aggregate identifiers.
- **OFF-AC-036** Given leaver/headcount/offboarding report queries at boundary dates, then results use the definitions and inclusive date rules in this specification.
- **OFF-AC-037** Given a cancelled attempt, then it is absent from default leaver/offboarding metrics and present in authorised history.
- **OFF-AC-038** Given repeated commands/events with the same idempotency identity, then no duplicate process, plan, obligation, task, notification or finalisation fact is created.
- **OFF-AC-039** Given a materially different payload with a reused idempotency key, then the command is rejected as a conflict.
- **OFF-AC-040** Given an interrupted cross-module operation, then reconciliation completes only the missing effects, records recovery audit, and raises an HR action after bounded retries are exhausted.
- **OFF-AC-041** Given events delivered out of order or for another company, then prerequisites are retried/held, cross-company data is rejected and no placeholder record is created.
- **OFF-AC-042** Given notification delivery failure, then the business transition remains committed and delivery is retried with visible failure history.

## Superseded historical behaviour

The following interpretations are explicitly obsolete:

- treating `termination date` as both leaving date and last working day;
- treating resignation as the only leaving route;
- delaying employment finalisation until offboarding tasks finish;
- completing a plan when every optional task is complete, or allowing a mandatory task to be silently skipped;
- dropping manager tasks or workflow creation when an employee has no manager;
- manually completing an asset return while Assets still records the assignment as unresolved;
- cancelling a departure by deleting its plan, tasks or history;
- restoring every cancelled leaver to `Active` instead of their captured prior state;
- recalculating the stored notice snapshot after an explicit date amendment;
- allowing former manager hierarchy to preserve operational access after departure;
- treating a final leave balance as a settlement calculation;
- relying on at-most-once delivery or best-effort no-op behaviour for cross-module consistency.
