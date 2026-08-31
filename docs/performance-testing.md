# Performance & scale testing (NFR-02)

Automated, repeatable performance coverage for the HR platform. Implemented as xUnit timed
integration tests in `tests/HR.Integration.Tests/Performance/`, run in a dedicated nightly workflow
(`.github/workflows/perf-nightly.yml`), never on the PR gate.

## Targets

From `specifications/product-specifications/31-non-functional-requirements.md`:

| Operation | Target | Represented by |
|-----------|--------|----------------|
| Page / API load | < 2 s | `GET /companies/{id}/employees` (paged list) |
| Dashboard load | < 2 s | `GET /companies/{id}/dashboards/manager/summary` |
| Search | < 500 ms | `GET /companies/{id}/employees?search=` |
| Standard CRUD | < 1 s | `POST /companies/{id}/departments` |
| Small synchronous report | < 10 s | `GET /companies/{id}/reporting/hr-headcount-summary` |

Scale range under test: **50 / 500 / 2000 employees per company** (the product's stated upper bound
is "50–2000 employees per company"). A second, smaller noise tenant is always seeded so every query
genuinely filters by `company_id`.

## Percentile

- **API / page / search / CRUD / report latency is asserted at p95.** Rationale: p50 hides tail
  latency users still hit routinely; p99 on a shared CI runner is dominated by unrelated host noise
  (GC, neighbour containers, disk). p95 is the standard "typical worst case" and is stable enough to
  gate on with the CI multiplier below.
- p50 (median), p99, max and the cold (first) call are all recorded in the artefact for trend
  analysis, but only p95 fails the build.

## Cold vs warm

Each test runs `PERF_WARMUP_ITERATIONS` (default 2) unmeasured calls first (JIT, connection pool
fill, EF model + query plan cache warm), then `PERF_ITERATIONS` (default 15) measured calls. The
first measured call's latency is retained separately as `coldMs` for visibility; the gate uses the
warm p95.

## N+1 / query-count detection

The harness observes the `Microsoft.EntityFrameworkCore` diagnostic source (`QueryCountingInterceptor`)
and counts every SQL command executed while a measurement scope is active, flagging commands slower
than 200 ms. Each test asserts an absolute commands-per-request ceiling — against the **minimum**
count seen across the iterations, since background drain from async domain-event handlers can only
add commands, never remove them — that does **not** grow with employee count. An N+1 over
employees/leave/tasks would push the count into the hundreds or thousands at scale 500+ and fail the
test even if wall-clock latency still looked acceptable on a fast runner.

Because the same ceiling is asserted at 50 / 500 / 2000 employees, the three scale points are
themselves the "flat, not linear" check: an N+1 that passed at 50 would push the 2000-employee run
into the thousands and fail it.

Observed floor commands-per-request (minimum across iterations) and current ceilings
(see `PerformanceScaleTests`):

| Operation | Observed floor | Ceiling |
|-----------|---------------|---------|
| Employee list page | ~21 | 45 |
| Employee search | ~17 | 45 |
| Headcount report | ~14 | 35 |
| Create department (CRUD) | ~14 | 35 |
| Manager dashboard summary | ~93 | 150 |

All flat across 50 / 500 / 2000. ~15 of the per-request commands on every operation are fixed
auth/tenant/subscription middleware lookups, not the feature query itself.

**Follow-up (NFR-02):** the manager dashboard summary issues ~93 DB commands per request. This is a
fixed provider fan-out — it is completely flat across all three scales, so it is not an N+1 on data
volume and latency is ~50 ms today — but it is a latent scaling risk if any provider query later
becomes row-dependent. Tracked for the Dashboards module to batch/consolidate; do not raise the
ceiling to mask a genuine regression.

## Regression gating

Pass/fail budget per operation = `target * PERF_CI_MULTIPLIER`.

- `PERF_CI_MULTIPLIER` default **3.0**. The product targets assume production hardware; GitHub-hosted
  runners are ~2–4x slower and noisier. The multiplier keeps the gate honest without flaky failures.
- Set `PERF_CI_MULTIPLIER=1` to assert the raw product target (use only on a known-representative
  runner).
- A run fails if warm p95 exceeds the budget, or if commands-per-request exceeds the ceiling.

When a target is genuinely missed, **do not widen the multiplier or the ceiling to go green**. Open
a ticket against the owning module (Employees / Reporting / Dashboards / Leave / Tasks) referencing
the failing operation + scale + the artefact, and link it here.

## Results artefact

`PerformanceResults` writes `perf-results/perf-results.json` (override path with
`PERF_RESULTS_PATH`). Schema:

```jsonc
{
  "schemaVersion": 1,
  "generatedUtc": "...",
  "machineName": "...",
  "processorCount": 4,
  "ciMultiplier": 3.0,
  "iterations": 15,
  "results": [
    {
      "operation": "employee-list-page",
      "scaleLabel": "medium",
      "scale": 500,
      "targetMs": 2000,
      "ciMultiplier": 3.0,
      "budgetMs": 6000,
      "iterations": 15,
      "coldMs": 180.4,
      "medianMs": 42.1,
      "p95Ms": 61.3,
      "p99Ms": 70.0,
      "maxMs": 72.5,
      "minCommandCount": 4,
      "maxCommandCount": 4,
      "slowCommandCount": 0,
      "capturedUtc": "..."
    }
  ]
}
```

The nightly workflow uploads this file as the `perf-results` build artefact (90-day retention) for
trend comparison across runs.

## Test environment for comparability

| Property | Value |
|----------|-------|
| Runner | GitHub-hosted `ubuntu-latest` (2 vCPU, 7 GB RAM at time of writing) |
| Database | `postgres:16-alpine` via Testcontainers, same host |
| Process | `dotnet test -c Release`, server + DB co-located (no network latency modelled) |
| Warmup iterations | 2 (`PERF_WARMUP_ITERATIONS`) |
| Measured iterations | 15 (`PERF_ITERATIONS`) |
| Multiplier | 3.0 (`PERF_CI_MULTIPLIER`) |
| Parallelism | none — assembly disables test parallelization |

Because the API and Postgres run on the same runner, absolute numbers are **lower** than production
(which has a network hop to a managed Postgres) but are directly comparable **run to run**, which is
what regression detection needs. Production-representative absolute numbers require a staging
load-test (out of scope for NFR-02; candidate follow-up with k6/NBomber against a deployed
environment).

## Running locally

```bash
# all scales, all operations
dotnet test tests/HR.Integration.Tests/HR.Integration.Tests.csproj \
  --filter "Category=Performance"

# just the fast scales while iterating
dotnet test tests/HR.Integration.Tests/HR.Integration.Tests.csproj \
  --filter "Category=Performance&DisplayName~small"
```
