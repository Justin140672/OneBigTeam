# 01. Solution Structure

## Overview

This document defines the top-level solution structure for the HR platform.

The system uses a strict modular monolith architecture with vertical slice implementation inside each module.

The architecture is designed to support:

- Strong module boundaries
- AI-assisted development
- Compile-time dependency enforcement
- Fast feature delivery
- Future module extraction if required
- Low operational complexity

---

## Architecture Style

The platform shall use:

```text
Strict Modular Monolith
Vertical Slice Architecture
One project per module
FastEndpoints
Blazor Interactive Server
Supabase PostgreSQL
Supabase Storage
Supabase Auth
Hangfire
Aspire
```

---

## Solution Layout

Recommended top-level layout:

```text
/src
  HR.AppHost
  HR.ServiceDefaults

  HR.Web
  HR.Api
  HR.Admin.Web
  HR.Marketing

  /Modules
    HR.Modules.Companies
    HR.Modules.Identity
    HR.Modules.Employees
    HR.Modules.Leave
    HR.Modules.Recruitment
    HR.Modules.Tasks
    HR.Modules.Documents
    HR.Modules.Assets
    HR.Modules.CompanyOnboarding
    HR.Modules.DataImport
    HR.Modules.Notifications
    HR.Modules.Offboarding
    HR.Modules.Onboarding
    HR.Modules.Probation
    HR.Modules.Reporting
    HR.Modules.Sickness
    HR.Modules.Support

  /Infrastructure
    HR.Infrastructure
    HR.Infrastructure.Abstractions

  /Shared
    HR.SharedKernel

/tests
  HR.Architecture.Tests
  HR.Integration.Tests
  HR.Modules.*.Tests
  HR.SharedKernel.Tests
  HR.Web.Tests
  HR.Web.E2E.Tests
  HR.Marketing.Tests
```

---

## Project Responsibilities

### HR.AppHost

Aspire AppHost project.

Responsible for:

- Local orchestration
- Service wiring
- Development diagnostics
- Resource configuration
- Observability wiring

This project must not contain business logic.

---

### HR.ServiceDefaults

Aspire service defaults project.

Responsible for:

- OpenTelemetry defaults
- Health checks
- Resilience defaults
- Shared service configuration

This project must remain infrastructure-focused.

---

### HR.Web

Blazor Interactive Server application.

Responsible for:

- UI composition
- Routing
- Layouts
- Syncfusion wrappers
- User interaction
- Server-side UI state

This project may reference:

- HR.Api contracts where appropriate
- HR.SharedKernel
- Module contracts if required for UI models

It must not contain domain logic.

---

### HR.Api

FastEndpoints host.

Responsible for:

- Endpoint registration
- Authentication middleware
- Authorization middleware
- Request pipeline
- API error mapping
- Result-to-response mapping

This project may reference modules.

It must not directly implement business rules.

---

### HR.Modules.*

Each business module is a separate project.

Modules own:

- Domain model
- Vertical slices
- EF Core DbContext
- Module schema
- Module migrations
- Module events
- Module contracts
- Module-specific validation

Modules must not directly reference other modules.

---

### HR.Infrastructure

Cross-cutting infrastructure.

Responsible for:

- Supabase integration
- Postmark email
- Hangfire
- Background processing and any module-scoped outbox dispatchers
- Auditing
- Shared export infrastructure where required by the Reporting module
- Storage access
- Logging
- Observability
- External integrations

Infrastructure must not own business rules.

---

### HR.SharedKernel

Very small shared primitives project.

Allowed contents:

- Result
- Error
- Pagination models
- Clock abstraction
- Domain event interfaces
- Guard helpers
- Current user abstractions
- Tenant abstractions

Forbidden contents:

- Business logic
- Shared entities
- Shared repositories
- Cross-module services
- Module-specific enums

---

## Module List

The current module set is:

```text
Companies
Identity
Employees
Leave
Recruitment
Tasks
Documents
Assets
CompanyOnboarding
DataImport
Notifications
Offboarding
Onboarding
Probation
Reporting
Sickness
Support
```

Notifications and Reporting are business capabilities with explicit module ownership rather than miscellaneous Infrastructure concerns. Audit persistence remains a cross-cutting Infrastructure capability. Infrastructure also provides technical implementations such as storage, email, job scheduling and database integration, but does not own module business rules.

---

## Dependency Rules

### Allowed Dependencies

```text
HR.Web
  -> HR.SharedKernel
  -> module contracts where required

HR.Api
  -> HR.Modules.*
  -> HR.Infrastructure
  -> HR.SharedKernel

HR.Modules.*
  -> HR.SharedKernel

HR.Infrastructure
  -> HR.SharedKernel

Tests
  -> projects under test
```

---

### Forbidden Dependencies

Modules must not reference other modules directly.

Forbidden examples:

```text
HR.Modules.Leave -> HR.Modules.Employees
HR.Modules.Recruitment -> HR.Modules.Tasks
HR.Modules.Documents -> HR.Modules.Identity
```

Cross-module interaction must occur through:

- Integration events
- Contracts
- Local projections
- Infrastructure-mediated dispatch

---

## Module Internal Structure

Each module should follow this pattern:

```text
HR.Modules.Example
  /Features
    /CreateThing
      Endpoint.cs
      Request.cs
      Response.cs
      Validator.cs
      Handler.cs

  /Domain
    /Entities
    /Events
    /ValueObjects

  /Persistence
    ExampleDbContext.cs
    /Configurations
    /Migrations

  /Contracts
    /Events
    /Dtos

  ModuleRegistration.cs
```

---

## Vertical Slice Rules

Each feature slice owns:

- Request type
- Response type
- Validator
- Handler
- Endpoint
- Feature-specific mapping

A slice should not depend on generic services unless they represent a true cross-cutting concern.

Avoid:

- Giant services
- Generic repositories
- Shared managers
- Cross-module DbContext access

---

## Database Ownership

Each module owns:

- Its own DbContext
- Its own PostgreSQL schema
- Its own migrations
- Its own tables

Example:

```text
HR.Modules.Employees
  -> employees schema

HR.Modules.Leave
  -> leave schema

HR.Modules.Recruitment
  -> recruitment schema
```

No module may query another module's tables directly.

---

## API Placement

FastEndpoints endpoints live inside the module feature slice.

Example:

```text
HR.Modules.Leave
  /Features
    /SubmitLeaveRequest
      Endpoint.cs
```

The API host discovers/registers module endpoints.

Endpoint classes must remain thin.

Business orchestration belongs in handlers.

---

## Naming Conventions

### Projects

```text
HR.Modules.Employees
HR.Modules.Leave
HR.Infrastructure
HR.SharedKernel
```

### Namespaces

```text
HR.Modules.Employees.Features.CreateEmployee
HR.Modules.Leave.Domain
HR.Infrastructure.Email
```

### Database Schemas

Use lowercase plural names:

```text
companies
identity
employees
leave
recruitment
tasks
documents
```

### Tables

Use snake_case plural table names:

```text
employees.employees
leave.leave_requests
documents.documents
```

---

## Testing Projects

### HR.Architecture.Tests

Enforces:

- Module dependency rules
- No cross-module references
- Naming conventions
- Layering rules

### HR.Domain.Tests

Tests domain invariants and state transitions.

### HR.Integration.Tests

Tests FastEndpoints, EF Core, Supabase/Postgres behaviour and vertical slices.

### HR.UI.Tests

bUnit component tests.

### HR.Playwright.Tests

End-to-end user journey tests.

---

## AI Implementation Rules

AI-generated code must follow the solution structure.

AI must not:

- Create new shared projects without approval
- Add module-to-module references
- Put business logic in HR.Web
- Put business logic in HR.Infrastructure
- Create generic repositories
- Bypass authorization services
- Bypass tenant filtering

AI should:

- Add new features inside the correct module
- Follow the vertical slice pattern
- Add tests with every feature
- Use existing abstractions
- Keep SharedKernel minimal

---

## Anti-Patterns

Avoid:

```text
HR.Core
HR.Common
HR.Services
HR.Business
HR.Data
```

These become dumping grounds.

Avoid:

```text
EmployeeService
LeaveManager
DocumentHelper
```

unless the abstraction is genuinely useful and module-local.

Avoid shared entities across modules.

---

## Acceptance Criteria

1. Solution contains one project per business module.
2. Modules do not directly reference each other.
3. Each module owns its DbContext.
4. Each module owns its database schema.
5. SharedKernel remains minimal.
6. Infrastructure contains cross-cutting concerns only.
7. Web project contains UI only.
8. API project contains request pipeline and endpoint hosting only.
9. Architecture tests enforce dependency rules.
10. AI-generated code follows the defined structure.
