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

## Field-level access matrix (GetEmployee)

A manager's hierarchy access to "the detailed employee record" above is **not** the same
response shape HR receives — reconciles the ambiguity previously between this document and
`30-administrative-role-separation-matrix.md` (which listed manager access as "Self", meaning
"no more than self-service" rather than describing hierarchy scope at all). GetEmployee returns
one of two contracts depending on the caller's resolved scope:

| Field group | Employee (self) | Manager (hierarchy) | HR Administrator |
|---|---|---|---|
| Identity, name, work email | Y | Y | Y |
| Department / location / position / employment type | Y | Y | Y |
| Manager / reports / reporting chain | Y | Y | Y |
| Start date, employment status, employee number | Y | Y | Y |
| Lifecycle tab flags (onboarding/probation/offboarding/leaving) | Y | Y | Y |
| Personal email, phone numbers, home address | Y | — | Y |
| Date of birth, nationality, gender | Y | — | Y |
| System-access state, working-pattern overrides | Y | — | Y |
| Leaving-process dates, notice period (raw + effective) | Y | — | Y |
| HR notes | Y | — | Y |
| Optimistic-concurrency version token | Y | — | Y |

Implemented by `HR.Modules.Employees.Features.GetEmployee.GetEmployeeHandler`: the manager path
returns `GetEmployeeManagerResponse`, a distinct, narrower response contract with no property for
any row marked "—" above — the full record is never fetched-and-hidden client-side. Covered by
`GetEmployeeHandlerTests.HandleAsync_Manager_Response_Contains_Only_Operational_Fields` (unit) and
`GetEmployeeResourceAuthorizationTests.Manager_Response_Contains_Only_Operational_Fields_Over_The_Wire`
(integration, asserts the actual JSON payload).

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
