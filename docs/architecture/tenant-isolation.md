# Tenant isolation: guardrails, exceptions and the query-filter / RLS decision

Status: guardrails implemented (this change). Global EF query filters and PostgreSQL RLS are
**evaluated only, not introduced** (see "Architectural decision" below).

## How isolation works today

1. `TenantRouteAuthorizationMiddleware` (Identity module) 403s any authenticated request whose route
   value `companyId` differs from the caller's resolved tenant. Endpoints with the `platform:admin`
   policy are exempt.
2. Every company-scoped endpoint lives under `/api/companies/{companyId:guid}/...` and its request
   type carries a `CompanyId` that the handler must include in every query and mutation.
3. There are no EF global query filters and no RLS. A handler that validates the route company but
   then loads a resource by id alone would leak or mutate another tenant's data. The middleware
   cannot see this case; handler code, tests and the guardrails below are the only defence.

## Guardrails (all run in CI)

| Guardrail | Where | What it catches |
|---|---|---|
| Route parameter convention | `TenantScopedRouteConventionTests.Company_Scoped_Routes_Name_Their_First_Path_Parameter_companyId` | `/api/companies/{id}` style routes that the middleware would not police (SEC-001). |
| Inspection can no longer be silent | `TenantScopedRouteConventionTests` (`InspectAll`, `ReadRouteTemplates`) | Any exception while reading an endpoint's `Configure()` now fails the test with endpoint type, exception type and message. Previously it returned an empty route list and the endpoint silently escaped the check. |
| Every endpoint inspected | `TenantScopedRouteConventionTests.Every_Endpoint_Is_Inspected_And_Declares_A_Route` | Endpoint missing from inspection, endpoint with no route, and a ratchet (`MinimumExpectedCompanyScopedRoutes`, currently 400 of about 415) so a mass dropout cannot pass. The Marketing module assembly was added to the inspected set. |
| Every entity is classified | `TenantIsolationGuardrailTests.Every_Entity_Is_Classified_For_Tenant_Isolation` | Builds every module `DbContext` model (Npgsql, no connection) and requires each entity to be (a) tenant-owned (`CompanyId`), (b) a required-FK child of a tenant-owned entity, or (c) on the documented exception list in the test. A new entity with none of these fails the build. |
| Tenant key shape | `TenantIsolationGuardrailTests.Tenant_Owned_Entities_Have_Required_Guid_company_id_Column` | `CompanyId` must be a non-nullable `Guid` mapped to `company_id` (one documented nullable exception: `ProcessedStripeEvent`). |
| No stale exceptions | `TenantIsolationGuardrailTests.Exception_Lists_Contain_No_Stale_Entries` | Exception entries for entities that no longer exist, or that became tenant-owned. |
| Request can carry the tenant | `TenantIsolationGuardrailTests.Company_Scoped_Endpoints_Expose_CompanyId_On_Their_Request` | A new company-scoped endpoint whose request type has no `Guid CompanyId` (so the handler cannot constrain by it) unless listed with a reason. |
| Cross-tenant behaviour | `tests/HR.Integration.Tests/CrossTenantResourceIsolationTests.cs` | Route carries the caller's own valid `companyId`, resource id belongs to another company: 403/404, no foreign data in the body, foreign resource unchanged. |

Existing integration coverage that remains and is complementary: `CompanyCrossTenantAccessTests`
(foreign company id in the route, middleware), `LeavingProcessTenantIsolationTests`,
`DisableUserEndpointTests`, `OrganisationDataExportOwnershipTests`, the `*ResourceAuthorizationTests`
classes.

### Cross-tenant operations covered by `CrossTenantResourceIsolationTests`

| Kind | Operation (module) |
|---|---|
| get by id | GetEmployee, GetCompensationHistory (Employees); GetDepartment; GetAsset (Assets) |
| update by id | UpdateDepartment (Employees); UpdateAssetCategory (Assets) |
| delete / archive | DeactivateDepartment; DeactivateAssetCategory |
| direct EF delete | DeleteFutureCompensationRecord (foreign record with foreign employee, and with the caller's own employee) |
| bulk | BulkApplyCompensationAdjustments (Employees); QueueInvitationBatch (Identity) with foreign employee ids |
| download | DownloadEmployeeDocument (Documents), asserts no redirect / storage URL is issued |
| background operation | GetInvitationBatchStatus / RetryInvitationBatch (Identity): foreign batch id, asserts no Hangfire job is enqueued |
| direct update | DisableUser (Identity) |

## Inventory result (task item 6)

* All module `DbContext` entities were enumerated from the EF model (see the guardrail test, which is
  the authoritative, always-current inventory). About 120 entity sets are directly tenant-keyed by
  `company_id`; the remainder are children reached through a required FK to a tenant-owned parent, or
  the documented non-tenant entities below.
* Every feature handler was scanned (statement-level) for queries against tenant-owned sets that
  filter by id without any `CompanyId` constraint. All hits were one of: child rows loaded through
  an already company-validated parent, FK lookups from a row that was itself loaded with `CompanyId`
  (names, categories, departments), or the load-then-compare pattern (`entity.CompanyId !=
  request.CompanyId` immediately after load, e.g. invitation batches, organisation data exports).
  Identity join tables are reached only after `ITargetUserCompanyGuard` / employee-reader checks.
  **No handler that loads or mutates a foreign-tenant resource by id was found; no production code
  change was required.**
* Style note: the load-then-compare pattern is safe but returns the entity into memory before the
  check; prefer `CompanyId` in the predicate for new code.

## Legitimate exceptions (documented)

| Category | Where | Why safe / how bounded |
|---|---|---|
| Platform-admin operations | Endpoints with policy `platform:admin`: `/api/companies/admin/*`, `/api/platform-admin/*`, `/api/platform-administrators/*`, `/api/notifications/admin/*`, `/api/marketing/admin/*`; some contain `{companyId}` naming the customer administered | Exempt in `TenantRouteAuthorizationMiddleware` and `RequireTenantMiddleware`. Access gated by the platform administrator policy (and MFA). Any support impersonation goes through Support Sessions with audit. |
| Public / anonymous workflows | `/api/signup`, `/api/verify-email`, `/api/reset-password`, `/api/resend-verification`, `/api/public/subscription-pricing`, `/api/nationalities`, `AcceptInvite`, Login/Logout | No tenant context by definition; they act on a token or on global reference data and are rate limited. |
| Global / platform entities | `Company`, `Nationality`, `Permission`, `Role`, `RolePermission`, `PlatformAdministrator`, `SessionRevocation`, `PlatformMetricsSnapshot`, `PlatformSettings`, Marketing content, maintenance progress rows (full list with reasons in `TenantIsolationGuardrailTests.GlobalEntities`) | Not customer data. |
| Identity join tables | `UserRole`, `UserPosition`, `PositionRole`, `ApplicationUser`, `InvitationBatchRecipient` | Keyed by user/position/batch id; reached only after the target user / parent has been proven to belong to the route company. |
| Migrations and seeders | `*Module.cs` seed methods, EF migrations, `Dev` endpoints | Run at startup with no tenant; dev endpoints are loopback-only. |
| Cross-company maintenance jobs | `*/Jobs/*` (`IdempotencyMaintenanceJob`, `Reconcile*Job`, `Purge*Job`, `AccountDisablementJob`, ...) | Hangfire, system principal. Jobs receive an operation/resource id created by a tenant-validated request and load by that id. |
| Background jobs iterating companies | Reminder/rollover jobs (`LeaveYearRolloverJob`, `ToilExpiryJob`, `DocumentExpiryReminderJob`, `AssetReminderJob`, `OnboardingReminderJob`, `OffboardingReminderJob`, `GenerateDueProbationReviewsJob`, `ProcessLeavingEmployeesJob`, ...) | Iterate tenants explicitly; per-company work must still pass that company's id to queries. Audit events carry `CompanyId`. |
| Endpoints that take the company from `Route<>` not the request | `CompleteInitialEmployeeSetup` (allow-listed in `RequestsWithoutCompanyId`) and `EmptyRequest` endpoints | Read the route value in the endpoint and pass it to the handler. **Not machine-checkable by request type** (see uncovered surfaces). |

## Architectural decision: EF global query filters and PostgreSQL RLS

Not introduced. Evaluation and recommendation follow.

### Option A: EF Core global query filters (`HasQueryFilter(e => e.CompanyId == tenant)`)

* Platform-admin bypass: `IgnoreQueryFilters()` at every platform query, or a second unfiltered
  context. Forgetting it fails closed (returns nothing), which is the safe direction. Auditing of
  deliberate bypass is straightforward because `IgnoreQueryFilters` is greppable and can be checked by an
  architecture test.
* Background-job tenant context: jobs have no HTTP principal. Requires an explicit ambient
  `ITenantContext` set by a job wrapper per company; the many cross-company jobs above would each
  need an explicit "all tenants" scope. Highest-risk part of the change.
* Migrations / seeding: filters apply to seeders and data fixes running through the context, so seed code
  needs `IgnoreQueryFilters()` or a system scope. Migrations themselves are not affected.
* Pooled connections: filters are evaluated in-process from the scoped context, so pooling is safe.
  The filter captures the tenant per `DbContext` instance; `AddDbContextPool` would need care
  because the tenant must be re-read per lease (use a context property, not a captured value).
* Gaps: does not cover raw SQL, `ExecuteUpdate/Delete` (they honour filters, `FromSql` does not),
  `Find` by key on tracked entities, or joins built against `IgnoreQueryFilters`.
  Filter-by-parent for children needs navigation filters or a `company_id` on every child.
* Leakage tests: one integration test class per module asserting that a context scoped to A never
  returns B rows (the cross-tenant tests in this change are the template).

### Option B: PostgreSQL row-level security (RLS)

* Defence in depth that also protects raw SQL, reports and any future non-EF path.
* Pooled connection safety is the main hazard: the tenant must be set per transaction
  (`SET LOCAL app.company_id = ...`) and never per session, otherwise a pooled connection can carry the
  previous request's tenant. Requires a connection/transaction interceptor and every write path to
  run in an explicit transaction. With Supabase's transaction pooler `SET LOCAL` is the only safe form.
* Platform-admin bypass: a dedicated Postgres role with `BYPASSRLS`, or a policy branch on
  `app.is_platform_admin`. Every use should be audited (log role switches).
* Background jobs, migrations and seeding: run as the owner/`BYPASSRLS` role, or set
  `app.company_id` per company inside the job. The application role must not own tables (owners bypass RLS
  unless `FORCE ROW LEVEL SECURITY`).
* Operational cost: about 120 policies plus child-table policies via joins (slow) or denormalised `company_id`;
  migration authoring changes (policies in every migration); harder local debugging; the Supabase Data
  API rules in `05-database-standards.md` already keep module data server-only, so RLS would protect
  only against application bugs, not exposure.
* Leakage tests: integration tests that run through the real pool with interleaved requests for two
  tenants to prove no tenant carry-over.

### Recommendation

1. **Now (done):** keep explicit `CompanyId` predicates, and rely on the metadata guardrails and
   cross-tenant tests in this document. Cheap, no runtime behaviour change, and it fails the build
   on new unclassified entities and on endpoints that cannot carry the tenant.
2. **Next (recommended, separate ticket):** add EF global query filters for the ~120 directly
   tenant-keyed entities behind an `ITenantContext` (HTTP request and explicit job scope), with
   `IgnoreQueryFilters()` restricted, via an architecture test, to platform-admin features and jobs on an
   allow-list. This turns a forgotten predicate from a data leak into an empty result. Roll out module by
   module, starting with a read-only "log when the filter would have differed" mode.
3. **Later, only if compliance demands:** RLS as a second layer, once filters exist and a
   transaction-scoped `SET LOCAL` interceptor is proven under the Supabase pooler. RLS alone is not
   recommended because of pooled-connection risk and the volume of policies.

## Uncovered surfaces / follow-ups

* The cross-tenant integration suite samples about 12 operations across Employees, Assets, Identity and
  Documents. The other roughly 400 company-scoped endpoints rely on the handler-level review above and the
  structural guardrails, not on a per-endpoint cross-tenant test. Recommended next modules to add
  coverage: Leave (requests, balances), Recruitment (candidates, applications), Sickness, Probation,
  Tasks, Notifications, Reporting exports and Support requests.
* `EmptyRequest` / `EndpointWithoutRequest` company-scoped endpoints (about 24, mostly `GetMy*` and
  `*AuditHistory`) read `Route<Guid>("companyId")` and cannot be validated by request type. A
  per-endpoint check would need a handler-signature convention.
* The guardrails verify entity shape and request shape, not that a handler actually uses
  `CompanyId` in every query. That remains a code-review and cross-tenant-test concern until query
  filters (above) exist.
* Background job handlers load by operation or resource id without a `CompanyId` predicate (for
  example `AccountDisablementJob`, `InviteAcceptanceReconciliationJob`, `TaskCompletionEffectsJob`,
  `ScanCandidateDocumentJob`, `EmailDeliveryJob`). They are system-principal and receive ids produced
  by tenant-validated requests. If the ids ever become caller-controlled they need a company check.
* Load-then-compare handlers (invitation batch status/retry, organisation data export download)
  should move `CompanyId` into the query predicate.
* Nullable `ProcessedStripeEvent.CompanyId` is a documented exception.
