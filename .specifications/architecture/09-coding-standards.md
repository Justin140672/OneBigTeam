# 09. Coding Standards

## Overview

This document defines coding standards for the HR platform.

The purpose is to ensure:

- Consistency
- Readability
- Maintainability
- Testability
- AI-assisted development compatibility

These standards apply to all production code.

---

## Core Principles

Code should be:

- Simple
- Explicit
- Testable
- Consistent
- Discoverable

Prefer clarity over cleverness.

---

# C# Standards

## Language Version

Use the latest stable .NET and C# version supported by the platform.

---

## Nullable Reference Types

Enabled.

Required for all projects.

Example:

```csharp
string? middleName;
```

---

## Implicit Usings

Enabled.

---

## File Scoped Namespaces

Preferred.

Example:

```csharp
namespace HR.Modules.Employees.Features.CreateEmployee;
```

---

# Naming Conventions

## Classes

PascalCase

Examples:

```text
Employee
LeaveRequest
CreateEmployeeHandler
```

---

## Methods

PascalCase

Examples:

```text
CreateEmployee
ApproveLeave
GenerateReport
```

---

## Fields

Private fields:

```text
_currentUser
_dbContext
_logger
```

---

## Interfaces

Prefix:

```text
I
```

Examples:

```text
ICurrentUser
IClock
IEmailSender
```

---

## Database

Tables:

```text
snake_case
```

Columns:

```text
snake_case
```

Schemas:

```text
lowercase
```

---

# Vertical Slice Standards

Every feature contains:

```text
Endpoint.cs
Request.cs
Response.cs
Validator.cs
Handler.cs
```

Optional:

```text
Mapper.cs
Tests.cs
```

---

## Endpoint Standards

Endpoints should:

- Be thin
- Validate permissions
- Call handlers
- Return responses

Endpoints should not:

- Contain business logic
- Perform data access

---

## Handler Standards

Handlers should:

- Orchestrate business actions
- Remain focused
- Be testable

Handlers should not exceed reasonable complexity.

Split large workflows.

---

## Validation Standards

Use:

```text
FluentValidation
```

Every write operation should have validation.

---

## EF Core Standards

Use:

- Fluent configuration
- Explicit indexes
- Explicit relationships

Avoid:

- Generic repositories
- Hidden conventions

---

## Query Standards

Prefer:

```text
AsNoTracking()
```

for read-only queries.

Only track entities when required.

---

## Result Pattern

Business operations return:

```text
Result
Result<T>
```

Avoid exception-driven business flow.

Use exceptions for exceptional failures only.

---

# Logging Standards

Use structured logging.

Good:

```text
Employee created
EmployeeId={EmployeeId}
```

Bad:

```text
Employee created " + id
```

---

## Sensitive Data

Never log:

- Salary
- Bank details
- Passwords
- Tokens

---

# Error Handling

Use:

- Result pattern
- Validation failures
- ProblemDetails responses

Avoid:

```text
catch(Exception)
```

without purpose.

---

# Dependency Injection

Prefer constructor injection.

Avoid service locators.

---

# Async Standards

Use async/await throughout.

Avoid:

```text
.Result
.Wait()
```

---

# Testing Standards

Every feature requires:

- Unit tests
- Integration tests

UI changes require:

- bUnit tests

User journeys require:

- Playwright tests

---

# Comments

Prefer self-explanatory code.

Comments should explain:

- Why

Not:

- What

---

# File Structure

Organise by:

```text
feature
```

not technical layer.

---

# Pull Request Checklist

Before merge:

- Tests pass
- Architecture tests pass
- No module boundary violations
- Logging added where appropriate
- Authorization reviewed
- Tenant isolation verified

---

# AI Development Standards

AI-generated code must:

- Follow vertical slice structure
- Include tests
- Respect module boundaries
- Respect authorization

AI-generated code must not:

- Create generic repositories
- Create shared business services
- Bypass tenant filtering
- Add direct module references

---

# Acceptance Criteria

1. Naming conventions are followed.
2. Vertical slice conventions are followed.
3. Result pattern is used.
4. Structured logging is used.
5. Sensitive values are not logged.
6. Async patterns are followed.
7. Tests are provided.
8. AI-generated code follows standards.
