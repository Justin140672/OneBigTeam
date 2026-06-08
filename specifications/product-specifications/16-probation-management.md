# 16. Probation Management

## Overview

The Probation Management capability tracks employee probation periods, check-ins, reminders, extensions and outcomes.

Probation is implemented using employee data, tasks, notifications and audit events rather than a separate complex workflow engine.

---

## Business Objectives

The system shall:

- Track probation periods
- Generate probation review tasks
- Notify managers and HR
- Record probation outcomes
- Support probation extensions
- Maintain audit history

---

## Probation Lifecycle

### Not Started

Employee has not yet started or probation not applicable.

### Active

Probation is currently running.

### Review Due

A review is required.

### Extended

Probation has been extended.

### Passed

Employee passed probation.

### Failed

Employee did not pass probation.

---

## Probation Fields

| Field | Required |
|---|---|
| Employee | Yes |
| Start Date | Yes |
| Expected End Date | Yes |
| Status | Yes |
| Manager | Yes |
| Outcome | Optional |
| Notes | Optional |

---

## Default Schedule

Default review checkpoints:

- 30 days
- 60 days
- 90 days

Company settings may adjust this.

---

## Tasks

Probation generates tasks such as:

- Manager check-in
- HR review
- Final probation decision
- Extension confirmation

---

## Notifications

Notify:

- Manager
- HR Admin
- Employee where appropriate

Examples:

- Probation review due
- Probation outcome recorded
- Probation extended

---

## Outcomes

Supported outcomes:

- Passed
- Extended
- Failed

Outcome should include:

- Decision date
- Decision maker
- Notes
- New end date if extended

---

## Permissions

### Manager

Can complete probation review tasks for reports.

### HR Admin

Can manage probation records.

### Employee

May view own probation status where company policy allows.

---

## Audit Events

Audit:

- Probation started
- Review task created
- Review completed
- Probation extended
- Probation passed
- Probation failed

---

## Acceptance Criteria

1. Probation periods are tracked.
2. Review tasks are generated.
3. Reminders are generated.
4. Outcomes are recorded.
5. Extensions are supported.
6. Probation changes are audited.
7. Permissions are enforced.
