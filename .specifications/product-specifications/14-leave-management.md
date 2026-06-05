# 14. Leave Management

## Overview

The Leave Management capability manages employee leave requests, balances, approvals, public holidays, carry-over, accrual and TOIL.

Leave is policy-driven and working-schedule aware.

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

Default leave types:

- Annual leave
- Sick leave
- Unpaid leave
- Compassionate leave
- Parental leave
- TOIL
- Other

Leave types should be configurable.

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

The system may warn about:

- Team clashes
- Excessive absence
- Public holiday conflicts
- Existing leave overlap

Warnings should not automatically block submission unless policy requires it.

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

Audit:

- Leave requested
- Leave approved
- Leave rejected
- Leave cancelled
- Balance adjusted
- TOIL adjusted

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
