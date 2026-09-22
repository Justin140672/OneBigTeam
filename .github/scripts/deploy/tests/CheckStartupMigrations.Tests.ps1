# Pester tests for reliability review issue 5 (P1): the health-detail secret must be required, not
# optional, and the script's diagnostics must distinguish "missing authorization / no detail
# returned" from "detail returned but migrations unhealthy".
#
# Moved under .github/scripts/deploy/tests (the directory ci.yml's `deploy-scripts-test` job
# actually discovers via `Invoke-Pester -Path .github/scripts/deploy/tests`) and rewritten to call
# Invoke-StartupMigrationsCheck (StartupMigrationsCheck.psm1) directly rather than invoking
# check-startup-migrations.ps1 as a script. The script itself still calls `exit` as a real CLI entry
# point, but that `exit` is no longer on any path Pester executes in-process, so a failing case can
# no longer terminate the test host mid-suite. See .github/scripts/check-startup-migrations.ps1 and
# .github/scripts/deploy/StartupMigrationsCheck.psm1.

BeforeAll {
    $script:DeployDir = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $script:DeployDir 'StartupMigrationsCheck.psm1') -Force -DisableNameChecking

    function New-FullDetailProbe {
        param([string]$ExpectedToken, [string]$Sha = 'abc123', [bool]$AllSucceeded = $true)
        {
            param($Uri, $Headers, $TimeoutSec)

            if ($Headers['X-Health-Token'] -ne $ExpectedToken) {
                # Mismatched/rotated token — mirrors HasDetailAccess degrading to a minimal payload.
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

Describe 'Invoke-StartupMigrationsCheck' {

    It 'fails immediately with a config error when BearerToken is missing (empty string)' {
        { Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken '' } |
            Should -Throw
    }

    It 'fails immediately with a config error when BearerToken is whitespace-only' {
        { Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken '   ' } |
            Should -Throw
    }

    It 'distinguishes a mismatched/rotated token (minimal payload) from a real migration failure' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -AllSucceeded $true

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'WRONG-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -WarningVariable warnings -WarningAction SilentlyContinue

        $result.ExitCode | Should -Be 1
        $result.IsMissingAuthorization | Should -Be $true
        ($warnings -join ' ') | Should -Match 'missing authorization'
    }

    It 'succeeds when the matching token is presented and all modules report succeeded' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -Sha 'deadbeef' -AllSucceeded $true

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'correct-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) }

        $result.ExitCode | Should -Be 0
        $result.Success | Should -Be $true
    }

    It 'fails with a genuine-unhealthy diagnostic (not a missing-authorization one) when the token matches but a module failed' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -AllSucceeded $false

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'correct-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -WarningVariable warnings -WarningAction SilentlyContinue

        $result.ExitCode | Should -Be 1
        $result.IsMissingAuthorization | Should -Be $false
        ($warnings -join ' ') | Should -Match 'not healthy'
        ($warnings -join ' ') | Should -Not -Match 'missing authorization'
    }

    It 'covers token rotation: a token valid at the start of a retry sequence still succeeds after the server-side token changes only if re-run with the new token' {
        # Simulates: the app's ReadinessDetailToken was rotated between two independent invocations
        # (the documented "rotate app first, then the deploy secret" order in the runbook) — the OLD
        # token now gets the minimal payload; the NEW token succeeds.
        $probeAfterRotation = New-FullDetailProbe -ExpectedToken 'new-token' -AllSucceeded $true

        $oldResult = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'old-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probeAfterRotation -SleepProvider { param($s) } `
            -WarningAction SilentlyContinue

        $newResult = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'new-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probeAfterRotation -SleepProvider { param($s) }

        $oldResult.ExitCode | Should -Be 1
        $newResult.ExitCode | Should -Be 0
    }

    It 'rejects a matching token whose reported release sha does not equal -ExpectedSha (stale/old instance)' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -Sha 'old-sha' -AllSucceeded $true

        $result = Invoke-StartupMigrationsCheck -ApiBaseUrl 'https://api.example.test' -BearerToken 'correct-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -ExpectedSha 'new-sha' -WarningVariable warnings -WarningAction SilentlyContinue

        $result.ExitCode | Should -Be 1
        ($warnings -join ' ') | Should -Match 'release mismatch'
    }
}

Describe 'check-startup-migrations.ps1 (real CLI entry point, isolated child process)' {
    # Proves the thin wrapper script still calls `exit` correctly for standalone/CI use — run in an
    # isolated child pwsh process (not in-process) so its `exit` can never affect this test host,
    # matching option (b) of the reliability-review fix for entry points that must remain scripts.
    It 'exits non-zero without a bearer token' {
        $scriptPath = Join-Path (Split-Path -Parent $script:DeployDir) 'check-startup-migrations.ps1'
        & pwsh -NoProfile -Command "& '$scriptPath' -ApiBaseUrl 'https://api.example.test' -BearerToken '' -MaxAttempts 1 -DelaySeconds 0" 2>$null
        $LASTEXITCODE | Should -Not -Be 0
    }
}
