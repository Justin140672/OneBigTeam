# Pester tests for the deployment-pipeline runbook section 5 rotation protocol (dual-token overlap
# window). These exercise Invoke-StartupMigrationsCheck (the same function check-startup-migrations.ps1
# and Invoke-VerifiedDeploy.ps1's recovery path both call) against simulated app instances that mimic
# HealthCheckEndpoints.HasDetailAccess (src/HR.ServiceDefaults/HealthCheckEndpoints.cs): an instance
# accepts its configured "current" token, and — only while a "previous" token is configured — also
# accepts that previous value. This proves the five states the rotation protocol depends on:
#   1. old app token + old deploy secret -> works (pre-rotation / steady state)
#   2. new app token + new deploy secret -> works (post-rotation / steady state)
#   3. forward deployment during the overlap window with the deploy secret not yet rotated -> works
#      (the dual-token app instance still accepts the old, still-current, deploy secret)
#   4. rollback to an old-token-only instance while the deploy secret already holds the new value
#      -> FAILS if that instance predates the dual-token deploy (expected: a rotation must be its own
#      dedicated deploy before the GitHub secret moves, exactly as documented) and SUCCEEDS once the
#      rollback target is itself a dual-token instance (i.e. captured at/after the rotation deploy)
#   5. previous-token removal after success is a real ending state, not silent permanent dual
#      acceptance -> the previous token is rejected once ReadinessDetailTokenPrevious is cleared

BeforeAll {
    $script:DeployDir = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $script:DeployDir 'StartupMigrationsCheck.psm1') -Force -DisableNameChecking

    # Simulates one deployed API instance's HasDetailAccess: accepts $Current always, and $Previous
    # only when it is non-empty (mirrors HealthChecks:ReadinessDetailTokenPrevious being unset).
    function New-SimulatedInstanceProbe {
        param(
            [string]$Current,
            [string]$Previous = '',
            [string]$Sha = 'sha-instance',
            [bool]$AllSucceeded = $true
        )
        {
            param($Uri, $Headers, $TimeoutSec)

            $presented = $Headers['X-Health-Token']
            $accepted = ($presented -eq $Current) -or (-not [string]::IsNullOrEmpty($Previous) -and $presented -eq $Previous)

            if (-not $accepted) {
                return [pscustomobject]@{ status = if ($AllSucceeded) { 'Healthy' } else { 'Unhealthy' } }
            }

            $status = if ($AllSucceeded) { 'succeeded' } else { 'failed' }
            return [pscustomobject]@{
                companies = [pscustomobject]@{ status = $status }
                identity  = [pscustomobject]@{ status = $status }
                release   = [pscustomobject]@{ sha = $Sha }
            }
        }.GetNewClosure()
    }
}

Describe 'Rotation overlap window (deployment-pipeline runbook section 5)' {

    It 'old app token + old deploy secret works (pre-rotation steady state)' {
        $probe = New-SimulatedInstanceProbe -Current 'old-token'

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'old-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) }

        $result.Success | Should -Be $true
    }

    It 'new app token + new deploy secret works (post-rotation steady state)' {
        $probe = New-SimulatedInstanceProbe -Current 'new-token'

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'new-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) }

        $result.Success | Should -Be $true
    }

    It 'forward deployment during the overlap window succeeds: dual-token instance still accepts the not-yet-rotated deploy secret' {
        # Step 2 of the runbook procedure: the API is redeployed with Current=new, Previous=old, but
        # the GitHub secret has NOT been rotated yet, so the forward-verification step still presents
        # the OLD token.
        $probe = New-SimulatedInstanceProbe -Current 'new-token' -Previous 'old-token'

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'old-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) }

        $result.Success | Should -Be $true
        $result.IsMissingAuthorization | Should -Be $false
    }

    It 'forward deployment failure during rotation: a single-token instance rejects a prematurely-rotated deploy secret' {
        # The failure mode the old contradictory procedure could hit: GitHub secret rotated to NEW
        # before the API redeploy that makes NEW valid. Proves the ordering in the runbook (rotate the
        # app FIRST, with dual-token config, before touching the GitHub secret) is not optional.
        $probe = New-SimulatedInstanceProbe -Current 'old-token'

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'new-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -WarningVariable warnings -WarningAction SilentlyContinue

        $result.Success | Should -Be $false
        $result.IsMissingAuthorization | Should -Be $true
    }

    It 'rollback to a dual-token known-good deployment succeeds even after the deploy secret has rotated to the new value' {
        # The known-good rollback target was captured at/after the rotation deploy (step 2), so it is
        # itself running dual-token config and still authenticates the NEW deploy secret via its
        # Current slot, or the OLD one via Previous — either way, rollback succeeds.
        $rollbackTargetProbe = New-SimulatedInstanceProbe -Current 'new-token' -Previous 'old-token' -Sha 'known-good-sha'

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'new-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $rollbackTargetProbe -SleepProvider { param($s) } `
            -ExpectedSha 'known-good-sha'

        $result.Success | Should -Be $true
    }

    It 'rollback to a pre-rotation, single-old-token known-good deployment succeeds while the deploy secret still holds the old value (never rotate the secret before the app)' {
        $rollbackTargetProbe = New-SimulatedInstanceProbe -Current 'old-token' -Sha 'known-good-sha'

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'old-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $rollbackTargetProbe -SleepProvider { param($s) } `
            -ExpectedSha 'known-good-sha'

        $result.Success | Should -Be $true
    }

    It 'previous-token removal after success is a real ending state: the previous token is rejected once ReadinessDetailTokenPrevious is cleared' {
        # Step 6 of the runbook: after revocation, only Current is accepted.
        $probeAfterRevocation = New-SimulatedInstanceProbe -Current 'new-token' -Previous ''

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'old-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probeAfterRevocation -SleepProvider { param($s) } `
            -WarningVariable warnings -WarningAction SilentlyContinue

        $result.Success | Should -Be $false
        $result.IsMissingAuthorization | Should -Be $true
    }
}
