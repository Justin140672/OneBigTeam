# Recruitment Module Specification

## Overview

The Recruitment module manages the complete hiring lifecycle from vacancy creation through candidate onboarding.

## Business Objectives

- Allow hiring managers to create vacancies
- Track candidates through configurable recruitment stages
- Schedule interviews
- Manage interview feedback
- Issue offers
- Convert successful candidates into employees
- Provide recruitment reporting
- Maintain a complete audit trail

## Candidate Pipeline

1. Applied
2. Screening
3. First Interview
4. Second Interview
5. Offer
6. Hired
7. Rejected

## Vacancy Employment Type

Every new vacancy must carry an Employment Type that is active and belongs to the same company; it is
validated on create and update, and a vacancy cannot be opened (published) without one. Vacancies created
before this rule have no Employment Type and are not backfilled: the UI shows "Employment type required"
and the vacancy cannot be opened or used to hire until one is set.

## External Hire: Source of Each Field

Hiring an external candidate creates the Employee. The Hire request is limited to what the vacancy and
offer cannot supply; everything else is derived server-side.

| Employee field | Source |
|---|---|
| First name, last name, work email, phone | Candidate record |
| Position profile, department, location | Vacancy's position profile (never client-supplied) |
| Employment type | Vacancy's employment type (never client-supplied; a client-sent value is ignored). Never silently defaulted. |
| Start date | Hire request, otherwise the accepted offer's proposed start date |
| Salary and frequency (first compensation record) | Accepted offer |
| Manager | Vacancy's hiring manager by default. The recruiter may pick another manager or "No manager"; this is sent as an explicit override so that "no override" and "No manager" are distinguishable. The final manager must be a non-former employee of the same company or the hire is rejected. |
| Date of birth, gender, employee number (manual mode) | Entered in the Hire dialog |
| Nationality | Always entered explicitly. It is never preselected, defaulted to British or inferred. |
| Address line 1, city, postcode | Entered in the Hire dialog and mandatory (whitespace-only is rejected; values are trimmed). Postcode must match the company's configured postcode rule. |
| Address line 2, county/state | Optional |

Add Employee requires the same three address fields. Existing employees with incomplete addresses are not
modified; editing an existing employee does not newly require them. The initial company administrator
created during sign-up and employees brought in by data import are exempt. Internal appointments never
create an employee: they preserve the employee's own employment type and trigger no onboarding.

## Acceptance Criteria

1. Recruiter can create vacancy.
2. Candidate can progress through defined stages.
3. Invalid stage transitions prevented.
4. Interview scheduling supported.
5. Offer workflow supported.
6. Successful hire creates onboarding workflow.
7. All stage transitions audited.
8. Reporting available.
9. Search available.
10. Permissions enforced.
