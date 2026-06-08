# 05. Database Standards

## Overview

This document defines database standards for the HR platform.

The platform uses:

- PostgreSQL (Supabase)
- EF Core
- Schema-per-module
- One DbContext per module
- Shared database
- Shared schema set
- Strict company isolation

The goal is consistency, maintainability, and prevention of accidental cross-module coupling.

---

## Core Principles

Database design shall:

- Support modular ownership
- Support tenant isolation
- Support auditing
- Support reporting
- Support future growth

Database design shall not:

- Encourage cross-module joins
- Create shared ownership
- Depend on generic repositories

---

## Database Platform

Primary database:

PostgreSQL (Supabase)

Primary ORM:

Entity Framework Core

Database access occurs through module-owned DbContexts.

---

## Multi-Tenancy Strategy

Model:

```text
Shared Database
Shared Instance
Shared Schemas
Company Isolation
```

Isolation is enforced through:

```text
company_id
```

---

## Company Ownership Rules

Every tenant-owned table must include:

```sql
company_id UUID NOT NULL
```

Examples:

- employees
- leave_requests
- tasks
- vacancies
- documents

---

## Exceptions

Global/system tables may omit company_id.

Examples:

- countries
- system_permissions
- lookup tables

These must remain read-only.

---

## Schema Strategy

Each module owns a schema.

Examples:

```text
companies
identity
employees
leave
recruitment
tasks
documents
```

---

## DbContext Strategy

Each module owns:

- DbContext
- Configurations
- Migrations

Examples:

```text
CompaniesDbContext
EmployeesDbContext
LeaveDbContext
RecruitmentDbContext
```

No shared DbContext.

---

## Table Naming

Use:

snake_case

Examples:

```text
employees
leave_requests
position_profiles
company_addresses
```

Avoid:

```text
Employee
LeaveRequest
PositionProfile
```

---

## Column Naming

Use:

snake_case

Examples:

```text
company_id
employee_id
created_at
updated_at
```

---

## Primary Keys

Standard:

```text
UUID
```

Examples:

```sql
id UUID PRIMARY KEY
```

Do not use integer identity keys.

---

## Foreign Keys

Use UUID foreign keys.

Example:

```sql
employee_id UUID NOT NULL
```

---

## Auditing Columns

All business entities should include:

```text
created_at
created_by
updated_at
updated_by
```

Where appropriate.

---

## Soft Delete

Preferred strategy:

Soft delete

Columns:

```text
deleted_at
deleted_by
```

Optional:

```text
delete_reason
```

---

## Query Filters

Global query filters should enforce:

```text
company_id
```

Example:

Current tenant context automatically applied.

Purpose:

- Prevent tenant leaks
- Reduce developer mistakes
- Improve AI-generated code safety

---

## Indexing Standards

Indexes required for:

```text
company_id
employee_id
status
created_at
```

Composite indexes encouraged for:

```text
company_id + status
company_id + employee_id
```

---

## Constraints

Use database constraints where practical.

Examples:

- unique indexes
- foreign keys
- check constraints

Do not rely solely on application validation.

---

## Migrations

Each module owns its migrations.

Examples:

```text
Employees.Migrations
Leave.Migrations
Recruitment.Migrations
```

---

## Migration Execution

Development:

Automatic migration allowed.

Production:

CI/CD controlled migration execution.

Avoid automatic production migrations.

---

## Reporting Considerations

Modules may maintain reporting projections.

Reporting tables remain owned by the consuming module.

Avoid cross-schema reporting joins where possible.

---

## EF Core Standards

Use:

- Fluent configuration
- Explicit mappings
- Explicit indexes

Avoid:

- Data annotations for complex mappings
- Hidden conventions

---

## Repository Strategy

Do not use generic repositories.

Preferred:

```text
DbContext directly
```

Inside vertical slice handlers.

---

## Transactions

Use transactions when:

- Multiple aggregates updated
- Outbox records written

Transactions should remain short-lived.

---

## Outbox Persistence

Outbox records stored in PostgreSQL.

Outbox write occurs within the same transaction as business changes.

---

## Security

Database access must:

- Respect company isolation
- Respect permissions
- Avoid exposing sensitive data

---

## Sensitive Data

Sensitive values include:

- Salary
- Bank details
- National Insurance numbers

Sensitive values:

- Must not appear in logs
- Must not appear in audit payloads

---

## Testing Standards

Database tests should validate:

- Company isolation
- Query filters
- Migrations
- Constraints
- Index behaviour

---

## Anti-Patterns

Avoid:

- Shared schemas
- Cross-module joins
- Generic repositories
- Shared DbContexts
- Integer keys
- Direct module table access

---

## Acceptance Criteria

1. Each module owns its schema.
2. Each module owns its DbContext.
3. Tenant-owned tables include company_id.
4. UUID keys are used.
5. Global query filters enforce tenant isolation.
6. Soft delete strategy is consistent.
7. Module migrations are independent.
8. Generic repositories are not used.
9. Constraints and indexes are explicitly defined.
10. Architecture tests enforce standards.
