# 26. Permissions & Access UX

## Overview

The Permissions & Access subsystem controls access to all data and functionality within the HR platform.

The platform uses:

- Role Based Access Control (RBAC)
- Scope-based permissions
- Manager hierarchy evaluation
- Position-based default permissions

The system is designed to be secure, predictable, auditable, and easy to administer.

---

## Business Objectives

The system shall:

- Protect company data
- Support role-based access
- Support manager hierarchy permissions
- Support company isolation
- Support permission auditing
- Minimise administration effort
- Avoid permission sprawl

---

# Permission Model

Permissions are granted through:

1. Position Profile
2. Employee Role Overrides
3. Scope Evaluation
4. Hierarchy Evaluation

---

# Position-Based Permissions

Permissions are primarily inherited from Position Profiles.

Examples:

### HR Administrator

- employee.read
- employee.edit
- leave.approve
- document.manage

### Manager

- employee.read
- leave.approve

Scope:

direct_reports

### Employee

Scope:

self

---

# Role Model

System roles include:

- Employee
- Manager
- HR Administrator
- Recruiter
- Finance
- Company Administrator

Roles are fixed in v1.

---

# Scope Model

Supported scopes:

## Self

User may access their own records only.

## Direct Reports

The scope name is retained for compatibility, but manager hierarchy access includes every employee beneath the manager in the complete reporting hierarchy, including indirect reports. It does not include peers, the manager's own manager or employees in unrelated branches.

## Department

Future enhancement.

## Company

Company-wide access.

---

# Employee Overrides

Position permissions may be supplemented by employee-specific roles.

Examples:

- Temporary HR access
- Recruitment access
- Finance access

Overrides should be rare.

---

# Permission Resolution

Evaluation order:

1. Company boundary
2. Permission check
3. Scope evaluation
4. Hierarchy evaluation
5. Resource ownership

---

# Permission Administration

## Role Assignment Screen

Displays:

- Current position
- Inherited roles
- Assigned overrides

---

## Override Indicator

Users with overrides should display:

Permission Override

to reduce permission sprawl.

---

## Effective Permissions View

Administrators can view:

- Inherited permissions
- Override permissions
- Effective permissions

---

# Manager Hierarchy

Managers receive access through:

manager_employee_id

Permissions apply only to employees beneath the manager in the complete hierarchy. Hierarchy membership must be evaluated on the server for the requested employee or resource.

---

# Employee Resource Access Matrix

| Caller | Detailed employee record | Salary/compensation |
|---|---|---|
| Employee | Own record only, full field set | Own salary only when `DisplaySalaryOnEmployeeProfile` is enabled |
| Manager | Employees beneath them in the complete hierarchy — **operational fields only** (see field-level access matrix below); never their own record via this scope — self-access, not hierarchy, covers that | Same hierarchy, only when `DisplaySalaryOnEmployeeProfile` is enabled |
| HR Administrator | Company-wide, full field set | Company-wide regardless of the display setting |
| Recruiter only | No general detailed employee access | No access |
| Company Administrator only | No general detailed employee access | No access |

Company Administrator is a company-settings role, not an HR role. The initial company creator also receives HR Administrator as a separately assigned, explicit exception.

Directory-style lists may expose a deliberately reduced set of work fields to a wider audience, but must not reuse a detailed employee response containing personal or sensitive fields.

## Field-level access matrix and the GetEmployee / GetEmployeeTeamView split

A manager's hierarchy access to "the detailed employee record" above is **not** the same
response shape HR receives — reconciles the ambiguity previously between this document and
`30-administrative-role-separation-matrix.md` (which listed manager access as "Self", meaning
"no more than self-service" rather than describing hierarchy scope at all). Self and HR access,
and manager access, are served by two separately typed API endpoints rather than one endpoint
returning different shapes — this keeps the public contract of each endpoint explicit (no
endpoint declares its response as an untyped `object`) and lets each endpoint's database query
select only the columns its own response can ever carry, rather than fetching the full record
and discarding fields afterwards:

| Endpoint | Route | Authorized callers | Response |
|---|---|---|---|
| GetEmployee | `GET /api/companies/{companyId}/employees/{id}` | Self, HR Administrator | `GetEmployeeResponse` (full HR record) |
| GetEmployeeTeamView | `GET /api/companies/{companyId}/employees/{id}/team-view` | Manager, anywhere in the target's reporting hierarchy (direct or indirect) | `GetEmployeeTeamViewResponse` (operational-only) |

| Field group | Employee (self, via GetEmployee) | Manager (via GetEmployeeTeamView) | HR Administrator (via GetEmployee) |
|---|---|---|---|
| Identity, name, work email | Y | Y | Y |
| Department / location / position / employment type | Y | Y | Y |
| Manager / reports / reporting chain | Y | Y | Y |
| Start date, employment status, employee number | Y | Y | Y |
| Lifecycle tab flags (onboarding/probation/offboarding/leaving) | Y | Y | Y |
| Personal email, phone numbers, home address | Y | — | Y |
| Date of birth, nationality, gender | Y | — | Y |
| System-access state, working-pattern overrides | Y | — | Y |
| Leaving-process dates, notice period (raw + effective), "start leaving process" action | Y | — | Y |
| HR notes | Y | — | Y |
| Optimistic-concurrency version token, timestamps | Y | — | Y |

Implemented by `HR.Modules.Employees.Features.GetEmployee.GetEmployeeHandler` (authorized via
`EmployeesResourceAuthorizer.CanViewFullRecordAsync` — self or HR-admin only, hierarchy
explicitly excluded) and `HR.Modules.Employees.Features.GetEmployeeTeamView.GetEmployeeTeamViewHandler`
(authorized via `CanViewAsManagerAsync` — hierarchy only, self excluded). The team-view handler
has no dependency on `IEffectiveNoticePeriodResolver` at all and its database projection has no
column not represented in `GetEmployeeTeamViewResponse` — the full record is never
fetched-and-hidden, at either the API or the UI layer.

UI route: managers reach the restricted view via a "View profile" action on each report in the
My Team widget (`MyTeamWidget.razor`), landing on `TeamMemberProfile.razor` at
`/companies/{companyId}/employees/{id}/team-view` — a distinct, read-only page, not a relaxed
guard on the existing HR administration page (`EmployeeEdit.razor`, still gated on
`Session.CanManageEmployees`). `TeamMemberProfile.razor` renders only the fields
`GetEmployeeTeamViewResponse` carries and has no edit control, "More actions" menu, or HR-only
tab.

Covered by `GetEmployeeHandlerTests` / `GetEmployeeTeamViewHandlerTests` (unit — including a
reflection-based assertion that the team-view response type has no property for any sensitive
field, so a future regression fails to compile) and `GetEmployeeResourceAuthorizationTests`
(integration — asserts the actual JSON payload for both endpoints, not just status codes).

UI visibility never substitutes for API authorization.

---

# Company Isolation

Users can never access another company's data.

All permissions remain subject to:

company_id isolation

---

# Permission Auditing

Audit:

- Role assigned
- Role removed
- Override added
- Override removed
- Permission evaluation failures

---

# UX Requirements

## Permission Summary

Display:

- Roles
- Scopes
- Effective access

---

## Permission Search

Administrators can search:

- Employees
- Roles
- Overrides

---

## Permission History

Display:

- Who changed permissions
- When
- Previous values
- New values

---

# Reporting

Reports:

- Users by role
- Permission overrides
- Access review report
- Security audit report

---

# Validation Rules

- Overrides require permission.
- Users cannot elevate themselves.
- Company boundaries cannot be bypassed.
- Role assignments must be valid.

---

# Acceptance Criteria

1. Permissions inherited from position profiles.
2. Employee overrides supported.
3. Scope evaluation supported.
4. Manager hierarchy supported.
5. Effective permissions visible.
6. Permission changes audited.
7. Cross-company access prevented.
8. Reporting available.
9. Managers can access the complete subordinate hierarchy but not unrelated employees.
10. Company Administrator alone grants no HR, employee-record or salary access.
11. Salary access follows the company setting for self and managers, while HR access is unconditional.
