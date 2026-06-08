# 32. Platform Acceptance Criteria

## Overview

This document defines the high-level acceptance criteria for the HR platform.

The platform is considered production-ready when the criteria in this document have been satisfied.

Acceptance criteria are grouped by capability area.

---

# Company Management

The platform shall:

1. Support multiple companies.
2. Enforce company isolation.
3. Support company profile management.
4. Support company settings management.
5. Support company branding.

---

# Authentication & Authorization

The platform shall:

1. Authenticate users using Supabase Auth.
2. Support role-based access control.
3. Support scope-based permissions.
4. Support manager hierarchy permissions.
5. Prevent cross-company access.
6. Audit permission changes.

---

# Employee Management

The platform shall:

1. Create employees.
2. Update employees.
3. Terminate employees.
4. Maintain employment history.
5. Support manager assignment.
6. Support position assignment.

---

# Organisation

The platform shall:

1. Support departments.
2. Support position profiles.
3. Support reporting hierarchies.
4. Support org-chart visualisation.

---

# Leave Management

The platform shall:

1. Support leave requests.
2. Support leave approvals.
3. Support leave balances.
4. Support leave reporting.
5. Audit leave activity.

---

# Recruitment

The platform shall:

1. Support vacancy management.
2. Support candidate pipelines.
3. Support interviews.
4. Support offers.
5. Support candidate hire workflows.
6. Audit recruitment activity.

---

# Tasks & Workflow

The platform shall:

1. Support manual tasks.
2. Support automated tasks.
3. Support reminders.
4. Support escalations.
5. Support workflow-driven task creation.

---

# Documents

The platform shall:

1. Store documents securely.
2. Support version history.
3. Support document permissions.
4. Support expiry tracking.
5. Audit document activity.

---

# Notifications

The platform shall:

1. Support in-app notifications.
2. Support email notifications.
3. Support scheduled reminders.
4. Support report completion notifications.

---

# Reporting

The platform shall:

1. Support grid exports.
2. Support formal reports.
3. Support asynchronous report generation.
4. Store generated reports securely.
5. Audit sensitive reports.

---

# Search

The platform shall:

1. Support employee search.
2. Support recruitment search.
3. Support document search.
4. Respect permissions.
5. Respect company isolation.

---

# Audit

The platform shall:

1. Maintain immutable audit history.
2. Support audit search.
3. Support audit reporting.
4. Redact sensitive values.

---

# Dashboard

The platform shall:

1. Provide role-aware dashboards.
2. Display tasks.
3. Display notifications.
4. Display compliance alerts.
5. Provide quick actions.

---

# Administration

The platform shall:

1. Support user administration.
2. Support permission administration.
3. Support settings administration.
4. Support branding administration.
5. Audit administrative actions.

---

# Non-Functional Requirements

The platform shall:

1. Meet defined performance targets.
2. Meet defined security requirements.
3. Support automated testing.
4. Support monitoring and observability.
5. Support backup and recovery.

---

# Testing Acceptance Criteria

The platform is not complete unless:

1. Unit tests pass.
2. Integration tests pass.
3. Architecture tests pass.
4. bUnit tests pass.
5. Playwright tests pass.
6. CI/CD pipeline passes.

---

# Release Acceptance Criteria

A release is acceptable when:

1. All automated tests pass.
2. Security validation passes.
3. Migration scripts succeed.
4. No critical defects remain open.
5. Deployment succeeds in staging.
6. Smoke tests succeed.
