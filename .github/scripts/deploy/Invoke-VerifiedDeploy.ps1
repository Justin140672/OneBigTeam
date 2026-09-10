<#
  CI entrypoint for Ticket 5 (+ follow-up). Wires the real Railway CLI + real Railway GraphQL API +
  real HTTP probes into VerifiedDeploy.psm1, runs the verified deploy, and on ANY verification
  failure OR any terminating error after rollout begins runs bounded recovery. Writes a per-service
  summary to $GITHUB_STEP_SUMMARY and sets the exit code (0 = deployed & verified, 1 = the rollout
  failed — regardless of whether recovery then succeeded).

  Required:
    -Environment           Railway environment slug (test|staging|production)
    -TargetSha             Full git SHA of the release being deployed (from quality-gate)
    -ServiceUrlsJson       JSON object: { "api": "https://...", "app": "https://...", ... }
  Optional:
    -Version               Build label injected as PLATFORM_VERSION (from quality-gate); derived
                           from -TargetSha when omitted. RELEASE_SHA is always set to -TargetSha.
    -RolloutTimeoutSeconds (default 900)   -RecoveryTimeoutSeconds (default 600)
    -PollSeconds (default 10)              -ApiReadyTimeoutSeconds (default 600)
    -MigrationBearerToken
    -RunMigrationsCheck    ($true default) gates the FORWARD deploy on startup-migrations. It does
                           NOT affect the recovery-side migration verification, which always runs.
    -RailwayApiEndpoint    (default https://backboard.railway.com/graphql/v2)
#>
param(
    [Parameter(Mandatory)] [string]$Environment,
    [Parameter(Mandatory)] [string]$TargetSha,
    [Parameter(Mandatory)] [string]$ServiceUrlsJson,
    [string]$Version = "",
    [int]$RolloutTimeoutSeconds = 900,
    [int]$RecoveryTimeoutSeconds = 600,
    [int]$PollSeconds = 10,
    [int]$ApiReadyTimeoutSeconds = 600,
    [string]$MigrationBearerToken = "",
    [bool]$RunMigrationsCheck = $true,
    [string]$RailwayApiEndpoint = "https://backboard.railway.com/graphql/v2"
)

$SkipApiMigrationCheck = -not $RunMigrationsCheck

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'Railway.psm1') -Force -DisableNameChecking
Import-Module (Join-Path $PSScriptRoot 'VerifiedDeploy.psm1') -Force -DisableNameChecking

function Write-Summary([string]$Text) {
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -Path $env:GITHUB_STEP_SUMMARY -Value $Text }
    Write-Host $Text
}

# --- Parse service URLs and FAIL FAST before any Railway state change ---
$urlMap = $ServiceUrlsJson | ConvertFrom-Json
$services = @()
foreach ($prop in $urlMap.PSObject.Properties) {
    $services += [pscustomobject]@{ Name = $prop.Name; BaseUrl = [string]$prop.Value }
}
if ($services.Count -eq 0) { throw "ServiceUrlsJson resolved to zero services." }

# admin-api stays excluded until a deployable HR.Admin.Api host exists.
$requiredServices = @('api', 'app', 'marketing', 'admin')
$missingUrls = @()
foreach ($required in $requiredServices) {
    $svc = $services | Where-Object { $_.Name -eq $required } | Select-Object -First 1
    if ($null -eq $svc -or [string]::IsNullOrWhiteSpace($svc.BaseUrl)) { $missingUrls += $required }
}
if ($missingUrls.Count -gt 0) {
    throw ("Required base URL(s) missing/empty before deploy: {0}. Set API_BASE_URL / APP_BASE_URL / " +
           "MARKETING_BASE_URL / ADMIN_BASE_URL for this environment. Refusing to change any Railway state.") -f ($missingUrls -join ', ')
}

$railwayToken = $env:RAILWAY_TOKEN
if ([string]::IsNullOrWhiteSpace($railwayToken)) { throw "RAILWAY_TOKEN is not set — cannot call the Railway API. Refusing to deploy." }

$runner = New-RailwayCommandRunner
$httpProbe = {
    param($uri)
    Invoke-RestMethod -Uri $uri -Method Get -TimeoutSec 15
}

# --- Resolve + HARD-VALIDATE the Railway context BEFORE any Railway state change (defect 1) ---
# Get-RailwayContext throws if $Environment is unknown / ambiguous / has no id, if the project id is
# missing, or if any required service id is absent. $ErrorActionPreference='Stop' aborts the whole
# entrypoint here — no Set-RailwayReleaseVars / railway up / deploymentRollback runs.
$ctx = Get-RailwayContext -Environment $Environment -Runner $runner -RequiredServices $requiredServices
$railwayApi = New-RailwayApiClient -Token $railwayToken -ProjectId $ctx.ProjectId `
    -EnvironmentId $ctx.EnvironmentId -ServiceIds $ctx.ServiceIds -Endpoint $RailwayApiEndpoint

# One resolved environment identity is used for capture, deploy, rollback and verification. The
# GraphQL client uses $ctx.EnvironmentId; the CLI calls take the resolved id too (the Railway CLI
# accepts an environment id for --environment).
$resolvedEnvironment = $ctx.EnvironmentId

# --- FORWARD-deploy API verifier: readiness + startup migrations vs the NEW sha ---
# -RunMigrationsCheck:$false is honoured HERE only (non-api environments / smoke deploys).
$apiVerification = {
    param($sha)
    $api = $services | Where-Object { $_.Name -eq 'api' } | Select-Object -First 1
    if ($null -eq $api) { return [pscustomobject]@{ Ok = $true; Detail = 'no api service in scope' } }
    if ($SkipApiMigrationCheck) { return [pscustomobject]@{ Ok = $true; Detail = 'forward api migration check skipped by request' } }

    $deadline = (Get-Date).AddSeconds($ApiReadyTimeoutSeconds)
    $ready = $false
    do {
        try {
            $r = Invoke-WebRequest -Uri "$($api.BaseUrl.TrimEnd('/'))/health/ready" -TimeoutSec 15 -SkipHttpErrorCheck
            if ($r.StatusCode -eq 200) { $ready = $true; break }
        } catch { }
        Start-Sleep -Seconds $PollSeconds
    } while ((Get-Date) -lt $deadline)

    if (-not $ready) { return [pscustomobject]@{ Ok = $false; Detail = "api /health/ready did not reach 200 within $ApiReadyTimeoutSeconds s" } }

    & (Join-Path $PSScriptRoot '..\check-startup-migrations.ps1') `
        -ApiBaseUrl $api.BaseUrl -BearerToken $MigrationBearerToken `
        -MaxAttempts 20 -DelaySeconds 6 -ExpectedSha $sha
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Ok = $false; Detail = "startup-migrations check failed for sha $sha (see log above)" }
    }
    return [pscustomobject]@{ Ok = $true; Detail = "api ready and startup migrations verified for sha $sha" }
}

# --- RECOVERY-side verifiers: readiness + startup migrations vs the recorded KNOWN-GOOD sha ---
# Deliberately independent of -RunMigrationsCheck: the recovery migration check is NEVER skipped.
$recoveryReadiness = {
    param($baseUrl, $timeoutSeconds)
    $t = if ($timeoutSeconds -and [int]$timeoutSeconds -gt 0) { [int]$timeoutSeconds } else { 15 }
    Test-ServiceReady -BaseUrl $baseUrl -TimeoutSeconds $t
}
# Defect 4 follow-up: the recovery identity probe is bounded by the seconds still left in the
# per-service recovery deadline (passed as the second arg), threaded through as -TimeoutSec so a
# near-exhausted budget can no longer be overrun by a ~15s /health/release request.
# Defect 4 follow-up: the recovery identity probe is bounded by the seconds still left in the
# per-service recovery deadline (passed as the second arg), threaded through as -TimeoutSec so a
# near-exhausted budget can no longer be overrun by a ~15s /health/release request.
$recoveryReleaseProbe = {
    param($baseUrl, $timeoutSeconds)
    $t = if ($timeoutSeconds -and [int]$timeoutSeconds -gt 0) { [int]$timeoutSeconds } else { 15 }
    Get-ServiceRelease -BaseUrl $baseUrl -HttpProbe { param($u) Invoke-RestMethod -Uri $u -Method Get -TimeoutSec $t }.GetNewClosure()
}
# Defect 4: the recovery migration check is bounded by the per-service recovery deadline threaded
# in as the third argument, so the nested 20-attempt loop can no longer overrun a near-exhausted
# budget by ~420s.
$recoveryApiMigrationVerifier = {
    param($baseUrl, $expectedSha, $deadlineUtc)
    & (Join-Path $PSScriptRoot '..\check-startup-migrations.ps1') `
        -ApiBaseUrl $baseUrl -BearerToken $MigrationBearerToken `
        -MaxAttempts 20 -DelaySeconds 6 -ExpectedSha $expectedSha `
        -DeadlineUtc ([datetime]$deadlineUtc).ToUniversalTime()
    if ($LASTEXITCODE -ne 0) {
        return [pscustomobject]@{ Ok = $false; Detail = "recovery startup-migrations check failed for known-good sha $expectedSha (see log above)" }
    }
    return [pscustomobject]@{ Ok = $true; Detail = "recovery startup migrations verified for known-good sha $expectedSha" }
}

$knownGoodPath = Join-Path $PSScriptRoot 'known-good.json'

# --- Run the verified deploy. ANY terminating error after this point becomes a failed deploy
#     result and still reaches the recovery decision (defect 2, controller boundary). ---
$deploy = $null
$deployError = $null
try {
    $deploy = Invoke-VerifiedDeploy -Services $services -TargetSha $TargetSha -Environment $resolvedEnvironment `
        -Version $Version -RolloutTimeoutSeconds $RolloutTimeoutSeconds -PollSeconds $PollSeconds `
        -Runner $runner -RailwayApi $railwayApi -HttpProbe $httpProbe -KnownGoodPath $knownGoodPath `
        -ApiVerification $apiVerification
}
catch {
    $deployError = $_
    $recoveredKnownGood = @()
    if (Test-Path $knownGoodPath) {
        try { $recoveredKnownGood = @(Get-Content -Raw $knownGoodPath | ConvertFrom-Json) } catch { }
    }
    $deploy = [pscustomobject]@{
        Success       = $false
        TargetSha     = $TargetSha
        KnownGood     = $recoveredKnownGood
        Results       = @()
        ApiResult     = $null
        Summary       = "### Verified deployment — target sha ``$TargetSha```n`n::error::Deploy controller threw after rollout began: $($_.Exception.Message)"
        FailureReason = "Verified deploy threw a terminating error after rollout began: $($_.Exception.Message)"
        KnownGoodPath = $knownGoodPath
    }
}

Write-Summary $deploy.Summary

if ($deploy.Success) {
    Write-Summary "`n**Deployment verified.** Every required service is running ``$TargetSha``."
    exit 0
}

Write-Summary "`n::error::$($deploy.FailureReason)"
if ($null -ne $deployError) {
    Write-Summary "Original terminating error (preserved): $($deployError.Exception.Message)`n$($deployError.ScriptStackTrace)"
}
Write-Summary "`n### Recovery"

$safety = Get-ReleaseSafety
$recovery = Invoke-Recovery -KnownGood $deploy.KnownGood -RailwayApi $railwayApi `
    -RecoveryTimeoutSeconds $RecoveryTimeoutSeconds -PollSeconds $PollSeconds `
    -ReleaseSafety $safety `
    -ReadinessProbe $recoveryReadiness -ApiMigrationVerifier $recoveryApiMigrationVerifier `
    -ReleaseProbe $recoveryReleaseProbe

Write-Summary $recovery.Summary
foreach ($line in $recovery.Lines) { Write-Summary $line }

switch ($recovery.Outcome) {
    'recovered'       { Write-Summary "`n**Recovery succeeded** — every required service rolled back to its recorded known-good deployment; artifact identity, readiness and API startup migrations re-verified." }
    'manual-required' { Write-Summary "`n**Manual recovery required** — automatic application rollback was refused for this release (migration-incompatible)." }
    default           { Write-Summary "`n**Recovery FAILED** — manual intervention required." }
}

# The rollout failed. The job is unsuccessful even when recovery then succeeded. Both errors are
# already in the summary above.
Write-Summary "`n::error::Original rollout failure: $($deploy.FailureReason)"
exit 1
