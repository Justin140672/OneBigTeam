# Recruitment Dashboard Metrics (DSH-04)

Status: implemented 2026-08-31 (ticket DSH-04 "Replace recruitment dashboard proxy metrics with
authoritative measures").

## Background

The recruitment dashboard summary row previously derived several tile values by *inferring* business
meaning from the position of a stage in the company's fully customisable pipeline ordering
(`recruitment_stages.display_order`). This produced wrong numbers for any company that reordered,
renamed, inserted or removed stages.

DSH-04 replaces those proxies with explicit server-side queries and introduces an explicit,
machine-readable **stage purpose** so "which stage is the offer stage" is a deliberate configuration
choice rather than a guess.

## Stage purpose

New nullable column `recruitment.recruitment_stages.purpose` (migration
`20260831053538_AddRecruitmentStagePurpose`). Enum `RecruitmentStagePurpose`:

| Value            | Meaning                                                                       |
|------------------|------------------------------------------------------------------------------|
| `NewApplication` | Newly received applications land here, awaiting first triage.                 |
| `Interview`      | Candidate is at an interview step.                                            |
| `Offer`          | An offer has been / is being extended; awaiting the candidate's response.     |
| *(null)*         | Stage carries no special metric meaning.                                      |

Rules:

- Purpose is only valid on **non-terminal** stages (terminal stages express meaning via
  `terminal_outcome`). Enforced by the Create/Update stage validators.
- **More than one stage may share a purpose** (e.g. "Verbal offer" + "Written offer" both `Offer`).
  Metrics count applications across every stage carrying the relevant purpose.
- Editable from the recruitment stage settings screen (`RecruitmentStageEdit`).
- The default seeded pipeline (`RecruitmentStageSeeder`) sets: Application Received → `NewApplication`,
  Interview → `Interview`, Offer → `Offer`. The migration back-fills the same for existing companies
  still on the default seed names.

## Metric definitions

All metrics are company-scoped: the `{companyId}` route segment is validated against the caller's
resolved tenant by `TenantRouteAuthorizationMiddleware`, and every query additionally filters
`company_id`. All metric endpoints require the `candidate:view` policy (held by the Recruiter role,
which also gates the dashboard page). Each endpoint returns `{ count, items[] }` where `count` is
`items.Count` by construction, so a tile and its drill-down list can never disagree.

### New applications
`GET /api/companies/{companyId}/recruitment/metrics/new-applications`

Live applications (`withdrawn_at IS NULL`, current stage non-terminal) whose current stage has
`purpose = NewApplication`.

Fallback when no stage has that purpose (`definedByStagePurpose = false` in the response): live
applications in a non-terminal stage received within the last `newWithinDays` days (default 14).
The fallback is time-based, never order-based.

### Candidates in progress
`GET /api/companies/{companyId}/recruitment/metrics/candidates-in-progress`

Live applications (`withdrawn_at IS NULL`) currently in a non-terminal stage. Driven only by
`is_terminal` + `withdrawn_at` — no dependency on ordering or naming.

### Offers awaiting response
`GET /api/companies/{companyId}/recruitment/metrics/offers-awaiting-response`

Live applications (`withdrawn_at IS NULL`, current stage non-terminal) whose current stage has
`purpose = Offer`, summed across all `Offer`-purpose stages. If no stage has that purpose the count
is 0 and `offerStageConfigured = false` (UI prompts the company to configure one). No order-based
fallback.

### Interviews requiring action
`GET /api/companies/{companyId}/recruitment/metrics/interviews-requiring-action`

Interviews with `outcome = Pending` scheduled at or before the end of the current UTC day. These
have started/finished but have no recorded outcome. Excludes cancelled/completed interviews and
interviews scheduled later than today. Replaces the previous "interviews scheduled today" proxy,
which both missed overdue interviews and included today's not-yet-happened interviews.

### Stale vacancies
`GET /api/companies/{companyId}/vacancies/stale?staleAfterDays=` (existing endpoint, `recruitment:manage`)

Open vacancies whose most recent activity is older than `staleAfterDays` days (default 14). Activity
is now the latest of: any application created/updated for the vacancy, **any interview scheduled,
rescheduled or resolved** for one of the vacancy's applications (added by DSH-04), or — if there is
no application/interview activity at all — the vacancy's `opened_at` / `created_at`.

## Drill-downs

Each application-based metric returns a uniform `RecruitmentMetricApplicationItem` row
(application, candidate name/email, vacancy id + display title, current stage id/name, applied-at).
"Interviews requiring action" returns interview rows (interview, application, candidate, vacancy,
scheduled-at, location). The dashboard renders `items` directly, so the drill-down always matches the
tile count.
