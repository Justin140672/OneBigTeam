param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    # Reliability review issue 5 (P1): this script's entire purpose is DETAILED migration
    # verification (per-module status + release.sha) — that detail is only ever returned by
    # /health/startup-migrations to a caller presenting the same secret as the deployed app's
    # HealthChecks:ReadinessDetailToken (see HR.ServiceDefaults.HealthCheckEndpoints /
    # StartupMigrationRunner.ToHealthResult). A blank token here can no longer silently degrade to
    # "send no auth, get back a minimal/anonymous payload, then fail with a confusing 'missing
    # companies/identity' error" — it now fails immediately with an actionable config error before
    # a single HTTP call is made.
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrWhiteSpace()]
    [string]$BearerToken,

    [Parameter(Mandatory = $false)]
    [int]$MaxAttempts = 10,

    [Parameter(Mandatory = $false)]
    [int]$DelaySeconds = 6,

    # Ticket 5: when supplied, the endpoint's reported release.sha MUST equal this value. This is
    # what stops a healthy OLD API instance (all modules "succeeded", but running the previous
    # release) from satisfying the new release's migration gate.
    [Parameter(Mandatory = $false)]
    [string]$ExpectedSha = "",

    # Ticket 5 follow-up (defect 2): injectable HTTP probe so a test can drive real retry
    # exhaustion. param($Uri, $Headers, $TimeoutSec) -> parsed response object. Defaults to
    # Invoke-RestMethod. $TimeoutSec may be $null (use the 15s default).
    [Parameter(Mandatory = $false)]
    [scriptblock]$HttpProbe,

    # Ticket 5 follow-up (defect 4): when supplied, ONE hard deadline covering every attempt.
    [Parameter(Mandatory = $false)]
    [Nullable[datetime]]$DeadlineUtc = $null,

    [Parameter(Mandatory = $false)]
    [scriptblock]$NowProvider,

    [Parameter(Mandatory = $false)]
    [scriptblock]$SleepProvider
)

# This script is a THIN CLI ENTRY POINT. All retry/validation logic lives in
# StartupMigrationsCheck.psm1's Invoke-StartupMigrationsCheck, which returns a result object instead
# of calling `exit`. This is the ONLY place in the migration-check code path that still calls `exit`
# for real (standalone / CI) usage — Pester exercises the module function directly, so a failing test
# case can no longer terminate the test host mid-suite (see
# .github/scripts/deploy/tests/CheckStartupMigrations.Tests.ps1).
#
# NOTE (defect 2, still true here): keep 'Stop' for the request itself, but on an EXHAUSTED-retries
# failure this script must exit non-zero WITHOUT raising a terminating error, so that when it is
# dot-invoked by Invoke-VerifiedDeploy.ps1 the caller's `if ($LASTEXITCODE -ne 0)` branch still runs.
$ErrorActionPreference = "Stop"

Import-Module (Join-Path $PSScriptRoot 'deploy/StartupMigrationsCheck.psm1') -Force -DisableNameChecking

# Build the argument set, forwarding only the parameters the caller actually supplied so the
# module function's own defaults (HttpProbe = Invoke-RestMethod, etc.) still apply untouched.
$forwardedParams = @{
    ApiBaseUrl  = $ApiBaseUrl
    BearerToken = $BearerToken
    MaxAttempts = $MaxAttempts
    DelaySeconds = $DelaySeconds
    ExpectedSha = $ExpectedSha
}
if ($PSBoundParameters.ContainsKey('HttpProbe')) { $forwardedParams['HttpProbe'] = $HttpProbe }
if ($PSBoundParameters.ContainsKey('DeadlineUtc')) { $forwardedParams['DeadlineUtc'] = $DeadlineUtc }
if ($PSBoundParameters.ContainsKey('NowProvider')) { $forwardedParams['NowProvider'] = $NowProvider }
if ($PSBoundParameters.ContainsKey('SleepProvider')) { $forwardedParams['SleepProvider'] = $SleepProvider }

$result = Invoke-StartupMigrationsCheck @forwardedParams
exit $result.ExitCode
