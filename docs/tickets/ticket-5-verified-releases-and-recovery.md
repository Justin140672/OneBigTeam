# Ticket 5 — Verify deployed releases and recover to a known-good release

Priority: High. Original issues 6 & 7.

> Report deployment success only when the requested release is running across every required
> service. Recover failed rollouts using verified previous deployments.

Scope: the deployment pipeline — `.github/workflows/deploy.yml` (+ the `deploy-test` / `deploy-staging`
/ `deploy-production` wrappers), `.github/scripts/check-startup-migrations.ps1`, and the new
verified-deploy / recovery controller under `.github/scripts/deploy/`. Plus a small, anonymous
release-identity surface added to every deployed host via `HR.ServiceDefaults`.

---

## Production paths affected

| Path | Change |
|---|---|
| `src/HR.ServiceDefaults/ReleaseIdentity.cs` (new) | Running-release identity: `sha` / `version` / `service` resolution with `RELEASE_SHA` / `PLATFORM_VERSION` / `RELEASE_SERVICE` env overrides. |
| `src/HR.ServiceDefaults/Extensions.cs` | `MapDefaultEndpoints()` now also maps anonymous `GET /health/release` — so **every** host (`HR.Api`, `HR.Web`, `HR.Marketing`, `HR.Admin.Web`) exposes it. |
| `src/HR.Api/Startup/StartupMigrationRunner.cs` | `/health/startup-migrations` payload gains a back-compatible `release` sibling key (`{sha,version}`); module keys and the 200/503 semantics are unchanged. |
| `.github/scripts/check-startup-migrations.ps1` | Iterates **all** module keys (not just `companies`/`identity`); new optional `-ExpectedSha` gate so a healthy *old* API cannot satisfy the new release's migration check. |
| `.github/scripts/deploy/Railway.psm1` (new) | Thin, injectable-runner wrapper over the Railway CLI: `Get-RailwayActiveDeployment`, `Start-RailwayServiceDeploy`, `Restore-RailwayDeployment`. |
| `.github/scripts/deploy/VerifiedDeploy.psm1` (new) | `Invoke-VerifiedDeploy` + `Invoke-Recovery` + helpers (`Get-ReleaseSafety`, `Wait-ServiceRelease`, `Get-ServiceRelease`, `New-DeploySummaryTable`). |
| `.github/scripts/deploy/Invoke-VerifiedDeploy.ps1` (new) | CI entrypoint: wires real Railway CLI + real HTTP probes, drives deploy then (on failure) recovery, writes `$GITHUB_STEP_SUMMARY`, sets exit code. |
| `.github/scripts/deploy/release-safety.json` (new) | `{ appRollbackSafe, reason, notes }`; default `true`. The release commit sets `false` when it ships a contracting migration. |
| `.github/workflows/deploy.yml` | `deploy` job body replaced by the verified-deploy controller; new inputs `rollout_timeout_seconds` / `recovery_timeout_seconds`; migration-vs-`release-safety.json` drift warning. |
| `.github/workflows/ci.yml` | New `deploy-scripts-test` job (Pester 5) wired into the required `ci-success` check. |
| `docs/runbooks/deployment-pipeline.md` | §5 (release-safety declaration, `-ExpectedSha` gate, `/health/release`), §6 (capture/restore/re-verify, migration-incompatible guard, "code only, never data" wording), §8/§9. |

No module, schema, entity, DB or authorization change. `/health/release` is anonymous and
disclosure-limited, consistent with `/alive` and `/health/ready` in
`08-deployment-architecture.md`.

---

## Before implementation — the four investigations

### 1. Required services and their readiness checks

One Railway project, one service per host. Real, deployable hosts today (confirmed in `src/` and
`OneBigTeam.slnx`): **`marketing`** (HR.Marketing), **`app`** (HR.Web), **`api`** (HR.Api),
**`admin`** (HR.Admin.Web). `admin-api` (HR.Admin.Api) **does not exist** — there is no project — so
it is deliberately excluded from the required-services list until it does. (The old `deploy.yml`
loop referenced `admin-api`; `railway up --service admin-api` would have failed or no-op'd.)

Every host calls `AddServiceDefaults()` + `MapDefaultEndpoints()`, so every host already exposes:
- `GET /alive` — liveness, anonymous, no dependency probing.
- `GET /health/ready` — readiness, anonymous, 503 only when a `critical`-tagged check is Unhealthy.
Only `HR.Api` runs migrations and exposes `GET /health/startup-migrations` (200 all-succeeded /
503 any-failed; the API serves health endpoints only if any required migration fails).

### 2. How each service proves the running release identity

Before this ticket there was **no** anonymous way to ask a running instance "which commit are you?".
`GetSystemHealth` (Companies module) has `GetPlatformVersion()` but it is behind a platform-admin
allow-list — unusable as a deploy probe. CI already stamps `AssemblyInformationalVersion` with
`-p:Version=1.0.<run>+<short_sha>` and `-p:SourceRevisionId=<full_sha>` (`Directory.Build.props`,
`ci.yml`), so the full SHA is compiled into every assembly.

New: `GET /health/release` → `{ service, sha, version, environment, startedAt }`.
- `sha`: `RELEASE_SHA` env var, else the 40-hex commit parsed from `AssemblyInformationalVersion`,
  else `"unknown"`. A local build (no `SourceRevisionId`) yields `"unknown"` rather than a
  misleading short token — CI always sets `RELEASE_SHA` to the full quality-gate SHA, so that is
  the source of truth for verification.
- `version`: `PLATFORM_VERSION` env var, else `AssemblyInformationalVersion`, else assembly version.
- Verification is an **exact** string match of reported `sha` to the target release SHA. A 200 from
  an old instance reporting the old SHA fails verification (criterion 4).

`/health/startup-migrations` also carries the same `{sha,version}` as a top-level `release` key so
the migration gate is likewise pinned to the new release.

### 3. Railway's supported deploy / rollback mechanisms

Relied on (Railway CLI `@railway/cli` v3, `railway --help` / Railway docs "CLI" and
"Deployments → Rollback", reviewed 2026-09-10):

| Need | Command | Notes |
|---|---|---|
| Deploy a service from the working-tree commit | `railway up --service <svc> --environment <env> --ci --detach` | `--detach` returns once the build is queued; the `/health/release` poll is what confirms it. Already used by the old workflow. |
| Read current active deployment per service | `railway status --json` | JSON shape has shifted between CLI versions — `Get-RailwayActiveDeployment` probes the known layouts (`services[].latestDeployment.id`, `edges[].node.latestDeployment.id`) defensively. |
| Re-activate a **specific historical** deployment | GraphQL `deploymentRollback(id: String!)` **only** | The CLI has no historical-deployment targeting, so there is **no CLI rollback path and no "redeploy latest" fallback** (after a partial rollout "latest" is the broken release). The Railway Public GraphQL API `deploymentRollback` mutation rolls back to a specific historical deployment by id, restoring that deployment's image **and** its variable snapshot, when `canRollback: true`. If the mutation is unavailable / denied / `canRollback:false`, the service is reported **not restored** — never a substituted release. See the follow-up sections below. |

Railway retains previous OCI images as redeployable releases (deployment-pipeline §3), so "restore
the previous known-good deployment" is a supported operation — via the GraphQL API, not the CLI.

---

## Follow-up (2026-09-10) — "Make release recovery trustworthy and handle verification failures safely"

Three implementation defects in the first cut, fixed here (part 1). Part 2 — six live Railway
demonstrations — is **BLOCKED**: no Railway access from this environment. See "Unverified / OUTSTANDING".

### Railway GraphQL contract relied on

Confirmed 2026-09-10 by a documentation review (WebFetch/WebSearch against docs.railway.com). Items
that could not be pinned to a formal schema page are flagged and must be reconfirmed in the live
demonstrations.

| Item | Value | Source / confidence |
|---|---|---|
| GraphQL endpoint | `https://backboard.railway.com/graphql/v2` (`.com`, not `.app`) | docs.railway.com/guides/public-api + /reference/public-api — verbatim. High. |
| Auth header (project token) | `Project-Access-Token: <token>` — **not** `Authorization: Bearer` (that is for account / workspace / OAuth tokens) | docs.railway.com/guides/public-api — verbatim. High. |
| Restore mutation | `mutation deploymentRollback($id: String!) { id status }` | docs.railway.com/guides/manage-deployments. High. |
| Rollback restores env snapshot? | **Yes** — "Both the Docker image and custom variables are restored during the rollback process" | docs.railway.com/deployments/deployment-actions — verbatim. High. So recovery no longer resets `RELEASE_SHA` before verifying; identity is proven by the Railway deployment id, not the env var. |
| Rollback gate | Only when that deployment has `canRollback: true`; retention-expired deployments report `false` and cannot be auto-restored | docs.railway.com/guides/manage-deployments. High. |
| List query | `deployments(input: { projectId, serviceId, environmentId }, first: N) { edges { node { id status createdAt url staticUrl canRollback meta } } }` | docs official example omits `meta`/`canRollback` from the selection set but both are documented fields on `Deployment`. Medium — reconfirm the selection set live. |
| Active deployment | `environment(id) { serviceInstances { edges { node { serviceId latestDeployment { id status meta } } } } }` | Assembled from Railway tooling examples. Medium — reconfirm live. |
| `meta.commitHash` | git-triggered deploys carry `commitHash` (also `commitMessage`) in the loosely-typed `meta` object | Railway tooling examples, not a typed schema page. Medium — `commitHash` may be null; the code treats a null recorded `commitHash` as "id match only". |
| Healthy status values | `SUCCESS` = serving; `SLEEPING` = scaled to zero. Full enum also includes BUILDING, DEPLOYING, FAILED, CRASHED, REMOVED, REMOVING, SKIPPED, WAITING, QUEUED, INITIALIZING | docs.railway.com/guides/manage-deployments (aggregated from example queries). Medium. Only `SUCCESS`/`SLEEPING` are accepted as a known-good anchor. |
| `railway status --json` fields | `id` (project), `environments.edges[].node.{id,name}`, `services.edges[].node.{id,name}` | Railway skills/tooling, not a formal doc. Medium — `Get-RailwayContext` probes defensively and throws if it cannot resolve a project + environment id. |
| Pinned CLI | `@railway/cli@5.52.0` | npm `dist-tags.latest` 2026-09-10 (see round-2 facts table, row 4). **Reconfirm with `npm view @railway/cli version` in demonstration 1.** The CLI is now only used for `railway status --json`, `railway variables --set --skip-deploys` (needs ≥ 3.5) and `railway up`; historical restore does not touch it. |

### Defect 1 — restore the recorded deployment and verify its ACTUAL identity

- `Railway.psm1`: new `Invoke-RailwayApi` (POST query+variables through an injectable `$ApiSender`,
  `Project-Access-Token` header, throws on any GraphQL `errors`), `Get-RailwayContext`,
  `New-RailwayApiClient`, `Get-RailwayDeployments` (list), `Get-RailwayActiveDeployment` (now via the
  API, not the CLI). `Restore-RailwayDeployment` rewritten to call `deploymentRollback(id:<recordedId>)`.
  **The plain-`railway redeploy` fallback is deleted.** If historical restore is unsupported / the
  deployment is not found / `canRollback:false` / the API denies it → the service is marked
  **not restored**, recovery cannot return `recovered`, and the operator instructions name the
  service + recorded deployment id. It never substitutes a different release.
- Known-good capture (`Invoke-VerifiedDeploy` step 1): `Get-RailwayDeployments` per service, record
  the newest deployment with `status ∈ {SUCCESS, SLEEPING}` **and** `canRollback:true` as
  `{ service, deploymentId, commitHash, status, canRollback, releaseVersion, releaseSha (display),
  capturedAtUtc }`. No such deployment → `deploymentId=$null` ("no known-good") and the other
  services are still processed.
- Post-restore identity: `Invoke-Recovery` polls `Get-RailwayActiveDeployment` until the now-active
  deployment `id` equals the recorded id **or** its `meta.commitHash` equals the recorded
  `commitHash` (for the case where rollback spawns a new deployment node). A matching
  `/health/release` sha is **not** accepted as proof — `RELEASE_SHA` is a reassignable env var.
- `deploy.yml`: `@railway/cli` pinned.
- **regression: under the previous implementation, `Invoke-Recovery > "REGRESSION (defect 1): a
  matching /health/release sha but a NON-matching active Railway deployment => identity FAILS"`
  fails**, because the old code verified recovery by re-reading `/health/release` for a sha it had
  itself just reset to the known-good value — a circular check that passes even when the wrong
  deployment is live.

### Defect 2 — migration-verification exceptions must enter recovery

- `check-startup-migrations.ps1`: `Write-Error` (which raises a terminating error under
  `$ErrorActionPreference='Stop'`, skipping the caller's `if ($LASTEXITCODE -ne 0)` branch) replaced
  with `Write-Warning` + `exit 1`. Standalone invocation still exits non-zero on every failure path.
  New optional injectable `-HttpProbe` so a test can drive real retry exhaustion.
- `VerifiedDeploy.psm1`: `Invoke-VerifiedDeploy` wraps the `$ApiVerification` call in try/catch — a
  throw becomes `@{ Ok=$false; Category='exception' }`, never a bare throw to the caller.
- `Invoke-VerifiedDeploy.ps1`: the whole `Invoke-VerifiedDeploy` invocation is wrapped at the
  controller boundary — any terminating error after rollout begins becomes a failed deployment
  result (original error preserved in the summary) and still reaches the recovery decision. The job
  stays `exit 1` whenever the rollout failed, even if recovery then succeeds; both the original
  verification error and any recovery error are surfaced in `$GITHUB_STEP_SUMMARY`.
- **regression: under the previous implementation, `Invoke-VerifiedDeploy > "verifier contract
  (defect 2) > a verifier that THROWS ... still fails the deploy"` and `"the REAL
  check-startup-migrations.ps1 with an always-failing probe exits non-zero WITHOUT throwing"`
  fail**, because `Write-Error` under `Stop` threw past the controller and the deploy job crashed
  without ever calling `Invoke-Recovery`.

### Defect 3 — verify readiness AND migrations after restoration

- `Invoke-Recovery` rewritten. For every restored required service, under **one shared
  `RecoveryTimeoutSeconds` deadline** spanning all three stages:
  1. artifact identity (defect 1);
  2. `GET /health/ready` == 200 (injectable `-ReadinessProbe`, default real);
  3. for `api` — startup migrations succeeded and the payload's `release.sha` == the **recorded
     known-good** sha (injectable `-ApiMigrationVerifier`, default real
     `check-startup-migrations.ps1 -ExpectedSha <knownGoodSha>`). This step is **never** skipped —
     `Invoke-Recovery` has no skip switch, independent of the deploy-time `run_migrations_check` /
     `-RunMigrationsCheck` flag.
- Identity / readiness / API-migration results are recorded separately per service in
  `ServiceResults` and in `New-RecoverySummaryTable`. `recovered` is returned only when every
  required service passes all applicable checks; any failure → `failed` with per-service manual
  instructions. The unconditional "every service ... reports ready" text is removed.
- **regression: under the previous implementation, `Invoke-Recovery > "REGRESSION (defect 3):
  identity OK but /health/ready 503 => recovery FAILS"`, `"... API migrations fail => recovery
  FAILS"` and `"... recovery-side API migration check runs even though the deploy-time check was
  skipped"` fail**, because the old `Invoke-Recovery` only polled `/health/release` for a sha match
  and then printed "reports ready" without ever calling `/health/ready` or the migration check.

### 4. Partial-rollout and migration-compatibility rules

- **Partial rollout is explicit.** Each service is tracked independently as `updated` (reported the
  target SHA within the timeout), `pending` (reachable, still on the old SHA at timeout) or
  `failed` (unreachable / `railway up` threw). Success requires **every** service `updated` **and**
  API readiness+migrations verified. Services that already updated are still restored to known-good
  during recovery (criterion 7).
- **Migration compatibility.** `deployment-pipeline.md` §5 mandates expand/contract: a migration
  must be backward compatible with the currently-deployed app because old and new code briefly run
  against the same DB during a Railway rollout. When a release *does* contain a contracting change,
  application rollback alone is unsafe — redeploying old code against the already-migrated schema
  does not restore data and can fail to start. `release-safety.json` `appRollbackSafe:false`
  encodes that, and `Invoke-Recovery` then refuses auto rollback and emits manual DB-first
  instructions (criteria 11–12).

---

## Follow-up round 2 (2026-09-10) — "Bind recovery to the correct environment, serving deployment and deadline"

Four further defects fixed here (part 1 — local implementation + regression). Part 2 (six live
Railway demonstrations) stays **BLOCKED** — no Railway access from this environment.

### Railway facts reconfirmed (documentation review, WebSearch/WebFetch against docs.railway.com + npm)

| # | Fact | Finding | Confidence | Source |
|---|---|---|---|---|
| 1 | `RAILWAY_DEPLOYMENT_ID` injected into every deployment incl. `railway up --ci` | Yes — listed under the unconditional "Railway-provided" runtime set; trigger type does not change runtime-variable injection. It is the immutable Railway-assigned id of the *running* instance; the pipeline never sets it. Also unconditional: `RAILWAY_SERVICE_ID`, `RAILWAY_SERVICE_NAME`, `RAILWAY_PROJECT_ID`, `RAILWAY_ENVIRONMENT_ID`, `RAILWAY_REPLICA_ID`. | High | docs.railway.com/reference/variables ("Railway-provided Variables") |
| 1b | `RAILWAY_GIT_COMMIT_SHA` | Provided **only** "if the deploy originated from a GitHub trigger". A `railway up` / `--ci` tarball deploy is not a GitHub trigger, so it is **absent** for our CLI deploys. `/health/release.railwayCommit` is therefore expected to be null in this pipeline and must be treated as "no git identity", not a mismatch. | Medium (absence inferred from the stated GitHub-only condition) | docs.railway.com/reference/variables |
| 2 | `deploymentRollback(id: String!)` return shape / lineage | Docs show only `{ id status }`. Docs wording ("revert to the previously successful deployment", contrasted with Redeploy which "creates a new deployment") implies it **re-activates the prior deployment (same id)**; Railway blog/community posts describe a new list entry. **Not definitively documented.** No `meta` / `snapshotId` / parent-ref field is documented on the return type. Code handles both: it records `ReturnedDeploymentId` and accepts a *new* id only with an explicit commit-hash relationship. | Medium | docs.railway.com/guides/manage-deployments; docs.railway.com/deployments/deployment-actions |
| 3 | latest vs currently-serving deployment | No documented `currentDeployment` / `deploymentInstance` field. `serviceInstance.latestDeployment` is "latest" regardless of state. Railway keeps the previous deployment serving with "a slight overlap for zero downtime" and only SIGTERMs it "once the new deployment is online" — so while `latestDeployment.status` ∈ {INITIALIZING, BUILDING, DEPLOYING, WAITING, QUEUED, NEEDS_APPROVAL} the **previous SUCCESS** deployment is still serving. Practical rule used: serving = latest deployment whose status ∈ {SUCCESS, SLEEPING}. | High (behaviour) / Medium (exact field/enum names) | docs.railway.com/deployments/reference; docs.railway.com/deployments/deployment-actions |
| 4 | `@railway/cli` pin | npm `dist-tags.latest` = **5.52.0** (2026-09-10), no deprecation. `deploy.yml` bumped `5.49.3 → 5.52.0`. `railway variables --skip-deploys` still needs ≥ 3.5. | High | registry.npmjs.org/@railway/cli/latest |

Unresolved contract assumptions (must be closed in the live demonstrations): (1b) exact
absent-vs-empty behaviour of `RAILWAY_GIT_COMMIT_SHA` for `railway up`; (2) whether
`deploymentRollback` reuses the id or spawns a new node, and its full return type; (3) existence of a
`currentDeployment` field and the authoritative `DeploymentStatus` enum.

### Defect 1 — fail when the requested environment cannot be resolved

`Railway.psm1 Get-RailwayContext`: the **first-environment fallback is deleted**. It now requires
exactly one `environments.edges[].node.name -eq $Environment` (0 → throw listing available names;
>1 → throw "ambiguous"; single match with no `node.id` → throw), a non-empty project id, and — new
`-RequiredServices` param (default `api, app, marketing, admin`; `admin-api` still excluded) — a
resolved id for every required service, or it throws listing the missing ones. `Get-RailwayContext`
also returns `EnvironmentName`. The entrypoint calls it (under `$ErrorActionPreference='Stop'`)
**before** `Set-RailwayReleaseVars` / `railway up` / any `deploymentRollback`, and threads the
resolved **environment id** into `Invoke-VerifiedDeploy` (the Railway CLI accepts an id for
`--environment`), so one identity is used for capture, deploy, rollback and verification.
*Regression:* `Get-RailwayContext — unambiguous environment resolution (defect 1)` — requesting
`test` against a `production`-only status fixture throws and issues **zero** `railway variables` /
`railway up` calls; arbitrary edge order still resolves; ambiguous / missing-id / missing-service /
malformed-JSON all fail safely. Under the old code the `test`-not-found case silently selected
`production` and proceeded to deploy.

### Defect 2 — prove the restored deployment is SERVING before declaring recovery

- `ReleaseIdentity.cs` / `/health/release`: adds Railway-injected immutable identity — `deploymentId`
  (`RAILWAY_DEPLOYMENT_ID`), `railwayServiceId`, `railwayEnvironmentId`, `railwayCommit`
  (`RAILWAY_GIT_COMMIT_SHA`, may be null) — read from Railway-provided env vars, **not** from
  `RELEASE_SHA` / `PLATFORM_VERSION` (still present, still clearly labelled mutable display fields).
- `Get-RailwayActiveDeployment` doc now states it returns the *latest* (not necessarily serving)
  deployment; recovery requires `Status ∈ {SUCCESS, SLEEPING}`, keeps polling on
  BUILDING/DEPLOYING/INITIALIZING/WAITING/QUEUED, and hard-fails on FAILED/CRASHED.
- `Restore-RailwayDeployment` returns `{ Service; Restored; RolledBackToId; ReturnedDeploymentId;
  Detail }`.
- Recovery identity stage passes **only when ALL**: (a) the Railway serving deployment status ∈
  {SUCCESS, SLEEPING}; (b) its id == recorded rollback-target **or** == the id `deploymentRollback`
  returned *with* a matching `commitHash` (the standalone commit-hash pass is removed — an unrelated
  deployment sharing a commit hash no longer passes); (c) the serving instance's
  `/health/release.deploymentId` == that same Railway serving-deployment id. Expected vs observed
  deployment id, deployment status and endpoint `deploymentId` are recorded separately in
  `ServiceResults` and `New-RecoverySummaryTable`.
*Regression:* six new cases under `Invoke-Recovery — restore + serving-identity re-verification` —
matching id but BUILDING/FAILED; Railway says restored is serving while the public URL still answers
with the old `deploymentId`; unrelated deployment with the same commit hash. All were impossible to
fail under the old circular `/health/release`-sha check.

### Defect 3 — capture a consistent rollback target AND expected migration identity

`Invoke-VerifiedDeploy` capture builds ONE record per service: `{ service, projectId,
environmentId, serviceId, rollbackTargetDeploymentId, targetReleaseIdentity, commitHash, status,
canRollback, servingMatchAtCapture, endpointDeploymentId, releaseSha/releaseVersion (display),
capturedAtUtc, captureOutcome ∈ {consistent|inconsistent|no-target} }`. The anchor is the
**verified currently-serving** deployment (SUCCESS/SLEEPING, present in the deployments list with
`canRollback:true`). Its recorded identity **is** the expected migration identity — recovery derives
`-ExpectedSha` from `rollbackTargetDeploymentId`'s recorded `targetReleaseIdentity`, **never** from a
live `/health/release` read at recovery time (that was the "restore A, verify migrations against B"
bug). Cross-check at capture: `/health/release.deploymentId` must equal the anchor id; on
disagreement it re-queries a bounded number of times, then records `captureOutcome='inconsistent'`
with **no** automatic target, emits manual instructions, and keeps processing other services.
`no-target` (no rollback-able SUCCESS/SLEEPING) and a missing serving sha are handled the same way —
**never** an empty `-ExpectedSha` (that disables the release gate), never identity copied from
another deployment. Any non-`consistent` record makes overall `recovered` impossible.
*Regression:* `consistent known-good capture` describe — deployment A + endpoint identity B →
`inconsistent`; no rollback-able deployment → `no-target`; missing serving sha → not `consistent`
and `targetReleaseIdentity` stays empty.

### Defect 4 — enforce the recovery deadline inside every operation

ONE per-service deadline covers rollback + identity + readiness + migrations (documented in the
runbook; summaries say "per-service budget: Ns"). Before each operation `remaining = deadline - now`
is computed; `<= 0` → immediate `'deadline exhausted'` per-service failure. The remaining budget is
threaded into: `Invoke-RailwayApi` / `New-RailwayApiSender` (new per-call `-TimeoutSec`, default 30,
recovery passes `min(30, remaining)`), `Test-ServiceReady` (`-TimeoutSeconds`, default 15, recovery
passes `min(15, remaining)`), retry sleeps (`min(PollSeconds, remaining)`), and
`check-startup-migrations.ps1` (new optional `-DeadlineUtc`: caps the per-request timeout and retry
delay to remaining, stops when exhausted, exits non-zero; when omitted the `-MaxAttempts` /
`-DelaySeconds` defaults are unchanged for standalone use). The recovery default
`$ApiMigrationVerifier` and the entrypoint's `$recoveryApiMigrationVerifier` pass the deadline so the
nested 20-attempt loop can no longer overrun a near-exhausted budget by ~420s. After each operation
returns the deadline is re-checked — a probe that returns `Ok` only counts if `now < deadline`,
otherwise the stage is `'late response — deadline exceeded'` and fails. Timeouts / verifier
exceptions become a per-service failure with preserved diagnostics; other services still process;
the original rollout failure stays in the summary and the job still `exit 1`.
*Regression:* `DEFECT 4` cases — a migration `Ok` arriving after the fake clock passed the deadline
is rejected; readiness that never recovers is bounded; within-deadline still returns `recovered`.

### What was run (follow-up round 2)

| Command | Tool version | Result | Run by |
|---|---|---|---|
| `Invoke-Pester .github/scripts/deploy/tests` | Pester 6.2.0 (v5 API), PowerShell 7.x | **42 passed, 0 failed** | lead developer |
| `dotnet build OneBigTeam.slnx -c Release` | .NET SDK 10 | **Build succeeded** — 0 errors | lead developer |
| `dotnet test tests/HR.Web.Tests --filter ReleaseIdentityTests` | .NET SDK 10 | **11 passed** | lead developer |
| `dotnet test tests/HR.Integration.Tests --filter HealthReleaseEndpointTests` | .NET SDK 10 (Testcontainers) | **2 passed** | lead developer |
| Railway documentation review (variables / deploymentRollback / serving deployment / CLI version) | WebSearch + WebFetch | see the facts table above | general-purpose sub-agent |

Not run: full `HR.Integration.Tests`, E2E. **Live Railway verification: BLOCKED — none run.**

### Six live Railway demonstrations, still OUTSTANDING

The per-round demonstration lists are now consolidated into a single authoritative set — see
**"Unverified / OUTSTANDING"** below. Nothing on that list has been run; a real Railway project
token + non-production environment is required and is not available from this environment.

**Status: local implementation and regression verification complete; live Railway verification blocked.**
Ticket 5 is **NOT** fully complete.

---

## Follow-up round 3 (2026-09-10) — "Bound the recovery identity probe and correct operator guidance"

Priority Medium. Scripts + docs only — **no application code changed**, no schema / module /
auth change. Everything already delivered in Ticket 5 and follow-up rounds 1–2 is preserved:
`Get-RailwayContext` environment validation, deployment/endpoint identity correlation,
`captureOutcome` rollback capture, the readiness checks, and the migration-deadline handling.
The single per-service recovery deadline (`$deadline` / `$remaining` / `$apiBudget` in
`Invoke-Recovery`) is unchanged — **no new timeout window was added**.

### The gap

In `Invoke-Recovery`'s identity stage the serving-instance probe was called as
`& $ReleaseProbe $kg.baseUrl` with no budget, and **both** `$ReleaseProbe` implementations
(the module default in `VerifiedDeploy.psm1` and the entrypoint `$recoveryReleaseProbe` in
`Invoke-VerifiedDeploy.ps1`) hard-coded `Invoke-RestMethod -TimeoutSec 15`. With ~1s of the
per-service budget left, the `/health/release` request could still run ~15s. The post-request
`if ((& $remaining) -le 0) { break }` rejected a *late result* but did not stop the overrun.

### The fix (part 1)

- The `ReleaseProbe` contract is now `param($baseUrl, $timeoutSeconds)` — an integer seconds
  budget. Both implementations thread it through as the `Invoke-RestMethod -TimeoutSec` (falling
  back to 15 only when handed nothing, so forward paths and standalone use are unaffected).
- At the call site the remaining budget is captured **once** (reusing the read that already
  gates the post-rollback identity loop, so no extra wall-clock read is introduced) and the probe
  is given `min(15, floor(remaining))`.
- **Fractional / expired budget does not round up past the deadline.** If `floor(remaining) < 1`
  the request is **not started**; an unsuccessful identity-probe result is recorded
  (`endpointDeploymentId` stays `$null`, `identityDetail` = "insufficient recovery budget remaining
  for the /health/release identity request") and the existing not-`identityOk` path emits the
  per-service diagnostic and `continue`s. (The readiness probe's `Max(1, …)` rounding is left
  as-is — out of scope; the identity probe follows this stricter rule.)
- The existing post-request deadline recheck is kept — a response arriving after the deadline
  still cannot count as success.
- A timeout / transport failure surfaces as an unsuccessful probe result (`Get-ServiceRelease`
  already catches and returns `Reachable=$false`), producing a per-service diagnostic; recovery
  then `continue`s and still processes the remaining required services. It never escapes
  `Invoke-Recovery`.
- Forward-deploy probing (`Wait-ServiceRelease` / the `$httpProbe` in `Invoke-VerifiedDeploy`) and
  the shared `Get-ServiceRelease` signature are unchanged.

The recovery timeout is therefore **one per-service budget covering rollback + identity +
readiness + migrations, including the identity `/health/release` HTTP request** — see
`docs/runbooks/deployment-pipeline.md` §6.

### Regression tests (part 2) — `.github/scripts/deploy/tests/VerifiedDeploy.Tests.ps1`

New describe `Invoke-Recovery — identity probe bounded by the remaining recovery budget (defect 4
follow-up)`, deterministic injected clock + injected probe, no real Railway / no real sleeps:

| Test | Asserts |
|---|---|
| 1 — nearly-exhausted budget | probe receives a timeout below `remaining` and **not** 15 (14 with the deterministic clock) |
| 2 — expired deadline (0 remaining) | no identity HTTP request starts; service not verified |
| 3 — fractional remaining (<1s) | probe does **not** start; budget never rounded up; `identityDetail` names the insufficient budget |
| 4 — late successful response | probe runs with a sub-15 timeout, but the post-deadline recheck rejects the match; recovery stays unsuccessful |
| 5 — probe timeout | failure recorded per service, does **not** escape `Invoke-Recovery`, the later required service is still processed |
| 6 — success within budget | matching identity before the deadline → recovery proceeds to readiness + migration and returns `recovered` |
| 7a / 7b / 7c — real wiring | the probe scriptblock is pulled **verbatim** from `VerifiedDeploy.psm1` (`$ReleaseProbe`) and `Invoke-VerifiedDeploy.ps1` (`$recoveryReleaseProbe`) with `Invoke-RestMethod` swapped for a capturing sender; asserts the computed budget reaches the actual HTTP invocation's `-TimeoutSec` (7a: default probe = 9; 7b: entrypoint probe = 7; 7c: end-to-end `Invoke-Recovery` with its built-in default probe = 14), and falls back to 15 only when handed nothing |

**Old-vs-new regression proof (lead developer's runs, `Invoke-Pester .github/scripts/deploy/tests`,
Pester 6.2.0 / PowerShell 7.6.5):**

- With the **fixed `-TimeoutSec 15`** implementation restored (no budget param, call site reverted):
  **47 passed, 6 failed** — tests 1, 3, 6, 7a, 7b, 7c fail (the pre-existing "readiness that never
  recovers is bounded" test still passes, i.e. the fix does not perturb existing timing behaviour).
- With the **corrected** implementation: **53 passed, 0 failed** (whole file, all rounds).

Both recovery-probe implementations — the entrypoint-supplied `$recoveryReleaseProbe` and the
`Invoke-Recovery` default `$ReleaseProbe` — are covered (tests 7a/7c cover the default, 7b covers
the entrypoint copy).

### Documentation search (part 3)

`grep -ri "redeploy --deployment\|redeploy latest\|redeploy the latest\|latest-deployment fallback" docs/`
after the edits returns only **negative statements** ("there is no `railway redeploy --deployment`
path and no 'redeploy latest' fallback") in `deployment-pipeline.md` §6, this ticket's §3 table,
criterion 8 and the residual-risk list. The obsolete operator instruction to "confirm the
`railway redeploy --deployment <id>` form is accepted … otherwise treat the dashboard procedure as
primary" has been removed; the duplicated per-round "six live demonstrations" lists are
consolidated into one authoritative **"Unverified / OUTSTANDING"** set.

### What was run (follow-up round 3)

**Local regression evidence (this repo, no live infrastructure):**

| Command | Tool version | Result | Run by |
|---|---|---|---|
| `Invoke-Pester .github/scripts/deploy/tests` | Pester 6.2.0 (v5 API), PowerShell 7.6.5 | **53 passed, 0 failed** | lead developer |
| Same, with the fixed-`-TimeoutSec 15` implementation temporarily restored | Pester 6.2.0 | **47 passed, 6 failed** (tests 1, 3, 6, 7a, 7b, 7c) — regression captured | lead developer |
| `grep -ri "redeploy --deployment\|redeploy latest\|redeploy the latest\|latest-deployment fallback" docs/` | ripgrep | only negative statements remain | lead developer |

Not run: `dotnet build` (no C# changed), full `HR.Integration.Tests`, E2E.

**Live-Railway evidence:** **None.** The six live non-production demonstrations remain OUTSTANDING
(see "Unverified / OUTSTANDING").

**Status: local implementation and regression verification complete; live Railway verification blocked.**
Ticket 5 is **NOT** fully complete.

---

## Criterion → implementation → verification evidence

| # | Criterion | Implementation | Verification evidence |
|---|---|---|---|
| 1 | Deploy every required service from one immutable release reference | `Invoke-VerifiedDeploy` resolves ONE SHA (`quality-gate` full SHA) and calls `Start-RailwayServiceDeploy` for every service from it. `deploy.yml` passes `DEPLOY_SHA` once. | Pester: `reports success only when every service reports the target sha` (asserts all three services deploy+verify from one `-TargetSha`). |
| 2 | Capture the previous known-good deployment identity for each service | Before any `railway up`, per service: `Get-RailwayActiveDeployment` (deployment id) + `Get-ServiceRelease` (running SHA) → `known-good.json` written **first**. | Pester: `captures the previous known-good deployment id + sha for each service before deploying` (asserts `deploymentId`/`releaseSha` per service). |
| 3 | Wait for each new deployment to finish and verify its release identity | Before each `railway up`, `Set-RailwayReleaseVars` injects `RELEASE_SHA=<target>` + `PLATFORM_VERSION=<version>` per service (`railway variables --set ... --skip-deploys`) — Railway's Nixpacks build does not stamp the assembly, so this is what makes `/health/release` report the real sha. `Version` is threaded from `deploy.yml` `quality-gate` (`1.0.<run_number>+<short_sha>`, derived from the sha if absent). `Wait-ServiceRelease` then polls `/health/release` until reported `sha == target` or bounded timeout. | Pester: `sets RELEASE_SHA + PLATFORM_VERSION for every service before its railway up`; `derives PLATFORM_VERSION from the target sha when no -Version is supplied`; success test (old→NEW transition then `updated`); `.NET`: `ReleaseIdentityTests` pins `sha` resolution + payload shape; integration: `HealthReleaseEndpointTests.Release_endpoint_is_anonymous_and_exposes_only_the_safe_fields`. |
| 4 | A healthy old instance cannot satisfy verification of the new release | Forward: exact `sha` match; a reachable 200 still reporting the old SHA yields `pending`, not `updated`; `check-startup-migrations.ps1 -ExpectedSha` rejects an old API. Recovery does **not** touch `RELEASE_SHA` (`deploymentRollback` restores the historical variable snapshot itself); identity is proven by the immutable Railway serving-deployment id + `/health/release.deploymentId` (`RAILWAY_DEPLOYMENT_ID`), not the reassignable env var, so a stale instance on the public URL fails. | Pester: `fails verification when a healthy OLD instance keeps reporting the old sha`; `serving Railway id matches but the old instance still answers /health/release`. |
| 5 | Verify API readiness and startup migrations against the new deployment | `ApiVerification` waits for `/health/ready`==200 then runs `check-startup-migrations.ps1 -ExpectedSha <new sha>`; the endpoint's `release.sha` must match. | Pester: `fails the deploy when API readiness/migration verification fails even if release sha matches`; integration: `Startup_migrations_payload_keeps_module_keys_and_adds_a_release_sibling`. |
| 6 | Apply bounded rollout and recovery timeouts | `RolloutTimeoutSeconds` (default 900) and `RecoveryTimeoutSeconds` (default 600), both workflow inputs; `Wait-ServiceRelease` is deadline-driven. | Pester: criterion-4 / partial-rollout tests use a fake clock that forces the deadline and asserts the bounded exit (`pending`/`failed`). |
| 7 | Handle partial rollout explicitly, including services that have already updated | Per-service state `updated`/`pending`/`failed`; `FailureReason` names the laggards; recovery restores **all** known-good services incl. the already-updated ones. | Pester: `records partial rollout explicitly when one service fails after others update` (api/app `updated`, marketing `failed`); recovery tests restore api+app. |
| 8 | Restore the recorded known-good deployment using a supported mechanism | `Restore-RailwayDeployment`: GraphQL `deploymentRollback(id: <rollbackTargetDeploymentId>)` **only** — no CLI path, no "redeploy latest" fallback. `deploymentRollback` restores the historical image + variable snapshot itself. `api`/`app` restored first. Unavailable/denied → not restored, `failed`. | Pester: `rolls each service back to its recorded target via deploymentRollback`; `historical targeting errors => zero successful restores ... never substitutes`. |
| 9 | Verify restored release identity and readiness before reporting recovery success | Per service under one deadline: (a) Railway serving deployment status ∈ {SUCCESS, SLEEPING} and its id correlates with the recorded target / `deploymentRollback` return id (commit-hash linked); (b) the serving instance's `/health/release.deploymentId` == that id; (c) `/health/ready` == 200; (d) api startup migrations vs the recorded `targetReleaseIdentity`. A matching `RELEASE_SHA` or commit-hash **alone** is never accepted. `Outcome='recovered'` only when every service passes all applicable checks. | Pester: `Invoke-Recovery — restore + serving-identity re-verification` describe (17 cases). |
| 10 | Report clearly when no previous deployment exists, rollback is unavailable, or recovery fails | Missing `deploymentId` → explicit `::error::No previous known-good deployment recorded for '<svc>'`; failed restore → `Outcome='failed'` + `::error::Recovery incomplete`; summary table + explicit lines. | Pester: `reports clearly when a service has no recorded previous deployment`; `reports failure without false success when the restore command fails`. |
| 11 | Prevent automatic application rollback when incompatible with the migrated database | `Get-ReleaseSafety`; when `appRollbackSafe:false`, `Invoke-Recovery` returns `Outcome='manual-required'` and issues **zero** `deploymentRollback` calls. | Pester: `refuses automatic rollback and issues manual instructions when appRollbackSafe is false (zero rollback calls)`. |
| 12 | Provide actionable manual recovery instructions without implying app rollback restores the DB | The `manual-required` block: (a) "will NOT restore the database", (b) PITR via `backup-and-disaster-recovery.md` **first**, (c) ordered known-good deployment-id restore, (d) confirm `/health/ready` + `/health/release`. Runbook §6 reworded ("code only, never data"). | Pester: same test asserts the emitted text matches `will NOT restore the database` and `backup-and-disaster-recovery`. Runbook §6 diff. |

Helper-level tests (Pester + the .NET/integration tests above) are **necessary but not sufficient**:
they exercise the control flow with a fake Railway CLI, a fake Railway GraphQL sender and a fake
HTTP probe. They do **not** prove the workflow against real Railway infrastructure, real
build/rollout timing, the real `railway status --json` shape, or the real Railway GraphQL
`deploymentRollback` behaviour of the CLI / API versions in use on the runner. Historical restore
is **GraphQL `deploymentRollback(id:)` only** — there is no `railway redeploy --deployment` path and
no "redeploy latest" fallback. The live demonstrations below remain the actual proof and are
**OUTSTANDING**.

---

## What was run

### Helper-test evidence (this repo, no live infrastructure)

| Command | Tool version | Result |
|---|---|---|
| `Invoke-Pester .github/scripts/deploy/tests` | Pester 6.2.0 (v5 API), PowerShell 7.6.5 | **28 passed, 0 failed** |
| `dotnet build OneBigTeam.slnx -c Release` | .NET SDK 10 | **Build succeeded** — 0 errors, 8 pre-existing `NU1510` warnings |
| `dotnet test tests/HR.Web.Tests --filter ReleaseIdentityTests` | .NET SDK 10 | **10 passed** |
| `dotnet test tests/HR.Integration.Tests --filter HealthReleaseEndpointTests` (Testcontainers) | .NET SDK 10 | **2 passed** |

Not run (per project testing constraints): the full `HR.Integration.Tests` suite, the E2E suite.

### Live-infrastructure evidence

**None.** Part 2 (six live Railway demonstrations) is blocked from this environment — see below.

---

## Unverified / OUTSTANDING

These require a real Railway environment and cannot be closed from this repo. All earlier
per-round demonstration lists are **consolidated here** — this is the single authoritative set.
Historical restore is **Railway GraphQL `deploymentRollback(id:)` only**; there is no
`railway redeploy --deployment` / "redeploy latest" step to demonstrate.

**Six live non-production Railway demonstrations — OUTSTANDING:**

1. **Environment binding + successful deploy.** Run `deploy-test.yml` on a known commit. Confirm the
   pinned `@railway/cli` installs, `railway status --json` yields project/service/environment ids,
   `Get-RailwayContext` resolves the `test` environment id + all four service ids (and a wrong
   `-Environment` aborts **before** any `railway variables` / `railway up`), every service is
   `updated`, `/health/release.deploymentId` (`RAILWAY_DEPLOYMENT_ID`) is populated and equals the
   Railway API serving-deployment id, API readiness + startup migrations `PASS` for the new SHA, and
   `Deployment verified`.
2. **New build fails, previous instance stays healthy.** Deploy an `api` container that fails to
   boot. Confirm the rollout times out with `api` non-`updated`, the old instance keeps serving
   `/health/ready` = 200, the deploy is reported failed, and recovery calls `deploymentRollback`
   with the recorded `rollbackTargetDeploymentId` then proves the restored deployment is **serving**
   (status ∈ {SUCCESS, SLEEPING} + id correlation + `/health/release.deploymentId`) before readiness
   and startup migrations against the known-good SHA — and a stale instance still answering the
   public URL cannot pass.
3. **One service fails after others update.** Deploy a commit where `marketing` fails but
   `api`/`app`/`admin` succeed. Confirm the summary shows `marketing = failed`, `FailureReason`
   names it, and recovery restores **all four** (including the already-updated three) to known-good.
4. **Migration / readiness failure paths.** (a) A deliberately failing module migration: every
   `/health/release` reports the new SHA but `check-startup-migrations.ps1 -ExpectedSha` returns
   non-zero and the deploy fails into recovery. (b) `/health/startup-migrations` unreachable /
   malformed JSON: the controller does **not** crash, records a failed deploy, and reaches
   `Invoke-Recovery` (or `manual-required` when `appRollbackSafe:false`). (c) Roll back to a
   known-good whose `/health/ready` stays 503 or whose migrations report a module `!= succeeded`:
   recovery returns `failed` **within** the single `recovery_timeout_seconds` per-service budget,
   not `recovered`.
5. **`deploymentRollback` lineage + identity correlation.** Record whether `deploymentRollback`
   reuses the deployment id or spawns a new node, whether it restores the variable snapshot, and
   confirm a deliberately mismatched `RELEASE_SHA` on the live service does **not** make recovery
   pass (identity is the Railway deployment id + `/health/release.deploymentId`, never the env var).
6. **Rollback unavailable / denied.** (a) A service whose only recent deployments have
   `canRollback:false` (retention-expired) → `captureOutcome='no-target'`, that service excluded,
   other services still processed, overall `failed`. (b) Revoke the project token mid-recovery →
   `deploymentRollback` denied → `Outcome='failed'` + `::error::Recovery incomplete` + non-zero job,
   with the original rollout error still surfaced. No substituted release in any case.

While demonstrating, also close the open Railway-contract assumptions: `RAILWAY_GIT_COMMIT_SHA`
absent-vs-empty for `railway up`; the `deploymentRollback` return type; the existence of a
`currentDeployment` field and the authoritative `DeploymentStatus` enum; and the
`deployments` / `serviceInstances` GraphQL selection sets incl. `meta.commitHash`.

### Other residual risk

- **`railway status --json` parsing** is defensive against several known CLI JSON shapes but is not
  pinned to a contract. `Get-RailwayContext` throws (fail-fast, before any Railway state change) if
  it cannot resolve a project id + environment id; an unrecognised service-list shape yields an
  unknown service id, which surfaces as an explicit per-service capture failure, not a silent wrong
  rollback.
- **Historical restore is now GraphQL-only** (`deploymentRollback`). There is no "redeploy latest"
  fallback. If the mutation is unavailable / denied / `canRollback:false`, the service is reported
  **not restored** and recovery returns `failed` (or `manual-required`) — never a substituted
  release. The GraphQL selection set for `deployments`/`serviceInstances` and the `meta.commitHash`
  field are confirmed only from Railway tooling examples, not a typed schema page — reconfirm in
  demonstrations 1 and 4.
- **`RELEASE_SHA` / `PLATFORM_VERSION` are display labels only.** The controller still injects them
  per service before `railway up` (`Set-RailwayReleaseVars`) so `/health/release` is truthful, but
  recovery no longer resets them and never uses them as the identity gate — the Railway deployment
  id is the anchor. `deploymentRollback` restores the historical variable snapshot itself
  (docs.railway.com/deployments/deployment-actions).
- **`@railway/cli` pinned to `5.52.0`** — npm `dist-tags.latest` as of 2026-09-10. `railway
  variables --skip-deploys` still needs ≥ 3.5.
- **`admin-api` is out of scope** until `HR.Admin.Api` exists as a host. When it is added it must be
  appended to `SERVICE_URLS` in `deploy.yml`, given an `ADMIN_API_BASE_URL` var, and it must call
  `MapDefaultEndpoints()` (which maps `/health/release` automatically).
- **No off-GitHub deployment log** (unchanged from NFR-06 — deployment-pipeline §7 optional item).
