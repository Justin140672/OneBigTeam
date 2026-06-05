# 17. Compensation Management

## Overview

The Compensation Management capability records employee salary and compensation history.

It is not a payroll engine.

Compensation data is highly sensitive and requires strict permission controls and audit redaction.

---

## Business Objectives

The system shall:

- Store salary records
- Maintain compensation history
- Track effective dates
- Support compensation changes
- Restrict access
- Audit changes with redaction

---

## Compensation Record

| Field | Required |
|---|---|
| Employee | Yes |
| Salary Amount | Yes |
| Currency | Yes |
| Effective Date | Yes |
| Reason | Optional |
| Created By | Yes |

---

## Compensation History

The system must preserve historical compensation records.

Changes should create new effective-dated records rather than overwriting history.

---

## Sensitive Data

Compensation data is sensitive.

Do not expose compensation data to:

- Employees unless explicitly allowed
- Managers unless permitted by role/scope
- Recruiters by default

---

## Manager Visibility

Managers may view compensation for reporting chain only if granted the relevant permission.

This should remain separate from payroll-sensitive data.

---

## Payroll Sensitive Data

Bank details, NI numbers and tax references are not compensation.

They must be modelled and permissioned separately if added later.

---

## Permissions

### HR Admin

Can manage compensation.

### Finance

Can view compensation where assigned.

### Manager

Can view reporting chain compensation only if permitted.

### Employee

No default access to compensation history.

---

## Audit Events

Audit:

- Compensation record created
- Compensation changed
- Effective date changed
- Compensation viewed where required

Audit payloads must redact salary values unless policy explicitly permits value capture.

---

## Reporting

Reports:

- Compensation history
- Salary change report
- Effective-date report

Sensitive reports require elevated permissions and audit entries.

---

## Acceptance Criteria

1. Compensation records can be created.
2. Compensation history is preserved.
3. Effective dates are supported.
4. Permissions restrict access.
5. Sensitive values are redacted in audit.
6. Reporting is supported.
7. Compensation is separate from payroll-sensitive data.
