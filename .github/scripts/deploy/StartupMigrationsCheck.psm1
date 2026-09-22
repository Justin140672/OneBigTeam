# Core logic for check-startup-migrations.ps1, extracted into a module so it can be exercised by
# Pester WITHOUT going through a terminating `exit` call.
#
# Reliability review finding 5 (P1 follow-up): the original check-startup-migrations.ps1 called
# `exit` directly inside its retry loop. When a Pester test invoked that script in-process (via
# `& $script:ScriptPath ...`, not a child process), a mid-suite `exit` risks terminating the whole
# Pester host before later `It` blocks run, silently truncating the suite instead of failing loudly.
# `Invoke-StartupMigrationsCheck` below contains the exact same retry/validation logic as before but
# RETURNS a result object ([pscustomobject]@{ ExitCode; Success; Message; IsMissingAuthorization })
# instead of exiting. The real CLI entry point (check-startup-migrations.ps1) is now a thin wrapper:
# it calls this function and is the ONLY place that still calls `exit`, for real standalone/CI usage.

function Invoke-StartupMigrationsCheck {
    param(
        [Parameter(Mandatory = $true)]
        [string]$ApiBaseUrl,

        # See check-startup-migrations.ps1 for the full rationale: this token is mandatory because
        # /health/startup-migrations only returns per-module detail (and release.sha) to a caller
        # presenting the same secret as the deployed app's HealthChecks:ReadinessDetailToken.
        [Parameter(Mandatory = $true)]
        [ValidateNotNullOrWhiteSpace()]
        [string]$BearerToken,

        [Parameter(Mandatory = $false)]
        [int]$MaxAttempts = 10,

        [Parameter(Mandatory = $false)]
        [int]$DelaySeconds = 6,

        # Ticket 5: when supplied, the endpoint's reported release.sha MUST equal this value.
        [Parameter(Mandatory = $false)]
        [string]$ExpectedSha = "",

        [Parameter(Mandatory = $false)]
        [scriptblock]$HttpProbe = {
            param($Uri, $Headers, $TimeoutSec)
            $t = if ($TimeoutSec -and [int]$TimeoutSec -gt 0) { [int]$TimeoutSec } else { 15 }
            Invoke-RestMethod -Uri $Uri -Headers $Headers -Method Get -TimeoutSec $t
        },

        [Parameter(Mandatory = $false)]
        [Nullable[datetime]]$DeadlineUtc = $null,

        [Parameter(Mandatory = $false)]
        [scriptblock]$NowProvider = { [datetime]::UtcNow },

        [Parameter(Mandatory = $false)]
        [scriptblock]$SleepProvider = { param($s) Start-Sleep -Seconds $s }
    )

    $trimmedBaseUrl = $ApiBaseUrl.TrimEnd('/')
    $uri = "$trimmedBaseUrl/health/startup-migrations"
    $headers = @{}
    $headers["Authorization"] = "Bearer $BearerToken"
    $headers["X-Health-Token"] = $BearerToken

    function Get-RemainingSeconds {
        if ($null -eq $DeadlineUtc) { return $null }
        return ([double](($DeadlineUtc - (& $NowProvider)).TotalSeconds))
    }

    for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
        $remaining = Get-RemainingSeconds
        if ($null -ne $remaining -and $remaining -le 0) {
            $msg = "Startup-migrations check deadline exhausted before attempt $attempt at '$uri'."
            Write-Warning $msg
            return [pscustomobject]@{ ExitCode = 1; Success = $false; Message = $msg; IsMissingAuthorization = $false }
        }
        $reqTimeout = if ($null -ne $remaining) { [int][Math]::Max(1, [Math]::Min(15, [Math]::Floor($remaining))) } else { $null }

        try {
            $response = & $HttpProbe $uri $headers $reqTimeout

            $afterCall = Get-RemainingSeconds
            if ($null -ne $afterCall -and $afterCall -le 0) {
                $msg = "Startup-migrations response for '$uri' arrived after the deadline — rejecting."
                Write-Warning $msg
                return [pscustomobject]@{ ExitCode = 1; Success = $false; Message = $msg; IsMissingAuthorization = $false }
            }

            # Reliability review issue 5 (P1): distinguish "the token didn't grant detail access"
            # (a mismatched/rotated-out token) from "detail was returned but migrations are
            # genuinely unhealthy".
            if ($null -eq $response.companies -or $null -eq $response.identity) {
                throw "No per-module detail was returned by '$uri' — this means X-Health-Token did not " + `
                    "match the deployed app's HealthChecks:ReadinessDetailToken (missing authorization), " + `
                    "not that migrations are unhealthy. Verify the deploy pipeline's migration bearer " + `
                    "token secret matches the app's configured ReadinessDetailToken for this environment."
            }

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

            if (-not [string]::IsNullOrWhiteSpace($ExpectedSha)) {
                $reportedSha = [string]$response.release.sha
                if ([string]::IsNullOrWhiteSpace($reportedSha)) {
                    throw "Health payload has no 'release.sha' — cannot confirm this is the new release."
                }
                if ($reportedSha -ne $ExpectedSha) {
                    throw "Startup-migrations release mismatch: endpoint reports sha '$reportedSha' but expected '$ExpectedSha' (a healthy OLD instance)."
                }
                $msg = "Startup migrations healthy for the expected release. sha=$reportedSha modules=$($moduleProps.Count)"
                Write-Host $msg
            }
            else {
                $msg = "Startup migrations healthy. modules=$($moduleProps.Count)"
                Write-Host $msg
            }

            return [pscustomobject]@{ ExitCode = 0; Success = $true; Message = $msg; IsMissingAuthorization = $false }
        }
        catch {
            $isMissingAuth = $_.Exception.Message -match 'missing authorization'

            if ($attempt -eq $MaxAttempts) {
                $msg = "Health check failed after $MaxAttempts attempts at '$uri'. Last error: $($_.Exception.Message)"
                Write-Warning $msg
                return [pscustomobject]@{ ExitCode = 1; Success = $false; Message = $msg; IsMissingAuthorization = $isMissingAuth }
            }

            $delay = $DelaySeconds
            $afterErr = Get-RemainingSeconds
            if ($null -ne $afterErr) {
                if ($afterErr -le 0) {
                    $msg = "Startup-migrations check deadline exhausted after attempt $attempt at '$uri'. Last error: $($_.Exception.Message)"
                    Write-Warning $msg
                    return [pscustomobject]@{ ExitCode = 1; Success = $false; Message = $msg; IsMissingAuthorization = $isMissingAuth }
                }
                $delay = [int][Math]::Max(0, [Math]::Min($DelaySeconds, [Math]::Floor($afterErr)))
            }
            Write-Host "Attempt $attempt/$MaxAttempts failed for '$uri'. Retrying in $delay seconds..."
            & $SleepProvider $delay
        }
    }

    $msg = "Health check failed unexpectedly."
    Write-Warning $msg
    return [pscustomobject]@{ ExitCode = 1; Success = $false; Message = $msg; IsMissingAuthorization = $false }
}

Export-ModuleMember -Function Invoke-StartupMigrationsCheck
