# Build and Deployment Pipeline Runbook (NFR-06)

Owner: Crazy Cat Software Limited (trading as One Big Team)
Status: **Draft for operator sign-off.** Steps marked _[OPERATOR]_ require access to the GitHub
repository settings and the Railway console and cannot be completed from the source repository.
Review frequency: at least annually and after any material change to hosting provider or pipeline.

Supports the deployment architecture (`specifications/architecture/08-deployment-architecture.md`),
the non-functional requirements (`specifications/product-specifications/31-non-functional-requirements.md`),
the availability & health-monitoring runbook (`docs/runbooks/availability-and-health-monitoring.md`)
and the backup & disaster-recovery runbook (`docs/runbooks/backup-and-disaster-recovery.md`).

---

## 1. Pipeline overview

| Stage | Workflow | Trigger | Blocks the next stage? |
|-------|----------|---------|------------------------|
| Continuous integration | `.github/workflows/ci.yml` | every PR + push to `main` | Yes — `ci-success` is the required check |
| Deploy to Test | `.github/workflows/deploy-test.yml` | push to `main` (auto) + manual | Yes — verifies CI, readiness, migrations |
| Deploy to Staging | `.github/workflows/deploy-staging.yml` | manual (`workflow_dispatch`, ref required) | Yes |
| Deploy to Production | `.github/workflows/deploy-production.yml` | manual + **required reviewer approval** | Yes |
| Nightly E2E | `.github/workflows/e2e-nightly.yml` | cron 03:00 UTC + manual | No — informational |
| Startup-migration health probe | `.github/workflows/deployment-health-check.yml` | reusable / manual | Used inside deploy |

`deploy-test`, `deploy-staging` and `deploy-production` are thin wrappers over the reusable
`.github/workflows/deploy.yml`.

---

## 2. CI job graph (`ci.yml`)

```
dependency-audit ─┐
version ──────────┼─► build-test ──────► package ─┐
                  ├─► integration-tests ──────────┼─► ci-success   (required status check)
                  └─► e2e-compile ────────────────┘
```

| Job | What it does | Notes |
|-----|--------------|-------|
| `dependency-audit` | `dotnet restore --locked-mode`; fails on any moderate+ NuGet advisory (NFR-09). | Unchanged from NFR-09. |
| `version` | Computes the immutable build version `1.0.<run_number>+<short_sha>` and exposes it + the full SHA as job outputs. | Single source of the stamp. |
| `build-test` | `dotnet restore --locked-mode`, `dotnet build -c Release` **version-stamped** (`-p:Version=`, `-p:SourceRevisionId=`), then runs every test project **except** `HR.Integration.Tests` and `HR.Web.E2E.Tests` — this includes `HR.Architecture.Tests` (module-boundary / vertical-slice enforcement). | Fast feedback. |
| `integration-tests` | Runs the full `HR.Integration.Tests` suite. Uses **Testcontainers** (`postgres:16-alpine`); the `ubuntu-latest` GitHub-hosted runner ships a working Docker daemon, so no `services:` block is needed. 30-minute session timeout. | Separate job so its runtime does not slow `build-test`. |
| `e2e-compile` | **Compiles** `HR.Web.E2E.Tests` only. The suite itself is never executed in the PR gate — it needs Aspire + Playwright + the whole 5-service stack and is slow/flaky headless. Executed by `e2e-nightly.yml` instead. | Rationale for keeping E2E out of the gate. |
| `package` | Produces the immutable deployment artefact (section 3). | Depends on `build-test`. |
| `ci-success` | `if: always()`, `needs:` every gate job. Fails if any dependency failed or was cancelled. | **This is the only required status check** for branch protection and the precondition every deploy checks. |

### Operator setup _[OPERATOR]_

- GitHub → Settings → Branches → branch protection for `main`: require the **`ci-success`** status
  check, require branches up to date, and (recommended) require a PR review.

---

## 3. Versioned, immutable deployment artefacts

Two layers, both keyed to the **git commit SHA** (the real immutable reference):

1. **Version stamp in the build.** `Directory.Build.props` sets a default `Version` of `0.1.0-dev`
   for local builds; CI overrides it with `-p:Version=1.0.<run_number>+<short_sha>` and
   `-p:SourceRevisionId=<full_sha>`. `Deterministic` + `ContinuousIntegrationBuild` are on under
   GitHub Actions for reproducible output. The version and SHA are compiled into
   `AssemblyInformationalVersion` of every assembly.
2. **Published artefact.** The `package` job runs `dotnet publish` for `HR.Api` and `HR.Web`,
   writes `build-info.json` (version, SHA, ref, run id, timestamp, actor) and uploads a single
   `deploy-<version>` artefact with **90-day retention**. Any historical deploy can be reproduced
   or inspected from this without a working tree.
3. **Railway image.** Railway builds its own OCI image (Nixpacks) from the exact commit passed to
   `railway up`. That image is immutable and retained by Railway as a redeployable release — this
   is what the rollback step redeploys.

There is no container registry in v1 (deployment-architecture: "minimise infrastructure
complexity"). If one is later added, push `ghcr.io/<repo>/<svc>:<version>` and
`:sha-<full_sha>` from the `package` job and have Railway pull by digest.

---

## 4. Environments and approval model

| GitHub Environment | Railway environment | Approval | Deploy branch policy | Trigger |
|--------------------|---------------------|----------|----------------------|---------|
| `test` | Test | none | `main` only | auto on push to `main` |
| `staging` | Staging | optional wait timer / reviewers _[SIGN-OFF]_ | `main` + tags | manual, ref required |
| `production` | Production | **Required reviewers (min 1, not the deployer)** | `main` + tags | manual, ref required |

### Operator setup _[OPERATOR]_

For each environment under GitHub → Settings → Environments:

1. Create `test`, `staging`, `production`.
2. `production`: enable **Required reviewers** and add the accountable operator(s). Optionally set
   a wait timer. Restrict deployment branches to `main` and `v*` tags.
3. Add environment secrets:
   - `RAILWAY_TOKEN` — the wrappers pass `RAILWAY_TOKEN_TEST` / `_STAGING` / `_PRODUCTION` as
     repo-level secrets; either name them that at repo scope, or rename in the wrappers to use
     per-environment `RAILWAY_TOKEN`. A Railway **project token** scoped to that environment.
   - `API_HEALTH_BEARER_TOKEN` (optional) — only if `/health/startup-migrations` is put behind auth.
4. Add environment variable `API_BASE_URL` — the public API base URL for that environment
   (e.g. `https://api-test.onebigteam.app`). Used by the readiness + migration probes.

**No secret value lives in the repository.** Every workflow reference is `${{ secrets.* }}` or
`${{ vars.* }}`. Verified: the only `secrets.` references in `.github/` are in `deploy.yml`,
the deploy wrappers and `deployment-health-check.yml`.

---

## 5. Database migration strategy

- **Mechanism.** `HR.Api` applies EF Core migrations **on startup**, per module, in `Program.cs`
  (`Migrate<Module>Async()` inside individual try/catch blocks), then records each module's result
  at `GET /health/startup-migrations` (200 all-succeeded / 503 any-failed).
- **Deploy gate.** After `railway up`, the deploy workflow waits for `/health/ready` (HTTP 200)
  and then runs `.github/scripts/check-startup-migrations.ps1` (20 attempts × 6 s). A 503 there
  fails the deploy and triggers rollback (section 6). Migrations that throw do **not** crash the
  process — the health gate is what converts a failed migration into a failed, rolled-back deploy.
- **Safe-change rule (expand / contract).** Because the new code and the old code briefly run
  against the same database during a Railway rollout, every migration must be **backward
  compatible with the currently-deployed app version**:
  1. *Expand* — add columns/tables/indexes as nullable or with defaults; add new enum values;
     create indexes `CONCURRENTLY` where possible. Ship and deploy.
  2. *Migrate data* — backfill in a follow-up migration or a Hangfire job.
  3. *Contract* — drop/rename/`NOT NULL`-tighten only in a **later** release, after no running
     code references the old shape.
  Never combine a destructive change with the feature that depends on it in one deploy.
- **Long migrations.** If a migration would hold a heavy lock or take minutes, run it out-of-band
  against the environment DB **before** the deploy (Supabase SQL editor / `dotnet ef database
  update` from an operator machine with the environment connection string) and deploy the
  already-migrated schema. Note it in the PR description.
- Module schema isolation (one schema per module, snake_case) is unchanged; each module migrates
  independently so a failure in one is attributable from the health payload.

---

## 6. Rollback

**Automatic (in `deploy.yml`).** Any failure after the deploy step runs a `Rollback on failure`
step: `railway redeploy --service <svc> --yes` for each of the 5 services, which re-activates the
previous Railway release (previous image + previous env vars). The GitHub deployment status is set
to `failure` and the job fails.

**Manual procedure** _[OPERATOR]_ — if automatic rollback also fails or the problem is found later:

1. Railway → project → target environment → each service → **Deployments** tab → pick the last
   known-good deployment → **Redeploy**. Do `api` and `app` first, then `marketing`, `admin`,
   `admin-api`.
2. Confirm `GET {api}/health/ready` = 200 and `GET {api}/health/startup-migrations` = 200.
3. If the bad deploy ran a **contracting** migration (dropped/renamed a column), code rollback is
   **not** sufficient — restore the database using `docs/runbooks/backup-and-disaster-recovery.md`
   (PITR to just before the deploy) or apply a compensating migration. This is why section 5
   forbids destructive changes in the same release as the feature.
4. Record the rollback: re-run is logged via the GitHub Deployments API + job summary; add an
   incident note under `docs/reviews/` if customer-impacting > 30 min (per the availability
   runbook §6.3).

**Rollback test** _[OPERATOR, SIGN-OFF]_: once per quarter, in Staging, deploy a deliberately
broken build, confirm the pipeline auto-rolls-back and readiness recovers, and record the result
in the assurance register (`docs/compliance/data-protection-operations.md`).

---

## 7. Deployment log / audit trail

Every run of `deploy.yml` records the deployment three ways:

1. **GitHub Deployments API** — a `deployment` object per run (environment, ref) with
   `in_progress` → `success`/`failure` statuses carrying `environment`, `actor`, `sha`, `outcome`.
   Visible in the repo's *Environments* / *Deployments* view and queryable via
   `gh api repos/<repo>/deployments`.
2. **Job summary** — a table (environment, git SHA, actor, run id, outcome) on every deploy run.
3. **Workflow run history** — retained per the repo's Actions retention policy; the `version` /
   `package` artefacts tie a running release back to its exact build.

_[OPERATOR, OPTIONAL]_ For a durable off-GitHub log, add a final step that appends a line to a
`deployments.log` in an object store, or posts to the ops Slack channel.

---

## 8. Mapping to NFR-06 acceptance criteria

| Criterion | Where |
|-----------|-------|
| CI: restore (locked), build Release, unit, architecture, integration, dependency checks | `ci.yml` jobs `dependency-audit`, `build-test`, `integration-tests` |
| CI: E2E in the gate or justified out | `e2e-compile` (compile-only) + `e2e-nightly.yml` — justification in section 2 |
| Versioned immutable artefacts | Section 3; `ci.yml` `version` + `package` jobs; `Directory.Build.props` |
| Environment-specific deploy workflows | `deploy-test.yml`, `deploy-staging.yml`, `deploy-production.yml` over `deploy.yml` |
| Approval before Production | GitHub Environment `production` required reviewers; section 4 |
| Safe DB migration strategy | Section 5 (expand/contract + startup-migration health gate) |
| Post-deploy readiness + migration checks | `deploy.yml` steps + `check-startup-migrations.ps1` / `deployment-health-check.yml` |
| Rollback (automatic + tested procedure) | Section 6; `deploy.yml` `Rollback on failure` step |
| Block deploy when quality gates fail | `ci-success` required check + `quality-gate` job in `deploy.yml` |
| Secrets stay in the platform | Section 4; workflows only reference `${{ secrets.* }}` / `${{ vars.* }}` |
| Record version, actor, environment, outcome | Section 7 |

---

## 9. Operator action checklist _[OPERATOR]_

- [ ] Branch protection on `main` requires the `ci-success` check.
- [ ] Create GitHub Environments `test`, `staging`, `production`.
- [ ] `production` environment: add required reviewers; restrict to `main` + `v*` tags.
- [ ] Create Railway **project tokens** per environment; store as the secrets named in the
      deploy wrappers (`RAILWAY_TOKEN_TEST` / `_STAGING` / `_PRODUCTION`).
- [ ] Set `API_BASE_URL` variable per environment.
- [ ] Confirm the 5 Railway service names (`marketing`, `app`, `api`, `admin`, `admin-api`) match
      the loop in `deploy.yml`; adjust if the project uses different slugs.
- [ ] Confirm each Railway service's health-check path is `/alive` (availability runbook §7).
- [ ] Run the first Test deploy manually and verify the readiness + migration gates pass.
- [ ] Schedule and run the quarterly rollback test in Staging (section 6).
- [ ] Decide whether `/health/startup-migrations` should be authenticated (currently anonymous).

## 10. Sign-off block

| Field | Value |
|-------|-------|
| Pipeline approved by | __________________ |
| Production approval reviewers agreed | __________________ |
| Migration strategy approved by | __________________ |
| Date | __________________ |
| Next review due | __________________ |
