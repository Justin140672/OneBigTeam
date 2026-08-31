# Availability and Health Monitoring Runbook (NFR-03)

Owner: Crazy Cat Software Limited (trading as One Big Team)
Status: **Draft for operator sign-off.** Values marked _[SIGN-OFF]_ must be confirmed by an
accountable operator. Steps marked _[OPERATOR]_ require access to the Railway, Supabase, Grafana
Cloud and/or alert-routing (PagerDuty / Opsgenie / email) consoles and cannot be completed from the
source repository.
Review frequency: at least annually and after any material change to hosting provider, dependency
set, or the customer-facing availability commitment.

This runbook supports the non-functional requirements
(`specifications/product-specifications/31-non-functional-requirements.md`), the deployment
architecture (`specifications/architecture/08-deployment-architecture.md`), and the backup and
disaster recovery runbook (`docs/runbooks/backup-and-disaster-recovery.md`).

---

## 1. Health endpoints

Every service built on `HR.ServiceDefaults` (HR.Api, HR.Web, HR.Marketing, HR.Admin.Web) exposes
two production endpoints, mapped in **all** environments by
`HealthCheckEndpoints.MapLivenessAndReadiness` (`src/HR.ServiceDefaults/HealthCheckEndpoints.cs`).

| Endpoint | Purpose | Auth | Probes | Body |
|----------|---------|------|--------|------|
| `GET /alive` | Liveness — is the process responsive? | Anonymous | Only checks tagged `live` (the in-process `self` check). **No** database, HTTP dependency or credential access. | `{"status":"Healthy"}`, HTTP 200. Restart the instance if this fails or times out. |
| `GET /health/ready` | Readiness — can the service serve traffic? | Anonymous for the minimal body; per-check detail requires the `X-Health-Token` header (value = `HealthChecks:ReadinessDetailToken`) or the Development environment. | All dependency checks (everything not tagged `live`). | Minimal: `{"status":"Healthy|Degraded|Unhealthy"}`. HTTP 200 unless a **critical** dependency is Unhealthy, then HTTP 503. |
| `GET /health` | Legacy Aspire aggregate (full detail, no auth). | Anonymous | All checks. | **Development only** — never mapped in Test/Staging/Production. |
| `GET /health/startup-migrations` (HR.Api only) | Per-module EF migration result captured at boot. | Anonymous | — | 200/503. Consumed by `.github/workflows/deployment-health-check.yml`. |

Detail responses expose only each check's curated `name`, `status`, `critical` flag and
`description`. Health-check `Exception` and `Data` values are **never** serialised (they can carry
connection strings / hosts / stack detail). Enforced by
`HealthReadinessAndLivenessEndpointTests`.

### Load-balancer / platform wiring _[OPERATOR]_

- Railway health check path for every service: `/alive` (liveness restart trigger).
- If a readiness gate is added in front of traffic (e.g. Cloudflare load balancer origin health, or
  a future multi-instance rollout), point it at `/health/ready` and treat only HTTP 503 as "remove
  from pool". A `200` + `Degraded` body must keep the instance in rotation.
- Store `HealthChecks:ReadinessDetailToken` as a per-environment secret. Rotate on the standard
  secret-rotation cycle. It is not a credential to any dependency — worst case on leak is
  disclosure of dependency up/down status and curated descriptions.

---

## 2. Dependency classification (critical vs degraded)

Tags are set where each check is registered. `critical` ⇒ a failure makes `/health/ready` return
503 ("not ready"). `degraded` ⇒ a failure is reported as `Degraded` with HTTP 200; the platform
keeps serving.

| Check | Owner (registration site) | What it probes | Class | Rationale | Behaviour when down |
|-------|---------------------------|----------------|-------|-----------|---------------------|
| `database` | `HR.Modules.Companies` (`CompaniesModule`) | `CanConnectAsync` on the shared Postgres (Supabase) via the Companies DbContext | **critical** | No request can read or write tenant data without Postgres. | `/health/ready` → 503 |
| `auth` | `HR.Modules.Identity` (`IdentityModule`) | `GET /auth/v1/settings` on Supabase Auth | **critical** | No user can sign in or have a token validated. | `/health/ready` → 503 |
| `storage` | `HR.Infrastructure` (`InfrastructureModule`) | List buckets on Supabase Storage | degraded | Only document/photo upload & download features are impaired; the rest of the platform serves. | `Degraded`, HTTP 200 |
| `email` | `HR.Infrastructure` (`InfrastructureModule`) | `GET /server` on Postmark | degraded | Invitations / notifications / reminders queue or fall back to logging; no user-facing outage. | `Degraded`, HTTP 200 |
| `stripe` | `HR.Modules.Companies` (`CompaniesModule`) | Retrieve account balance | degraded | Billing / subscription changes are impaired; existing tenants keep working. | `Degraded`, HTTP 200 |
| `hangfire` | `HR.Infrastructure` (`AddHangfireBackgroundJobs`) | Monitoring API: server count + failed-job count | degraded | Background jobs back up but the web/API request surface is unaffected. | `Degraded`, HTTP 200 (also `Degraded` when failed jobs > 0) |
| `self` | `HR.ServiceDefaults` (`AddDefaultHealthChecks`) | In-process sentinel | liveness (`live`) | Process responsiveness only. | `/alive` → non-200 |

Not-configured dependencies (`storage`, `email`, `stripe`, `auth` with no URL/key — e.g. a bare
environment) report `Degraded`, never `Unhealthy`, so an intentionally unconfigured optional
integration never trips readiness.

> **Escalation of a degraded dependency to critical.** If product later depends on e.g. Storage for
> a core flow, change its tag to `["ready", "critical"]` at the registration site and update this
> table plus the tests. Do not special-case it in `HR.ServiceDefaults`.

---

## 3. Availability SLO (99.5%)

### 3.1 Definition

- **SLO:** 99.5% successful availability of the customer-facing application, measured per calendar
  month, per production environment.
- **Error budget:** 0.5% ≈ **3h 39m** of unavailability per 30-day month.
- **Service boundary:** the HR.Web application and the HR.Api it depends on. HR.Marketing and
  HR.Admin.Web are measured separately and are **not** covered by the customer SLO.

### 3.2 What counts as "available" (the SLI)

A 1-minute interval is **good** if **both**:

1. An external synthetic probe of `GET /alive` on HR.Web **and** HR.Api returns HTTP 200 within
   5 seconds; **and**
2. The real-traffic success ratio for that minute is ≥ 95%, where a request is a **failure** only
   if it returns HTTP 5xx **or** exceeds 5s (from the `http.server.request.duration` histogram
   already exported per NFR-02). 4xx responses are **not** failures.

Monthly availability = good minutes ÷ (total minutes − excluded minutes) × 100.

If there is no real traffic in a minute, only the synthetic probe decides that minute.

### 3.3 Planned-maintenance exclusions

Minutes are **excluded** from both numerator and denominator when **all** of:

- a maintenance window was announced to customers **≥ 48 hours** in advance (status page + email)
  _[SIGN-OFF: notice period]_;
- the window is inside the pre-agreed maintenance band (proposed: **Sundays 02:00–04:00 UTC**)
  _[SIGN-OFF]_;
- total excluded time ≤ **4 hours per month** _[SIGN-OFF]_;
- the maintenance is recorded in the status-page history before it starts.

Emergency maintenance (e.g. an urgent security patch) with < 48h notice is **not** excluded and
consumes error budget. Third-party outages (Supabase, Railway, Cloudflare, Postmark, Stripe) are
**not** excluded from the SLI — provider redundancy and failover are One Big Team's responsibility —
but are annotated on the dashboard for root-cause context.

### 3.4 Error-budget policy _[SIGN-OFF]_

| Budget consumed in trailing 30 days | Action |
|-------------------------------------|--------|
| < 50% | Normal delivery. |
| 50–90% | Review recent incidents at the weekly ops check-in; prioritise reliability fixes. |
| > 90% | Change freeze on non-reliability work until budget recovers; incident review with accountable operator. |
| Budget exhausted | All engineering effort to reliability; customer communication per contract. |

---

## 4. Metrics that feed monitoring

All emitted today via OpenTelemetry from `HR.ServiceDefaults` (NFR-01/NFR-02). Exporter target is
set by `OTEL_EXPORTER_OTLP_ENDPOINT` _[OPERATOR: point at Grafana Cloud OTLP / Prometheus]_.

| Metric | Source | Used for |
|--------|--------|----------|
| `http.server.request.duration` (histogram, with `http.response.status_code`, `http.route`) | ASP.NET Core instrumentation | SLI success ratio, latency SLOs, error rate |
| `http.client.request.duration` | HttpClient instrumentation | Dependency latency (Supabase/Postmark/Stripe calls) |
| Runtime metrics (`process.runtime.dotnet.*`) | Runtime instrumentation | Saturation (GC, threadpool, memory) — leading indicator |
| Synthetic probe result for `/alive`, `/health/ready` | External checker (see 5) | SLI availability, per-dependency readiness |
| Health-check status per dependency | `/health/ready` detail (tokened) scrape | Dependency health-over-time panel |
| `/health/startup-migrations` status | Existing GH workflow / probe | Deploy verification |

### Deriving a per-dependency time series

Run an external collector (Grafana Synthetic Monitoring job, or a small scheduled Railway cron, or a
Prometheus `blackbox`-style scrape) every 60s that:

1. `GET {service}/health/ready` with the `X-Health-Token` header;
2. parses `checks[]`;
3. emits a gauge `dependency_up{service,dependency,critical}` = 1 for `Healthy`, 0.5 for `Degraded`,
   0 for `Unhealthy`;
4. emits `readiness_ready{service}` = 1 when HTTP 200 else 0;
5. emits `liveness_up{service}` from a separate `GET /alive`.

_[OPERATOR]_ implement this collector and point it at the metrics backend. Config template lives in
`docs/runbooks/` follow-ups (not yet created — see open questions).

---

## 5. Dashboard (to be built) _[OPERATOR]_

Build one Grafana dashboard, "One Big Team — Availability & Health", with these panels:

1. **Monthly availability gauge** vs 99.5% target, per environment. Query: good-minute ratio from
   the SLI recording rule, with planned-maintenance minutes excluded.
2. **Error budget remaining** (stat + 30-day burn-down timeseries).
3. **Availability heatmap** — good/bad minute per hour over 30 days.
4. **Request success rate** — `1 - (sum(rate(5xx)) + slow) / sum(rate(all))` over 5m, HR.Web + HR.Api.
5. **Latency** — p50/p90/p99 of `http.server.request.duration`, with the < 2s page / < 1s CRUD /
   < 500ms search NFR lines overlaid.
6. **Dependency health over time** — state-timeline from `dependency_up{}` for database, auth,
   storage, email, stripe, hangfire. Critical dependencies pinned to the top.
7. **Readiness / liveness** — state-timeline of `readiness_ready{}` and `liveness_up{}` per service.
8. **Hangfire backlog** — enqueued / failed job counts (from the health-check `Data`, scraped
   server-side only — never exposed publicly).
9. **Annotations** — deploys, incidents, planned-maintenance windows, known third-party outages.

Recording rules needed: `sli:good_minute`, `sli:month_availability_ratio`,
`slo:error_budget_remaining`. Define these alongside the dashboard JSON in the ops repo _[OPERATOR:
repo location TBD]_.

---

## 6. Alerting

### 6.1 Thresholds

| # | Alert | Condition | Severity | Notes |
|---|-------|-----------|----------|-------|
| A1 | Service down (liveness) | `/alive` non-200 or no response for **2 consecutive minutes** on HR.Web or HR.Api | **Critical / page** | Auto-restart first (Railway); page if it recurs ≥ 3× in 30 min or stays down 5 min. |
| A2 | Not ready (critical dependency) | `/health/ready` returns 503 for **3 consecutive minutes** | **Critical / page** | Body/`checks` identifies `database` vs `auth`. |
| A3 | Elevated error rate | Request success rate < 99% over 10 min (fast burn: 2% budget in 1h) | **Critical / page** | Multi-window burn-rate alert (1h & 6h). |
| A4 | Slow burn | Error budget burn projecting > 100% monthly over 6h window | **High / ticket** | — |
| A5 | Latency regression | p90 `http.server.request.duration` > 2s for 15 min | **High / ticket** | Correlate with dependency latency. |
| A6 | Degraded dependency | any `degraded` dependency `Unhealthy` for **10 min** | **Warning / notify** | `storage`, `email`, `stripe`, `hangfire`. No page. |
| A7 | Hangfire backlog | failed jobs > 0 for 30 min, or enqueued > 500 for 15 min | **Warning / notify** | — |
| A8 | Startup migration failure | `/health/startup-migrations` 503 after a deploy | **Critical / page** | Existing GH workflow already checks this at deploy time; add a standing probe. |
| A9 | Synthetic probe blind | No probe data for 5 min | **High / ticket** | Monitoring-of-monitoring. |
| A10 | SLO breach (informational) | Monthly availability < 99.5% | **High / ticket + customer-comms trigger** | Fires once per month-to-date crossing. |

### 6.2 Routing _[SIGN-OFF]_

| Severity | Channel | Target |
|----------|---------|--------|
| Critical / page | PagerDuty/Opsgenie → phone + push, and `#ops-alerts` | On-call engineer |
| High / ticket | `#ops-alerts` + ticket queue | Ops rota, next business hours |
| Warning / notify | `#ops-monitoring` only | No individual notification |

### 6.3 Escalation

1. **0 min** — page primary on-call (critical). Ack required within **5 min**.
2. **No ack in 5 min** — escalate to secondary on-call.
3. **No ack in 15 min**, or incident still active at 30 min — escalate to accountable operator
   (Crazy Cat Software Limited) and open a formal incident.
4. **Customer-impacting > 30 min** — post to the public status page; begin the customer
   communication obligations in the contract / DPA.
5. **Post-incident** — within 3 business days: written incident review (timeline, root cause,
   error-budget impact, corrective actions). File under `docs/reviews/`.

Follow the same on-call handoff and console-access model as the backup & disaster recovery runbook
(`docs/runbooks/backup-and-disaster-recovery.md` §on-call).

---

## 7. Operator action checklist (outside the repository) _[OPERATOR]_

- [ ] Set Railway health check path to `/alive` for HR.Web, HR.Api, HR.Marketing, HR.Admin.Web.
- [ ] Create `HealthChecks:ReadinessDetailToken` secret per environment; add to secret-rotation list.
- [ ] Set `OTEL_EXPORTER_OTLP_ENDPOINT` (+ auth headers) to the metrics backend for every service.
- [ ] Stand up the synthetic probe / readiness collector (§4) on a 60s schedule.
- [ ] Build the Grafana dashboard and recording rules (§5).
- [ ] Configure alerts A1–A10 and routing (§6); test each path end-to-end.
- [ ] Agree and document the maintenance band, notice period, and monthly exclusion cap (§3.3).
- [ ] Sign off the error-budget policy (§3.4) and alert routing (§6.2).
- [ ] Add a standing probe for `/health/startup-migrations` (A8).
- [ ] Record the maintenance band on the customer status page.

## 8. Sign-off block

| Field | Value |
|-------|-------|
| SLO definition approved by | __________________ |
| Alerting & escalation approved by | __________________ |
| Role | __________________ |
| Date | __________________ |
| Next review due | __________________ |
