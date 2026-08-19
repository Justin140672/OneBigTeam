# 08. Deployment Architecture

## Overview

This document defines the deployment architecture for the HR platform.

The solution is designed to be:

- Simple to operate
- Low cost
- Cloud friendly
- AI-assisted development friendly
- Suitable for SMEs
- Easy to evolve

The platform intentionally avoids unnecessary infrastructure complexity during v1.

---

## Deployment Principles

The platform should:

- Minimise operational cost
- Minimise infrastructure complexity
- Support rapid deployment
- Support observability
- Support automated recovery

The platform should avoid:

- Microservices
- Kubernetes
- Distributed databases
- Premature scaling solutions

---

# High-Level Architecture

```text
Users
  ->
Blazor Web Application
  ->
FastEndpoints API
  ->
PostgreSQL (Supabase)

Background Processing
  ->
Hangfire

Storage
  ->
Supabase Storage

Authentication
  ->
Supabase Auth

Email
  ->
Postmark
```

---

# Aspire

The platform uses:

```text
.NET Aspire
```

Purpose:

- Local development orchestration
- Service configuration
- Diagnostics
- Observability

Aspire is primarily a developer productivity tool.

---

## Aspire Projects

### HR.AppHost

Responsible for:

- Service registration
- Local orchestration
- Environment setup

### HR.ServiceDefaults

Responsible for:

- OpenTelemetry
- Health checks
- Resilience defaults

---

# Hosting Strategy

Recommended v1 approach:

```text
Single Application Deployment
```

Components:

- HR.Web
- HR.Api

Deployed together.

Advantages:

- Simpler operations
- Lower hosting cost
- Easier debugging

---

# Environment Strategy

Supported environments:

```text
Development
Test
Staging
Production
```

Each environment should have:

- Separate configuration
- Separate secrets
- Separate databases

---

# Database

Provider:

```text
Supabase PostgreSQL
```

Responsibilities:

- Business data
- Audit data
- Module-scoped outbox data where implemented

Backups managed through Supabase.

---

# Authentication

Provider:

```text
Supabase Auth
```

Responsibilities:

- Login
- User identity
- Authentication tokens

Application responsibilities:

- Authorization
- Permissions
- Tenant resolution

---

# File Storage

Provider:

```text
Supabase Storage
```

Used for:

- Employee documents
- Recruitment documents
- Branding assets

Storage remains private.

Signed URLs used for access.

---

# Email Delivery

Provider:

```text
Postmark
```

Used for:

- Invitations
- Notifications
- Reminders

Business modules never send emails directly.

---

# Background Processing

Provider:

```text
Hangfire
```

Responsibilities:

- Notifications
- Reminder processing
- Long-running report generation if introduced for a demonstrated requirement
- Module-scoped outbox processing where implemented

Hangfire uses PostgreSQL storage.

---

# CI/CD

Deployment pipeline should:

1. Build
2. Run tests
3. Run architecture validation
4. Publish artifacts
5. Deploy
6. Run smoke tests

---

# Secrets Management

Secrets must never be stored in source control.

Examples:

- Supabase keys
- Postmark keys
- Connection strings

Use environment-specific secret stores.

---

# Configuration Strategy

Configuration sources:

- appsettings.json
- Environment variables
- Secret storage

Environment variables override configuration.

---

# Logging

Use structured logging.

Include:

- CorrelationId
- CompanyId
- UserId

Where appropriate.

Sensitive values must be redacted.

---

# Monitoring

Monitor:

- API failures
- Authentication failures
- Hangfire failures
- Report generation failures

---

# Health Checks

Health checks should validate:

- Database connectivity
- Supabase availability
- Storage availability
- Hangfire availability

---

# Disaster Recovery

Recovery requirements:

- Database backup recovery
- Storage recovery
- Configuration recovery

Recovery process should be documented.

---

# Cost Optimisation

The chosen architecture intentionally reduces cost by:

- Avoiding Kubernetes
- Avoiding microservices
- Using Supabase
- Using Postmark
- Using a modular monolith

This aligns with SME requirements.

---

# Future Evolution

Possible future changes:

- Separate API deployment
- Dedicated reporting service
- Dedicated search service

Not required for v1.

---

# Acceptance Criteria

1. Aspire supports local development.
2. Supabase hosts database, auth and storage.
3. Postmark handles email delivery.
4. Hangfire handles background processing.
5. CI/CD automates deployment.
6. Secrets are externalised.
7. Health checks are available.
8. Monitoring is configured.
9. Recovery procedures are documented.
10. Operational complexity remains low.
