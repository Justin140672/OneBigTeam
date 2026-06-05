# 06. Authentication & Authorization

## Overview

This document defines authentication, authorization, tenant resolution, and access control standards for the HR platform.

Authentication is provided by Supabase Auth.

Authorization is implemented within the application using role, scope, hierarchy, and tenant evaluation.

---

## Core Principles

The platform shall:

- Authenticate users through Supabase Auth
- Authorize users through application permissions
- Enforce company isolation
- Support manager hierarchy access
- Support position-based permissions
- Support employee-specific overrides

---

# Authentication Architecture

Provider:

```text
Supabase Auth
```

Supported providers:

- Email/password
- Magic link
- Microsoft Entra ID (future)
- Google (future)

---

## User Model

Authentication identity:

```text
auth.users
```

Application profile:

```text
identity.user_profiles
```

The application never stores passwords.

---

## User Profile

Each authenticated user maps to:

```text
UserProfile
```

Fields:

- UserId
- CompanyId
- EmployeeId
- Email
- Status

---

## Tenant Resolution

Tenant resolution occurs after authentication.

Source:

```text
user_profiles.company_id
```

Current tenant is loaded into:

```text
CurrentUser
```

abstraction.

---

# Current User Abstraction

Every request should access user information through:

```text
ICurrentUser
```

Example properties:

- UserId
- EmployeeId
- CompanyId
- Email
- Roles

Avoid reading claims directly inside handlers.

---

# Authorization Model

Authorization evaluates:

1. Tenant
2. Permission
3. Scope
4. Hierarchy
5. Resource ownership

All checks must pass.

---

# Roles

System roles:

- Employee
- Manager
- Recruiter
- HR Administrator
- Finance
- Company Administrator

Roles are application concepts.

---

# Position-Based Permissions

Primary permission source:

```text
Position Profile
```

Examples:

HR Administrator

- employee.read
- employee.edit
- document.manage

Manager

- leave.approve
- employee.read

Employee

- self.read

---

# Employee Overrides

Additional permissions may be granted directly.

Examples:

- Temporary recruiter
- Temporary HR administrator
- Finance access

Overrides should remain rare.

---

# Permission Format

Recommended format:

```text
employee.read
employee.edit
leave.approve
document.manage
```

Naming convention:

```text
resource.action
```

---

# Scope Model

Supported scopes:

## Self

Access own records only.

## Direct Reports

Access direct reports.

## Hierarchy

Access entire reporting chain.

## Company

Company-wide access.

---

# Hierarchy Evaluation

Hierarchy based on:

```text
manager_employee_id
```

Employees inherit access through reporting relationships.

---

# Authorization Flow

Example:

Manager requests employee record.

System evaluates:

```text
Authenticated
    ->
Same Company
    ->
employee.read
    ->
Direct Report?
    ->
Allow
```

---

# Endpoint Authorization

Authorization declared at endpoint level.

Example:

```text
Permissions("employee.read")
```

Business decisions remain in handlers.

---

# Tenant Isolation

All requests must include:

```text
company_id
```

evaluation.

Cross-company access is never allowed.

---

# Sensitive Operations

Examples:

- Salary changes
- Permission changes
- Employee termination

Require elevated permissions.

---

# Auditing

Audit:

- Login success
- Login failure
- User invited
- Role assigned
- Permission changes

Sensitive values must be redacted.

---

# Security Rules

Never trust:

- Route values
- Client-supplied company identifiers
- Client-supplied permissions

Always resolve security context server-side.

---

# Testing Requirements

Tests should verify:

- Company isolation
- Role evaluation
- Scope evaluation
- Hierarchy evaluation
- Permission overrides

---

# AI Implementation Rules

AI-generated code must:

- Use ICurrentUser
- Use authorization policies
- Respect company boundaries
- Respect scope evaluation

AI must not:

- Read claims directly
- Bypass authorization
- Trust client tenant identifiers

---

# Anti-Patterns

Avoid:

- Hard-coded role checks
- Claims parsing in handlers
- Cross-company queries
- Permission logic in UI

---

# Acceptance Criteria

1. Authentication uses Supabase Auth.
2. User profiles map authenticated users.
3. Company resolution occurs server-side.
4. Position permissions are supported.
5. Employee overrides are supported.
6. Scope evaluation is supported.
7. Hierarchy evaluation is supported.
8. Cross-company access is prevented.
9. Permission changes are audited.
10. Authorization is testable and consistent.
