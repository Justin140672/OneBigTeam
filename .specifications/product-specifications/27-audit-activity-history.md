# 27. Audit & Activity History

## Overview

The Audit subsystem provides immutable, searchable, company-scoped activity history across the HR platform.

Auditing is implemented as a cross-cutting infrastructure concern and applies consistently across all modules.

The audit system supports:

- Compliance
- Security investigations
- Operational troubleshooting
- Change history
- Regulatory requirements

---

## Business Objectives

The audit subsystem shall:

- Record significant business actions
- Record security-sensitive events
- Maintain immutable history
- Support filtering and search
- Support compliance reporting
- Protect sensitive information through redaction

---

# Audit Principles

Audit records are:

- Append only
- Immutable
- Tenant isolated
- Permission controlled

Audit records must never be edited.

Audit records must never be hard deleted.

---

# Audited Events

## Employee Events

Examples:

- Employee created
- Employee updated
- Employee terminated
- Manager changed
- Position changed

---

## Leave Events

Examples:

- Leave requested
- Leave approved
- Leave rejected
- Leave cancelled

---

## Recruitment Events

Examples:

- Vacancy created
- Candidate stage changed
- Interview completed
- Offer accepted

---

## Document Events

Examples:

- Document uploaded
- Document viewed
- Document downloaded
- Document archived
- Document restored

---

## Permission Events

Examples:

- Role assigned
- Role removed
- Override added
- Override removed

---

## Reporting Events

Examples:

- Report requested
- Report generated
- Report downloaded

---

## Authentication Events

Examples:

- Login successful
- Login failed
- User invited
- User disabled

---

# Audit Record Model

| Field | Required |
|---------|---------|
| Id | Yes |
| CompanyId | Yes |
| EmployeeId | Optional |
| EventType | Yes |
| EntityType | Yes |
| EntityId | Yes |
| Timestamp | Yes |
| PerformedBy | Yes |
| Metadata | Optional |

---

# Activity History

Every major entity should expose activity history.

Examples:

### Employee

Display:

- Created
- Updated
- Manager changes
- Position changes

### Leave Request

Display:

- Requested
- Approved
- Rejected
- Cancelled

### Candidate

Display:

- Stage changes
- Interview events
- Offer events

---

# Sensitive Data Redaction

Sensitive values must not be stored directly.

Examples:

- Salary amounts
- National Insurance numbers
- Bank details
- Authentication tokens
- Medical information

Audit records should store:

- Change occurred
- Who changed it
- When

without exposing protected values.

---

# Search

Audit search supports:

- Employee
- Entity
- Event type
- Date range
- User

---

# Permissions

## Employee

Can view limited personal activity.

## Manager

Can view activity for authorised employees.

## HR Admin

Can view HR audit history.

## Company Admin

Can view company-wide audit history.

---

# Audit Dashboard

Provide:

- Recent activity
- Security events
- Failed actions
- Permission changes

---

# Reporting

Reports:

- Security audit report
- Employee change report
- Permission change report
- Document access report

---

# Retention

Audit records retained indefinitely unless legal requirements dictate otherwise.

Audit records are not subject to standard soft-delete rules.

---

# Validation Rules

- Audit events must contain timestamps.
- Audit events must contain actor information.
- Audit events must contain entity references.
- Sensitive data must be redacted.

---

# Acceptance Criteria

1. Major business actions are audited.
2. Audit records are immutable.
3. Sensitive values are redacted.
4. Audit history is searchable.
5. Activity history is available on entities.
6. Permissions control audit access.
7. Audit reporting is supported.
8. Company isolation is enforced.
