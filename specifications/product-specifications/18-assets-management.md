# 18. Assets Management

## Overview

The Assets Management capability tracks company assets assigned to employees.

Assets are linked to onboarding, offboarding and operational workflows.

---

## Business Objectives

The system shall:

- Record company assets
- Assign assets to employees
- Track asset returns
- Support onboarding/offboarding workflows
- Maintain audit history
- Report on assigned assets

---

## Asset Types

Examples:

- Laptop
- Monitor
- Phone
- Access card
- Vehicle
- Uniform
- Equipment

Asset types should be configurable.

---

## Asset Fields

| Field | Required |
|---|---|
| Asset Tag | Yes |
| Asset Type | Yes |
| Description | Optional |
| Serial Number | Optional |
| Status | Yes |
| Assigned Employee | Optional |
| Assigned Date | Optional |
| Returned Date | Optional |

---

## Asset Statuses

Supported statuses:

- Available
- Assigned
- Returned
- Lost
- Damaged
- Retired

---

## Assignment Workflow

1. Asset is available.
2. HR/Admin assigns asset to employee.
3. Assignment is audited.
4. Employee record shows assigned asset.

---

## Return Workflow

1. Asset return task created.
2. Asset returned by employee.
3. Condition recorded.
4. Status updated.
5. Audit event created.

---

## Onboarding Integration

Candidate hired / employee created may trigger:

- Laptop assignment task
- Access card task
- Equipment preparation task

---

## Offboarding Integration

Employee termination may trigger:

- Asset return task
- Equipment review
- Lost asset escalation

---

## Permissions

### Employee

Can view own assigned assets.

### Manager

Can view team assigned assets where permitted.

### HR Admin

Can manage asset assignments.

### Company Admin

Can manage asset catalogue.

---

## Audit Events

Audit:

- Asset created
- Asset assigned
- Asset returned
- Asset marked lost
- Asset retired

---

## Reporting

Reports:

- Assigned assets
- Overdue returns
- Lost assets
- Assets by employee
- Assets by department

---

## Acceptance Criteria

1. Assets can be created.
2. Assets can be assigned.
3. Assets can be returned.
4. Asset status is tracked.
5. Onboarding tasks can include assets.
6. Offboarding tasks can include asset returns.
7. Asset reports are available.
8. Asset changes are audited.
