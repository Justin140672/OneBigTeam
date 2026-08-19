# 10. AI Implementation Guardrails

## Overview

This document defines mandatory rules for AI-assisted development within the HR platform.

Its purpose is to ensure AI-generated code remains:

- Consistent
- Secure
- Maintainable
- Testable
- Architecturally compliant

These rules are considered non-negotiable.

---

# Core Principle

AI must work within the architecture.

AI must never change the architecture without explicit approval.

The architecture documents are the source of truth.

---

# Architectural Constraints

AI may:

- Add features
- Add handlers
- Add endpoints
- Add tests
- Add migrations
- Add entities within a module

AI may not:

- Create new architectural layers
- Create new shared projects
- Create module-to-module references
- Introduce alternative patterns

---

# Solution Structure Rules

AI must follow:

```text
01-solution-structure.md
```

Every feature belongs inside a module.

No business logic may exist in:

- HR.Web
- HR.Api
- HR.Infrastructure

---

# Module Boundary Rules

AI must follow:

```text
02-module-boundaries.md
```

AI must not:

```text
Leave -> Employees
Recruitment -> Documents
Tasks -> Recruitment
```

through direct references.

Cross-module communication must use events.

---

# Vertical Slice Rules

Every new feature should contain:

```text
Endpoint.cs
Request.cs
Response.cs
Validator.cs
Handler.cs
```

Tests should be created alongside the feature.

---

# Database Rules

AI must:

- Use module DbContext
- Use module schema
- Use UUID keys
- Respect company_id filtering

AI must not:

- Create shared DbContexts
- Create generic repositories
- Query another module's schema

---

# Authorization Rules

Every feature must consider:

1. Authentication
2. Authorization
3. Tenant isolation
4. Scope evaluation

AI must use:

```text
ICurrentUser
```

AI must not:

- Read claims directly
- Trust route company identifiers
- Bypass authorization

---

# Event Rules

Cross-module behaviour uses integration events or explicit contracts owned by the providing module.

AI must:

- Publish events when the workflow is event-driven
- Use module-owned contracts for synchronous reads or explicitly coordinated operations
- Build idempotent consumers where delivery may repeat
- Add outbox processing only when the owning module explicitly requires durable deferred delivery

AI must not:

- Reference another module's implementation project
- Query another module's database schema directly

---

# Testing Requirements

Every feature requires:

## Mandatory

- Unit Tests
- Integration Tests

## When UI Changes

- bUnit Tests

## When User Journeys Change

- Playwright Tests

No feature is complete without tests.

---

# Logging Rules

AI must:

- Use structured logging
- Include identifiers where appropriate

AI must not log:

- Salary
- Bank details
- Tokens
- Passwords

---

# Auditing Rules

AI must audit:

- Business-critical changes
- Security-sensitive changes
- Permission changes

Sensitive values must be redacted.

---

# Error Handling Rules

Use:

```text
Result
Result<T>
```

Avoid exception-driven business flow.

Exceptions are for unexpected failures only.

---

# Naming Rules

Use business language.

Good:

```text
CreateEmployee
ApproveLeave
HireCandidate
```

Bad:

```text
EmployeeManager
LeaveProcessor
DataService
```

---

# Forbidden Patterns

AI must not create:

```text
HR.Core
HR.Common
HR.Business
HR.Data
```

AI must not create:

```text
EmployeeService
LeaveService
DocumentService
```

unless explicitly approved.

---

# Pull Request Checklist

Before code is accepted:

- Architecture tests pass
- Unit tests pass
- Integration tests pass
- Authorization reviewed
- Tenant isolation reviewed
- Logging reviewed
- Auditing reviewed

---

# AI Definition of Done

A feature is complete only when:

1. Business functionality works.
2. Validation exists.
3. Authorization exists.
4. Tenant isolation exists.
5. Tests exist.
6. Logging exists where appropriate.
7. Audit exists where required.
8. Events are published where required.
9. Architecture rules are respected.

---

# Review Questions

Before accepting AI-generated code ask:

- Does it violate module boundaries?
- Does it bypass authorization?
- Does it bypass tenant isolation?
- Does it create a new pattern?
- Does it include tests?
- Is the code understandable?

If any answer is yes, reject the change.

---

# Acceptance Criteria

1. AI-generated code follows architecture standards.
2. AI-generated code respects module boundaries.
3. AI-generated code includes tests.
4. AI-generated code respects authorization.
5. AI-generated code respects tenant isolation.
6. AI-generated code uses events correctly.
7. AI-generated code follows coding standards.
8. AI-generated code can pass architecture review.
