# 01. Product Vision & Scope

## Overview

The HR platform is a modular SME-focused human resources system designed to manage the full employee lifecycle, from recruitment through onboarding, employment, leave, sickness, documents, tasks, reporting and audit.

The product is designed for small and medium-sized organisations that need a practical HR system without the complexity of enterprise HR suites.

---

## Product Vision

To provide an affordable, secure, workflow-driven HR platform that helps companies manage people, documents, approvals, absence, recruitment, and compliance in one place.

The platform should feel:

- Simple enough for SMEs
- Structured enough for growing businesses
- Secure enough for sensitive HR data
- Flexible enough for future expansion

---

## Target Customers

Primary target users:

- Small businesses
- Medium-sized businesses
- Growing teams without mature HR systems
- Companies moving away from spreadsheets
- Companies needing better visibility of employees, documents and workflows

---

## Core Product Goals

The system shall:

1. Centralise employee information.
2. Support employee lifecycle management.
3. Manage recruitment pipelines.
4. Manage leave and sickness.
5. Store HR documents securely.
6. Provide workflow/task automation.
7. Support notifications and reminders.
8. Provide audit trails.
9. Support reporting and exports.
10. Enforce role-based and hierarchy-aware permissions.

---

## Primary Actors

| Actor | Description |
|---|---|
| Employee | Uses self-service features such as leave requests, documents and tasks |
| Manager | Approves leave, views team information and completes workflow actions |
| HR Administrator | Manages employees, recruitment, documents, policies and compliance |
| Company Administrator | Manages company profile, settings and branding; does not receive HR access from this role |
| Recruiter | Manages vacancies, candidates and interview workflow |
| Finance User | May view authorised compensation exports |

The initial company creator is deliberately assigned both Company Administrator and HR Administrator. This is an explicit first-account exception, not an implication between the roles.

---

## In Scope for MVP

The MVP includes:

- Company setup
- Employee records
- Departments and position profiles
- Position-based permissions
- Supabase Auth integration
- Leave management
- Sickness management
- Recruitment pipeline
- Task/workflow system
- Document management
- Notifications
- Reporting/exports
- Audit/activity history
- Search experience
- Role-aware dashboards
- Admin experience

---

## Out of Scope for MVP

The MVP does not include:

- Pay processing
- Benefits administration
- Advanced performance reviews
- Public recruitment portal
- Mobile app
- External integrations
- Slack/Teams integration
- Multi-language support
- White-label domains
- Advanced analytics
- AI assistant features

---

## Product Principles

### Practical over complex

The platform should solve real HR problems without becoming an enterprise HR monster.

### Secure by default

HR data is sensitive. Access must always be permission-controlled and tenant-isolated.

### Workflow-driven

Approvals, onboarding, reminders and compliance actions should use tasks and notifications.

### Audit-first

Important business actions should be recorded in an immutable audit trail.

### Modular

The system should be easy to expand without rewriting core features.

---

## Success Criteria

The product is successful when:

1. A company can set up its HR system.
2. HR can create and manage employees.
3. Employees can use self-service features.
4. Managers can approve and manage team workflows.
5. Documents are stored securely.
6. Recruitment can be tracked from vacancy to hire.
7. Leave and sickness can be managed accurately.
8. Compliance tasks and reminders are surfaced.
9. Reports and exports can be generated.
10. Permission and audit requirements are satisfied.
