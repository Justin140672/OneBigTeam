# 14. Leave Management

## Overview

The Leave Management capability manages employee leave requests, balances, approvals, public holidays, carry-over, accrual and TOIL.

Leave is policy-driven and working-schedule aware.

Leave is distinct from the Sickness module (see `specifications/architecture/02-module-boundaries.md`). Sickness absence — sickness records, evidence and return-to-work reviews — is owned entirely by the Sickness module and is never modelled as a Leave type. There is no "Sick Leave" leave type; see the current-decisions register for the superseded behaviour.

---

## Business Objectives

The system shall:

- Allow employees to request leave
- Allow managers to approve/reject leave
- Track leave balances
- Support partial-day leave
- Support public holidays
- Support carry-over rules
- Support configurable accrual
- Support TOIL
- Audit all leave actions

---

## Leave Types

Default leave types provisioned for every company:

- Annual leave
- Unpaid leave
- Compassionate leave
- Parental leave
- TOIL

Sick leave is deliberately not a Leave type — sickness absence is tracked exclusively by the separate Sickness module.

"Other" is not provisioned by default. Leave types are fully configurable per company (create/update/deactivate), so a company that needs a general/miscellaneous type can add one.

---

## Leave Request Lifecycle

### Draft

Created but not submitted.

### Submitted

Awaiting approval.

### Approved

Approved by authorised approver.

### Rejected

Rejected by approver.

### Cancelled

Cancelled by requester or HR.

---

## Leave Policy

A Leave Policy defines:

- Annual entitlement
- Accrual method
- Carry-over rules
- Public holiday behaviour
- Approval requirements
- TOIL behaviour

---

## Accrual Methods

Supported:

- Annual upfront
- Monthly accrual
- Pro-rated joiners/leavers

---

## TOIL

TOIL is tracked as a first-class balance using ledger-style transactions.

Transaction types:

- Earned
- Used
- Expired
- Adjusted

---

## Approval Workflow

Leave approval is task-based.

Flow:

1. Employee submits request.
2. Manager approval task created.
3. Manager approves or rejects.
4. Employee notified.
5. Leave balance updated.
6. Audit event created.

---

## Conflict Warnings

Implemented, and returned consistently by preview (`PreviewLeaveRequest`) and both submission paths (`SubmitLeaveRequest`, `SubmitLeaveRequestDraft`) via the shared `LeaveWarningCalculator` and conflict queries:

- Public holiday conflicts (a public holiday falls inside the requested range, when the company excludes public holidays from leave)
- Existing leave overlap (another non-rejected/non-cancelled request for the employee overlaps the requested range)

These warnings never block submission.

Deferred — explicitly not implemented in MVP, not surfaced by any handler:

- Team clashes (concurrent leave across a team/department)
- Excessive absence

They may be introduced later for a demonstrated requirement. Balance sufficiency and cross-policy-year span are the only checks that block submission, and both are already enforced.

---

## Permissions

### Employee

Can request own leave.

### Manager

Can approve leave for reporting chain.

### HR Admin

Can manage leave across company.

---

## Audit Events

Audit, each carrying company, employee (where applicable), actor and meaningful before/after data:

- Leave requested
- Leave approved
- Leave rejected
- Leave cancelled
- Balance adjusted
- TOIL awarded
- TOIL expired
- Leave policy created
- Leave policy updated
- Leave policy assigned to an employee
- Leave type created
- Leave type updated
- Leave type deactivated

---

## Acceptance Criteria

1. Leave requests can be submitted.
2. Manager approval is supported.
3. Leave balances are maintained.
4. Partial-day leave is supported.
5. Public holidays are supported.
6. Carry-over rules are supported.
7. TOIL is supported.
8. Leave actions are audited.
9. Permissions are enforced.
