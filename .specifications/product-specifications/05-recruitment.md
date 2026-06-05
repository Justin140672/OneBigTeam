# Recruitment Engineering Specification

## Purpose

Manage candidate pipeline through conversion to employee.

---

## Candidate Lifecycle

Allowed transitions:

applied → screening → interview → offer → hired

Any stage → rejected

Invalid:
hired → screening

---

## Candidate Entity

| Field | Type | Required | Notes |
|---|---:|---:|---|
| id | UUID | Yes | PK |
| companyId | UUID | Yes | |
| firstName | VARCHAR | Yes | |
| lastName | VARCHAR | Yes | |
| email | VARCHAR | Yes | unique warning |
| phone | VARCHAR | No | |
| currentStage | ENUM | Yes | |
| source | VARCHAR | No | |
| appliedAt | TIMESTAMP | Yes | |

### Stage Enum

- applied
- screening
- interview
- offer
- hired
- rejected

---

## API Expectations

### Create Candidate

POST /api/v1/candidates

### Move Stage

PATCH /api/v1/candidates/{id}/stage

Validation:
- valid transitions only

---

## Workflow Automation

### Offer Accepted

Triggers:

1. employee_created
2. onboarding workflow
3. onboarding tasks
4. notifications
5. audit records

---

## Notifications

Examples:

- interview scheduled
- offer accepted
- onboarding started

---

## Audit Events

- candidate_created
- stage_changed
- offer_sent
- offer_accepted
- candidate_hired
