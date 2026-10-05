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
| Company Administrator | **Company configuration only** | Company profile, branding, company settings, subscription/billing, onboarding checklist. **No HR data of any kind and no support-request access** unless the user also holds HR Administrator. |

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
| Subscription / billing | `subscription:manage` | — | — | — | — | **Y** |
| Getting-started / onboarding checklist | `onboarding:view` / `onboarding:manage` | — | — | — | Y | **Y** |
| Support requests — create, view own, view company queue, reply (no status changes) | `support:request` / `support:manage` | — | — | — | Y | **—** |
| Support requests — cross-tenant view and status change (Admin Portal) | `platform:admin` (enabled Platform Administrator, no tenant role needed) | — | — | — | — | **—** |
| HR settings (leave year, probation, salary display, reminders, recruitment settings) | `hr-settings:manage` | — | — | — | Y | **—** |
| Employee directory — list | `employee:manage` | — | — | — | Y | **—** |
| Employee analytics / scoped workflow summaries | `employee:read` | — | S | — | Y | **—** |
| Employee administration — create / edit / promote / manager assignment / notes / leaving process | `employee:manage` (`employee.edit`) | — | — | — | Y | **—** |
| Employee detail record (single) — GetEmployee (full) | `role:employee` + `EmployeesResourceAuthorizer.CanViewFullRecordAsync` (self / HR-admin only) | Self (full self-service fields) | **—** (managers use the separate team-view endpoint below, never this one) | Self only | Y (full record) | Self only |
| Employee detail record (single) — GetEmployeeTeamView (operational) | `role:employee` + `EmployeesResourceAuthorizer.CanViewAsManagerAsync` (hierarchy only) | **—** (self uses GetEmployee, never this endpoint) | Hierarchy — direct + indirect reports, **operational fields only** (see 26-permissions-access-ux.md's field-level access matrix; no personal contact/address/DOB/demographic data, leaving-process detail, notice period or HR notes) | — | — (HR uses GetEmployee for the full record) | — |
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
| Recruitment reports | `reporting:view-recruitment` | — | — | Y | — | **—** |
| Leave / probation / onboarding / workload reports | `reporting:view-*` | — | S | — | Y | **—** |
| Reporting catalogue / saved views / favourites | `reporting:view` | — | Y | Y | Y | **—** |
| Probation administration / review | `probation:manage` / `probation:review` | — | S | — | Y | **—** |
| Assets administration | `employee:manage` | — | — | — | Y | **—** |
| Asset catalogue (view) | `asset:view` | Y | Y | — | Y | **—** |
| Platform / admin-portal (cross-tenant) | `platform:admin` | — | — | — | — | **—** |

Hiring an external candidate stays under `recruitment:manage`. The hire's employment type, position
profile, department and location come from the vacancy, and the manager defaults to the vacancy's hiring
manager; the recruiter's override is validated server-side against the company's employees. Hiring does
not require `employee:manage`. See `19-recruitment.md` for the source of every hire field.

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
- Employee documents administration

They retain: company profile/branding/settings, subscription/billing, onboarding checklist,
and their own self-service pages. Support requests are no longer a Company Administrator capability.

### Support requests

- HR Administrator: create requests, view own submitted requests, view the company queue, reply. Cannot change status (status is read-only in HR.Web).
- Platform Administrator (Admin Portal, cross-tenant): view requests across tenants and change status only (`PUT /api/admin/companies/{companyId}/support/requests/{id}/status`). Cannot create. No tenant role required.
- Employee / Manager / Recruiter / Company Administrator: no support access unless the user also holds HR Administrator (union of roles). The tenant status endpoint has been removed.

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

## Legacy database reconciliation (IAM-08)

The Company Administrator permission catalogue has been correct in the seed since migration
`20260710120353_NarrowCompanyAdministratorPermissions`, but databases created before it (or
drifted by a rolled-back/aborted migration, a restored snapshot or manual seeding) could retain
obsolete `role_permissions` rows (`employee.*`, `leave.*`, `sickness.*`, `document.*`, `role.assign`)
for the Company Administrator role — which is enough to make Employees appear in navigation for a
"Company Administrator only" user even though `IdentityAuthorizationService` and every API policy
are already correct. Migration `20260903114519_IAM08_ReconcileCompanyAdministratorPermissions`
deletes every Company Administrator `role_permissions` row outside the allow-list
(`company.read`, `company.edit`, `onboarding.view`, `onboarding.manage`, `subscription.manage`,
`support.manage`) with a single set-based statement that is safe to re-run (idempotent). It never
touches the `permissions` catalogue rows themselves (still used by other roles) or any user's
direct role assignments / position roles / overrides — a user who genuinely still holds
`HrAdministrator` (directly, via a position, or via an active Grant override) keeps HR access, and
loses it on the next authenticated session once that role is removed (permissions are computed
live, with no server-side cache).
