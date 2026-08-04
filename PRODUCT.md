# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Primary actors, per role:

- **Employee** — self-service: leave requests, documents, tasks, own profile.
- **Manager** — approves leave, views team information, completes workflow actions for direct reports.
- **HR Administrator** — manages employees, recruitment, documents, policies, and compliance.
- **Company Administrator** — manages company settings, branding, users, and permissions.
- **Recruiter** — manages vacancies, candidates, and interview workflow.
- **Finance User** — may view compensation/payroll-related exports if authorised.

Target customers are small and medium-sized businesses (roughly 50–2,000 employees per company) — growing teams without mature HR systems, and companies moving off spreadsheets who need better visibility of employees, documents, and workflows.

## Product Purpose

An affordable, secure, workflow-driven HR platform ("One Big Team") that helps SME companies manage people, documents, approvals, absence, recruitment, and compliance in one place, across the full employee lifecycle from recruitment through onboarding, employment, leave, sickness, documents, tasks, reporting, and audit.

Success means: a company can set itself up; HR can create and manage employees; employees can self-serve; managers can approve and manage team workflows; documents are stored securely; recruitment is tracked end to end; leave/sickness are managed accurately; compliance tasks and reminders surface on their own; reports/exports can be generated; permission and audit requirements are satisfied.

## Positioning

Simple enough for SMEs, structured enough for growing businesses, secure enough for sensitive HR data, flexible enough for future expansion — practical HR without the complexity of enterprise HR suites. The product is not trying to be a payroll system, a performance-management suite, or an enterprise HRIS; it solves real day-to-day HR administration and compliance problems for companies currently getting by on spreadsheets.

## Operating Context

- Multi-tenant: every company's data is isolated (`company_id` boundary enforced at every permission check); users can never see another company's data.
- Auth via Supabase Auth.
- Role-Based Access Control layered with scope evaluation (self / direct reports / company) and manager-hierarchy evaluation; permissions are primarily inherited from Position Profiles, with rare employee-specific overrides. Roles are fixed in v1: Employee, Manager, HR Administrator, Recruiter, Finance, Company Administrator.
- Workflow-driven: approvals, onboarding, reminders, and compliance actions run through a task/notification system, not ad hoc process.
- Audit-first: business-critical actions are recorded in an immutable, searchable audit trail; sensitive data (salary, National Insurance numbers, bank details, credentials) must never appear in logs or audit records.
- Companies can apply their own branding (logo, primary/secondary/accent colours) on top of the base One Big Team platform, surfaced in-app, in emails, and on generated reports/documents — branding is company-specific and does not allow custom CSS/JS or full UI replacement.
- Modular architecture (vertical slices per module) so the system can expand without rewriting core features; module boundaries are enforced by architecture tests.

## Capabilities and Constraints

In scope for MVP: company setup, employee records, departments and position profiles, position-based permissions, Supabase Auth integration, leave management, sickness management, recruitment pipeline, task/workflow system, document management, notifications, reporting/exports, audit/activity history, search, role-aware dashboards, admin experience. A Support module (in-app help requests/dashboard) is also in active development.

Explicitly out of scope for MVP: payroll processing, benefits administration, advanced performance reviews, a public recruitment portal, a mobile app, external integrations (incl. Slack/Teams), multi-language support, white-label domains, advanced analytics, AI assistant features.

Performance targets: page/dashboard load < 2s, search results < 500ms, standard CRUD actions < 1s, small reports < 10s (large reports generated asynchronously). Target availability 99.5%.

## Brand Commitments

The app currently ships a name and logo (`src/HR.Web/wwwroot/images/logo.svg`): "One Big Team," a dark navy background (#021C32), a teal/mint (#00C9A3) interlocking-rings mark, set in Montserrat/Proxima Nova/Inter. **Status: undecided** — not yet confirmed as binding for future design work, and not confirmed as a placeholder either. Treat it as existing incumbent evidence (per Impeccable's authority rules) rather than a locked brand system; confirm before either preserving it as-is in new visual work or discarding it as anti-reference.

Note: this platform-level identity is distinct from per-company branding (logo/colours tenants configure for themselves) described under Operating Context.

## Evidence on Hand

No real customers, pilot users, case studies, testimonials, or sample production data exist yet. Future design and content work must not fabricate customer logos, quotes, metrics, or usage numbers.

## Product Principles

1. **Practical over complex** — solve real HR problems without becoming an enterprise HR monster.
2. **Secure by default** — access is always permission-controlled and tenant-isolated; HR data is sensitive.
3. **Workflow-driven** — approvals, onboarding, reminders, and compliance actions flow through tasks and notifications, not manual chasing.
4. **Audit-first** — important business actions are recorded in an immutable audit trail.
5. **Modular** — the system expands without rewriting core features.

## Accessibility & Inclusion

Application shall support keyboard navigation, screen readers, and responsive layouts. Target: WCAG 2.1 AA where practical.
