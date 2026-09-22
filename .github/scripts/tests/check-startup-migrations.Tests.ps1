# Pester tests for reliability review issue 5 (P1): the health-detail secret must be required, not
# optional, and the script's diagnostics must distinguish "missing authorization / no detail
# returned" from "detail returned but migrations unhealthy".

BeforeAll {
    $script:ScriptPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'check-startup-migrations.ps1'

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

Describe 'check-startup-migrations.ps1' {

    It 'fails immediately with a config error when BearerToken is missing (empty string)' {
        { & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken '' } |
            Should -Throw
    }

    It 'fails immediately with a config error when BearerToken is whitespace-only' {
        { & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken '   ' } |
            Should -Throw
    }

    It 'distinguishes a mismatched/rotated token (minimal payload) from a real migration failure' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -AllSucceeded $true

        & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken 'WRONG-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -ErrorAction SilentlyContinue -WarningVariable warnings 2>$null

        $LASTEXITCODE | Should -Be 1
        ($warnings -join ' ') | Should -Match 'missing authorization'
    }

    It 'succeeds when the matching token is presented and all modules report succeeded' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -Sha 'deadbeef' -AllSucceeded $true

        & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken 'correct-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) }

        $LASTEXITCODE | Should -Be 0
    }

    It 'fails with a genuine-unhealthy diagnostic (not a missing-authorization one) when the token matches but a module failed' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -AllSucceeded $false

        & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken 'correct-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -ErrorAction SilentlyContinue -WarningVariable warnings 2>$null

        $LASTEXITCODE | Should -Be 1
        ($warnings -join ' ') | Should -Match 'not healthy'
        ($warnings -join ' ') | Should -Not -Match 'missing authorization'
    }

    It 'covers token rotation: a token valid at the start of a retry sequence still succeeds after the server-side token changes only if the script is re-run with the new token' {
        # Simulates: the app's ReadinessDetailToken was rotated between two independent script
        # invocations (the documented "rotate app first, then the deploy secret" order in the
        # runbook) — the OLD token now gets the minimal payload; the NEW token succeeds.
        $probeAfterRotation = New-FullDetailProbe -ExpectedToken 'new-token' -AllSucceeded $true

        & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken 'old-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probeAfterRotation -SleepProvider { param($s) } `
            -ErrorAction SilentlyContinue 2>$null
        $oldTokenExit = $LASTEXITCODE

        & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken 'new-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probeAfterRotation -SleepProvider { param($s) }
        $newTokenExit = $LASTEXITCODE

        $oldTokenExit | Should -Be 1
        $newTokenExit | Should -Be 0
    }

    It 'rejects a matching token whose reported release sha does not equal -ExpectedSha (stale/old instance)' {
        $probe = New-FullDetailProbe -ExpectedToken 'correct-token' -Sha 'old-sha' -AllSucceeded $true

        & $script:ScriptPath -ApiBaseUrl 'https://api.example.test' -BearerToken 'correct-token' `
            -MaxAttempts 1 -DelaySeconds 0 -HttpProbe $probe -SleepProvider { param($s) } `
            -ExpectedSha 'new-sha' -ErrorAction SilentlyContinue -WarningVariable warnings 2>$null

        $LASTEXITCODE | Should -Be 1
        ($warnings -join ' ') | Should -Match 'release mismatch'
    }
}
