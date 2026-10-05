# 17. Compensation Management

## Overview

The Compensation Management capability records employee salary and compensation history.

It does not calculate pay, tax, deductions or statutory submissions.

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

## Compensation on Add Employee

Salary information is mandatory when an employee is added through the Add Employee form or the create-employee API.

- The request must include a salary greater than zero, a salary type (Annual, Hourly or Daily) and a 3-letter currency code.
- The employee's first compensation record is created in the same transaction as the employee, effective from the employee's start date, with the reason New Hire.
- The creating user must hold `employee:manage`, the same permission required to add compensation separately; no additional permission is needed.
- The compensation creation is audited (`employee.compensation.created`) with the same redaction as any other compensation record.
- Automated paths that do not use the Add Employee form keep their own behaviour: Hire Candidate seeds compensation from the accepted offer, company sign-up seeds a zero-value placeholder for the initial administrator, and data import writes its own opening record.

---

## Compensation History

The system must preserve historical compensation records.

Changes should create new effective-dated records rather than overwriting history.

---

## Sensitive Data

Compensation data is sensitive.

Compensation visibility is:

- Always available to HR Administrators within their company
- Available to an employee for their own salary when `DisplaySalaryOnEmployeeProfile` is enabled
- Available to managers for employees beneath them in the complete reporting hierarchy when `DisplaySalaryOnEmployeeProfile` is enabled
- Not granted by Recruiter or Company Administrator alone

---

## Manager Visibility

Managers may view compensation for their complete subordinate hierarchy only when `DisplaySalaryOnEmployeeProfile` is enabled.

This should remain separate from bank, tax and payment-sensitive data.

---

## Payment-Sensitive Data

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

Can view their own salary when `DisplaySalaryOnEmployeeProfile` is enabled. Editing and administrative history operations remain restricted.

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
7. Compensation is separate from bank, tax and payment-sensitive data.
