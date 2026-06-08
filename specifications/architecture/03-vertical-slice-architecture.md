# 03. Vertical Slice Architecture

## Overview

The platform uses Vertical Slice Architecture within each module.

Each feature owns:

- Endpoint
- Request
- Response
- Validator
- Handler
- Tests

Features are implemented independently and organised around business capabilities rather than technical layers.

This architecture aligns naturally with FastEndpoints and reduces coupling.

---

## Core Principles

Organise code by:

```text
feature
```

not by:

```text
controllers
services
repositories
```

Each feature should be independently understandable.

A developer should be able to navigate directly to a feature and understand:

- Input
- Validation
- Business logic
- Output
- Tests

without searching the entire solution.

---

## Feature Structure

Example:

```text
CreateEmployee

  Endpoint.cs
  Request.cs
  Response.cs
  Validator.cs
  Handler.cs
  Tests.cs
```

Everything required for the feature lives together.

---

## Example Module Structure

```text
HR.Modules.Employees

  /Features

    /CreateEmployee
      Endpoint.cs
      Request.cs
      Response.cs
      Validator.cs
      Handler.cs

    /UpdateEmployee
      Endpoint.cs
      Request.cs
      Response.cs
      Validator.cs
      Handler.cs

    /GetEmployee
      Endpoint.cs
      Response.cs
      Handler.cs

  /Domain

  /Persistence

  /Contracts
```

---

## Endpoint Responsibilities

FastEndpoints endpoints should remain thin.

Responsible for:

- Route configuration
- Authentication
- Authorization
- Calling handler
- Returning response

Not responsible for:

- Business rules
- Complex orchestration
- Data access

---

## Request Objects

Request objects define:

- Input payload
- Route values
- Query parameters

Example:

```text
CreateEmployeeRequest
```

Request types should be small and focused.

---

## Response Objects

Response objects define API contracts.

Example:

```text
CreateEmployeeResponse
```

Responses should not expose entities directly.

Always return dedicated response models.

---

## Validation

Validation uses FluentValidation.

Each feature owns its validator.

Example:

```text
CreateEmployeeValidator
```

Validation should occur before business logic executes.

Validation responsibilities:

- Required fields
- Length checks
- Format checks
- Basic business validation

---

## Handler Responsibilities

Handlers contain:

- Business orchestration
- Domain interaction
- Persistence coordination
- Event publication

Handlers should:

- Be focused
- Be testable
- Be feature-specific

Avoid giant handlers.

---

## Result Pattern

Use Result pattern from SharedKernel.

Example outcomes:

- Success
- Validation failure
- Not found
- Unauthorized
- Conflict

Avoid exception-driven business flow.

---

## Domain Rules

Business invariants belong in:

- Domain entities
- Value objects

Avoid placing domain rules inside endpoints.

---

## Persistence Access

Handlers may use:

```text
Module DbContext
```

Example:

```text
EmployeesDbContext
```

Do not use generic repositories.

Direct EF Core access is preferred.

---

## Event Publishing

Handlers publish:

- Domain events
- Integration events

Examples:

```text
EmployeeCreated
LeaveApproved
CandidateHired
```

Cross-module communication occurs through integration events only.

---

## Mapping Rules

Mapping should be simple.

Preferred:

```text
explicit mapping
```

Avoid large mapping frameworks unless justified.

Mapping belongs inside feature implementation.

---

## Testing Strategy

Each feature should have:

### Unit Tests

Validation
Domain rules
Handler logic

### Integration Tests

Endpoint
Database
Authorization

---

## Naming Conventions

Feature names should use business language.

Good:

```text
CreateEmployee
ApproveLeave
HireCandidate
```

Bad:

```text
EmployeeService
EmployeeManager
ProcessHandler
```

---

## Authorization

Authorization should be declared at the endpoint level.

Business decisions remain inside handlers.

---

## Anti-Patterns

Avoid:

```text
Services folder
Managers folder
Utilities folder
Generic repositories
Shared business services
```

Avoid creating:

```text
EmployeeService
LeaveService
DocumentService
```

unless a truly shared abstraction exists.

---

## AI Code Generation Rules

AI-generated features must:

- Follow feature structure
- Include validator
- Include tests
- Use module DbContext
- Use Result pattern
- Respect tenant boundaries

AI must not:

- Add generic repositories
- Add module references
- Put business logic in endpoints
- Bypass authorization

---

## Example Feature Lifecycle

```text
Request
    ->
Validation
    ->
Handler
    ->
Domain Rules
    ->
Persistence
    ->
Events
    ->
Response
```

---

## Acceptance Criteria

1. Features are organised by business capability.
2. Endpoints remain thin.
3. Validators are feature-specific.
4. Handlers contain orchestration logic.
5. EF Core used directly.
6. Generic repositories are not used.
7. Integration events used for module communication.
8. Tests exist for every feature.
9. AI-generated code follows feature conventions.
