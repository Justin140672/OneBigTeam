# 15. Sickness Management

## Overview

The Sickness Management capability records sickness absence, fit note requirements, return-to-work checks and attendance alerts.

The system is designed to support HR processes without becoming an occupational health system.

---

## Business Objectives

The system shall:

- Record sickness absence
- Support partial-day sickness
- Categorise sickness at a high level
- Trigger fit note requirements
- Trigger return-to-work tasks
- Detect attendance patterns
- Audit sickness activity
- Protect sensitive health information

---

## Sickness Categories

Supported categories:

- Illness
- Injury
- Mental health
- Medical appointment
- Dependant care
- Other

Categories should remain broad and non-diagnostic.

---

## Sickness Record Fields

| Field | Required |
|---|---|
| Employee | Yes |
| Start Date/Time | Yes |
| End Date/Time | Optional |
| Category | Yes |
| Notes | Optional |
| Status | Yes |

---

## Fit Notes

Fit note support is triggered after a configurable threshold.

Default:

- 7 calendar days

The system should create a task for evidence upload/review.

---

## Return-to-Work

Return-to-work checks are triggered after a configurable threshold.

Default:

- 3 working days

Return-to-work checks capture:

- Fit to return
- Adjustments needed
- Manager notes

---

## Attendance Alerts

The system may generate soft warnings for:

- Frequent absences
- Monday/Friday patterns
- Long absence duration
- Missing return-to-work checks

Alerts do not trigger automatic disciplinary action.

---

## Permissions

### Employee

Can view own sickness records where appropriate.

### Manager

Can view limited sickness information for reporting chain.

### HR Admin

Can manage sickness records.

---

## Sensitive Data Handling

Medical details should be minimised.

Detailed diagnoses should not be required.

Sensitive health data should be redacted in logs and audit payloads.

---

## Audit Events

Audit:

- Sickness recorded
- Sickness updated
- Fit note requested
- Evidence uploaded
- Return-to-work completed
- Attendance alert generated

---

## Acceptance Criteria

1. Sickness can be recorded.
2. Partial-day sickness is supported.
3. Broad categories are supported.
4. Fit note tasks are generated.
5. Return-to-work tasks are generated.
6. Attendance alerts are generated.
7. Sensitive data is protected.
8. Permissions are enforced.
9. Sickness activity is audited.
