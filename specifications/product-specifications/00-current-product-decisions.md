# 00. Current Product Decisions

## Status and precedence

Last confirmed: 23 August 2026

This document records current product decisions that supersede conflicting older requirements. Update it when a decision changes, including the date and the superseded behaviour.

## Leaving and offboarding

- `SPEC-OFF-01` in `34-leaving-offboarding.md` is the authoritative end-to-end leaving and offboarding contract from 23 August 2026.
- The leaving date is the inclusive final date of employment; the last working day is the final day work is expected and may be earlier.
- Employment finalises on the leaving date even if offboarding is incomplete. Mandatory unresolved obligations remain open, transfer to an available owner or HR queue, and escalate after departure.
- Resignation, redundancy, dismissal, retirement, end of contract, mutual agreement and other routes use the same controlled lifecycle, audit and recovery rules.
- Payroll processing remains outside scope. Leave supplies a final balance statement and the product records an auditable payroll hand-off.

## Roles and employee access

- HR Administrators may view employee records throughout their company.
- Employees may view their own detailed employee record.
- Managers may view detailed employee records for every employee beneath them in the complete reporting hierarchy, including indirect reports.
- Recruiters do not receive general employee-record access merely because they hold the Recruiter role.
- Company Administrators do not receive employee or HR access merely because they hold the Company Administrator role.
- Company Administrator is limited to company administration, including company profile, settings and branding.
- UI visibility is not authorization. Every API operation must enforce the same resource scope on the server.

## Initial company creator

- A new company is created only through the public initial sign-up flow.
- The person creating the company receives Employee, Company Administrator and HR Administrator roles so that the first account can configure and use the whole application.
- This is an explicit initial-creator exception. Assigning Company Administrator to an existing or invited user must not automatically assign HR Administrator.
- An authenticated user who already belongs to a company cannot create another company through the normal company API.

## Salary and compensation visibility

- HR Administrators always have access to salary and compensation data within their company.
- Employees may view their own salary when `DisplaySalaryOnEmployeeProfile` is enabled.
- Managers may view salary for employees beneath them in the complete reporting hierarchy when `DisplaySalaryOnEmployeeProfile` is enabled.
- Company Administrators without HR Administrator and Recruiters without an additional authorised role do not receive salary access.
- Bank details, National Insurance numbers, tax data and payroll credentials are separate from salary visibility and remain outside this rule.

## Employee creation and onboarding

- Recruitment is the normal route for new starters.
- HR may create an employee directly in exceptional cases; directly created employees receive the normal employee onboarding process.
- A hired recruitment candidate receives the normal employee onboarding process.
- Imported employees represent an existing workforce and do not automatically enter employee onboarding.
- Data import covers departments, locations, employment types, position profiles and employees as one coordinated import capability.

## Leave and TOIL units

- Days are the canonical stored unit for all leave types, including TOIL.
- Leave balances store entitlement, usage and adjustments in days.
- Leave requests store total days.
- TOIL transactions store days.
- Hours may be accepted or displayed by a TOIL-specific UI and converted using the employee's effective hours per day.
- `LeaveBalanceAdjustment.AdjustmentHours` is supplementary audit/display information; `AdjustmentDays` remains canonical.

## Dashboards and landing behaviour

- HR Administrator, Recruiter and Manager roles have role-specific dashboards.
- Multi-role users may switch between dashboards available to their roles; the application chooses an authorised default when no preference exists.
- An ordinary Employee lands on their own profile rather than a generic Employee dashboard.
- A Company Administrator without an operational role lands in company administration rather than a generic Company Administrator dashboard.

## Reporting and exports

- Syncfusion grids may provide local grid exports on individual screens.
- The Reporting module and formal report catalogue are implemented.
- Current formal reports are live, permission-scoped report views with filters, grouping, saved views, favourites and supported server export endpoints.
- Asynchronous generation, stored report files, scheduled reports and report subscriptions are not general MVP requirements. They may be introduced later for a demonstrated large-report requirement.
- Reporting remains separate from payroll. Payroll processing is outside product scope.

## Events and outbox

- An outbox is not a universal V1 requirement.
- Modules may use in-process integration events and direct application contracts while preserving module boundaries.
- Commands, imports, webhooks, jobs and event consumers must still be transactional or idempotent as appropriate.
- A module may adopt a local outbox for a concrete delivery requirement. The Companies module currently has a scoped outbox; that does not mandate outboxes elsewhere.

## Leave management

- `14-leave-management.md` is the authoritative Leave capability document; where `06-leave-management.md` conflicts with it, `14-leave-management.md` takes precedence.
- Leave is distinct from the Sickness module. Leave owns leave requests, leave balances, leave policies, TOIL and general absence tracking. Sickness owns sickness records, evidence and return-to-work reviews as a separate domain, not a leave type. There is no "Sick Leave" leave type — it was removed (migration `RemoveSickLeaveType`) precisely to avoid double-tracking the same absence in two modules.
- Default leave types provisioned for every new company (`LeaveTypeDefaultsProvisioner`): Annual Leave, Unpaid Leave, Compassionate Leave, Parental Leave, Time Off In Lieu (TOIL). Sick Leave is deliberately excluded — sickness absence is tracked exclusively by the Sickness module.
- "Other" is not a provisioned default leave type. Leave types are fully configurable per company (`CreateLeaveType`/`UpdateLeaveType`/`DeactivateLeaveType`), so a company that wants a general/miscellaneous leave type can create one; MVP does not assume every company needs it.
- Supported leave request warnings, returned consistently by `PreviewLeaveRequest`, `SubmitLeaveRequest` and `SubmitLeaveRequestDraft` via the shared `LeaveWarningCalculator`/conflict queries: public-holiday-in-range warnings and existing-leave-request overlap warnings. None of these warnings block submission by themselves — insufficient balance and cross-policy-year requests are the only blocking validations.
- Team-clash warnings (concurrent leave across a team/department) and excessive-absence warnings are explicitly deferred, not implemented. They are not wired into `PreviewLeaveRequest`, `SubmitLeaveRequest` or `SubmitLeaveRequestDraft`, and are not MVP requirements. They may be introduced later for a demonstrated requirement.
- All leave-policy, leave-type, policy-assignment, balance-adjustment and TOIL mutations publish an audit event (`LeaveAudit.cs`) carrying company, employee (where applicable), actor and before/after data.

## Sickness management

- `15-sickness-management.md` is the authoritative Sickness capability document; where `07-sickness-management.md` conflicts with it, `15-sickness-management.md` takes precedence.
- `ReturnToWorkRequiredAfterDays` is confirmed as **1 working day** (`CompanySettings.CreateDefault`, `CompanySicknessSettings.Default`, and the `HR.Web` HR settings edit-model fallback all agree). An earlier draft of `15-sickness-management.md` stated 3 working days; that was spec drift against the already-implemented (SICK-04-era) behaviour, not a considered decision, so the spec was corrected to match the code rather than the reverse — a lightweight "does this warrant a return-to-work chat" check is meant to happen almost immediately after any absence, not only longer ones.
- The return-to-work threshold is evaluated against `SicknessRecord.TotalDays`, which is a **working-day** count (`SicknessCalculator`) — the employee's own working pattern, excluding non-working days and, if the company opts in, public holidays. This is deliberately different from the fit-note threshold, which is evaluated in **calendar** days (`FitNoteEvaluator`, SICK-01) because weekends and holidays should count toward "how long has this person actually been off" for evidence purposes but not toward "did this absence disrupt enough working time to warrant a chat". Do not conflate the two without updating this record.
- `FitNoteRequiredAfterDays` default remains 7 calendar days (SICK-01, unchanged by SICK-05).
- Default sickness categories provisioned for every new company (`SicknessCategoryDefaultsProvisioner`): Illness, Injury, Mental health, Medical appointment, Dependant care, Other. This replaces a previous diagnostic-looking default set ("Cold/Flu", "Back Pain", "Migraine"), which conflicted with the spec's explicit requirement that categories stay broad and non-diagnostic.
- The category default-set change is intentionally **not** a data migration. `SicknessCategoryDefaultsProvisioner` only provisions categories for a company that has none yet; existing companies — including ones seeded under the old diagnostic defaults — keep whatever categories they already have. Categories remain fully editable per company (`CreateSicknessCategory`/`UpdateSicknessCategory`/`DeactivateSicknessCategory`), so an existing tenant can rename or replace its categories itself if it wants to.

## Product scope

- Payroll processing is outside scope.
- Redis is not currently required.
- Supabase provides authentication, database and storage capabilities.
- Hangfire is used for background jobs and Postmark for email.
