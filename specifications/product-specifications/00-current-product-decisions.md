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

## Product scope

- Payroll processing is outside scope.
- Redis is not currently required.
- Supabase provides authentication, database and storage capabilities.
- Hangfire is used for background jobs and Postmark for email.
