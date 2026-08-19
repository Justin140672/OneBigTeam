# 02. Module Boundaries

## Overview

This document defines the ownership, responsibilities, dependencies, and integration rules for each business module.

The primary goal is to prevent accidental coupling while maintaining a simple modular monolith architecture.

---

## Boundary Principles

Every module:

- Owns its own data
- Owns its own schema
- Owns its own business rules
- Owns its own API endpoints
- Owns its own events

Modules communicate through:

- Integration events
- Contracts
- Read models

Modules must not:

- Query another module's tables
- Reference another module directly
- Share entities

---

# Companies Module

## Responsibilities

Owns:

- Company profile
- Company addresses
- Company settings
- Company branding
- Tenant defaults

## Database Schema

```text
companies
```

## Key Entities

- Company
- CompanyAddress
- CompanySettings
- CompanyBranding

## Publishes Events

- CompanyCreated
- CompanySettingsUpdated
- CompanyBrandingUpdated

---

# Identity Module

## Responsibilities

Owns:

- Supabase user mapping
- User profiles
- Roles
- Permission evaluation
- Tenant resolution
- Current user context

## Database Schema

```text
identity
```

## Key Entities

- UserProfile
- RoleAssignment

## Publishes Events

- UserInvited
- UserDisabled
- RoleAssigned

---

# Employees Module

## Responsibilities

Owns:

- Employees
- Departments
- Position profiles
- Employment history
- Manager hierarchy

## Database Schema

```text
employees
```

## Key Entities

- Employee
- Department
- PositionProfile

## Publishes Events

- EmployeeCreated
- EmployeeUpdated
- EmployeeTerminated
- ManagerAssigned

---

# Leave Module

## Responsibilities

Owns:

- Leave requests
- Leave balances
- Leave policies
- Absence tracking

## Database Schema

```text
leave
```

## Key Entities

- LeaveRequest
- LeaveAllowance
- LeavePolicy

## Consumes Events

- EmployeeCreated
- EmployeeTerminated

## Publishes Events

- LeaveRequested
- LeaveApproved
- LeaveRejected

---

# Recruitment Module

## Responsibilities

Owns:

- Vacancies
- Candidates
- Interviews
- Offers
- Recruitment pipeline

## Database Schema

```text
recruitment
```

## Key Entities

- Vacancy
- Candidate
- Interview
- Offer

## Publishes Events

- CandidateCreated
- CandidateHired
- OfferAccepted

---

# Tasks Module

## Responsibilities

Owns:

- Tasks
- Checklists
- Reminders
- Escalations

## Database Schema

```text
tasks
```

## Key Entities

- Task
- Checklist

## Consumes Events

- EmployeeCreated
- CandidateHired
- LeaveRequested

---

# Documents Module

## Responsibilities

Owns:

- Document metadata
- Document categories
- Version history
- Expiry tracking

## Database Schema

```text
documents
```

## Key Entities

- Document
- DocumentVersion

## Consumes Events

- EmployeeCreated
- CandidateCreated

---

# Additional Current Modules

The repository also contains the following independently owned capabilities:

| Module | Primary ownership |
|---|---|
| Assets | Asset catalogue, assignments, acknowledgements and returns |
| CompanyOnboarding | Initial tenant setup checklist and exploration guidance |
| DataImport | Coordinated import of organisation structure and existing employees |
| Notifications | In-app notifications and notification delivery state |
| Onboarding | Employee onboarding plans and task progress |
| Offboarding | Employee leaving plans and task progress |
| Probation | Probation periods, reviews and outcomes |
| Reporting | Formal report catalogue, report queries, saved views, favourites and exports |
| Sickness | Sickness records, evidence and return-to-work reviews |
| Support | Customer support requests, responses and attachments |

These modules follow the same schema ownership, migration ownership and dependency rules as the original module set.

---

# Cross-Module Communication

## Allowed

- Integration events
- Explicit cross-module interfaces and DTOs owned by the providing capability
- Purpose-specific read models

Example:

```text
CandidateHired
    ->
Create Employee
    ->
Create Onboarding Tasks
```

## Not Allowed

References or calls to another module's implementation service:

```text
Leave -> EmployeesService
Recruitment -> DocumentsService
```

Direct table access:

```text
leave.leave_requests
    joins
employees.employees
```

---

# Read Model Strategy

Modules may maintain local projections.

Example:

Leave module stores:

```text
EmployeeId
EmployeeName
ManagerId
```

for reporting and display purposes.

The source of truth remains the Employees module.

---

# Ownership Matrix

| Capability | Owner Module |
|------------|--------------|
| Company Profile | Companies |
| Branding | Companies |
| Settings | Companies |
| Authentication | Identity |
| Permissions | Identity |
| Employees | Employees |
| Departments | Employees |
| Positions | Employees |
| Leave | Leave |
| Recruitment | Recruitment |
| Tasks | Tasks |
| Documents | Documents |
| Assets | Assets |
| Initial Company Setup | CompanyOnboarding |
| Data Import | DataImport |
| Notifications | Notifications |
| Employee Onboarding | Onboarding |
| Employee Offboarding | Offboarding |
| Probation | Probation |
| Formal Reporting | Reporting |
| Sickness | Sickness |
| Customer Support | Support |

---

# Acceptance Criteria

1. Every business capability has a single owner.
2. A module does not reference another module's implementation project.
3. Cross-module communication uses integration events or explicit contracts owned by the providing capability.
4. Each module owns its schema.
5. Each module owns its migrations.
6. Read models are local projections or purpose-specific contract results; they never expose another module's entities or DbContext.
7. Architecture tests enforce boundaries.
