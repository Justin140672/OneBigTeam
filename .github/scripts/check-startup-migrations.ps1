param(
    [Parameter(Mandatory = $true)]
    [string]$ApiBaseUrl,

    [Parameter(Mandatory = $false)]
    [string]$BearerToken = "",

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
    [scriptblock]$HttpProbe = {
        param($Uri, $Headers, $TimeoutSec)
        $t = if ($TimeoutSec -and [int]$TimeoutSec -gt 0) { [int]$TimeoutSec } else { 15 }
        Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -TimeoutSec $t
    },

    # Ticket 5 follow-up (defect 4): when supplied, ONE hard deadline covering every attempt. Before
    # each attempt the remaining budget is checked; the per-request timeout and the retry delay are
    # both capped to what is left; when the budget is exhausted the script stops and exits non-zero.
    # The nested 20-attempt loop can then no longer run ~420s past a near-exhausted recovery budget.
    # When NOT supplied, -MaxAttempts / -DelaySeconds behave exactly as before (standalone use).
    [Parameter(Mandatory = $false)]
    [Nullable[datetime]]$DeadlineUtc = $null,

    [Parameter(Mandatory = $false)]
    [scriptblock]$NowProvider = { [datetime]::UtcNow },

    [Parameter(Mandatory = $false)]
    [scriptblock]$SleepProvider = { param($s) Start-Sleep -Seconds $s }
)

# NOTE (defect 2): keep 'Stop' for the request itself, but on an EXHAUSTED-retries failure this
# script must exit non-zero WITHOUT raising a terminating error. Under $ErrorActionPreference='Stop'
# `Write-Error` throws, which — when this script is dot-invoked by Invoke-VerifiedDeploy.ps1 — skips
# the caller's `if ($LASTEXITCODE -ne 0)` branch and escapes recovery entirely. Use Write-Warning /
# Write-Host + `exit 1` so both standalone runs and the in-process caller see a clean non-zero exit.
$ErrorActionPreference = "Stop"

$trimmedBaseUrl = $ApiBaseUrl.TrimEnd('/')
$uri = "$trimmedBaseUrl/health/startup-migrations"
$headers = @{}

if (-not [string]::IsNullOrWhiteSpace($BearerToken)) {
    $headers["Authorization"] = "Bearer $BearerToken"
}

function Get-RemainingSeconds {
    if ($null -eq $DeadlineUtc) { return $null }
    return ([double](($DeadlineUtc - (& $NowProvider)).TotalSeconds))
}

for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    $remaining = Get-RemainingSeconds
    if ($null -ne $remaining -and $remaining -le 0) {
        Write-Warning "Startup-migrations check deadline exhausted before attempt $attempt at '$uri'."
        exit 1
    }
    $reqTimeout = if ($null -ne $remaining) { [int][Math]::Max(1, [Math]::Min(15, [Math]::Floor($remaining))) } else { $null }

    try {
        $response = & $HttpProbe $uri $headers $reqTimeout

        # Defect 4: a response that only arrived after the deadline does not count.
        $afterCall = Get-RemainingSeconds
        if ($null -ne $afterCall -and $afterCall -le 0) {
            Write-Warning "Startup-migrations response for '$uri' arrived after the deadline — rejecting."
            exit 1
        }

        if ($null -eq $response.companies -or $null -eq $response.identity) {
            throw "Health payload is missing 'companies' or 'identity' sections."
        }

        # Every module key (everything except the 'release' sibling) must report "succeeded".
        $moduleProps = $response.PSObject.Properties | Where-Object { $_.Name -ne 'release' }
        $notSucceeded = @()
        foreach ($prop in $moduleProps) {
            $status = [string]$prop.Value.status
            if ($status -ne "succeeded") {
                $notSucceeded += "$($prop.Name)=$status"
            }
        }

        if ($notSucceeded.Count -gt 0) {
            throw "Startup migrations not healthy: $($notSucceeded -join ', ')"
        }

        # Release-identity gate.
        if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
            $reportedSha = [string]$response.release.sha
            if ([string]::IsNullOrWhiteSpace($reportedSha)) {
                throw "Health payload has no 'release.sha' — cannot confirm this is the new release."
            }
            if ($reportedSha -ne $ExpectedSha) {
                throw "Startup-migrations release mismatch: endpoint reports sha '$reportedSha' but expected '$ExpectedSha' (a healthy OLD instance)."
            }
            Write-Host "Startup migrations healthy for the expected release. sha=$reportedSha modules=$($moduleProps.Count)"
        }
        else {
            Write-Host "Startup migrations healthy. modules=$($moduleProps.Count)"
        }

        exit 0
    }
    catch {
        if ($attempt -eq $MaxAttempts) {
            Write-Warning "Health check failed after $MaxAttempts attempts at '$uri'. Last error: $($_.Exception.Message)"
            exit 1
        }

        $delay = $DelaySeconds
        $afterErr = Get-RemainingSeconds
        if ($null -ne $afterErr) {
            if ($afterErr -le 0) {
                Write-Warning "Startup-migrations check deadline exhausted after attempt $attempt at '$uri'. Last error: $($_.Exception.Message)"
                exit 1
            }
            $delay = [int][Math]::Max(0, [Math]::Min($DelaySeconds, [Math]::Floor($afterErr)))
        }
        Write-Host "Attempt $attempt/$MaxAttempts failed for '$uri'. Retrying in $delay seconds..."
        & $SleepProvider $delay
    }
}

Write-Warning "Health check failed unexpectedly."
exit 1
