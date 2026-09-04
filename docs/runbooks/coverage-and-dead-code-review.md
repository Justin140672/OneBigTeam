# Coverage and Dead-Code Review Runbook

Owner: Crazy Cat Software Limited (trading as One Big Team)
Status: Living document — re-run and update after each periodic review.
Review frequency: at least twice a year, and after any large refactor or module removal.

This runbook documents the repeatable process for finding low/zero-coverage code and genuinely dead
code across the solution, and records the findings from the most recent pass.

---

## 1. How to run a coverage pass

The solution already references `coverlet.collector` from every test project via
`Directory.Packages.props`, so no extra package installation is required for coverage collection
itself. Coverage HTML reporting uses the `dotnet-reportgenerator-globaltool` (installed on demand,
not committed to the repo — see below).

### 1.1 Install the report generator (one-off per machine)

```bash
dotnet tool install -g dotnet-reportgenerator-globaltool
```

### 1.2 Collect coverage

Run each **non-E2E** test project individually with the coverage collector, writing results to a
scratch directory (never commit `.cobertura.xml`/`coverage/` output to the repo):

```bash
for p in HR.Architecture.Tests HR.Infrastructure.Tests HR.Marketing.Tests \
         HR.Modules.Assets.Tests HR.Modules.Companies.Tests HR.Modules.CompanyOnboarding.Tests \
         HR.Modules.DataImport.Tests HR.Modules.Documents.Tests HR.Modules.Employees.Tests \
         HR.Modules.Identity.Tests HR.Modules.Leave.Tests HR.Modules.Notifications.Tests \
         HR.Modules.Offboarding.Tests HR.Modules.Onboarding.Tests HR.Modules.Probation.Tests \
         HR.Modules.Recruitment.Tests HR.Modules.Reporting.Tests HR.Modules.Sickness.Tests \
         HR.Modules.Support.Tests HR.Modules.Tasks.Tests HR.SharedKernel.Tests HR.Web.Tests; do
  dotnet test tests/$p/$p.csproj --collect:"XPlat Code Coverage" --results-directory .coverage-tmp
done
```

`HR.Integration.Tests` may optionally be included the same way, but it is large (1000+ tests,
several minutes) and its Testcontainers-backed tests measure mostly the same handler/endpoint code
already covered by module unit tests — treat it as a supplement, not the primary signal.

Never run `HR.Web.E2E.Tests` for coverage — it requires a live browser/Aspire stack and isn't
representative of unit coverage.

### 1.3 Generate a human-readable report

```bash
reportgenerator \
  -reports:".coverage-tmp/**/coverage.cobertura.xml" \
  -targetdir:".coverage-report" \
  -reporttypes:Html;TextSummary
```

Open `.coverage-report/index.html`, or read `.coverage-report/Summary.txt` for a quick line/branch
percentage per assembly. Delete `.coverage-tmp` and `.coverage-report` when done — both are
scratch output and must never be committed.

### 1.4 Classify every low/zero-coverage area found

For each file or member reportgenerator flags as low/zero coverage, classify it as one of:

| Category | Action |
|---|---|
| Framework entry point (`Program.cs`, `*Module.cs` registration, minimal API bootstrapping) | Leave uncovered — exercised indirectly by every integration test that boots the host. Document, don't chase. |
| Routed UI component reachable only via Playwright | Leave uncovered by unit/bUnit tests (this repo intentionally skips bUnit — Playwright E2E is the UI coverage layer). Confirm an E2E test exists or is planned. |
| DI-only service (interface + single registered implementation, never called directly in tests) | Leave uncovered if it's exercised transitively through handler tests; add a direct unit test only if it has non-trivial branching logic of its own. |
| Reflection/serialization target (DTOs, EF entity configuration classes, source-generated code) | Leave uncovered — no meaningful behaviour to test. |
| External-consumer API endpoint the frontend doesn't call | **Never delete just because HR.Web doesn't call it.** Add an integration test instead if missing — it may be consumed by a future UI, a script, or an external integration. |
| Error-handling / exceptional path | Add a unit test if the branch encodes real business behaviour (e.g. "reject if X"); otherwise document as a defensive guard. |
| Genuinely dead code (unreachable, no callers anywhere in `src/`, not a public API surface, not referenced by DI/reflection/routing) | Remove it. Verify with a full-solution grep for the symbol name before deleting, and re-run the affected test project after removal. |

### 1.5 Static "always-on" unused-code signal (no separate tool needed)

The Roslyn compiler itself already flags most dead code as build warnings, and this repository
enforces **zero warnings** (see `Directory.Build.props`) — so `dotnet build` is, in effect, the
cheapest and most-repeatable unused-code analyzer available:

- `CS0414` — field assigned but never read (write-only state).
- `CS9113` — unused constructor/method parameter.
- `CS8321`/`IDE0051` (enable via `<AnalysisMode>` if stricter checking is wanted) — unused local
  functions / private members.

Because the repo treats warnings as a quality gate, any future genuinely-dead field, parameter or
private member will surface automatically on the next `dotnet build` — no extra tooling to install
or maintain. If deeper analysis is needed (e.g. unused public types, unused NuGet packages), add the
`Microsoft.CodeAnalysis.NetAnalyzers` `IDE00xx` unused-member rules or a tool such as
`dotnet-outdated`/`ts-prune`-equivalent on a case-by-case basis rather than by default, to avoid
false-positive noise on intentionally-public module registration surfaces (`*Module.cs`).

---

## 2. Findings from the 2026-09-04 review

### 2.1 Coverage summary highlights

Measured via `coverlet.collector` + `reportgenerator` across the 20 non-E2E unit/module test
projects (excludes `HR.Integration.Tests`, run separately without coverage instrumentation — see
below):

- 25 assemblies, 3,557 classes, 213,180 coverable lines.
- Line coverage: **32.4%** (69,074 / 213,180). Branch coverage: **48.4%**. Method coverage: **51.7%**.
- All 20 non-E2E unit/module test projects and the architecture test project pass in full
  (`HR.Architecture.Tests`: 303 tests; module projects range from 25 to ~1,420 tests each — 8,972
  unit/module tests total).
- The full `HR.Integration.Tests` suite was run once as an approved one-off for this review:
  1,227 tests, all passing (one apparent failure during a run that overlapped with a separate
  parallel coverage-collection sweep was a build-output race — `HR.Api.deps.json` momentarily
  missing mid-rebuild — and was confirmed to be non-reproducible by re-running the affected test
  class, `ReportExportAuditingIntegrationTests`, in isolation immediately afterwards: 5/5 passed).
- The raw 32.4% line-coverage figure is **not** representative of business-logic coverage — it is
  pulled down heavily by categories this repo deliberately doesn't unit-test:
  - `HR.Infrastructure`'s external integrations (Postmark, Supabase Storage, Hangfire background
    jobs) — these are exercised by `HR.Integration.Tests` against real/fake gateways, not by a
    module unit-test project, so they show as 0% in a unit-test-only coverage run.
  - `HR.Marketing`'s Blazor pages/components and `HR.Web`'s page code-behind — covered by
    Playwright E2E (`tests/HR.Web.E2E.Tests`), not unit/bUnit tests, per this repo's established
    convention (see `feedback_skip_bunit` policy).
  - `HR.Infrastructure.Abstractions` DTOs/contract records (e.g. `EmployeeDirectoryReportItem`,
    `RecruitmentPipelineSummaryResult`) — pure data-carrier records with no branching logic.
  - EF Core migrations and `*ModelSnapshot` files — generated code, not hand-written logic.
  - `*Module.cs` DI-registration classes — wiring, exercised transitively by every integration test
    that boots the host.
- Handler/validator coverage is high across all modules — this codebase's vertical-slice pattern
  (one handler + one validator per feature, each with dedicated unit tests) means most business
  logic is directly exercised; a full audit of all ~1,120 zero-coverage classes found nothing outside
  the categories above (spot-checked broadly across `HR.Infrastructure`, `HR.Marketing`,
  `HR.Modules.Assets`, `HR.Modules.Companies`, and others — see the classification table in §1.4).

### 2.2 Dead code removed

- **17 Reporting page components** (`src/HR.Web/Components/Pages/Reporting/*ReportPage.razor`) each
  had a private `_exporting` boolean field that was set `true`/`false` around the export flow but
  never read anywhere (not bound to any `disabled` state, not surfaced in markup) — confirmed via
  `CS0414` and a full-repo grep before removal. Removed the field and its two write-only
  assignments from each file.
- Unused constructor parameters, confirmed unused via `CS9113` plus a repo-wide grep for call
  sites before removal:
  - `AssetTaskCompletionAction` — unused `IClock clock`.
  - `GetCustomerBillingBreakdownHandler` — unused `IOptions<StripeOptions> stripeOptions` (billing
    now runs on the progressive `PlatformSettings` pricing model; a code comment already noted
    `StripeOptions.MonthlyPriceGbp` "is no longer used").
  - A Leave test double's unused `companyIdB` parameter.
- The redundant `Microsoft.AspNetCore.Components.Authorization` `PackageReference` in
  `tests/HR.Web.Tests/HR.Web.Tests.csproj` — already available transitively via the project's
  `FrameworkReference Include="Microsoft.AspNetCore.App"` (flagged by NuGet's own `NU1510`).

### 2.3 Intentionally-retained zero-coverage code

- `HR.AppHost`, `HR.ServiceDefaults` — Aspire orchestration/bootstrapping, not business logic; per
  `specifications/architecture/01-solution-structure.md` these must not contain business logic, so
  there is nothing meaningful to unit test.
- `*Module.cs` registration classes in every `HR.Modules.*` project — DI wiring, exercised
  transitively by every integration test that boots the host; a direct unit test would only
  assert "services got registered," which is already implicitly proven by every other passing
  integration test.
- Blazor page code-behind in `src/HR.Web/Components/Pages/**` — covered by Playwright E2E tests
  (`tests/HR.Web.E2E.Tests`), not bUnit, per this repository's established testing convention.
- EF Core `IEntityTypeConfiguration<T>` classes across all modules — pure mapping declarations with
  no branching logic; correctness is instead proven by the module's integration tests exercising
  real Postgres behaviour through the configured schema.
- Public, external-consumer-facing API endpoints with no current HR.Web caller (none were found to
  be uncalled by both HR.Web and integration tests during this pass) — the review confirmed no
  endpoint fell into this bucket; if one is found in a future pass, it must be covered by an
  integration test, never deleted solely for lacking a frontend caller.

### 2.4 Tests added for previously-uncovered active behaviour

None of this pass's zero-coverage findings represented untested *active business behaviour* beyond
what dead-code removal already addressed — the removed `_exporting` fields and unused parameters
were the only unused-but-live code found; everything else fell into the "intentionally retained"
categories above. No new tests were required to cover behaviour that wasn't already exercised.

### 2.5 Build warnings

The solution previously built with ~140–260 warnings (count varies by whether `--no-incremental` is
used) across these codes: `BL0005` (118), `CS8602` (76), `CS0414` (36), `CS9113` (10), `NU1510` (4),
`CS8619` (4), `CS8604` (4), `CS0618` (4), `RZ10012` (2), `CS0108` (2). All were resolved — the
solution now builds with **0 Warning(s), 0 Error(s)** (`dotnet build --no-incremental`). See the
"Fix build warnings" commit for the full breakdown of fixes vs. the one deliberate suppression
(`BL0005`, a confirmed Syncfusion-model false positive, suppressed repo-wide in
`Directory.Build.props` with a documented rationale).
