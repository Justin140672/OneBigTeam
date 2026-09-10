# Pester 5 tests for the Ticket 5 verified-deploy / recovery workflow + the follow-up
# "Make release recovery trustworthy and handle verification failures safely".
#
# HELPER-LEVEL tests: fake Railway CLI runner, fake Railway GraphQL API sender, fake HTTP probe.
# They prove the control flow. They do NOT prove the workflow against real Railway infrastructure —
# see docs/tickets/ticket-5-verified-releases-and-recovery.md ("Unverified / OUTSTANDING").
#
# Regression coverage (old behaviour that each defect fix makes impossible):
#   Defect 1 — recovery used `railway redeploy` (latest) which, after a partial rollout, re-activates
#              the BROKEN release; and it "verified" the restore by the same reassignable RELEASE_SHA
#              it had just reset. New tests: rollback targets the recorded deployment id via GraphQL;
#              a matching /health/release sha with a non-matching active Railway deployment FAILS.
#   Defect 2 — check-startup-migrations.ps1 raised a terminating error (Write-Error under
#              ErrorActionPreference=Stop) that escaped the caller and skipped recovery. New tests:
#              a throwing verifier still reaches the recovery decision; the real script with an
#              injected always-failing probe exits non-zero without throwing.
#   Defect 3 — recovery only polled /health/release for a sha match and then printed "reports ready".
#              New tests: recovery also checks artifact identity, /health/ready and (api) startup
#              migrations against the KNOWN-GOOD sha, under one shared deadline; any miss => not
#              'recovered'.

BeforeAll {
    $script:DeployDir = Split-Path -Parent $PSScriptRoot
    Import-Module (Join-Path $script:DeployDir 'VerifiedDeploy.psm1') -Force -DisableNameChecking
    Import-Module (Join-Path $script:DeployDir 'Railway.psm1') -Force -DisableNameChecking

    $script:NoSleep = { param($s) }

    function New-FakeClock {
        param([int]$MaxTicks = 4000)
        $state = @{ T = [datetime]'2026-01-01T00:00:00Z'; Ticks = 0; Max = $MaxTicks }
        return {
            $state.Ticks++
            if ($state.Ticks -gt $state.Max) { $state.T = $state.T.AddYears(1) } else { $state.T = $state.T.AddSeconds(5) }
            return $state.T
        }.GetNewClosure()
    }

    function New-FakeRunner {
        param([hashtable]$Plan = @{})
        $state = @{ Plan = $Plan; Calls = @() }
        $runner = {
            param([string[]]$RailwayArgs)
            $verb = $RailwayArgs[0]
            $state.Calls += , $RailwayArgs
            $seq = $state.Plan[$verb]
            if (-not $seq) { return [pscustomobject]@{ ExitCode = 0; Output = '' } }
            $idx = @($state.Calls | Where-Object { $_[0] -eq $verb }).Count - 1
            if ($idx -ge $seq.Count) { $idx = $seq.Count - 1 }
            return [pscustomobject]@{ ExitCode = $seq[$idx].ExitCode; Output = $seq[$idx].Output }
        }.GetNewClosure()
        return [pscustomobject]@{ Runner = $runner; State = $state }
    }

    function New-FakeProbe {
        param([hashtable]$ShaByUrl, [hashtable]$DepIdByUrl = @{})
        $state = @{ Map = $ShaByUrl; Hits = @{}; DepIds = $DepIdByUrl }
        $probe = {
            param($uri)
            $base = ($uri -replace '/health/release$', '').TrimEnd('/')
            if (-not $state.Map.ContainsKey($base)) { throw "unreachable: $uri" }
            $seq = $state.Map[$base]
            if ($null -eq $seq) { throw "connection refused: $uri" }
            $n = [int]($state.Hits[$base] ?? 0)
            $state.Hits[$base] = $n + 1
            $sha = if ($n -ge $seq.Count) { $seq[$seq.Count - 1] } else { $seq[$n] }
            if ($null -eq $sha) { throw "connection refused: $uri" }
            $depId = if ($state.DepIds.ContainsKey($base)) { $state.DepIds[$base] } else { $null }
            return [pscustomobject]@{ service = 'x'; sha = $sha; version = "v-$sha"; deploymentId = $depId }
        }.GetNewClosure()
        return $probe
    }

    # /health/release probe for recovery: reports the CURRENT serving deployment id straight from the
    # fake Railway state (mutated by deploymentRollback), so condition (c) — endpoint deploymentId ==
    # Railway serving deployment id — is satisfied exactly when the restore really is live.
    function New-FakeReleaseProbe {
        param([Parameter(Mandatory)] $Fake, [hashtable]$UrlToService = @{
            'https://api.test' = 'api'; 'https://app.test' = 'app'; 'https://mkt.test' = 'marketing'; 'https://admin.test' = 'admin'
        }, [hashtable]$OverrideDepId = @{})
        $state = $Fake.State
        return {
            param($baseUrl)
            $svc = $UrlToService[$baseUrl.TrimEnd('/')]
            $depId = $null
            if ($OverrideDepId.ContainsKey($svc)) { $depId = $OverrideDepId[$svc] }
            elseif ($svc -and $state.Active.ContainsKey($svc) -and $state.Active[$svc]) { $depId = $state.Active[$svc].Id }
            return [pscustomobject]@{ Reachable = $true; ReportedSha = 'A'; ReportedVersion = 'v-A'; ReportedDeploymentId = $depId }
        }.GetNewClosure()
    }

    # Fake Railway GraphQL API. Drives Get-RailwayDeployments / Get-RailwayActiveDeployment /
    # Restore-RailwayDeployment (deploymentRollback) with in-memory fixtures.
    function New-FakeRailwayApi {
        param(
            [hashtable]$Deployments = @{},          # svcName -> @(@{ Id; Status; CanRollback; CommitHash })
            [hashtable]$Active = @{},               # svcName -> @{ Id; Status; CommitHash }  (mutated by rollback)
            [hashtable]$RollbackErrors = @{},       # svcName OR deploymentId -> error message
            [switch]$RollbackSpawnsNewNode,         # rollback -> Active.Id = "redeploy-of-<id>" (commit preserved)
            [string[]]$RollbackNoOpServices = @()   # rollback "succeeds" but Active is NOT updated
        )
        $serviceIds = @{ api = 'svc-api'; app = 'svc-app'; marketing = 'svc-mkt'; admin = 'svc-admin' }
        $idToName = @{}
        foreach ($kv in $serviceIds.GetEnumerator()) { $idToName[$kv.Value] = $kv.Key }

        $state = @{
            Deployments = $Deployments; Active = $Active; RollbackErrors = $RollbackErrors
            SpawnNew = [bool]$RollbackSpawnsNewNode; NoOp = $RollbackNoOpServices
            Queries = @(); RollbackIds = @()
        }

        $sender = {
            param($Request)
            $payload = $Request.Body | ConvertFrom-Json
            $q = [string]$payload.query
            $vars = $payload.variables
            $state.Queries += $q

            if ($q -match 'deploymentRollback') {
                $depId = [string]$vars.id
                $state.RollbackIds += $depId
                $svcName = $null
                foreach ($kv in $state.Deployments.GetEnumerator()) {
                    if (@($kv.Value | Where-Object { $_.Id -eq $depId }).Count -gt 0) { $svcName = $kv.Key; break }
                }
                if ($state.RollbackErrors.ContainsKey($depId)) {
                    return [pscustomobject]@{ data = $null; errors = @([pscustomobject]@{ message = $state.RollbackErrors[$depId] }) }
                }
                if ($svcName -and $state.RollbackErrors.ContainsKey($svcName)) {
                    return [pscustomobject]@{ data = $null; errors = @([pscustomobject]@{ message = $state.RollbackErrors[$svcName] }) }
                }
                $src = $null
                if ($svcName) { $src = $state.Deployments[$svcName] | Where-Object { $_.Id -eq $depId } | Select-Object -First 1 }
                $commit = if ($src) { $src.CommitHash } else { $null }
                $newId = if ($state.SpawnNew) { "redeploy-of-$depId" } else { $depId }
                if ($svcName -and ($state.NoOp -notcontains $svcName)) {
                    $state.Active[$svcName] = @{ Id = $newId; Status = 'SUCCESS'; CommitHash = $commit }
                }
                return [pscustomobject]@{ data = [pscustomobject]@{ deploymentRollback = [pscustomobject]@{ id = $newId; status = 'SUCCESS' } } }
            }

            if ($q -match 'environment\(id') {
                $edges = @()
                foreach ($kv in $state.Active.GetEnumerator()) {
                    $a = $kv.Value
                    $ld = if ($a) {
                        $m = if ($a.CommitHash) { [pscustomobject]@{ commitHash = $a.CommitHash } } else { $null }
                        [pscustomobject]@{ id = $a.Id; status = $a.Status; meta = $m }
                    } else { $null }
                    $edges += [pscustomobject]@{ node = [pscustomobject]@{ serviceId = $serviceIds[$kv.Key]; latestDeployment = $ld } }
                }
                return [pscustomobject]@{ data = [pscustomobject]@{ environment = [pscustomobject]@{ serviceInstances = [pscustomobject]@{ edges = $edges } } } }
            }

            if ($q -match 'deployments\(') {
                $svcName = $idToName[[string]$vars.input.serviceId]
                $edges = @()
                if ($svcName -and $state.Deployments.ContainsKey($svcName)) {
                    foreach ($d in $state.Deployments[$svcName]) {
                        $meta = if ($d.CommitHash) { [pscustomobject]@{ commitHash = $d.CommitHash } } else { $null }
                        $edges += [pscustomobject]@{ node = [pscustomobject]@{
                                id = $d.Id; status = $d.Status; createdAt = '2026-09-10T00:00:00Z'
                                url = $null; staticUrl = $null; canRollback = $d.CanRollback; meta = $meta } }
                    }
                }
                return [pscustomobject]@{ data = [pscustomobject]@{ deployments = [pscustomobject]@{ edges = $edges } } }
            }

            throw "fake railway api: unrecognised query: $q"
        }.GetNewClosure()

        $api = New-RailwayApiClient -Token 'fake-token' -ProjectId 'proj-1' -EnvironmentId 'env-1' `
            -ServiceIds $serviceIds -Endpoint 'https://fake/graphql' -Sender $sender
        return [pscustomobject]@{ Api = $api; State = $state }
    }

    $script:Services = @(
        [pscustomobject]@{ Name = 'api'; BaseUrl = 'https://api.test' }
        [pscustomobject]@{ Name = 'app'; BaseUrl = 'https://app.test' }
        [pscustomobject]@{ Name = 'marketing'; BaseUrl = 'https://mkt.test' }
    )

    $script:SafeRelease = [pscustomobject]@{ AppRollbackSafe = $true; Reason = ''; Notes = '' }
    $script:OkReadiness = { param($baseUrl) [pscustomobject]@{ Ok = $true; Detail = "ready $baseUrl" } }
    $script:OkMigration = { param($baseUrl, $sha) [pscustomobject]@{ Ok = $true; Detail = "migrations ok for $sha" } }

    function New-KnownGood {
        param([string[]]$ServicesWanted = @('api', 'app'), [switch]$NoApiId,
              [string]$CaptureOutcome = 'consistent')
        $urls = @{ api = 'https://api.test'; app = 'https://app.test'; marketing = 'https://mkt.test'; admin = 'https://admin.test' }
        $out = @()
        foreach ($s in $ServicesWanted) {
            $depId = if ($s -eq 'api' -and $NoApiId) { $null } else { "kg-$s" }
            $outcome = if ($s -eq 'api' -and $NoApiId) { 'no-target' } else { $CaptureOutcome }
            $out += [pscustomobject]@{
                service = $s; baseUrl = $urls[$s]
                deploymentId = $depId; rollbackTargetDeploymentId = $depId
                targetReleaseIdentity = 'A'
                commitHash = "c-$s"; status = 'SUCCESS'; canRollback = $true
                servingMatchAtCapture = $true; endpointDeploymentId = $depId
                captureOutcome = $outcome
                releaseVersion = "v-A-$s"; releaseSha = 'A'
            }
        }
        return $out
    }

    function New-DeploymentFixture {
        param([string[]]$ServicesWanted = @('api', 'app', 'marketing'))
        $d = @{}
        foreach ($s in $ServicesWanted) {
            $d[$s] = @(
                @{ Id = "kg-$s"; Status = 'SUCCESS'; CanRollback = $true; CommitHash = "c-$s" }
            )
        }
        return $d
    }
}

Describe 'Invoke-VerifiedDeploy — consistent known-good capture (defects 1 & 3)' {

    BeforeAll {
        function Invoke-Capture {
            param($Fake, [hashtable]$DepIdByUrl, [string[]]$ShaSeq = @('NEW'))
            $shaMap = @{
                'https://api.test' = $ShaSeq; 'https://app.test' = $ShaSeq; 'https://mkt.test' = $ShaSeq
            }
            $probe = New-FakeProbe -ShaByUrl $shaMap -DepIdByUrl $DepIdByUrl
            return Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
                -RolloutTimeoutSeconds 60 -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $Fake.Api `
                -HttpProbe $probe -KnownGoodPath (New-TemporaryFile) `
                -ApiVerification { param($s) [pscustomobject]@{ Ok = $true; Detail = 'ok' } } `
                -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep
        }
    }

    It 'records a consistent target only when the serving deployment AND /health/release.deploymentId agree with a rollback-able SUCCESS/SLEEPING deployment' {
        $fake = New-FakeRailwayApi -Deployments @{
            api = @(
                @{ Id = 'd-latest'; Status = 'FAILED'; CanRollback = $false; CommitHash = 'c-bad' }
                @{ Id = 'd-good'; Status = 'SUCCESS'; CanRollback = $true; CommitHash = 'c-good' }
            )
            app = @(@{ Id = 'd-app'; Status = 'SLEEPING'; CanRollback = $true; CommitHash = 'c-app' })
            marketing = @(@{ Id = 'd-mkt'; Status = 'SUCCESS'; CanRollback = $true; CommitHash = 'c-mkt' })
        } -Active @{
            api = @{ Id = 'd-good'; Status = 'SUCCESS'; CommitHash = 'c-good' }
            app = @{ Id = 'd-app'; Status = 'SLEEPING'; CommitHash = 'c-app' }
            marketing = @{ Id = 'd-mkt'; Status = 'SUCCESS'; CommitHash = 'c-mkt' }
        }
        $r = Invoke-Capture -Fake $fake -DepIdByUrl @{
            'https://api.test' = 'd-good'; 'https://app.test' = 'd-app'; 'https://mkt.test' = 'd-mkt'
        }

        $api = $r.KnownGood | Where-Object { $_.service -eq 'api' }
        $api.captureOutcome | Should -Be 'consistent'
        $api.rollbackTargetDeploymentId | Should -Be 'd-good'
        $api.targetReleaseIdentity | Should -Be 'NEW'
        ($r.KnownGood | Where-Object { $_.service -eq 'app' }).captureOutcome | Should -Be 'consistent'
    }

    It 'REGRESSION (defect 3): serving deployment A but /health/release.deploymentId B => recorded inconsistent, NO automatic target' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture) -Active @{
            api = @{ Id = 'kg-api'; Status = 'SUCCESS'; CommitHash = 'c-api' }
            app = @{ Id = 'kg-app'; Status = 'SUCCESS'; CommitHash = 'c-app' }
            marketing = @{ Id = 'kg-marketing'; Status = 'SUCCESS'; CommitHash = 'c-marketing' }
        }
        $r = Invoke-Capture -Fake $fake -DepIdByUrl @{
            'https://api.test' = 'SOMETHING-ELSE'; 'https://app.test' = 'kg-app'; 'https://mkt.test' = 'kg-marketing'
        }

        $api = $r.KnownGood | Where-Object { $_.service -eq 'api' }
        $api.captureOutcome | Should -Be 'inconsistent'
        $api.rollbackTargetDeploymentId | Should -BeNullOrEmpty
        ($r.KnownGood | Where-Object { $_.service -eq 'app' }).captureOutcome | Should -Be 'consistent'
    }

    It 'no rollback-able SUCCESS/SLEEPING deployment => captureOutcome no-target, other services still processed' {
        $fake = New-FakeRailwayApi -Deployments @{
            api = @(@{ Id = 'd-building'; Status = 'BUILDING'; CanRollback = $false; CommitHash = 'c1' })
            app = @(@{ Id = 'd-expired'; Status = 'SUCCESS'; CanRollback = $false; CommitHash = 'c2' })
            marketing = @(@{ Id = 'd-mkt'; Status = 'SUCCESS'; CanRollback = $true; CommitHash = 'c-mkt' })
        } -Active @{
            api = @{ Id = 'd-building'; Status = 'BUILDING'; CommitHash = 'c1' }
            app = @{ Id = 'd-expired'; Status = 'SUCCESS'; CommitHash = 'c2' }
            marketing = @{ Id = 'd-mkt'; Status = 'SUCCESS'; CommitHash = 'c-mkt' }
        }
        $r = Invoke-Capture -Fake $fake -DepIdByUrl @{
            'https://api.test' = 'd-building'; 'https://app.test' = 'd-expired'; 'https://mkt.test' = 'd-mkt'
        }

        ($r.KnownGood | Where-Object { $_.service -eq 'api' }).captureOutcome | Should -Be 'no-target'
        ($r.KnownGood | Where-Object { $_.service -eq 'app' }).captureOutcome | Should -Be 'no-target'
        ($r.KnownGood | Where-Object { $_.service -eq 'marketing' }).captureOutcome | Should -Be 'consistent'
        ($r.KnownGood | Where-Object { $_.service -eq 'marketing' }).rollbackTargetDeploymentId | Should -Be 'd-mkt'
    }

    It 'never records an empty targetReleaseIdentity when the serving instance reports no sha' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api')) -Active @{
            api = @{ Id = 'kg-api'; Status = 'SUCCESS'; CommitHash = 'c-api' }
        }
        $probe = New-FakeProbe -ShaByUrl @{ 'https://api.test' = @($null) } -DepIdByUrl @{ 'https://api.test' = 'kg-api' }
        # $null sha => probe throws 'connection refused' => Reachable=$false => no sha
        $svc = @([pscustomobject]@{ Name = 'api'; BaseUrl = 'https://api.test' })
        $r = Invoke-VerifiedDeploy -Services $svc -TargetSha 'NEW' -Environment 'test' `
            -RolloutTimeoutSeconds 20 -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $fake.Api `
            -HttpProbe $probe -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) [pscustomobject]@{ Ok = $true; Detail = 'ok' } } `
            -NowProvider (New-FakeClock -MaxTicks 6) -SleepProvider $script:NoSleep

        $api = $r.KnownGood | Where-Object { $_.service -eq 'api' }
        $api.captureOutcome | Should -Not -Be 'consistent'
        $api.targetReleaseIdentity | Should -BeNullOrEmpty
    }
}

Describe 'Invoke-VerifiedDeploy — forward verification' {

    It 'reports success only when every service reports the target sha and API verification passes' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $fr = New-FakeRunner
        $probe = New-FakeProbe @{
            'https://api.test' = @('old', 'NEW'); 'https://app.test' = @('old', 'NEW'); 'https://mkt.test' = @('old', 'NEW')
        }
        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -RolloutTimeoutSeconds 300 -PollSeconds 1 -Runner $fr.Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) [pscustomobject]@{ Ok = $true; Detail = "verified $s" } } `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Success | Should -BeTrue
        @($r.Results | Where-Object { $_.state -ne 'updated' }).Count | Should -Be 0
    }

    It 'sets RELEASE_SHA + PLATFORM_VERSION for every service before its railway up' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $fr = New-FakeRunner
        $probe = New-FakeProbe @{
            'https://api.test' = @('NEW'); 'https://app.test' = @('NEW'); 'https://mkt.test' = @('NEW')
        }
        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -Version '1.0.42+abc1234' -PollSeconds 1 -Runner $fr.Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) [pscustomobject]@{ Ok = $true; Detail = 'ok' } } `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Success | Should -BeTrue
        foreach ($svc in 'api', 'app', 'marketing') {
            $varIdx = [array]::FindIndex($fr.State.Calls, [Predicate[object]] { param($c) $c[0] -eq 'variables' -and ($c -contains $svc) })
            $upIdx = [array]::FindIndex($fr.State.Calls, [Predicate[object]] { param($c) $c[0] -eq 'up' -and ($c -contains $svc) })
            $varIdx | Should -BeGreaterThan -1
            $varIdx | Should -BeLessThan $upIdx
            ($fr.State.Calls[$varIdx] -contains 'RELEASE_SHA=NEW') | Should -BeTrue
            ($fr.State.Calls[$varIdx] -contains 'PLATFORM_VERSION=1.0.42+abc1234') | Should -BeTrue
        }
    }

    It 'fails verification when a healthy OLD instance keeps reporting the old sha' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $probe = New-FakeProbe @{
            'https://api.test' = @('OLD'); 'https://app.test' = @('OLD', 'NEW'); 'https://mkt.test' = @('OLD', 'NEW')
        }
        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -RolloutTimeoutSeconds 60 -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) [pscustomobject]@{ Ok = $true; Detail = 'ok' } } `
            -NowProvider (New-FakeClock -MaxTicks 6) -SleepProvider $script:NoSleep

        $r.Success | Should -BeFalse
        ($r.Results | Where-Object { $_.service -eq 'api' }).state | Should -Be 'pending'
        $r.FailureReason | Should -Match 'Partial rollout'
    }

    It 'records partial rollout explicitly when one service fails after others update' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $probe = New-FakeProbe @{
            'https://api.test' = @('NEW'); 'https://app.test' = @('NEW'); 'https://mkt.test' = $null
        }
        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -RolloutTimeoutSeconds 60 -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) [pscustomobject]@{ Ok = $true; Detail = 'ok' } } `
            -NowProvider (New-FakeClock -MaxTicks 6) -SleepProvider $script:NoSleep

        $r.Success | Should -BeFalse
        ($r.Results | Where-Object { $_.service -eq 'api' }).state | Should -Be 'updated'
        ($r.Results | Where-Object { $_.service -eq 'marketing' }).state | Should -Be 'failed'
    }
}

Describe 'Invoke-VerifiedDeploy — verifier contract (defect 2)' {

    It 'a verifier returning {Ok=$false} fails the deploy and yields a recovery-triggering FailureReason' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $probe = New-FakeProbe @{
            'https://api.test' = @('NEW'); 'https://app.test' = @('NEW'); 'https://mkt.test' = @('NEW')
        }
        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) [pscustomobject]@{ Ok = $false; Detail = 'startup-migrations 503' } } `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Success | Should -BeFalse
        $r.FailureReason | Should -Match 'API verification failed'
    }

    It 'a verifier that THROWS a terminating exception still fails the deploy (never a bare throw to the caller)' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $probe = New-FakeProbe @{
            'https://api.test' = @('NEW'); 'https://app.test' = @('NEW'); 'https://mkt.test' = @('NEW')
        }
        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) `
            -ApiVerification { param($s) throw 'boom: migration endpoint parse error' } `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Success | Should -BeFalse
        $r.ApiResult.Category | Should -Be 'exception'
        $r.FailureReason | Should -Match 'API verification failed'
    }

    It 'the REAL check-startup-migrations.ps1 with an always-failing probe exits non-zero WITHOUT throwing, so the controller reaches recovery' {
        $script = Join-Path (Split-Path -Parent $script:DeployDir) 'check-startup-migrations.ps1'
        $failingProbe = { param($Uri, $Headers) throw 'connection refused' }

        # Standalone: must exit non-zero and must NOT raise a terminating error.
        { & $script -ApiBaseUrl 'https://api.test' -MaxAttempts 2 -DelaySeconds 0 -HttpProbe $failingProbe } |
            Should -Not -Throw
        $LASTEXITCODE | Should -Not -Be 0

        # As the forward verifier: rollout fails, recovery decision reached.
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture)
        $probe = New-FakeProbe @{
            'https://api.test' = @('NEW'); 'https://app.test' = @('NEW'); 'https://mkt.test' = @('NEW')
        }
        $verifier = {
            param($sha)
            & $script -ApiBaseUrl 'https://api.test' -MaxAttempts 2 -DelaySeconds 0 -ExpectedSha $sha -HttpProbe $failingProbe
            if ($LASTEXITCODE -ne 0) { return [pscustomobject]@{ Ok = $false; Detail = "startup-migrations failed for $sha" } }
            return [pscustomobject]@{ Ok = $true; Detail = 'ok' }
        }.GetNewClosure()

        $r = Invoke-VerifiedDeploy -Services $script:Services -TargetSha 'NEW' -Environment 'test' `
            -PollSeconds 1 -Runner (New-FakeRunner).Runner -RailwayApi $fake.Api -HttpProbe $probe `
            -KnownGoodPath (New-TemporaryFile) -ApiVerification $verifier `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Success | Should -BeFalse
        $r.FailureReason | Should -Match 'API verification failed'
    }

    It 'DEFECT 4: check-startup-migrations.ps1 -DeadlineUtc stops retrying once the budget is exhausted' {
        $script = Join-Path (Split-Path -Parent $script:DeployDir) 'check-startup-migrations.ps1'
        $calls = [ref]0
        $failingProbe = { param($Uri, $Headers, $TimeoutSec) $calls.Value++; throw 'connection refused' }.GetNewClosure()
        # 20 attempts * 6s would normally run minutes; the deadline is already in the past.
        { & $script -ApiBaseUrl 'https://api.test' -MaxAttempts 20 -DelaySeconds 6 `
            -HttpProbe $failingProbe -DeadlineUtc ([datetime]::UtcNow.AddSeconds(-1)) } | Should -Not -Throw
        $LASTEXITCODE | Should -Not -Be 0
        $calls.Value | Should -Be 0   # never even started an attempt past the deadline
    }

    It 'DEFECT 4: without -DeadlineUtc the -MaxAttempts / -DelaySeconds behaviour is unchanged' {
        $script = Join-Path (Split-Path -Parent $script:DeployDir) 'check-startup-migrations.ps1'
        $calls = [ref]0
        $failingProbe = { param($Uri, $Headers, $TimeoutSec) $calls.Value++; throw 'connection refused' }.GetNewClosure()
        { & $script -ApiBaseUrl 'https://api.test' -MaxAttempts 3 -DelaySeconds 0 -HttpProbe $failingProbe } | Should -Not -Throw
        $LASTEXITCODE | Should -Not -Be 0
        $calls.Value | Should -Be 3
    }

    It 'wrong migration release.sha (real script, -ExpectedSha) fails the rollout' {
        $script = Join-Path (Split-Path -Parent $script:DeployDir) 'check-startup-migrations.ps1'
        $oldInstanceProbe = {
            param($Uri, $Headers)
            [pscustomobject]@{
                companies = [pscustomobject]@{ status = 'succeeded' }
                identity  = [pscustomobject]@{ status = 'succeeded' }
                release   = [pscustomobject]@{ sha = 'OLD-SHA'; version = 'v-old' }
            }
        }
        { & $script -ApiBaseUrl 'https://api.test' -MaxAttempts 2 -DelaySeconds 0 -ExpectedSha 'NEW-SHA' -HttpProbe $oldInstanceProbe } |
            Should -Not -Throw
        $LASTEXITCODE | Should -Not -Be 0
    }
}

Describe 'Invoke-Recovery — restore + serving-identity re-verification (defects 1, 3 & 4)' {

    It 'rolls each service back to its recorded target via deploymentRollback and re-verifies serving identity + readiness (+ api migrations)' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; Status = 'SUCCESS'; CommitHash = 'c-B' }; app = @{ Id = 'B'; Status = 'SUCCESS'; CommitHash = 'c-B' } }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 300 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'recovered'
        $fake.State.RollbackIds[0] | Should -Be 'kg-api'   # api before app
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeTrue
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).migrationOk | Should -BeTrue
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).endpointDeploymentId | Should -Be 'kg-api'
    }

    It 'accepts a NEW deployment node from deploymentRollback only with a commitHash relationship' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } `
            -RollbackSpawnsNewNode

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 300 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'recovered'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityDetail | Should -Match 'redeploy-of-kg-api'
    }

    It 'REGRESSION (defect 1): serving deployment != recorded target (rollback no-op) => identity FAILS' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B-live'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } `
            -RollbackNoOpServices @('api')

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 20 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 12) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeFalse
    }

    It 'REGRESSION (defect 1): serving Railway id matches but the old instance still answers /health/release (deploymentId != target) => identity FAILS' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }

        # Railway API says the restored deployment (kg-api) is serving, but the public URL is still
        # answered by an instance reporting the OLD deploymentId.
        $staleProbe = New-FakeReleaseProbe -Fake $fake -OverrideDepId @{ api = 'OLD-INSTANCE' }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 20 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe $staleProbe `
            -NowProvider (New-FakeClock -MaxTicks 14) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeFalse
    }

    It 'REGRESSION (defect 1): unrelated deployment with the same commit hash but no rollback linkage => identity FAILS' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'UNRELATED'; CommitHash = 'c-api'; Status = 'SUCCESS' } } `
            -RollbackNoOpServices @('api')

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 20 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 12) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeFalse
    }

    It 'REGRESSION (defect 1): status BUILDING on a matching id => not "restored" yet, cannot pass before the deadline' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } `
            -RollbackNoOpServices @('api')
        # rollback is a no-op on Active, so force the "serving id == target but BUILDING" case directly:
        $fake.State.Active['api'] = @{ Id = 'kg-api'; CommitHash = 'c-api'; Status = 'BUILDING' }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 20 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 12) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeFalse
    }

    It 'REGRESSION (defect 1): status FAILED on the serving deployment => immediate hard fail' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } `
            -RollbackNoOpServices @('api')
        $fake.State.Active['api'] = @{ Id = 'kg-api'; CommitHash = 'c-api'; Status = 'FAILED' }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 60 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityDetail | Should -Match 'hard-failed'
    }

    It 'REGRESSION (defect 1): historical targeting errors => zero successful restores, names the recorded id, never substitutes' {
        $fake = New-FakeRailwayApi `
            -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } `
            -RollbackErrors @{ api = 'Not authorized to rollback this deployment'; app = 'deployment not found' }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 30 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        @($r.ServiceResults | Where-Object { $_.restored }).Count | Should -Be 0
        ($r.Lines -join "`n") | Should -Match "kg-api"
        ($r.Lines -join "`n") | Should -Match 'Do NOT substitute a different release'
    }

    It 'REGRESSION (defect 3): serving identity OK but /health/ready 503 => recovery FAILS' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 30 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe { param($u, $t) [pscustomobject]@{ Ok = $false; Detail = '503' } } `
            -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 40) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).readyOk | Should -BeFalse
    }

    It 'REGRESSION (defect 3): serving identity + readiness OK but API migrations fail => recovery FAILS' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 30 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness `
            -ApiMigrationVerifier { param($u, $s, $d) [pscustomobject]@{ Ok = $false; Detail = "module companies=failed" } } `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 40) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).migrationOk | Should -BeFalse
    }

    It 'REGRESSION (defect 3): recovery-side API migration check runs regardless of any deploy-time skip; -ExpectedSha comes from the recorded target' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }
        $seenSha = [ref]$null

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 60 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness `
            -ApiMigrationVerifier { param($u, $s, $d) $seenSha.Value = $s; [pscustomobject]@{ Ok = $false; Detail = 'ran anyway' } }.GetNewClosure() `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $seenSha.Value | Should -Be 'A'   # recorded targetReleaseIdentity, not a live read
        $r.Outcome | Should -Be 'failed'
    }

    It 'partial-rollout recovery rolls back ALL required services, including ones already on B' {
        $svcs = @('api', 'app', 'marketing', 'admin')
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted $svcs) `
            -Active @{
                api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }
                app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }
                marketing = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }
                admin = @{ Id = 'OLD-admin'; CommitHash = 'c-admin'; Status = 'SUCCESS' }
            }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted $svcs) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 600 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'recovered'
        $r.ServiceResults.Count | Should -Be 4
        ($fake.State.RollbackIds | Sort-Object) | Should -Be (@('kg-admin', 'kg-api', 'kg-app', 'kg-marketing'))
    }

    It 'one failed service among healthy ones => overall recovery not successful' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } `
            -RollbackErrors @{ app = 'permission denied' }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 120 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).verified | Should -BeTrue
        ($r.ServiceResults | Where-Object { $_.service -eq 'app' }).verified | Should -BeFalse
    }

    It 'a capture record with no automatic target (no-target / inconsistent) is excluded, others still processed' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('app')) `
            -Active @{ app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }

        $kg = New-KnownGood -ServicesWanted @('api', 'app') -NoApiId
        $r = Invoke-Recovery -KnownGood $kg -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 120 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.Lines -join "`n") | Should -Match "No usable known-good rollback target for 'api'"
        ($r.ServiceResults | Where-Object { $_.service -eq 'app' }).verified | Should -BeTrue
        $fake.State.RollbackIds | Should -Not -Contain 'kg-api'
    }

    It 'refuses automatic rollback and issues manual instructions when appRollbackSafe is false (zero rollback calls)' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api', 'app')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' }; app = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }
        $unsafe = [pscustomobject]@{ AppRollbackSafe = $false; Reason = 'drops employees.legacy_code'; Notes = '' }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 60 -PollSeconds 1 -ReleaseSafety $unsafe `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'manual-required'
        $fake.State.RollbackIds.Count | Should -Be 0
        ($r.Lines -join "`n") | Should -Match 'will NOT restore the database'
        ($r.Lines -join "`n") | Should -Match 'backup-and-disaster-recovery'
    }

    It 'DEFECT 4: readiness that never recovers is bounded by the shared per-service deadline' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }
        $probeCalls = [ref]0

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 40 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe { param($u, $t) $probeCalls.Value++; [pscustomobject]@{ Ok = $false; Detail = 'still 503' } }.GetNewClosure() `
            -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 30) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        $probeCalls.Value | Should -BeGreaterThan 0
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).migrationOk | Should -BeNullOrEmpty
    }

    It 'DEFECT 4: a migration success that only arrives after the deadline is rejected' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }

        # identity + readiness pass quickly; the migration verifier "succeeds" but the fake clock has
        # jumped past the deadline by the time it returns.
        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 20 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness `
            -ApiMigrationVerifier { param($u, $s, $d) [pscustomobject]@{ Ok = $true; Detail = 'late OK' } } `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock -MaxTicks 9) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).migrationOk | Should -Not -BeTrue
    }

    It 'DEFECT 4: everything within the deadline still returns recovered' {
        $fake = New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted @('api')) `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 600 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-FakeReleaseProbe -Fake $fake) `
            -NowProvider (New-FakeClock) -SleepProvider $script:NoSleep

        $r.Outcome | Should -Be 'recovered'
    }
}

Describe 'Get-ReleaseSafety' {
    It 'defaults appRollbackSafe to true when the file is absent' {
        (Get-ReleaseSafety -Path (Join-Path ([System.IO.Path]::GetTempPath()) 'nope-does-not-exist.json')).AppRollbackSafe | Should -BeTrue
    }
    It 'reads the checked-in release-safety.json' {
        (Get-ReleaseSafety -Path (Join-Path $script:DeployDir 'release-safety.json')).AppRollbackSafe | Should -BeTrue
    }
}

Describe 'Restore-RailwayDeployment' {
    It 'calls deploymentRollback with the recorded id and reports Restored=$true' {
        $fake = New-FakeRailwayApi -Deployments @{ api = @(@{ Id = 'kg-api'; Status = 'SUCCESS'; CanRollback = $true; CommitHash = 'c-api' }) } `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } }
        $r = Restore-RailwayDeployment -Api $fake.Api -Service 'api' -DeploymentId 'kg-api'
        $r.Restored | Should -BeTrue
        $fake.State.RollbackIds | Should -Contain 'kg-api'
    }
    It 'returns Restored=$false (no substitution) when there is no deployment id' {
        $fake = New-FakeRailwayApi
        (Restore-RailwayDeployment -Api $fake.Api -Service 'api' -DeploymentId $null).Restored | Should -BeFalse
        $fake.State.RollbackIds.Count | Should -Be 0
    }
    It 'returns Restored=$false when the API denies the rollback' {
        $fake = New-FakeRailwayApi -Deployments @{ api = @(@{ Id = 'kg-api'; Status = 'SUCCESS'; CanRollback = $true; CommitHash = 'c' }) } `
            -RollbackErrors @{ 'kg-api' = 'canRollback is false' }
        $r = Restore-RailwayDeployment -Api $fake.Api -Service 'api' -DeploymentId 'kg-api'
        $r.Restored | Should -BeFalse
        $r.Detail | Should -Match 'canRollback is false'
    }
    It 'reports the recorded target and the returned deployment id' {
        $fake = New-FakeRailwayApi -Deployments @{ api = @(@{ Id = 'kg-api'; Status = 'SUCCESS'; CanRollback = $true; CommitHash = 'c-api' }) } `
            -Active @{ api = @{ Id = 'B'; CommitHash = 'c-B'; Status = 'SUCCESS' } } -RollbackSpawnsNewNode
        $r = Restore-RailwayDeployment -Api $fake.Api -Service 'api' -DeploymentId 'kg-api'
        $r.RolledBackToId | Should -Be 'kg-api'
        $r.ReturnedDeploymentId | Should -Be 'redeploy-of-kg-api'
    }
}

Describe 'Get-RailwayContext — unambiguous environment resolution (defect 1)' {

  BeforeAll {
    function New-StatusRunner {
        param([string]$Json, [int]$ExitCode = 0)
        $state = @{ Calls = @() }
        $runner = {
            param([string[]]$RailwayArgs)
            $state.Calls += , $RailwayArgs
            [pscustomobject]@{ ExitCode = $ExitCode; Output = $Json }
        }.GetNewClosure()
        return [pscustomobject]@{ Runner = $runner; State = $state }
    }

    $script:FullStatusJson = @'
{
  "id": "proj-1",
  "environments": { "edges": [
    { "node": { "id": "env-prod", "name": "production" } },
    { "node": { "id": "env-test", "name": "test" } }
  ] },
  "services": { "edges": [
    { "node": { "id": "svc-api", "name": "api" } },
    { "node": { "id": "svc-app", "name": "app" } },
    { "node": { "id": "svc-mkt", "name": "marketing" } },
    { "node": { "id": "svc-admin", "name": "admin" } }
  ] }
}
'@
  }

    It 'resolves the requested environment id regardless of edge order and returns all required service ids' {
        $ctx = Get-RailwayContext -Environment 'test' -Runner (New-StatusRunner -Json $script:FullStatusJson).Runner
        $ctx.EnvironmentId | Should -Be 'env-test'
        $ctx.ProjectId | Should -Be 'proj-1'
        $ctx.ServiceIds['admin'] | Should -Be 'svc-admin'
    }

    It 'throws (no first-environment fallback) when the requested environment does not exist, and makes NO further Railway calls' {
        $sr = New-StatusRunner -Json ($script:FullStatusJson -replace ',\s*\{ "node": \{ "id": "env-test", "name": "test" \} \}', '')
        { Get-RailwayContext -Environment 'test' -Runner $sr.Runner } | Should -Throw -ExpectedMessage "*environment 'test' not found*production*"
        @($sr.State.Calls | Where-Object { $_[0] -in 'variables', 'up' }).Count | Should -Be 0
    }

    It 'throws when the environment name is ambiguous' {
        $dupe = $script:FullStatusJson -replace '"env-prod", "name": "production"', '"env-test", "name": "test"'
        { Get-RailwayContext -Environment 'test' -Runner (New-StatusRunner -Json $dupe).Runner } |
            Should -Throw -ExpectedMessage '*ambiguous*'
    }

    It 'throws when the matched environment has no id' {
        $noId = $script:FullStatusJson -replace '"id": "env-test", ', ''
        { Get-RailwayContext -Environment 'test' -Runner (New-StatusRunner -Json $noId).Runner } |
            Should -Throw -ExpectedMessage '*carries no id*'
    }

    It 'throws when a required service id is missing' {
        $noAdmin = $script:FullStatusJson -replace ',\s*\{ "node": \{ "id": "svc-admin", "name": "admin" \} \}', ''
        { Get-RailwayContext -Environment 'test' -Runner (New-StatusRunner -Json $noAdmin).Runner } |
            Should -Throw -ExpectedMessage '*missing service id*admin*'
    }

    It 'throws on malformed status JSON' {
        { Get-RailwayContext -Environment 'test' -Runner (New-StatusRunner -Json 'not json{').Runner } |
            Should -Throw -ExpectedMessage '*Could not parse*'
    }
}

Describe 'Invoke-Recovery — identity probe bounded by the remaining recovery budget (defect 4 follow-up)' {

    BeforeAll {
        # Deterministic fixed-step clock. Each `& $NowProvider` advances the wall clock by $Step
        # seconds, so the per-service $remaining budget shrinks by a known amount per call. With the
        # current control flow the identity budget is read on the 6th `& $NowProvider` of a service
        # iteration, i.e. budget = floor(RecoveryTimeoutSeconds - 5 * Step).
        function New-StepClock {
            param([double]$Step = 1, [string]$Start = '2026-01-01T00:00:00Z')
            $s = @{ T = [datetime]$Start }
            return { $s.T = $s.T.AddSeconds($Step); return $s.T }.GetNewClosure()
        }

        function New-ApiFake {
            param([string[]]$Svcs = @('api'))
            $active = @{}
            foreach ($s in $Svcs) { $active[$s] = @{ Id = "kg-$s"; CommitHash = "c-$s"; Status = 'SUCCESS' } }
            New-FakeRailwayApi -Deployments (New-DeploymentFixture -ServicesWanted $Svcs) -Active $active
        }

        # ReleaseProbe honouring the param($baseUrl, $timeoutSeconds) contract: records every timeout
        # it is handed into the caller-supplied [System.Collections.Generic.List[object]] and then
        # delegates to the fake serving-identity probe.
        function New-RecordingReleaseProbe {
            param([Parameter(Mandatory)] $Fake, [Parameter(Mandatory)] $Sink, [hashtable]$OverrideDepId = @{})
            $inner = New-FakeReleaseProbe -Fake $Fake -OverrideDepId $OverrideDepId
            return { param($baseUrl, $timeoutSeconds) $Sink.Add($timeoutSeconds); & $inner $baseUrl }.GetNewClosure()
        }
    }

    It 'TEST 1 — nearly-exhausted budget: the identity probe receives a timeout below the remaining budget and specifically NOT 15' {
        $seen = [System.Collections.Generic.List[object]]::new()
        $fake = New-ApiFake -Svcs @('api')
        Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 19 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-RecordingReleaseProbe -Fake $fake -Sink $seen) `
            -NowProvider (New-StepClock -Step 1) -SleepProvider $script:NoSleep | Out-Null

        $seen.Count | Should -BeGreaterThan 0
        $seen[0] | Should -Not -Be 15
        $seen[0] | Should -BeLessThan 15
        $seen[0] | Should -BeGreaterOrEqual 1
        # budget = floor(19 - 5 * 1) = 14 with this deterministic clock
        $seen[0] | Should -Be 14
    }

    It 'TEST 2 — expired deadline (0 remaining): no identity HTTP request starts and the service is not verified' {
        $seen = [System.Collections.Generic.List[object]]::new()
        $fake = New-ApiFake -Svcs @('api')
        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 5 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-RecordingReleaseProbe -Fake $fake -Sink $seen) `
            -NowProvider (New-StepClock -Step 1) -SleepProvider $script:NoSleep

        $seen.Count | Should -Be 0
        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).verified | Should -BeFalse
    }

    It 'TEST 3 — fractional remaining (<1s): the probe does not start and the budget is never rounded up' {
        $seen = [System.Collections.Generic.List[object]]::new()
        $fake = New-ApiFake -Svcs @('api')
        # step 0.9, budget 4.6 => Railway reads still have >1s (floor >= 1) but the identity budget
        # read lands on remaining = 4.6 - 5*0.9 = 0.1s -> floor 0 -> stop, never rounded up to 1.
        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 4.6 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-RecordingReleaseProbe -Fake $fake -Sink $seen) `
            -NowProvider (New-StepClock -Step 0.9) -SleepProvider $script:NoSleep

        $seen.Count | Should -Be 0
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeFalse
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).identityDetail | Should -Match 'insufficient recovery budget'
    }

    It 'TEST 4 — late successful response: identity that only correlates after the deadline is rejected' {
        $seen = [System.Collections.Generic.List[object]]::new()
        $fake = New-ApiFake -Svcs @('api')
        # budget = floor(6 - 5) = 1 -> probe runs with TimeoutSec 1, then the post-request deadline
        # recheck fires (remaining hits 0) and the match is discarded.
        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 6 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-RecordingReleaseProbe -Fake $fake -Sink $seen) `
            -NowProvider (New-StepClock -Step 1) -SleepProvider $script:NoSleep

        $seen.Count | Should -BeGreaterThan 0
        $seen[0] | Should -BeLessThan 15
        $r.Outcome | Should -Be 'failed'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).verified | Should -BeFalse
    }

    It 'TEST 5 — probe timeout: failure is recorded per service, does not escape Invoke-Recovery, later services still processed' {
        $seen = [System.Collections.Generic.List[object]]::new()
        $fake = New-ApiFake -Svcs @('api', 'app')
        $inner = New-FakeReleaseProbe -Fake $fake
        $timingOutProbe = {
            param($baseUrl, $timeoutSeconds)
            $seen.Add($timeoutSeconds)
            if ($baseUrl.TrimEnd('/') -eq 'https://api.test') {
                # Get-ServiceRelease catches transport failures and returns Reachable=$false.
                return Get-ServiceRelease -BaseUrl $baseUrl -HttpProbe { param($u) throw [System.Net.WebException]::new('The operation has timed out.') }
            }
            return & $inner $baseUrl
        }.GetNewClosure()

        $captured = $null
        $err = $null
        try {
            $captured = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api', 'app')) -RailwayApi $fake.Api `
                -RecoveryTimeoutSeconds 300 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
                -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
                -ReleaseProbe $timingOutProbe `
                -NowProvider (New-StepClock -Step 1) -SleepProvider $script:NoSleep
        }
        catch { $err = $_ }

        $err | Should -BeNullOrEmpty
        $captured.Outcome | Should -Be 'failed'
        ($captured.ServiceResults | Where-Object { $_.service -eq 'api' }).identityOk | Should -BeFalse
        ($captured.ServiceResults | Where-Object { $_.service -eq 'app' }).verified | Should -BeTrue
    }

    It 'TEST 6 — success within budget: matching identity before the deadline proceeds to readiness + migration' {
        $seen = [System.Collections.Generic.List[object]]::new()
        $fake = New-ApiFake -Svcs @('api')
        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 600 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe (New-RecordingReleaseProbe -Fake $fake -Sink $seen) `
            -NowProvider (New-StepClock -Step 1) -SleepProvider $script:NoSleep

        $seen.Count | Should -BeGreaterThan 0
        $seen[0] | Should -BeGreaterOrEqual 1
        $seen[0] | Should -BeLessOrEqual 15
        $r.Outcome | Should -Be 'recovered'
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).readyOk | Should -BeTrue
        ($r.ServiceResults | Where-Object { $_.service -eq 'api' }).migrationOk | Should -BeTrue
    }

    # For the "real wiring" tests we pull the probe scriptblock verbatim out of the shipped source
    # (module default in VerifiedDeploy.psm1, entrypoint copy in Invoke-VerifiedDeploy.ps1) and run
    # it with Invoke-RestMethod textually swapped for a fake sender that records the -TimeoutSec it
    # actually receives — proving the computed budget reaches the HTTP request, not just a callback.
    BeforeAll {
        function Get-SourceProbe {
            param([Parameter(Mandatory)] [string]$Path, [Parameter(Mandatory)] [string]$VarName)
            $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$null, [ref]$null)
            $assign = $ast.FindAll({
                    param($n)
                    $n -is [System.Management.Automation.Language.AssignmentStatementAst] -and
                    $n.Left.Extent.Text -eq $VarName
                }, $true) | Select-Object -First 1
            if (-not $assign) { throw "could not find assignment for $VarName in $Path" }
            $text = $assign.Right.Extent.Text -replace 'Invoke-RestMethod', 'global:Test-CapturingSender'
            return & ([scriptblock]::Create($text))
        }

        # global: so the scriptblocks created by [scriptblock]::Create (runspace-scoped) can resolve it
        function global:Test-CapturingSender {
            param($Uri, $Method, $TimeoutSec)
            $global:CapturedTimeoutSec = $TimeoutSec
            [pscustomobject]@{ service = 'api'; sha = 'A'; version = 'v-A'; deploymentId = 'kg-api' }
        }
    }

    AfterAll { Remove-Item Function:\Test-CapturingSender -ErrorAction SilentlyContinue }

    It 'TEST 7a — real wiring: the Invoke-Recovery default ReleaseProbe threads its seconds budget into the Invoke-RestMethod -TimeoutSec' {
        $global:CapturedTimeoutSec = $null
        $probeSb = Get-SourceProbe -Path (Join-Path $script:DeployDir 'VerifiedDeploy.psm1') -VarName '$ReleaseProbe'

        $rel = & $probeSb 'https://api.test' 9
        $global:CapturedTimeoutSec | Should -Be 9
        $global:CapturedTimeoutSec | Should -Not -Be 15
        $rel.ReportedDeploymentId | Should -Be 'kg-api'

        # and it falls back to 15 only when handed nothing
        $global:CapturedTimeoutSec = $null
        & $probeSb 'https://api.test' $null | Out-Null
        $global:CapturedTimeoutSec | Should -Be 15
    }

    It 'TEST 7b — real wiring: the entrypoint $recoveryReleaseProbe threads its seconds budget into the Invoke-RestMethod -TimeoutSec' {
        $global:CapturedTimeoutSec = $null
        $probeSb = Get-SourceProbe -Path (Join-Path $script:DeployDir 'Invoke-VerifiedDeploy.ps1') -VarName '$recoveryReleaseProbe'

        & $probeSb 'https://api.test' 7 | Out-Null
        $global:CapturedTimeoutSec | Should -Be 7
        $global:CapturedTimeoutSec | Should -Not -Be 15
    }

    It 'TEST 7c — end-to-end: Invoke-Recovery with its built-in default ReleaseProbe still computes a bounded budget (< normal 15) and recovers' {
        $seen = [System.Collections.Generic.List[object]]::new()
        # Wrap the real module default so we can observe the timeout it computes end-to-end.
        $realDefault = Get-SourceProbe -Path (Join-Path $script:DeployDir 'VerifiedDeploy.psm1') -VarName '$ReleaseProbe'
        $observed = { param($baseUrl, $timeoutSeconds) $seen.Add($timeoutSeconds); & $realDefault $baseUrl $timeoutSeconds }.GetNewClosure()
        $fake = New-ApiFake -Svcs @('api')

        $r = Invoke-Recovery -KnownGood (New-KnownGood -ServicesWanted @('api')) -RailwayApi $fake.Api `
            -RecoveryTimeoutSeconds 19 -PollSeconds 1 -ReleaseSafety $script:SafeRelease `
            -ReadinessProbe $script:OkReadiness -ApiMigrationVerifier $script:OkMigration `
            -ReleaseProbe $observed `
            -NowProvider (New-StepClock -Step 1) -SleepProvider $script:NoSleep

        $seen.Count | Should -BeGreaterThan 0
        $seen[0] | Should -Be 14
        $global:CapturedTimeoutSec | Should -Be 14
        $r.Outcome | Should -Be 'recovered'
    }
}
