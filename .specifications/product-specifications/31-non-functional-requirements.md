# 31. Non-Functional Requirements

## Overview

This document defines the non-functional requirements (NFRs) for the HR platform.

These requirements establish the quality attributes expected of the system including performance, security, reliability, scalability, observability, and maintainability.

---

## Performance Requirements

### User Interface

Target response times:

| Operation | Target |
|------------|------------|
| Page Load | < 2 seconds |
| Dashboard Load | < 2 seconds |
| Search Results | < 500ms |
| Standard CRUD Actions | < 1 second |

---

### Reporting

Small reports:

- < 10 seconds

Large reports:

- Generated asynchronously

---

## Availability

Target availability:

99.5% minimum

Planned maintenance windows permitted.

---

## Scalability

Initial target:

- Small to medium businesses

Expected scale:

- 50–2000 employees per company

System should support:

- Multiple tenants
- Growth without redesign

---

## Security

The platform shall:

- Require authentication
- Enforce authorization
- Enforce company isolation
- Encrypt traffic using HTTPS
- Protect sensitive information

---

## Data Protection

Sensitive data includes:

- Salary information
- National Insurance numbers
- Bank details
- Authentication credentials

Sensitive data must never be exposed through logs or audit records.

---

## Auditability

Major business actions must be auditable.

Audit records must:

- Be immutable
- Be searchable
- Support reporting

---

## Backup & Recovery

Backups shall include:

- PostgreSQL data
- Supabase Storage assets

Recovery procedures should be documented and tested.

---

## Reliability

System failures should:

- Fail safely
- Avoid data corruption
- Preserve audit history

---

## Accessibility

The application shall support:

- Keyboard navigation
- Screen readers
- Responsive layouts

Target:

WCAG 2.1 AA where practical.

---

## Observability

The platform shall provide:

- Structured logging
- Error tracking
- Health checks
- Metrics

---

## Monitoring

Monitor:

- API failures
- Background job failures
- Report generation failures
- Authentication failures

---

## Maintainability

The system shall:

- Use modular architecture
- Use vertical slices
- Support automated testing
- Support AI-assisted development

---

## Testability

The platform shall support:

- Unit tests
- Integration tests
- bUnit tests
- Playwright tests
- Architecture tests

Target coverage:

High coverage for business-critical functionality.

---

## Deployment

The platform shall support:

- Development
- Test
- Staging
- Production

Deployments should be automated.

---

## Logging

Logs should include:

- Correlation identifiers
- Tenant identifiers
- User identifiers where appropriate

Sensitive values must be redacted.

---

## Compliance

The platform shall support:

- GDPR requirements
- Audit retention
- Document retention policies

---

## Acceptance Criteria

1. Performance targets achieved.
2. Security controls enforced.
3. Audit requirements satisfied.
4. Monitoring available.
5. Backup strategy defined.
6. Accessibility supported.
7. Automated testing supported.
8. Deployment automation supported.
