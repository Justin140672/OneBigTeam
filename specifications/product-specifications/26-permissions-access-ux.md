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

User may access direct reports.

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

Permissions apply only to employees within the hierarchy.

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
