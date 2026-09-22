<#
.SYNOPSIS
    Dry-run rehearsal checklist for the API_HEALTH_BEARER_TOKEN / HealthChecks:ReadinessDetailToken
    rotation protocol (specifications/runbooks/deployment-pipeline.md section 5, "Rotation procedure
    — dual-token overlap window").

.DESCRIPTION
    This script does NOT touch Railway or GitHub. It prints the exact sequence of commands an
    operator runs to rehearse a rotation in a non-production environment (Test or Staging), plus
    the evidence to capture at each step. Run it, follow the printed commands manually, and paste
    the command output / screenshots into the incident or runbook sign-off record referenced at the
    bottom.

    This is intentionally a manual rehearsal aid, not an automated rehearsal. Actually executing
    these commands against a live Railway project and a live GitHub Environment requires
    credentials and infrastructure this script has no access to — that execution, and retaining its
    evidence, is a manual operator step.

.PARAMETER Environment
    The GitHub Environment / Railway environment to rehearse against. Must be 'test' or 'staging' —
    refuses 'production' (rehearsals must never target production).

.EXAMPLE
    ./.github/scripts/deploy/tests/Invoke-TokenRotationRehearsal.ps1 -Environment test
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('test', 'staging')]
    [string]$Environment
)

$railwayEnvName = (Get-Culture).TextInfo.ToTitleCase($Environment)
$githubSecretSuffix = $Environment.ToUpperInvariant()

Write-Host "=== Token rotation rehearsal checklist: $Environment ===" -ForegroundColor Cyan
Write-Host "This script prints commands only. Nothing below is executed automatically." -ForegroundColor Yellow
Write-Host ""

Write-Host "Step 0 — capture the CURRENT token value before touching anything:" -ForegroundColor Green
Write-Host "  railway variables --service api --environment $railwayEnvName --json | jq -r '.HealthChecks__ReadinessDetailToken'"
Write-Host "  # Record this as OLD_TOKEN. Do not lose it — it is Previous in step 1."
Write-Host ""

Write-Host "Step 1 — generate the NEW token and deploy the API with dual-token config (config-only deploy, no other release content):" -ForegroundColor Green
Write-Host "  NEW_TOKEN=`$(openssl rand -hex 32)"
Write-Host "  railway variables --service api --environment $railwayEnvName --skip-deploys ``"
Write-Host "    --set HealthChecks__ReadinessDetailToken=`$NEW_TOKEN ``"
Write-Host "    --set HealthChecks__ReadinessDetailTokenPrevious=`$OLD_TOKEN"
Write-Host "  # Trigger the dedicated config-only deploy (e.g. gh workflow run deploy-$Environment.yml)"
Write-Host "  # Evidence to capture: deploy job summary link; forward-verification step output showing"
Write-Host "  #   'Startup migrations healthy' (it will present the GitHub secret's CURRENT value, which"
Write-Host "  #   is still OLD_TOKEN at this point, and the dual-token app must accept it via Previous)."
Write-Host ""

Write-Host "Step 2 — confirm the dual-token instance accepts BOTH values before rotating the GitHub secret:" -ForegroundColor Green
Write-Host "  curl -sf -H `"X-Health-Token: `$OLD_TOKEN`" https://api-$Environment.onebigteam.app/health/startup-migrations | jq ."
Write-Host "  curl -sf -H `"X-Health-Token: `$NEW_TOKEN`" https://api-$Environment.onebigteam.app/health/startup-migrations | jq ."
Write-Host "  # Evidence to capture: both responses return full per-module detail (not the minimal"
Write-Host "  #   anonymous payload) and the same release.sha."
Write-Host ""

Write-Host "Step 3 — rotate the GitHub Environment secret to the new value:" -ForegroundColor Green
Write-Host "  gh secret set API_HEALTH_BEARER_TOKEN_$githubSecretSuffix --env $Environment --body `"`$NEW_TOKEN`""
Write-Host "  # Evidence to capture: gh secret list --env $Environment shows an updated timestamp."
Write-Host ""

Write-Host "Step 4 — trigger a normal deploy and confirm forward-verification passes with the NEW secret:" -ForegroundColor Green
Write-Host "  gh workflow run deploy-$Environment.yml"
Write-Host "  # Evidence to capture: deploy job summary link; forward-verification step output."
Write-Host ""

Write-Host "Step 5 — rehearse the recovery path: force a rollback and confirm the recovery-side migration verifier still authenticates:" -ForegroundColor Green
Write-Host "  # Trigger a deploy that intentionally fails readiness (per the availability runbook's"
Write-Host "  #   rollback-test procedure) and confirm the run summary reports 'recovered' with the"
Write-Host "  #   api-startup-migrations check passing against the rollback target's release.sha."
Write-Host "  # Evidence to capture: run summary + the per-service verification table."
Write-Host ""

Write-Host "Step 6 — revoke the previous token (close the overlap window):" -ForegroundColor Green
Write-Host "  railway variables --service api --environment $railwayEnvName --skip-deploys ``"
Write-Host "    --unset HealthChecks__ReadinessDetailTokenPrevious"
Write-Host "  # Trigger a config-only deploy, then confirm OLD_TOKEN is rejected:"
Write-Host "  curl -s -o /dev/null -w '%{http_code}' -H `"X-Health-Token: `$OLD_TOKEN`" https://api-$Environment.onebigteam.app/health/startup-migrations"
Write-Host "  # Evidence to capture: the above returns the minimal/anonymous payload (no per-module"
Write-Host "  #   detail) for OLD_TOKEN, and full detail for NEW_TOKEN."
Write-Host ""

Write-Host "Record all captured evidence (links, command output, timestamps) in:" -ForegroundColor Cyan
Write-Host "  specifications/compliance/data-protection-operations.md (assurance register), alongside"
Write-Host "  the quarterly rollback-test record already required by the deployment-pipeline runbook."
