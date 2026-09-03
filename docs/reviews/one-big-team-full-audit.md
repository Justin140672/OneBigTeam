# One Big Team — Full Repository Quality and Specification Audit

**Audit date:** 2026-07-11  
**Reviewed revision:** `5c9b48a` plus the pre-existing uncommitted working tree listed under Limitations  
**Method:** Read-only review of source, tests, specifications, migrations, project structure, and relevant Git history. The only repository change made by this audit is this report.

## Decision list — product-owner confirmation required

**None outstanding as of 2026-07-11.** The decisions originally raised by this audit were reviewed with the product owner and are recorded in section 7.

## 1. Executive summary

The repository has a recognisable modular-monolith structure, strong feature-level test volume, explicit tenant route checking, and passing architecture/module/component tests. It is **not presently safe for production use**. The primary reason is not cross-tenant route substitution—the dedicated middleware prevents that demonstrably—but missing same-tenant object and scope authorisation on several sensitive endpoints. Any authenticated employee can currently request another employee’s full personal/HR record, task details and leave request details by identifier. Notification mutation endpoints similarly do not prove ownership.

The Release build succeeds with 31 warnings. A high-severity vulnerable `Newtonsoft.Json` 11.0.1 is present transitively. All 899 tests whose projects completed in the full run passed with no skips. The 292 E2E tests did not reach test bodies because the shared Aspire fixture failed to allocate `web-http`; the isolated integration suite subsequently hung until the five-minute audit timeout. Coverage collection also hung and therefore no defensible percentages are available.

The specification corpus is extensive but has no dates, versions, statuses, ADR index, or supersession markers. It mandates items displaced by the confirmed product decisions (outbox and universal dashboards) and simultaneously describes both early and later versions of several domains. The decisions are now known, but they should be placed in a dated repository decision register so future work can distinguish intentional evolution from drift.

### Five greatest risks

1. Same-tenant horizontal disclosure of complete employee PII and HR notes (`SEC-001`).
2. Same-tenant disclosure of other employees’ tasks, leave and related workflow data (`SEC-002`).
3. Notification ownership is not enforced for read-state mutations (`SEC-003`).
4. Background notification/event processing is non-atomic and subject to duplicate side effects on retries or concurrency (`JOB-001`).
5. The only end-to-end and database-backed verification layers are currently non-runnable, leaving security and persistence regressions undetected (`TST-001`).

## 2. Repository and module map

| Area | Projects/modules | Ownership observed |
|---|---|---|
| Hosts | `HR.AppHost`, `HR.Api`, `HR.Web`, `HR.ServiceDefaults` | Aspire orchestration; FastEndpoints API composition; Blazor Server UI |
| Shared | `HR.SharedKernel` | Results, clock/current-user contracts, integration/audit event contracts |
| Infrastructure | `HR.Infrastructure`, `HR.Infrastructure.Abstractions` | Audit persistence, Hangfire, logging, email, cross-module ports |
| Business modules | Companies, Identity, Employees, Leave, Tasks, Notifications, Documents, Assets, Recruitment, Probation, Sickness, DataImport, Onboarding | Separate projects and PostgreSQL schemas/DbContexts; vertical-slice feature folders |
| Tests | 13 projects in `OneBigTeam.slnx`, plus `HR.Modules.Sickness.Tests` present on disk but omitted from the solution; Integration, Web component, Architecture, and Web E2E projects | xUnit, EF in-memory tests, Testcontainers PostgreSQL, bUnit, Playwright/Aspire |

No `AGENTS.md` exists in the repository. `implementation-guide.md` points to a non-existent `specifications/product/*` path; the actual path is `specifications/product-specifications/*`. Generated EF migration designer/snapshot files and `wwwroot/lib/fontawesome` were treated as generated/third-party material and reviewed only for schema/dependency implications.

Project references preserve the primary rule that business modules do not reference one another. Inter-module behaviour uses shared events and infrastructure abstractions. Architecture tests enforce this boundary.

## 3. Commands executed and results

| Command | Result | Interpretation |
|---|---|---|
| `git status --short`; project/doc inventory; `git log` and targeted history | Succeeded | Working tree was already materially dirty; audit used current files and did not overwrite them. |
| `dotnet restore OneBigTeam.slnx --locked-mode` (sandbox) | Failed | Environmental: user NuGet config was inaccessible. |
| Same restore with approved user-package access | Succeeded with NU1903 | Repository resolves; vulnerable `Newtonsoft.Json` 11.0.1 reported. No lock files were found, so `--locked-mode` did not provide reproducibility. |
| `dotnet build OneBigTeam.slnx --no-restore -c Release` | Succeeded: 0 errors, 31 warnings | Buildable, but warnings include dependency version conflicts, nullable/Blazor diagnostics and test analyzer issues. |
| `dotnet test OneBigTeam.slnx --no-build -c Release` | Failed after 135 s | 899 completed tests passed; E2E fixture startup failed before tests with `Service web-http should have valid address at this point`. |
| `dotnet test tests/HR.Integration.Tests/...` | Timed out after 304 s | Environmental/tooling limitation or test-harness hang; no trustworthy integration result. |
| Non-E2E coverage collection with XPlat collector | Terminated after runner hang | No trustworthy coverage output; percentages intentionally not estimated. |
| `dotnet format OneBigTeam.slnx --verify-no-changes --no-restore` | Failed | Thousands of whitespace diagnostics across production and tests; formatter also reported workspace-load warnings. |
| Repository-wide `rg` security, endpoint, query-filter, secret, job, TODO and skip scans | Succeeded | No committed production credential was found. Local PostgreSQL design-time defaults are development credentials. |

## 4. Build and test status

- Restore: pass after environmental access approval; NU1903 remains.
- Release build: pass, 31 warnings, 0 errors.
- Architecture tests: 16/16 pass.
- Module tests reported in the full run: 811 pass (Assets 160, Companies 64, DataImport 102, Documents 7, Employees 65, Leave 105, Notifications 5, Onboarding 14, Probation 20, Recruitment 94, Tasks 175).
- Blazor/component tests: 72/72 pass.
- Total completed: 899 passed, 0 failed, 0 skipped.
- `HR.Modules.Sickness.Tests`: omitted from `OneBigTeam.slnx`, so it was not run by the solution command.
- Integration: indeterminate; isolated run timed out.
- E2E: 292 discovered statically across 71 files; all fail at shared fixture startup, before assertions.

The passing 899 tests do not validate HTTP authorisation for the vulnerable endpoints identified below. Many module tests invoke handlers directly and therefore cannot detect endpoint policy/scope defects.

## 5. Coverage summary

Overall line coverage, branch coverage, per-project coverage, and partially covered branches are **unavailable**. The repository includes `coverlet.collector`, but the audit collection hung. No checked-in coverage baseline or CI coverage artifact exists. Reporting a percentage would be misleading.

Static test-to-code inspection indicates comparatively dense handler/domain coverage in Tasks, Assets, Leave and Recruitment, but only 5 Notifications tests and 7 Documents tests. Critical endpoint authorisation, job retry atomicity, production JWT validation, migrations, and real PostgreSQL constraints have insufficient behavioural coverage. `HR.Modules.Sickness.Tests` not being in the solution makes its effective CI coverage zero unless CI invokes it explicitly (no evidence found).

## 6. Specification map

| Domain | Most recent apparent requirement | Evidence | Conflict | Confidence | Confirmation? |
|---|---|---|---|---|---|
| Tenancy | Every request and record is company-scoped; route company must match authenticated tenant | Supplied constraints; architecture 05/06; middleware added and security tests | Some same-tenant scope checks absent | High | No for tenant match; yes for role scopes |
| Roles | Employee, Manager, HR, Recruitment, Company Admin remain distinct; Company Admin is not HR | Supplied constraints; product specs 01, 26, 29; recent role tests/history | Multiple endpoints use only `authenticated` | High | No |
| Employees | Recruitment is the normal path; HR may directly create exceptional new starters, who receive onboarding; imports represent an existing workforce and do not trigger onboarding | Product-owner confirmation 2026-07-11; recent recruitment/onboarding/import commits | Older wording implied all new employees must originate in recruitment | High | No — resolved |
| Data import | Departments, locations, positions and employees imported together; imported employees are not starters | Supplied constraints; commits 9bfa420–8fc486c | API still models selectable entity type/session flows | Medium | Yes |
| Leave | Days are the canonical persisted/calculation unit for every leave type, including TOIL; hours are derived for display/input where appropriate | Product-owner confirmation 2026-07-11; `LeaveBalance`, `LeaveRequest.TotalDays`, `ToilTransaction.Days`, `LeaveCalculator`; TOIL adjustment conversion tests | Earlier supplied review context said leave was recorded in hours | High | No — resolved |
| Tasks | Central workflow and approval mechanism | Supplied constraints; product spec 20; extensive implementation | Read/complete scope enforcement inconsistent | High | Scope only |
| Dashboards | Employees land on their profile; Company Administrators without operational roles land in company administration; Manager, HR and Recruitment dashboards are the next ticket; multi-role users receive a combined/prioritised dashboard | Product-owner confirmation 2026-07-11; recent dashboard/UI commits | Product spec 30 mandates dashboards for all roles | High | No — resolved |
| Reporting | Syncfusion grid exports are implemented in individual screens; formal reporting is a separate, upcoming workstream | Product-owner confirmation 2026-07-11 | Specs 01, 23, 32 do not distinguish implemented grid export from upcoming formal reporting clearly | High | No — resolved |
| Outbox | Not required for v1; workflows must still be transactional/idempotent where needed | Product-owner confirmation 2026-07-11; no outbox implementation exists | Architecture 01/04/05/07/10 mandates it | High | No — resolved supersession |
| Pay processing | Outside scope | Supplied constraint; product spec 01 | Reporting supports authorised compensation exports only | Medium | Keep processing outside the product |
| Integrations | Supabase auth/db/storage, Hangfire, Postmark; no Redis | Supplied constraints; project packages/code | Product spec 01 says “external integrations” out of MVP while architecture specifies them | High | Documentation cleanup |

## 7. Specification conflicts and confirmed product decisions

No product decision remains outstanding from this audit. On 2026-07-11 the product owner confirmed:

1. Company creation occurs only once during initial signup/platform onboarding. A user who already belongs to a company cannot create another; Company Administrators manage their existing company only.
2. HR may view all employee fields company-wide. Managers may view employee records for everyone beneath them in the complete reporting hierarchy. Employees may view only their own detailed record. Company Administrators without HR and recruitment-only users receive no general detailed employee-record access.
3. HR always sees salary. Employees see their own salary, and managers see salaries for their reporting hierarchy, only when `DisplaySalaryOnEmployeeProfile` is enabled. Company Administrators without HR and recruitment-only users do not see salary.
4. Days are canonical storage for every leave type, including TOIL. Hours are derived for display/input. TOIL adjustment converts hours to canonical days; the API-only Award TOIL contract explicitly accepts days.
5. An outbox is not required for v1. Transactions, uniqueness and idempotency remain required where workflows can retry or partially fail.
6. Syncfusion grid exports are already implemented within screens. Formal reporting is a separate upcoming workstream.
7. Employees land on their profile; Company Administrators without an operational role land in company administration; Manager, HR and Recruitment role-specific dashboards are the next ticket; multi-role users receive a suitable combined or prioritised dashboard.
8. Recruitment is the normal new-hire path. HR may directly create an employee in exceptional cases; that employee receives normal onboarding. Imported employees represent an existing workforce and do not trigger onboarding.

The underlying governance problem remains: none of the 43 specification files carries a date, owner, approval status, version, or “supersedes” relationship. These confirmations should be copied into a repository decision register referencing the implementing commits.

Classification examples:

- **Clearly superseded:** mandatory outbox and universal dashboard statements conflict with explicit current review constraints.
- **Likely intentional change:** reporting deferral and role-specific/direct landing behaviour are consistent with current constraints and recent UI direction, but specs remain stale.
- **Likely implementation defect:** full employee detail and arbitrary other-employee task/leave access contradict both old and current role/scope principles.
- **Resolved product decisions:** company creation authority, employee field/salary access, manager hierarchy depth, leave units, outbox deferral, reporting phase, landing pages, and direct employee creation/import behaviour.
- **Documentation/test drift:** `implementation-guide.md` path, solution omission of sickness tests, outbox/reporting/dashboard acceptance criteria.

## 8. Prioritised confirmed defects

### SEC-001 — Full employee PII exposed to any same-company authenticated user

- **Severity / confidence:** High / High
- **Category / module:** Authorisation, privacy / Employees
- **Location:** `src/Modules/HR.Modules.Employees/Features/GetEmployee/Endpoint.cs:11-20`; `Handler.cs:20-80`; `Response.cs:18-47`
- **Description:** The endpoint requires only `authenticated`. It accepts any employee ID and returns work/personal email, DOB, gender, phones, home address, employment dates, system access, and free-form notes. It checks company but not self, manager hierarchy, HR permission, or field-level permission.
- **Why it matters:** A normal employee or non-HR Company Administrator can enumerate colleagues and retrieve sensitive HR/PII.
- **Evidence:** Architecture 06:242-260 requires permission and scope after same-company validation; product spec 26:92-98 defines self/direct-report scope; current review constraint separates Company Admin and HR.
- **Scenario:** An employee obtains a colleague ID from a list/task and calls `GET /api/companies/{ownCompany}/employees/{colleague}`; the handler returns the full record.
- **Exploitation:** Demonstrably possible from code; tenant boundary prevents cross-company use.
- **Correction:** Split self, scoped summary, and HR-detail contracts; enforce endpoint permission plus a central resource-scope authorisation check. Default to least privilege.
- **Tests:** HTTP tests for employee self vs peer, manager direct report vs non-report, recruiter, Company Admin without HR, HR, and cross-company; assert sensitive fields are absent in summary contracts.
- **Safe without decision?:** Yes. The field/scope matrix is confirmed in section 7.

### SEC-002 — Other employees’ tasks and leave details readable by ID

- **Severity / confidence:** High / High
- **Category / modules:** IDOR / Tasks, Leave (and analogous authenticated employee-resource endpoints)
- **Location:** `Tasks/Features/GetEmployeeTasks/Endpoint.cs:10-17`, `Handler.cs:14-53`; `Leave/Features/GetLeaveRequest/Endpoint.cs:11-20`, `Handler.cs:13-32`
- **Description:** Both endpoints require only authentication and trust route `employeeId`. Company filtering exists, but ownership/manager/HR scope does not. Task responses include description, action/source and workflow entity IDs; leave includes reason and rejection reason.
- **Why it matters:** Workflow and absence information is sensitive and can reveal medical/personal circumstances.
- **Scenario:** A same-company employee cycles known employee/task IDs and reads colleagues’ tasks or leave reasons.
- **Exploitation:** Demonstrably possible; prevented cross-tenant by middleware.
- **Correction:** Use self endpoints for self data; require task/leave permission plus resource-scope evaluation for other employees. Review every endpoint listed as `Policies("authenticated")` using the same route pattern.
- **Tests:** Table-driven HTTP matrix across roles and relationship scopes, including guessed valid IDs and non-existent IDs.
- **Safe without decision?:** Yes. Managers receive full downward-hierarchy scope.

### SEC-003 — Notification read-state mutations do not enforce ownership

- **Severity / confidence:** Medium / High
- **Category / module:** IDOR, integrity / Notifications
- **Location:** `MarkNotificationRead/Endpoint.cs:11-17`, `Handler.cs:11-20`; `MarkAllNotificationsRead/Endpoint.cs:10-17`, `Handler.cs:10-19`
- **Description:** Any authenticated tenant member can mark a notification read if its ID is known, and can mark all notifications for any route employee ID. Neither path compares the notification/employee to authenticated `sub`.
- **Evidence:** Product spec 22:189-196 explicitly says users view and mark only their own notifications.
- **Scenario:** An employee marks an HR user’s action-required notifications read, suppressing attention indicators.
- **Exploitation:** Demonstrably possible within a tenant; notification UUID knowledge is required for the single-item endpoint, while employee IDs are broadly exposed for mark-all.
- **Correction:** Derive employee/user ID from authenticated context, not route input; include owner in the single-item predicate. Consider immutable acknowledgement audit.
- **Tests:** HTTP tests for own/other notification and mark-all; verify state unchanged on 403/404.
- **Safe without decision?:** Yes.

### SEC-004 — Team-onboarding data can be requested for an arbitrary manager

- **Severity / confidence:** High / High
- **Category / module:** Authorisation / Onboarding
- **Location:** `GetTeamOnboarding/Endpoint.cs:11-20`; `Handler.cs:17-31`
- **Description:** An endpoint introduced in the current uncommitted working tree requires only authentication and trusts route `managerId`; it does not establish that the caller is that manager or HR.
- **Scenario:** Any tenant employee supplies an HR/manager employee ID and obtains names, dates and onboarding progress for that manager’s direct reports.
- **Exploitation:** Demonstrably possible in the reviewed working tree; not yet in `HEAD`.
- **Correction/tests:** Derive manager identity for self-service; separately authorise HR company scope. Add negative endpoint tests.
- **Safe without decision?:** Yes. Self-manager hierarchy and HR scope are confirmed.

### DEP-001 — Known high-severity vulnerable Newtonsoft.Json 11.0.1

- **Severity / confidence:** High / High
- **Category / module:** Supply chain / API dependency graph
- **Location:** restore/build NU1903 for `src/HR.Api/HR.Api.csproj`
- **Description:** NuGet reports GHSA-5crp-9r3c-p9vr for resolved Newtonsoft.Json 11.0.1.
- **Why it matters:** The affected package version has a known high-severity advisory; reachability was not established in this audit, so this is not rated Critical.
- **Correction:** Use `dotnet nuget why`/assets file to identify the parent, upgrade or centrally override to a non-vulnerable compatible release, and add vulnerability scanning that fails on High/Critical.
- **Tests:** Full build/test plus serialization/integration checks for the owning package.
- **Safe without decision?:** Yes, subject to compatibility verification.

### OPS-001 — Startup continues after module migration/seed failure

- **Severity / confidence:** High / High
- **Category / module:** Reliability, persistence / API composition
- **Location:** `src/HR.Api/Program.cs:114-306`, app continues at 412-430
- **Description:** Each migration is caught independently and only recorded in a health response; the process continues to schedule jobs and accept API traffic with partially migrated modules.
- **Scenario:** Recruitment migration fails but other migrations pass; Hangfire jobs and endpoints run against a missing/old schema, producing runtime errors or partial workflows.
- **Correction:** Fail startup for required schema failures, or gate readiness and all traffic/jobs until every required migration succeeds. Separate migration execution from application replicas for production.
- **Tests:** Startup test with one deliberately failing migration; assert no endpoint/job readiness.
- **Safe without decision?:** Yes.

## 9. Multi-tenancy and security findings

The known route-company risk is **prevented elsewhere**: `TenantRouteAuthorizationMiddleware.cs:20-35` compares `{companyId}` with the authenticated `company_id` claim and returns 403, and it is ordered after authentication/routing. This protection applies only to the exact route key `companyId`; routes without that key and same-tenant object scope still require endpoint/resource authorisation.

Additional observations:

- **SEC-005 (High, High):** `CreateCompany` (`Companies/Features/CreateCompany/Endpoint.cs:11-19`) is available to any authenticated tenant user, contrary to the confirmed one-time initial-signup rule. Restrict it to the dedicated onboarding flow and test that already-tenant-bound users are forbidden.
- `SendInvite` removes prior invites by employee ID without including company (`Identity/Features/SendInvite/Endpoint.cs:30-36`). The route-company middleware prevents a cross-tenant route attack, but the query should still include company ownership as defence in depth and validate that employee/email belong together. Potentially possible if IDs collide or internal invocation bypasses routing.
- JWT bearer registration (`HR.Api/Program.cs:57-62`) has no visible issuer/audience validation code; it may be supplied through configuration. Production configuration was not available, so this is an investigation item, not a confirmed defect. Add a startup validation/integration test for issuer, audience, signature, expiry, and claim mapping.
- No committed Supabase service-role key, Postmark key, private key, or production password was found.
- Storage paths inspected use company identifiers; cross-tenant download/delete handlers still need ownership tests across all buckets.
- `BackgroundJobFailedAuditEvent` sets `CompanyId = Guid.Empty` and serializes raw exception messages (`Infrastructure/BackgroundJobs/BackgroundJobFailedAuditEvent.cs:28-48`). This loses tenant ownership and may persist PII/secrets from exceptions. Medium severity, high confidence. Carry tenant context when available and redact exception details; keep operational detail in secured logs.

## 10. Architecture findings

Positive: business project references do not cross module boundaries; 16 architecture tests pass; feature folders generally follow vertical slices; no MediatR, Redis, microservice split, or generic repository was introduced.

Findings:

- **ARC-001 (Medium, High):** `HR.Api/Program.cs` is a 430-line composition root containing repeated per-module migration state and error handling. Duplication already creates inconsistent seed/migration ordering risk. Replace with module migration descriptors in Infrastructure while preserving module ownership; do not add a general business repository.
- **ARC-002 (Medium, High):** Authorisation is not a consistently reusable resource-scope abstraction. Endpoint policy names cover roles, but self/direct-report/company checks are duplicated or omitted. Introduce an explicit scope-authorisation port in Identity/Infrastructure abstractions and keep business-resource lookup in owning modules.
- **ARC-003 (Low, High):** `implementation-guide.md` references the wrong product-spec path and is escaped as literal Markdown, weakening agent guidance.
- **ARC-004 (Medium, High):** `HR.Modules.Sickness.Tests` exists but is absent from `OneBigTeam.slnx`, so the standard solution build/test does not compile/run it.
- **ARC-005 (Low, High):** Release build resolves EF Core Relational 10.0.4 in several tests against production 10.0.9. Align package versions centrally to ensure tests execute the production provider surface.

## 11. Business-logic findings

- **BUS-001 (Informational, High; resolved):** Days are intentionally canonical for all leave types, including TOIL. `LeaveCalculator` may calculate in hours internally but stores days; TOIL balance adjustment accepts an hours-shaped UI value, converts it using the effective working pattern, persists canonical days, and optionally retains `AdjustmentHours` for audit/display. `AwardToilRequest.Days` is also internally consistent: there is currently no Award TOIL UI, and its API contract explicitly accepts days. Any future hours-labelled Award TOIL UI must perform the same hours-to-days conversion before submitting or the API contract should be changed explicitly.
- **BUS-002 (Informational, High; resolved):** Direct employee creation is an HR-only exceptional new-starter path and must trigger normal onboarding. Candidate hires do likewise. Imported employees are existing workforce and must not trigger onboarding. Add explicit source/audit and regression tests to preserve this distinction.
- **BUS-003 (Medium, High):** Invalid optional task status strings are silently treated as “all statuses” (`GetEmployeeTasks/Handler.cs:18-22`, analogous handlers). A typo can expose more history than requested and masks client defects. Return 422 for invalid enum values.
- **BUS-004 (Medium, High):** `SendInvite` commits and returns the raw invite token even after email failure (`Endpoint.cs:38-60`) and logs the email address. Manual-link behaviour may be intentional, but tokens should be returned only to a specifically authorised admin contract and email logs should be classified/redacted.

## 12. Persistence findings

- **PER-001 (High, High):** Notification job idempotency is check-then-insert (`ExistsAsync` then `WriteAsync`) with no demonstrated unique constraint. Concurrent Hangfire execution/retry can create duplicates. Add a unique key such as `(company_id, employee_id, source_entity_id, type)` and treat conflict as success.
- **PER-002 (High, High):** Several jobs publish notifications/events before saving state. `SicknessEvidenceReminderJob.cs:70-97` writes notification and publishes event, then saves `Overdue`; failure between these operations causes duplicates on retry and inconsistent state. Use an owning-module transaction/idempotency record; an outbox is not required to correct this in v1.
- **PER-003 (Medium, High):** Many list handlers are unbounded, including employee tasks, assets/assignments, document requests, leave requests/history, import sessions, recruitment lists and sickness lists. Add request pagination to user-facing history/list endpoints based on their actual query paths; reference examples include `Tasks/GetEmployeeTasks/Handler.cs:24-28` and `DataImport/ListImportSessions/Handler.cs`.
- **PER-004 (Medium, Medium):** Startup runs migrations from every application start and catches failures. Multi-replica concurrency and partial schema deployment are unsafe (see OPS-001).
- Tenant predicates are present in the inspected entity handlers; no `IgnoreQueryFilters` was found. However, there are also no global tenant query filters, so one omitted predicate remains enough to leak data. Retain explicit predicates and add architecture/security tests rather than relying solely on global filters.
- Default local design-time connection strings use `postgres/postgres`; these are development-only and not a secret, but production must require configuration with no fallback.

## 13. API findings

- **API-001 (High):** SEC-001/002/003/004 show inconsistent endpoint authorisation.
- **API-002 (Medium, High):** All FastEndpoints validation errors are globally forced to 422 (`HR.Api/Program.cs:423-427`) while handlers variously return 400, 404, 403 and 422 for similar errors. Define error semantics and a stable problem contract.
- **API-003 (Medium, High):** Many list endpoints do not paginate; see PER-003.
- **API-004 (Medium, High):** Resource requests bind tenant/employee IDs directly from routes and then pass them into handlers. Tenant route middleware is effective, but self/resource IDs should be derived from claims where the operation is explicitly “my”.
- **API-005 (Low, High):** Cancellation is generally propagated in endpoint/EF paths, but background jobs expose parameterless `ExecuteAsync()` and use `CancellationToken.None`; graceful shutdown cannot stop long scans or external writes.

## 14. UI findings

Static review finds role-aware navigation and E2E cases for Company Administrator separation, but UI route guards cannot compensate for the vulnerable APIs. The Web application contains client services that call the full employee endpoints; server enforcement must be corrected first.

- **UI-001 (High, High):** UI hides/redirects unauthorised pages, but SEC-001/002 prove the API still serves data. Direct HTTP calls bypass Blazor navigation.
- **UI-002 (Medium, High):** E2E fixture uses a global mutable dev persona shared by Blazor Server circuits (`E2ETestBase.cs:7-43`), fixed 3-second teardown delays, and a single shared app. This creates order dependence and explains comments about identity bleeding between tests.
- **UI-003 (Low, High):** Build warning at `CompanyProfileTab.razor:14` indicates possible null dereference.
- **UI-004 (Low, High):** `ExportMenu.razor:12-14` constructs component parameter objects in a way triggering nine BL0005 warnings.
- Accessibility is mentioned in specs, but no automated accessibility tooling or assertions were found. Add axe-style checks to a small set of representative E2E pages after the harness is repaired.

## 15. Background-job and integration findings

- **JOB-001 (High, High):** Retry/concurrency duplicate risks described in PER-001/002.
- **JOB-002 (Medium, High):** Recurring jobs scan all tenants in one unbounded execution with no cancellation (`OnboardingReminderJob.cs:15-58`, `AssetReminderJob.cs:15-143`, `InterviewReminderJob.cs:24-54`). Tenant IDs are carried into writes, so cross-tenant writes are not demonstrated, but one large tenant can delay all others and failure/retry scope is global. Batch by tenant/page and accept Hangfire cancellation tokens.
- **JOB-003 (Medium, High):** `BackgroundJobFailedAuditEvent` lacks tenant ownership and persists raw exception text (security section).
- Postmark/Supabase failure paths generally catch storage cleanup or email failure, but cleanup catches are silent. Emit redacted structured operational events and metrics.
- `SendInvite` saves before email, intentionally preserving the invite. This is retry-safe only if repeated endpoint submission is serialized; current remove/add flow can race.

## 16. Test-quality findings

- **TST-001 (High, High):** Database integration and E2E layers are not currently runnable in this environment. The E2E failure is deterministic at `AppFixture.cs:25-32`: Aspire cannot resolve `web-http` before Playwright launches.
- **TST-002 (High, High):** Handler-heavy tests bypass endpoint policies and cannot catch SEC-001–004. Security tests exist for selected features, but there is no systematic endpoint matrix.
- **TST-003 (Medium, High):** E2E tests share persistent seeded data and mutable persona/application state, use visible Chromium and `SlowMo=100`, terminate every other `testhost` process (`AppFixture.cs:18-20, 50-55, 65-74`), and sleep three seconds after every test. They are slow, destructive to parallel test sessions, and order/flakiness prone.
- **TST-004 (Medium, High):** `HR.Modules.Sickness.Tests` is omitted from the solution.
- **TST-005 (Medium, High):** No skipped tests were found, but “not skipped” is not meaningful when an entire project fails before test bodies.
- **TST-006 (Low, High):** Build reports seven xUnit analyzer warnings in integration tests; fix assertions to keep analyzer output actionable.
- Several E2E assertions accept broad alternative text (for example task/notification tests accept multiple unrelated fragments), allowing incorrect records to satisfy the test. Prefer unique IDs created by the test and exact state assertions.

### E2E harness repair recommendation

1. In a dedicated test AppHost configuration, explicitly declare a single HTTP endpoint for `api` and `web`, or disable launch-profile inference; then call `GetEndpoint("web", "http")` consistently. The current multi-profile launch settings produce inferred `web-http`/HTTPS endpoints that the test DCP cannot allocate.
2. Run Chromium headless by default; make headed/SlowMo opt-in through environment variables.
3. Replace global persona switching with per-browser-context auth headers/cookies or an isolated app per collection.
4. Remove `KillStaleTestHosts`; use deterministic fixture disposal, unique ports/resource names, and bounded startup cancellation.
5. Fail the readiness loop explicitly after three minutes; it currently continues to browser launch even if readiness never succeeds.
6. First prove one smoke test, then tenant-isolation and role tests, then enable the full suite with collection parallelisation deliberately configured.

## 17. Important missing tests

Priority order:

1. Endpoint-wide tenant/role/scope matrix generated from route metadata: cross-company, peer employee, direct report, non-report, HR, recruiter, Company Admin-without-HR.
2. Full employee-detail field exposure tests and separate summary/self contracts.
3. Notification owner mutation tests.
4. Company creation authority test for one-time signup versus already-tenant-bound callers.
5. JWT validation tests using invalid issuer/audience/signature/expiry and forged `company_id`.
6. Background job double-execution, concurrent execution and fail-between-side-effects tests.
7. Import transaction/partial-failure tests across department/location/position/employee creation and explicit “does not trigger new starter/onboarding” assertions.
8. Duplicate submission/concurrency tests for leave approval, invitations, candidate hire, employee creation and task completion.
9. Migration tests from the previous released schema on real PostgreSQL.
10. Blazor accessibility, error/empty/loading state and double-click submission tests.

## 18. Documentation drift

- Architecture docs mandate an outbox contrary to current v1 constraint.
- Product specs do not clearly separate already-implemented Syncfusion grid exports from the upcoming formal reporting workstream.
- Dashboard spec mandates Employee and Company Administrator dashboards contrary to accepted direct landing behaviour.
- Leave’s canonical-days model is now product-owner confirmed. Documentation should explicitly distinguish canonical day storage from TOIL’s hours-based display/input conversion to prevent future implementations treating `AwardToilRequest.Days` as hours.
- `implementation-guide.md` references `specifications/product/*`, which does not exist.
- Architecture 01 describes test project names/shape that do not fully match the solution; sickness tests are omitted.
- Specs do not document the new DataImport and Onboarding modules in the architecture module list consistently.
- No README explains local restore/build/test prerequisites, Docker, Playwright browser installation, or why integration/E2E may hang.
- No ADR or dated decision register records the deliberate role/dashboard/import/outbox/reporting changes.

## 19. Recommended remediation order

1. Immediately restrict SEC-001–004 and add negative HTTP tests; do not wait for broader refactoring.
2. Resolve/mitigate DEP-001 and validate production JWT configuration.
3. Repair integration/E2E harnesses and add the systematic authorisation matrix.
4. Make startup fail closed on migration failures; stop jobs until readiness.
5. Add database-enforced idempotency and atomic state/side-effect handling to jobs.
6. Publish the confirmed 2026-07-11 decisions in a dated repository decision register and update superseded specifications.
7. Implement confirmed business changes with explicit compatibility tests; no leave-unit data migration is required.
8. Add pagination, cancellation and formatting/package centralisation quality work.

## 20. Items inspected with no material problems found

- Direct module-to-module project references: none found; architecture tests pass.
- MediatR/Redis/microservice introduction: none found.
- Known cross-tenant `{companyId}` URL substitution: explicitly blocked by middleware and covered by selected tests.
- Committed production credentials/private keys: none found.
- Generated migrations use module-specific schemas and DbContexts.
- Most inspected EF handlers include company predicates and cancellation tokens.
- File upload handlers validate declared type/content and perform best-effort orphan cleanup.
- Reminder jobs attempt idempotency through existence checks, although database atomicity is missing.
- Company Administrator is excluded from several HR/Assets policies and dedicated tests exist, showing the separation is intentional in parts of the system.

## 21. Limitations of the review

- The repository was already dirty: 23 modified tracked files and numerous untracked onboarding/UI/test files plus local `.claude`/`.codex` content. This audit reviewed that current state and did not attribute those edits to a committed decision. `SEC-004` specifically concerns uncommitted code.
- Integration tests timed out and E2E tests failed in infrastructure startup; no behavioural conclusion is drawn from their failure cascade.
- Coverage collection hung; no percentages are claimed.
- Production Supabase, Postmark, database, storage, JWT and deployment configuration were not present and no external service was contacted.
- Specifications lack dates/statuses; Git commit subjects show implementation chronology but cannot prove product approval.
- This was a repository audit, not dynamic penetration testing, load testing, browser accessibility testing, or migration rehearsal against a production-sized database.
- Generated EF files and third-party Font Awesome code were not line-reviewed as application logic.

## A. Safe fixes

1. SEC-003 notification ownership enforcement.
2. SEC-004 arbitrary manager onboarding access enforcement.
3. Block peer/full employee record access pending a narrower positive field matrix.
4. Block unscoped peer task/leave reads.
5. Upgrade/override vulnerable Newtonsoft.Json after dependency tracing.
6. Fail readiness/startup on required migration failure and delay job registration.
7. Add database unique idempotency constraints for notification side effects.
8. Redact background-job audit exception messages and attach tenant where known.
9. Add Sickness tests to the solution and align EF package versions.
10. Validate invalid enum query inputs rather than broadening to all results.

## B. Product decisions required

None. All product decisions raised by this audit were confirmed on 2026-07-11 and are recorded in section 7. Implementation should reference that section until a formal repository decision record is added.

## C. Test and quality improvements

1. Repair the E2E AppHost endpoint/auth isolation design and run headless in CI.
2. Make integration fixtures fail fast with diagnostics rather than hang.
3. Add endpoint metadata-driven authorisation matrices.
4. Collect line/branch coverage per production assembly in CI, with risk-based gates for security/business paths rather than a single superficial percentage.
5. Add job retry/concurrency and partial-failure tests.
6. Add migration upgrade tests on PostgreSQL.
7. Add pagination contract/performance tests for growing lists.
8. Add accessibility and duplicate-submission UI checks.
9. Establish formatting/editor configuration and make `dotnet format --verify-no-changes` actionable.
10. Add a dated specification/ADR decision register and trace tests to accepted decisions.

---

**Production assessment:** No. The repository is buildable and has substantial tests, but confirmed same-tenant privacy/authorisation defects, unrestricted post-signup company creation, a vulnerable dependency, non-atomic background processing, fail-open migration startup, and unavailable end-to-end/database verification are release blockers. The recommended follow-up is security containment first, test-harness restoration second, reliability/idempotency third, then confirmed product-alignment and broader quality work.
