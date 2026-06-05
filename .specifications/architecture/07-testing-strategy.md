# 07. Testing Strategy

## Overview

This document defines the testing strategy for the HR platform.

The platform is intentionally designed for high automated test coverage due to the use of AI-assisted development.

Testing is considered a first-class architectural concern.

No feature is considered complete without automated tests.

---

## Testing Principles

The platform shall:

- Prefer automated testing
- Test business behaviour
- Test architecture rules
- Test authorization rules
- Test tenant isolation
- Test user journeys

Testing should provide confidence for:

- Refactoring
- AI-generated code
- Production releases

---

# Testing Pyramid

Recommended distribution:

```text
Architecture Tests
Unit Tests
Integration Tests
UI Tests
End-to-End Tests
```

Most tests should exist in:

- Unit Tests
- Integration Tests

---

# Test Projects

```text
HR.Architecture.Tests
HR.Domain.Tests
HR.Integration.Tests
HR.UI.Tests
HR.Playwright.Tests
```

---

# Architecture Tests

Purpose:

Prevent architectural drift.

Validate:

- Module boundaries
- Dependency rules
- Naming conventions
- Forbidden references

Examples:

```text
Leave cannot reference Employees
Recruitment cannot reference Tasks
```

Tools:

- NetArchTest
- Custom architecture rules

---

# Unit Tests

Purpose:

Validate business logic.

Test:

- Domain entities
- Value objects
- Validators
- Business rules

Examples:

```text
Cannot approve cancelled leave
Cannot hire rejected candidate
Cannot terminate active vacancy
```

Unit tests should execute quickly.

---

# Integration Tests

Purpose:

Validate complete feature slices.

Test:

- FastEndpoints
- EF Core
- Authorization
- Persistence
- Events

Examples:

```text
Create Employee
Approve Leave
Upload Document
Hire Candidate
```

Integration tests should use real database behaviour where practical.

---

# Database Testing

Validate:

- Migrations
- Constraints
- Query filters
- Company isolation

Critical requirement:

Tenant leakage must be tested.

---

# Authorization Testing

Every secured feature should validate:

- Authorized user
- Unauthorized user
- Wrong scope
- Wrong tenant

Examples:

```text
Employee
Manager
HR Admin
Company Admin
```

---

# Event Testing

Validate:

- Event publication
- Event consumption
- Idempotency
- Outbox processing

Examples:

```text
CandidateHired
EmployeeCreated
LeaveApproved
```

---

# UI Testing

Framework:

```text
bUnit
```

Purpose:

Validate Blazor components.

Test:

- Rendering
- State changes
- User interaction
- Conditional visibility

Examples:

- Leave form
- Employee editor
- Dashboard widgets

---

# End-to-End Testing

Framework:

```text
Playwright
```

Purpose:

Validate complete user journeys.

Examples:

- Login
- Create employee
- Request leave
- Approve leave
- Hire candidate

---

# Test Data

Use:

- Builders
- Fixtures
- Factories

Avoid:

- Large shared test databases
- Magic values
- Hidden dependencies

---

# Coverage Expectations

Priority areas:

- Authorization
- Tenant isolation
- Business rules
- Financial impacts
- Compliance logic

Coverage target:

High coverage for business-critical code.

Coverage alone is not considered quality.

---

# CI/CD Requirements

Pull requests must:

- Build successfully
- Run architecture tests
- Run unit tests
- Run integration tests

Release pipelines should additionally run:

- UI tests
- Playwright tests

---

# AI Development Rules

AI-generated features must include:

- Unit tests
- Integration tests

Where UI is added:

- bUnit tests

Where workflows are added:

- End-to-end tests

---

# Regression Testing

Defects should result in:

1. Failing test created
2. Defect fixed
3. Test retained permanently

---

# Performance Testing

Future phase.

Candidate areas:

- Reporting
- Search
- Dashboard loading

---

# Anti-Patterns

Avoid:

- Untested handlers
- UI-only testing
- Coverage chasing
- Shared mutable test state

Avoid accepting:

```text
Feature complete
```

without tests.

---

# Acceptance Criteria

1. Architecture tests enforce boundaries.
2. Unit tests cover business rules.
3. Integration tests cover vertical slices.
4. Authorization scenarios are tested.
5. Tenant isolation is tested.
6. bUnit tests cover UI components.
7. Playwright tests cover key journeys.
8. CI/CD enforces test execution.
9. AI-generated code includes tests.
10. Regressions are protected by automated tests.
