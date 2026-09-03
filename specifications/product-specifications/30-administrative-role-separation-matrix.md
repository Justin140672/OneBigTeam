# 30. Administrative Role Separation Matrix (ADM-05)

## Purpose

This document is the authoritative role-access matrix for every administrative screen and
action in the HR platform. It exists to enforce **administrative role separation**: the
*Company Administrator* role is a company-configuration role only and must never, on its own,
reach HR, employee, recruitment, leave, sickness or document administration.

The matrix is derived from — and kept honest by — three code artefacts that are the real
source of truth:

- `src/Modules/HR.Modules.Identity/Persistence/Configurations/RolePermissionConfiguration.cs` — role → permission grants (seed data).
- `src/Modules/HR.Modules.Identity/Authorization/PolicyCatalog.cs` — named policy → permission id.
- `tests/HR.Modules.Identity.Tests/PolicyMatrixTests.cs` — exhaustive role × policy regression guard.

API endpoints are the authoritative enforcement boundary. UI hiding and page guards are a
usability layer only and never a substitute for endpoint authorization.

---

## Roles (v1, fixed)

| Role | Nature | Notes |
|---|---|---|
| Employee | Baseline | Everyone has it. Self-service only. |
| Manager | HR (scoped) | Direct + indirect reports via manager hierarchy. |
| Recruiter | HR (function) | Recruitment + candidate data only. |
| HR Administrator | HR (company-wide) | Full HR/employee/leave/sickness/document administration. |
| Company Administrator | **Company configuration only** | Company profile, branding, company settings, subscription/billing, onboarding checklist, support requests. **No HR data of any kind.** |

Combined roles are a set union (OR) of permissions. The initial company creator is granted
`HR Administrator` as an explicit, separately-assigned exception — that is an HR Administrator,
not a Company Administrator capability.

The `Finance` role referenced in older specs was removed
(`Migrations/20260725181020_RemoveFinanceRole.cs`).

---

## Administrative area matrix

Legend: Y = full access · S = scoped (hierarchy / self / function) · — = denied (401/403).

| Administrative area | Backing policy (permission) | Employee | Manager | Recruiter | HR Admin | Company Admin (alone) |
|---|---|---|---|---|---|---|
| Company profile / branding / company settings | `company:manage` (`company.edit`) | — | — | — | — | **Y** |
| Subscription / billing | `subscription:manage` | — | — | — | Y | **Y** |
| Getting-started / onboarding checklist | `onboarding:view` / `onboarding:manage` | — | — | — | Y | **Y** |
| Support requests queue | `support:manage` | — | — | — | Y | **Y** |
| HR settings (leave year, probation, salary display, reminders, recruitment settings) | `hr-settings:manage` | — | — | — | Y | **—** |
| Employee directory — list | `employee:manage` | — | — | — | Y | **—** |
| Employee analytics / scoped workflow summaries | `employee:read` | — | S | — | Y | **—** |
| Employee administration — create / edit / promote / manager assignment / notes / leaving process | `employee:manage` (`employee.edit`) | — | — | — | Y | **—** |
| Employee detail record (single) | `role:employee` + handler scope | Self | Self | Self | Y | Self only |
| Salary / compensation (view + edit + bulk + import) | `employee:manage` | — | — | — | Y | **—** |
| Data import (org structure + employees) | `employee:manage` | — | — | — | Y | **—** |
| User & role administration (invite, roles, overrides, position defaults, access review) | `users:view` / `users:manage` | — | — | — | Y | **—** |
| Leave administration (policies, types, balance adjust, TOIL award, assign policy) | `leave:manage` / `employee:manage` | — | — | — | Y | **—** |
| Leave approval | `leave:approve` | — | S | — | Y | **—** |
| Sickness administration (records, categories, RTW) | `sickness:manage` / `sickness:review` | — | S | — | Y | **—** |
| Employee documents administration (types, versions, archive, search, requests) | `employee:manage` | — | — | — | Y | **—** |
| Shared company documents — management / publish / archive / ack status | `shared-document:manage` etc. | — | — | — | Y | **—** |
| Recruitment — vacancies / candidates / offers / interviews (manage) | `recruitment:manage`, `candidate:view` | — | — | Y | — | **—** |
| Recruitment — vacancy board (view only) | `recruitment:view` | Y | Y | Y | Y | **—** |
| Recruitment — candidate GDPR purge (destructive) | `role:company-administrator` | — | — | — | — | **Y** (governance exception — see below) |
| HR reports | `reporting:view-hr` | — | — | — | Y | **—** |
| Compliance Centre (consolidated: expiring visas/certifications, missing & requested documents, probation reviews due/overdue) | `compliance:view` | — | — | — | Y | **—** |
| Administrative alerts & incidents inbox (ADM-03: compliance alerts, failed report generation, failed integrations / external-service delivery, security alerts; acknowledge / resolve) | `admin-alerts:view` | — | — | — | Y | **—** |
| Recruitment reports | `reporting:view-recruitment` | — | — | Y | — | **—** |
| Leave / probation / onboarding / workload reports | `reporting:view-*` | — | S | — | Y | **—** |
| Reporting catalogue / saved views / favourites | `reporting:view` | — | Y | Y | Y | **—** |
| Probation administration / review | `probation:manage` / `probation:review` | — | S | — | Y | **—** |
| Assets administration | `employee:manage` | — | — | — | Y | **—** |
| Asset catalogue (view) | `asset:view` | Y | Y | — | Y | **—** |
| Platform / admin-portal (cross-tenant) | `platform:admin` | — | — | — | — | **—** |

Self-service (my profile, my leave, my sickness, my documents, my tasks, my emergency
contacts, notifications) is available to every authenticated user including a Company
Administrator who also has an employee record, and is out of scope for administrative role
separation.

---

## Company Administrator — explicit deny list (ADM-05 acceptance)

A user whose only role is Company Administrator is denied (401/403 at the API, access-denied
outcome in the UI, category hidden in navigation) for all of:

- Employee administration (list, detail beyond self, create, edit, analytics, offboarding)
- User-role administration
- Salary / compensation
- HR reports
- Recruitment (management and board view)
- Leave administration and approval
- Sickness administration
- Compliance Centre (`compliance:view`)
- Administrative alerts & incidents inbox (`admin-alerts:view`)
- Employee documents administration

They retain: company profile/branding/settings, subscription/billing, onboarding checklist,
support requests, and their own self-service pages.

### Governance exception

`POST /api/companies/{companyId}/candidates/purge-eligible` (candidate GDPR data purge) is
deliberately gated to `role:company-administrator` — it is irreversible data-protection
redaction, treated as a company-governance act rather than a recruitment-workflow action,
mirroring `PurgeEligibleArchivedEmployeeDocuments`. This is the single intentional point where
a Company Administrator touches a recruitment-owned endpoint, and it grants no read access to
recruitment data. Flagged here as a known, reviewed exception.

---

## Enforcement layers

1. **API (authoritative):** every endpoint declares `Policies("<name>")`; the name resolves
   through `PolicyCatalog` to a permission and `PermissionAuthorizationHandler`. Raw
   `role:*` policies are used only for the "any authenticated employee" floor and the two
   documented governance/platform exceptions.
2. **UI page guards:** admin pages check the corresponding capability on `AppSession`
   (permission-derived, e.g. `CanViewUsers`, `CanManageHrSettings`, `CanViewReporting`,
   `CanManageRecruitment`) in `OnBeforeLoadAsync` and redirect to `/access-denied` on failure.
3. **Navigation:** the persistent `MainLayout` sidebar is available only to HR Administrators
   and Recruiters. Recruiter destinations are all top-level and must not expose the grouped
   `People and users`, `Company`, or `HR configuration` sections. Company Administrator-only,
   Manager-only and Employee-only users receive no sidebar. Within the two permitted sidebars,
   each action is still rendered only when its matching capability is present.
4. **Access-denied outcome:** direct navigation to a disallowed route yields a consistent
   `/access-denied` page rather than a silent bounce or a broken screen.

---

## Regression protection

- `tests/HR.Modules.Identity.Tests/PolicyMatrixTests.cs` — role × policy matrix vs seed data.
- `tests/HR.Integration.Tests/AdministrativeRoleSeparationTests.cs` — each role vs each
  protected administrative endpoint (the authoritative boundary).
- `tests/HR.Web.E2E.Tests/Tests/AdministrativeRoleSeparationTests.cs` +
  `CompanyAdministratorAccessTests.cs` — per-role UI matrix, direct-URL access-denied,
  navigation visibility.
