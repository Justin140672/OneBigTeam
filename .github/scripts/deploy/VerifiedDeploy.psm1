# VerifiedDeploy.psm1
#
# Ticket 5 — "Verify deployed releases and recover to a known-good release" + follow-up
# "Make release recovery trustworthy and handle verification failures safely".
#
# Orchestrates: deploy every required service from ONE immutable release ref; capture the previous
# known-good Railway DEPLOYMENT per service (only a SUCCESS/SLEEPING deployment with canRollback:true
# counts); wait (bounded) for each new deployment and verify its release identity by EXACT sha match;
# verify API readiness + startup migrations against the NEW sha; handle partial rollout explicitly;
# on failure roll every required service back to its recorded known-good deployment via the Railway
# GraphQL `deploymentRollback` mutation and then verify, per service under one shared deadline:
#   (1) the now-active Railway deployment IS the recorded known-good one (independent artifact
#       identity — deployment id / commitHash correlation, NOT a /health/release sha match, because
#       RELEASE_SHA is a reassignable env var);
#   (2) GET /health/ready == 200;
#   (3) for api: startup migrations succeeded AND the payload's release.sha is the recorded
#       known-good sha.
# Recovery reports 'recovered' only when every required service passes all its applicable checks.
#
# Every side effect (Railway CLI, Railway API, HTTP, wall-clock, sleep) is injected so the whole
# workflow is unit-testable with fakes. Helper-level tests are NECESSARY BUT NOT SUFFICIENT — they do
# not prove the workflow against real Railway; see the ticket doc's "Unverified / outstanding" section.

Set-StrictMode -Version Latest

Import-Module (Join-Path $PSScriptRoot 'Railway.psm1') -DisableNameChecking

$script:KnownGoodStatuses = @('SUCCESS', 'SLEEPING')

function Get-KnownGoodTargetId {
    <#
      The rollback target deployment id from a known-good record, tolerating both the new capture
      shape (`rollbackTargetDeploymentId`) and the legacy/JSON shape (`deploymentId`).
    #>
    param([Parameter(Mandatory)] $Record)
    foreach ($name in 'rollbackTargetDeploymentId', 'deploymentId') {
        if (($Record.PSObject.Properties.Name -contains $name) -and -not [string]::IsNullOrWhiteSpace([string]$Record.$name)) {
            return [string]$Record.$name
        }
    }
    return $null
}

function Get-ReleaseSafety {
    <#
      Reads the checked-in release-safety.json. Missing file / missing key => appRollbackSafe = $true
      (the safe default: most releases are expand-only and CAN be rolled back by redeploying code).
      Set appRollbackSafe:false in the release commit whenever it ships a contracting/destructive
      migration (see docs/runbooks/deployment-pipeline.md section 5).
    #>
    param([string]$Path = (Join-Path $PSScriptRoot 'release-safety.json'))

    $safe = $true
    $reason = ''
    $notes = ''
    if (Test-Path $Path) {
        $json = Get-Content -Raw -Path $Path | ConvertFrom-Json
        if ($json.PSObject.Properties.Name -contains 'appRollbackSafe') { $safe = [bool]$json.appRollbackSafe }
        if ($json.PSObject.Properties.Name -contains 'reason') { $reason = [string]$json.reason }
        if ($json.PSObject.Properties.Name -contains 'notes') { $notes = [string]$json.notes }
    }
    return [pscustomobject]@{ AppRollbackSafe = $safe; Reason = $reason; Notes = $notes; Path = $Path }
}

function Get-ServiceRelease {
    <#
      GET <BaseUrl>/health/release via the injected probe. Returns
      @{ Reachable; ReportedSha; ReportedVersion; ReportedDeploymentId; Detail }.

      ReportedSha / ReportedVersion are DISPLAY ONLY (mutable RELEASE_SHA / PLATFORM_VERSION) — never
      an identity gate. ReportedDeploymentId is the Railway-injected immutable RAILWAY_DEPLOYMENT_ID
      the serving instance reports (defect 2): the trustworthy serving-instance identity anchor. It
      may be $null on an old instance that predates this field — callers must treat $null as "cannot
      prove serving identity", not a match.
    #>
    param(
        [Parameter(Mandatory)] [string]$BaseUrl,
        [Parameter(Mandatory)] [scriptblock]$HttpProbe
    )
    $uri = "$($BaseUrl.TrimEnd('/'))/health/release"
    try {
        $body = & $HttpProbe $uri
        if ($null -eq $body) {
            return [pscustomobject]@{ Reachable = $false; ReportedSha = $null; ReportedVersion = $null; ReportedDeploymentId = $null; Detail = "empty response from $uri" }
        }
        $depId = $null
        if ($body.PSObject.Properties.Name -contains 'deploymentId' -and -not [string]::IsNullOrWhiteSpace([string]$body.deploymentId)) {
            $depId = [string]$body.deploymentId
        }
        return [pscustomobject]@{
            Reachable           = $true
            ReportedSha         = [string]$body.sha
            ReportedVersion     = [string]$body.version
            ReportedDeploymentId = $depId
            Detail              = "sha=$($body.sha) version=$($body.version) deploymentId=$depId"
        }
    }
    catch {
        return [pscustomobject]@{ Reachable = $false; ReportedSha = $null; ReportedVersion = $null; ReportedDeploymentId = $null; Detail = $_.Exception.Message }
    }
}

function Wait-ServiceRelease {
    <#
      Polls <BaseUrl>/health/release until the reported sha EXACTLY equals $ExpectedSha, or the
      bounded timeout elapses. Used ONLY for forward-deploy verification (criterion 4). Returns
      @{ Service; State('updated'|'pending'|'failed'); ReportedSha; Detail }.
    #>
    param(
        [Parameter(Mandatory)] [string]$Service,
        [Parameter(Mandatory)] [string]$BaseUrl,
        [Parameter(Mandatory)] [string]$ExpectedSha,
        [Parameter(Mandatory)] [int]$TimeoutSeconds,
        [int]$PollSeconds = 10,
        [Parameter(Mandatory)] [scriptblock]$HttpProbe,
        [scriptblock]$NowProvider = { Get-Date },
        [scriptblock]$SleepProvider = { param($s) Start-Sleep -Seconds $s }
    )

    $deadline = (& $NowProvider).AddSeconds($TimeoutSeconds)
    $last = $null
    while ((& $NowProvider) -lt $deadline) {
        $rel = Get-ServiceRelease -BaseUrl $BaseUrl -HttpProbe $HttpProbe
        $last = $rel
        if ($rel.Reachable -and $rel.ReportedSha -eq $ExpectedSha) {
            return [pscustomobject]@{ Service = $Service; State = 'updated'; ReportedSha = $rel.ReportedSha
                Detail = "verified release $ExpectedSha" }
        }
        & $SleepProvider $PollSeconds
    }

    $state = if ($null -ne $last -and $last.Reachable) { 'pending' } else { 'failed' }
    $detail = if ($null -ne $last) {
        if ($last.Reachable) { "timed out; still reporting sha=$($last.ReportedSha) (expected $ExpectedSha)" }
        else { "timed out; endpoint unreachable: $($last.Detail)" }
    } else { "timed out with no response" }
    $lastSha = if ($null -ne $last) { $last.ReportedSha } else { $null }
    return [pscustomobject]@{ Service = $Service; State = $state; ReportedSha = $lastSha; Detail = $detail }
}

function Test-ServiceReady {
    <#
      Default readiness probe: GET <BaseUrl>/health/ready. Returns @{ Ok; Detail }. Injectable so
      tests drive it. NEVER throws.
    #>
    param(
        [Parameter(Mandatory)] [string]$BaseUrl,
        [scriptblock]$HttpProbe,
        # Defect 4: recovery passes min(15, remaining per-service budget).
        [int]$TimeoutSeconds = 15
    )
    $uri = "$($BaseUrl.TrimEnd('/'))/health/ready"
    if ($TimeoutSeconds -le 0) { return [pscustomobject]@{ Ok = $false; Detail = "GET $uri skipped — no time left in the recovery budget" } }
    try {
        if ($null -ne $HttpProbe) {
            $r = & $HttpProbe $uri
            $code = if ($null -ne $r -and ($r.PSObject.Properties.Name -contains 'StatusCode')) { [int]$r.StatusCode } else { 200 }
        }
        else {
            $resp = Invoke-WebRequest -Uri $uri -TimeoutSec $TimeoutSeconds -SkipHttpErrorCheck
            $code = [int]$resp.StatusCode
        }
        if ($code -eq 200) { return [pscustomobject]@{ Ok = $true; Detail = "GET $uri = 200" } }
        return [pscustomobject]@{ Ok = $false; Detail = "GET $uri = $code" }
    }
    catch {
        return [pscustomobject]@{ Ok = $false; Detail = "GET $uri failed: $($_.Exception.Message)" }
    }
}

function Invoke-VerifiedDeploy {
    <#
      $Services : array of [pscustomobject]@{ Name; BaseUrl }. The API service MUST be named 'api'.
      Returns [pscustomobject]@{ Success; TargetSha; KnownGood(array); Results(array); ApiResult;
                                 Summary(string); FailureReason; KnownGoodPath }.
      Writes $KnownGoodPath as JSON BEFORE triggering any deploy.
    #>
    param(
        [Parameter(Mandatory)] [object[]]$Services,
        [Parameter(Mandatory)] [string]$TargetSha,
        [Parameter(Mandatory)] [string]$Environment,
        [string]$Version = '',
        [int]$RolloutTimeoutSeconds = 900,
        [int]$PollSeconds = 10,
        [Parameter(Mandatory)] [scriptblock]$Runner,
        [Parameter(Mandatory)] [object]$RailwayApi,
        [Parameter(Mandatory)] [scriptblock]$HttpProbe,
        [string]$KnownGoodPath = (Join-Path $PSScriptRoot 'known-good.json'),
        # Verifies API readiness + startup migrations against $TargetSha. Tests inject a fake.
        # Must return @{ Ok; Detail }. A throw here is caught and treated as Ok=$false.
        [scriptblock]$ApiVerification,
        [scriptblock]$NowProvider = { Get-Date },
        [scriptblock]$SleepProvider = { param($s) Start-Sleep -Seconds $s }
    )

    $results = @()

    $resolvedVersion = if ([string]::IsNullOrWhiteSpace($Version)) {
        "0.0.0+$($TargetSha.Substring(0, [Math]::Min(7, $TargetSha.Length)))"
    } else { $Version }

    # --- 1. Capture ONE consistent known-good record per service (defect 1 + defect 3) ---
    # The rollback anchor is the VERIFIED CURRENTLY-SERVING Railway deployment: latest deployment with
    # status in {SUCCESS, SLEEPING} that also appears in the deployments list with canRollback:true.
    # Its recorded identity (releaseSha / commitHash) is the EXPECTED MIGRATION IDENTITY for recovery
    # — recovery derives -ExpectedSha from this record, never from a live /health/release read.
    #
    # Cross-check: the serving instance's /health/release.deploymentId must equal the chosen anchor's
    # id. On disagreement we re-query both a bounded number of times; if still inconsistent the record
    # is captureOutcome='inconsistent' with NO automatic target (manual instructions, and overall
    # 'recovered' becomes impossible). No target found => 'no-target'. Either way the other services
    # are still processed. We NEVER copy identity from another deployment and NEVER record an empty sha.
    $captureRetries = 3
    $knownGood = @()
    foreach ($svc in $Services) {
        $rel = Get-ServiceRelease -BaseUrl $svc.BaseUrl -HttpProbe $HttpProbe
        $record = [ordered]@{
            service                   = $svc.Name
            baseUrl                   = $svc.BaseUrl
            projectId                 = $RailwayApi.ProjectId
            environmentId             = $RailwayApi.EnvironmentId
            serviceId                 = $null
            rollbackTargetDeploymentId = $null
            targetReleaseIdentity     = $null          # the sha the restored instance must report
            commitHash                = $null
            status                    = $null
            canRollback               = $false
            servingMatchAtCapture     = $false
            endpointDeploymentId      = $rel.ReportedDeploymentId
            releaseVersion            = $rel.ReportedVersion   # display only
            releaseSha                = $rel.ReportedSha       # display only
            capturedAtUtc             = (Get-Date).ToUniversalTime().ToString('o')
            captureOutcome            = 'no-target'
            captureDetail             = ''
            # legacy alias kept so older summary code / json readers keep working
            deploymentId              = $null
        }
        try {
            $record.serviceId = Resolve-RailwayServiceId -Api $RailwayApi -Service $svc.Name
        } catch { }

        try {
            $anchor = $null
            $endpointId = $rel.ReportedDeploymentId
            for ($try = 1; $try -le $captureRetries; $try++) {
                $active = Get-RailwayActiveDeployment -Api $RailwayApi -Service $svc.Name
                $deployments = Get-RailwayDeployments -Api $RailwayApi -Service $svc.Name -First 20

                $candidate = $null
                if ($null -ne $active -and $active.Status -in $script:KnownGoodStatuses) {
                    $candidate = $deployments |
                        Where-Object { $_.Id -eq $active.Id -and $_.CanRollback } |
                        Select-Object -First 1
                    # active is serving but retention-expired (canRollback:false) — fall back to the
                    # newest rollback-able SUCCESS/SLEEPING deployment.
                    if (-not $candidate) {
                        $candidate = $deployments |
                            Where-Object { $_.Status -in $script:KnownGoodStatuses -and $_.CanRollback } |
                            Select-Object -First 1
                    }
                }
                else {
                    $candidate = $deployments |
                        Where-Object { $_.Status -in $script:KnownGoodStatuses -and $_.CanRollback } |
                        Select-Object -First 1
                }

                if (-not $candidate) { $anchor = $null; break }

                $servingMatch = ($null -ne $active) -and ($active.Id -eq $candidate.Id)
                $endpointMatch = (-not [string]::IsNullOrWhiteSpace($endpointId)) -and ($endpointId -eq $candidate.Id)

                if ($servingMatch -and $endpointMatch) {
                    $anchor = $candidate
                    $record.servingMatchAtCapture = $true
                    break
                }
                $anchor = $candidate   # remember, but keep retrying for a consistent reading
                if ($try -lt $captureRetries) {
                    & $SleepProvider $PollSeconds
                    $rel = Get-ServiceRelease -BaseUrl $svc.BaseUrl -HttpProbe $HttpProbe
                    $endpointId = $rel.ReportedDeploymentId
                    $record.endpointDeploymentId = $endpointId
                }
            }

            if (-not $anchor) {
                $record.captureOutcome = 'no-target'
                $record.captureDetail = "no SUCCESS/SLEEPING deployment with canRollback:true — no automatic rollback target for '$($svc.Name)'; manual restore required."
            }
            elseif (-not $record.servingMatchAtCapture) {
                $record.captureOutcome = 'inconsistent'
                $record.status = $anchor.Status
                $record.commitHash = $anchor.CommitHash
                $record.captureDetail = "capture INCONSISTENT for '$($svc.Name)': anchor deployment $($anchor.Id) but the serving instance reports deploymentId '$($record.endpointDeploymentId)'. No automatic rollback target recorded — manual restore required."
            }
            else {
                if ([string]::IsNullOrWhiteSpace($rel.ReportedSha)) {
                    $record.captureOutcome = 'no-target'
                    $record.captureDetail = "serving instance for '$($svc.Name)' reports no release.sha — cannot record a migration identity; refusing an empty -ExpectedSha. Manual restore required."
                }
                else {
                    $record.captureOutcome            = 'consistent'
                    $record.rollbackTargetDeploymentId = $anchor.Id
                    $record.deploymentId              = $anchor.Id
                    $record.commitHash                = $anchor.CommitHash
                    $record.status                    = $anchor.Status
                    $record.canRollback               = $anchor.CanRollback
                    $record.targetReleaseIdentity     = $rel.ReportedSha
                    $record.captureDetail = "consistent known-good: deployment $($anchor.Id) (status $($anchor.Status), commit $($anchor.CommitHash)); serving instance confirms deploymentId + release sha $($rel.ReportedSha)."
                }
            }
        }
        catch {
            $record.captureOutcome = 'no-target'
            $record.captureDetail = "known-good capture FAILED for '$($svc.Name)': $($_.Exception.Message)"
        }
        $knownGood += [pscustomobject]$record
    }
    $knownGood | ConvertTo-Json -Depth 6 | Set-Content -Path $KnownGoodPath -Encoding utf8

    # --- 2. Deploy every service from the one immutable ref ---
    foreach ($svc in $Services) {
        try {
            Set-RailwayReleaseVars -Service $svc.Name -Environment $Environment `
                -ReleaseSha $TargetSha -Version $resolvedVersion -Runner $Runner | Out-Null
            Start-RailwayServiceDeploy -Service $svc.Name -Environment $Environment -Runner $Runner | Out-Null
        }
        catch {
            $results += [pscustomobject]@{ service = $svc.Name; state = 'failed'; reportedSha = $null
                detail = "railway deploy failed: $($_.Exception.Message)" }
        }
    }

    # --- 3. Bounded wait + EXACT release-identity verification per service ---
    foreach ($svc in $Services) {
        if (@($results | Where-Object { $_.service -eq $svc.Name }).Count -gt 0) { continue }
        $w = Wait-ServiceRelease -Service $svc.Name -BaseUrl $svc.BaseUrl -ExpectedSha $TargetSha `
            -TimeoutSeconds $RolloutTimeoutSeconds -PollSeconds $PollSeconds -HttpProbe $HttpProbe `
            -NowProvider $NowProvider -SleepProvider $SleepProvider
        $results += [pscustomobject]@{ service = $svc.Name; state = $w.State; reportedSha = $w.ReportedSha; detail = $w.Detail }
    }

    $allUpdated = @($results | Where-Object { $_.state -ne 'updated' }).Count -eq 0

    # --- 4. API readiness + startup-migrations verified against the NEW sha (defect 2: never a bare throw) ---
    $apiResult = $null
    if ($allUpdated) {
        if ($null -eq $ApiVerification) {
            $apiResult = [pscustomobject]@{ Ok = $true; Detail = 'API verification skipped (no verifier supplied).'; Category = 'skipped' }
        }
        else {
            try {
                $apiResult = & $ApiVerification $TargetSha
                if ($null -eq $apiResult) {
                    $apiResult = [pscustomobject]@{ Ok = $false; Detail = 'API verifier returned null.'; Category = 'contract' }
                }
            }
            catch {
                $apiResult = [pscustomobject]@{ Ok = $false
                    Detail = "API verifier threw a terminating error: $($_.Exception.Message)"; Category = 'exception' }
            }
        }
    }

    $success = $allUpdated -and $null -ne $apiResult -and $apiResult.Ok
    $failureReason = ''
    if (-not $allUpdated) {
        $bad = ($results | Where-Object { $_.state -ne 'updated' } | ForEach-Object { "$($_.service)=$($_.state)" }) -join ', '
        $failureReason = "Partial rollout — not every service is running $TargetSha ($bad)."
    }
    elseif ($null -ne $apiResult -and -not $apiResult.Ok) {
        $failureReason = "API verification failed: $($apiResult.Detail)"
    }

    $summary = New-DeploySummaryTable -TargetSha $TargetSha -KnownGood $knownGood -Results $results -ApiResult $apiResult

    return [pscustomobject]@{
        Success       = $success
        TargetSha     = $TargetSha
        KnownGood     = $knownGood
        Results       = $results
        ApiResult     = $apiResult
        Summary       = $summary
        FailureReason = $failureReason
        KnownGoodPath = $KnownGoodPath
    }
}

function Invoke-Recovery {
    <#
      Roll every required service back to its recorded known-good deployment (Railway GraphQL
      `deploymentRollback`) and verify, per service under ONE shared deadline:
        (1) artifact identity — the now-active Railway deployment IS the recorded known-good one;
        (2) GET /health/ready == 200;
        (3) for api — startup migrations succeeded and payload release.sha == recorded known-good sha
            (this step is NEVER skipped, regardless of any deploy-time -SkipApiMigrationCheck flag).
      Returns [pscustomobject]@{ Outcome('recovered'|'manual-required'|'failed'); Lines(array);
                                 ServiceResults(array); Summary(string) }.
    #>
    param(
        [Parameter(Mandatory)] [object[]]$KnownGood,
        [Parameter(Mandatory)] [object]$RailwayApi,
        [int]$RecoveryTimeoutSeconds = 600,
        [int]$PollSeconds = 10,
        [Parameter(Mandatory)] [object]$ReleaseSafety,
        # Readiness probe: param($baseUrl, $timeoutSeconds) -> @{ Ok; Detail }. Default: real GET /health/ready.
        [scriptblock]$ReadinessProbe,
        # API migration verifier: param($baseUrl, $expectedSha, $deadlineUtc) -> @{ Ok; Detail }.
        # Default: real check-startup-migrations.ps1 -ExpectedSha -DeadlineUtc. Tests inject a fake.
        [scriptblock]$ApiMigrationVerifier,
        # Serving-instance identity probe: param($baseUrl, $timeoutSeconds) -> Get-ServiceRelease-shaped
        # object with .Reachable / .ReportedDeploymentId / .ReportedSha. $timeoutSeconds is the integer
        # seconds budget left in the per-service recovery deadline and MUST bound the HTTP request.
        # Default: real GET /health/release.
        [scriptblock]$ReleaseProbe,
        [scriptblock]$NowProvider = { Get-Date },
        [scriptblock]$SleepProvider = { param($s) Start-Sleep -Seconds $s }
    )

    $lines = @()

    if ($null -eq $ReadinessProbe) {
        $ReadinessProbe = { param($baseUrl, $timeoutSeconds) Test-ServiceReady -BaseUrl $baseUrl -TimeoutSeconds ([int]$timeoutSeconds) }
    }
    if ($null -eq $ReleaseProbe) {
        # param($baseUrl, $timeoutSeconds): $timeoutSeconds is an integer seconds budget carved from
        # the remaining per-service recovery deadline (defect 4 follow-up) — it must reach the HTTP
        # request as -TimeoutSec so a near-exhausted budget cannot be overrun by a ~15s request.
        $ReleaseProbe = {
            param($baseUrl, $timeoutSeconds)
            $t = if ($timeoutSeconds -and [int]$timeoutSeconds -gt 0) { [int]$timeoutSeconds } else { 15 }
            Get-ServiceRelease -BaseUrl $baseUrl -HttpProbe { param($u) Invoke-RestMethod -Uri $u -Method Get -TimeoutSec $t }.GetNewClosure()
        }
    }
    if ($null -eq $ApiMigrationVerifier) {
        $ApiMigrationVerifier = {
            param($baseUrl, $expectedSha, $deadlineUtc)
            & (Join-Path $PSScriptRoot '..\check-startup-migrations.ps1') `
                -ApiBaseUrl $baseUrl -MaxAttempts 20 -DelaySeconds 6 -ExpectedSha $expectedSha `
                -DeadlineUtc ([datetime]$deadlineUtc).ToUniversalTime()
            if ($LASTEXITCODE -ne 0) {
                return [pscustomobject]@{ Ok = $false; Detail = "startup-migrations check failed for known-good sha $expectedSha (see log above)" }
            }
            return [pscustomobject]@{ Ok = $true; Detail = "startup migrations verified for known-good sha $expectedSha" }
        }
    }

    # --- Migration-incompatible guard ---
    if (-not $ReleaseSafety.AppRollbackSafe) {
        $lines += "::error::AUTOMATIC APPLICATION ROLLBACK IS DISABLED FOR THIS RELEASE."
        $lines += "Reason: $($ReleaseSafety.Reason)"
        $lines += "This release contains a database migration that is NOT backward compatible."
        $lines += "Redeploying the previous application build will NOT restore the database and may fail"
        $lines += "against the migrated schema. Manual recovery is required:"
        $lines += ""
        $lines += "  1. Decide: roll FORWARD (fix-forward deploy) or restore the database."
        $lines += "  2. If restoring: follow docs/runbooks/backup-and-disaster-recovery.md — PITR the"
        $lines += "     environment database to just BEFORE this deploy started."
        $lines += "  3. Only after the database is at the matching point, roll back the recorded known-good"
        $lines += "     deployments in this order: api, app, then marketing, admin, admin-api."
        foreach ($kg in $KnownGood) {
            $tid = Get-KnownGoodTargetId $kg
            $idText = if ([string]::IsNullOrWhiteSpace($tid)) { 'NONE RECORDED — restore manually from the Railway dashboard' } else { "deploymentRollback(id: '$tid')" }
            $lines += "       - $($kg.service): $idText (was displaying sha $($kg.releaseSha))"
        }
        $lines += "  4. Confirm GET {api}/health/ready = 200 and the now-active Railway deployment per"
        $lines += "     service equals the recorded known-good deployment id."
        $lines += "  5. Record the incident under docs/reviews/ if customer impact exceeded 30 minutes."
        return [pscustomobject]@{ Outcome = 'manual-required'; Lines = $lines; ServiceResults = @()
            Summary = (New-RecoverySummaryTable -ServiceResults @() -Outcome 'manual-required') }
    }

    # --- Roll every required service back, api/app first ---
    $ordered = @()
    $ordered += $KnownGood | Where-Object { $_.service -in @('api', 'app') }
    $ordered += $KnownGood | Where-Object { $_.service -notin @('api', 'app') }

    $serving = @('SUCCESS', 'SLEEPING')

    $serviceResults = @()
    foreach ($kg in $ordered) {
        # ONE per-service deadline covering rollback + identity + readiness + migrations (defect 4).
        $deadline = (& $NowProvider).AddSeconds($RecoveryTimeoutSeconds)
        $remaining = { [double]((($deadline) - (& $NowProvider)).TotalSeconds) }
        $apiBudget = { [int][Math]::Max(0, [Math]::Min(30, [Math]::Floor((& $remaining)))) }

        $targetId    = Get-KnownGoodTargetId $kg
        $expectedSha = if (-not [string]::IsNullOrWhiteSpace($kg.targetReleaseIdentity)) { [string]$kg.targetReleaseIdentity } else { [string]$kg.releaseSha }
        $outcomeTag  = if ($kg.PSObject.Properties.Name -contains 'captureOutcome' -and $kg.captureOutcome) { [string]$kg.captureOutcome } else { $null }

        $sr = [ordered]@{
            service          = $kg.service
            recordedId       = $targetId
            recordedSha      = $expectedSha
            captureOutcome   = $outcomeTag
            restored         = $false
            returnedDeploymentId = $null
            expectedDeploymentId = $targetId
            observedDeploymentId = $null
            observedStatus       = $null
            endpointDeploymentId = $null
            identityOk       = $false
            readyOk          = $false
            migrationOk      = $null      # $null = not applicable (non-api)
            identityDetail   = 'not attempted'
            readyDetail      = 'not attempted'
            migrationDetail  = 'n/a'
            verified         = $false
        }

        # captureOutcome must be 'consistent' (or, for legacy/test records with no tag, a target id).
        if (($null -ne $outcomeTag -and $outcomeTag -ne 'consistent') -or [string]::IsNullOrWhiteSpace($targetId)) {
            $why = if ($null -ne $outcomeTag -and $outcomeTag -ne 'consistent') { "capture outcome '$outcomeTag'" } else { 'no rollback target deployment id recorded' }
            $sr.identityDetail = "excluded from automatic recovery — $why. Manual restore required from the Railway dashboard (do NOT substitute a different release)."
            $lines += "::error::No usable known-good rollback target for '$($kg.service)' ($why) — cannot roll back automatically. Manual restore required."
            $serviceResults += [pscustomobject]$sr
            continue
        }

        if ((& $remaining) -le 0) {
            $sr.identityDetail = 'deadline exhausted before rollback could start'
            $lines += "::error::'$($kg.service)': recovery deadline exhausted before rollback. $($sr.identityDetail)"
            $serviceResults += [pscustomobject]$sr
            continue
        }

        $restore = Restore-RailwayDeployment -Api $RailwayApi -Service $kg.service -DeploymentId $targetId -TimeoutSec (& $apiBudget)
        $lines += $restore.Detail
        $sr.restored = $restore.Restored
        $sr.returnedDeploymentId = $restore.ReturnedDeploymentId
        if (-not $restore.Restored) {
            $sr.identityDetail = $restore.Detail
            $lines += "::error::Roll back FAILED for '$($kg.service)'. Manual restore required — recorded known-good deployment id: '$targetId'. Do NOT substitute a different release."
            $serviceResults += [pscustomobject]$sr
            continue
        }

        # (1) artifact identity — ALL of: (a) serving deployment status in {SUCCESS,SLEEPING};
        #     (b) serving id == recorded target OR == deploymentRollback's returned id (with a
        #     commitHash relationship — never an unrelated commit-hash match alone);
        #     (c) the serving instance's /health/release.deploymentId == that same serving id.
        $hardFail = $false
        while ((& $remaining) -gt 0) {
            $active = $null
            try { $active = Get-RailwayActiveDeployment -Api $RailwayApi -Service $kg.service -TimeoutSec (& $apiBudget) }
            catch { $sr.identityDetail = "could not read serving deployment: $($_.Exception.Message)" }

            $remainingBeforeIdentity = & $remaining
            if ($remainingBeforeIdentity -le 0) { break }   # response arrived after the deadline — does not count

            if ($null -ne $active) {
                $sr.observedDeploymentId = $active.Id
                $sr.observedStatus       = $active.Status

                if ($active.Status -in @('FAILED', 'CRASHED')) {
                    $sr.identityDetail = "serving deployment $($active.Id) status $($active.Status) — restore hard-failed for '$($kg.service)'."
                    $hardFail = $true
                    break
                }

                $statusServing = $active.Status -in $serving
                $idIsTarget    = $active.Id -eq $targetId
                $idIsReturned  = (-not [string]::IsNullOrWhiteSpace($restore.ReturnedDeploymentId)) -and ($active.Id -eq $restore.ReturnedDeploymentId) -and ($active.Id -ne $targetId)
                $commitLinked  = (-not [string]::IsNullOrWhiteSpace($kg.commitHash)) -and ($active.CommitHash -eq $kg.commitHash)
                $deploymentIdOk = $idIsTarget -or ($idIsReturned -and $commitLinked)

                # (c) serving-instance HTTP identity — the request must not be allowed to run longer
                #     than the recovery budget still has left (defect 4 follow-up). Modelled on the
                #     readiness probe below, but — per this ticket's stricter rule — a sub-1s / expired
                #     budget STOPS instead of rounding up: no request starts and the identity probe is
                #     recorded unsuccessful so the not-identityOk path emits the per-service diagnostic.
                $normalTimeout = 15
                $identityBudget = [int][Math]::Min($normalTimeout, [Math]::Floor([Math]::Max(0, $remainingBeforeIdentity)))
                if ($identityBudget -lt 1) {
                    $sr.identityDetail = "insufficient recovery budget remaining for the /health/release identity request"
                    break
                }
                $rel = & $ReleaseProbe $kg.baseUrl $identityBudget
                $sr.endpointDeploymentId = if ($null -ne $rel) { $rel.ReportedDeploymentId } else { $null }
                if ((& $remaining) -le 0) { break }
                $endpointOk = (-not [string]::IsNullOrWhiteSpace($sr.endpointDeploymentId)) -and ($sr.endpointDeploymentId -eq $active.Id)

                if ($statusServing -and $deploymentIdOk -and $endpointOk) {
                    $sr.identityOk = $true
                    $sr.identityDetail = "serving deployment $($active.Id) (status $($active.Status), commit $($active.CommitHash)) correlated with recorded target $targetId and serving instance /health/release.deploymentId=$($sr.endpointDeploymentId)."
                    break
                }
                $sr.identityDetail = "not yet serving the restored deployment: status=$($active.Status) servingId=$($active.Id) expected=$targetId returned=$($restore.ReturnedDeploymentId) endpointDeploymentId=$($sr.endpointDeploymentId) (statusServing=$statusServing idOk=$deploymentIdOk endpointOk=$endpointOk)"
            }
            $sleep = [Math]::Min($PollSeconds, [Math]::Max(0, (& $remaining)))
            & $SleepProvider $sleep
        }
        if (-not $sr.identityOk) {
            $msg = if ($hardFail) { "restored deployment hard-failed (FAILED/CRASHED)" } elseif ((& $remaining) -le 0) { "not correlated before the recovery deadline" } else { "not correlated" }
            $lines += "::error::'$($kg.service)': restored serving deployment identity $msg. $($sr.identityDetail)"
            $serviceResults += [pscustomobject]$sr
            continue
        }

        # (2) readiness — same shared deadline
        while ((& $remaining) -gt 0) {
            $rd = & $ReadinessProbe $kg.baseUrl ([int][Math]::Max(1, [Math]::Min(15, [Math]::Floor((& $remaining)))))
            $sr.readyDetail = $rd.Detail
            if ((& $remaining) -le 0) { $sr.readyDetail = "late response — deadline exceeded ($($rd.Detail))"; break }
            if ($rd.Ok) { $sr.readyOk = $true; break }
            & $SleepProvider ([Math]::Min($PollSeconds, [Math]::Max(0, (& $remaining))))
        }
        if (-not $sr.readyOk) {
            $lines += "::error::'$($kg.service)': /health/ready did not return 200 within the recovery deadline. $($sr.readyDetail)"
            $serviceResults += [pscustomobject]$sr
            continue
        }

        # (3) api migrations — same shared deadline; NEVER skipped; -ExpectedSha derived from the
        #     recorded target identity, NOT a live /health/release read (defect 3).
        if ($kg.service -eq 'api') {
            $sr.migrationOk = $false
            $sr.migrationDetail = 'not attempted'
            while ((& $remaining) -gt 0) {
                $mv = & $ApiMigrationVerifier $kg.baseUrl $expectedSha $deadline
                $sr.migrationDetail = $mv.Detail
                if ((& $remaining) -le 0) { $sr.migrationOk = $false; $sr.migrationDetail = "late response — deadline exceeded ($($mv.Detail))"; break }
                if ($mv.Ok) { $sr.migrationOk = $true; break }
                & $SleepProvider ([Math]::Min($PollSeconds, [Math]::Max(0, (& $remaining))))
            }
            if (-not $sr.migrationOk) {
                $lines += "::error::'api': startup-migrations verification against the known-good sha ($expectedSha) failed within the recovery deadline. $($sr.migrationDetail)"
                $serviceResults += [pscustomobject]$sr
                continue
            }
        }

        $sr.verified = $sr.identityOk -and $sr.readyOk -and ($kg.service -ne 'api' -or $sr.migrationOk -eq $true)
        if ($sr.verified) {
            $lines += "'$($kg.service)': rolled back to known-good deployment $targetId, serving-identity + readiness$(if ($kg.service -eq 'api') { ' + startup migrations' }) verified."
        }
        $serviceResults += [pscustomobject]$sr
    }

    $requiredCount = @($ordered).Count
    $allVerified = ($serviceResults.Count -eq $requiredCount) -and
                   (@($serviceResults | Where-Object { -not $_.verified }).Count -eq 0)

    $summary = New-RecoverySummaryTable -ServiceResults $serviceResults -Outcome ($allVerified ? 'recovered' : 'failed')

    if ($allVerified) {
        return [pscustomobject]@{ Outcome = 'recovered'; Lines = $lines; ServiceResults = $serviceResults; Summary = $summary }
    }

    $lines += "::error::Recovery incomplete. Not every required service was rolled back AND verified (artifact identity + readiness + api startup migrations) — see the table. Manual intervention required (docs/runbooks/deployment-pipeline.md section 6)."
    return [pscustomobject]@{ Outcome = 'failed'; Lines = $lines; ServiceResults = $serviceResults; Summary = $summary }
}

function New-DeploySummaryTable {
    param(
        [Parameter(Mandatory)] [string]$TargetSha,
        [Parameter(Mandatory)] [object[]]$KnownGood,
        [Parameter(Mandatory)] [object[]]$Results,
        [object]$ApiResult
    )
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("### Verified deployment — target sha ``$TargetSha``")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("_Verified artifact identity = the Railway deployment id captured pre-deploy. ``RELEASE_SHA`` / version are display labels only._")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Service | Known-good deployment id | Known-good commit | Known-good status | Displayed sha | Final state | Verified |")
    [void]$sb.AppendLine("|---------|--------------------------|------------------|-------------------|---------------|-------------|----------|")
    foreach ($r in $Results) {
        $kg = $KnownGood | Where-Object { $_.service -eq $r.service } | Select-Object -First 1
        $kgId = if ($kg -and $kg.deploymentId) { $kg.deploymentId } else { '_none_' }
        $kgCommit = if ($kg -and $kg.commitHash) { $kg.commitHash } else { '_n/a_' }
        $kgStatus = if ($kg -and $kg.status) { $kg.status } else { '_none_' }
        $kgSha = if ($kg -and $kg.releaseSha) { $kg.releaseSha } else { '_unknown_' }
        $verified = if ($r.state -eq 'updated') { 'yes' } else { 'NO' }
        [void]$sb.AppendLine("| $($r.service) | $kgId | $kgCommit | $kgStatus | $kgSha | $($r.state) | $verified |")
    }
    [void]$sb.AppendLine("")
    if ($null -ne $ApiResult) {
        [void]$sb.AppendLine("API readiness + startup migrations vs new sha: **$(if ($ApiResult.Ok) { 'PASS' } else { 'FAIL' })** — $($ApiResult.Detail)")
    }
    return $sb.ToString()
}

function New-RecoverySummaryTable {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$ServiceResults,
        [Parameter(Mandatory)] [string]$Outcome
    )
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine("### Recovery — outcome **$Outcome**")
    [void]$sb.AppendLine("")
    if ($Outcome -eq 'manual-required') {
        [void]$sb.AppendLine("_Automatic application rollback refused (release-safety.json: appRollbackSafe=false). See operator instructions above._")
        return $sb.ToString()
    }
    [void]$sb.AppendLine("_Serving identity = the Railway API's currently-serving deployment id AND the serving instance's ``/health/release.deploymentId`` (RAILWAY_DEPLOYMENT_ID) both equal the recorded rollback target. A matching ``RELEASE_SHA`` alone is NOT accepted._")
    [void]$sb.AppendLine("")
    [void]$sb.AppendLine("| Service | Capture | Expected deployment id | Observed serving id | Observed status | /health/release deploymentId | Restored | Serving identity | Readiness | API migrations | Verified |")
    [void]$sb.AppendLine("|---------|---------|------------------------|---------------------|-----------------|------------------------------|----------|------------------|-----------|----------------|----------|")
    foreach ($sr in $ServiceResults) {
        $mig = if ($null -eq $sr.migrationOk) { '_n/a_' } elseif ($sr.migrationOk) { 'PASS' } else { 'FAIL' }
        $rid = if ([string]::IsNullOrWhiteSpace($sr.recordedId)) { '_none_' } else { $sr.recordedId }
        $cap = if ($sr.PSObject.Properties.Name -contains 'captureOutcome' -and $sr.captureOutcome) { $sr.captureOutcome } else { '_n/a_' }
        $obs = if ([string]::IsNullOrWhiteSpace($sr.observedDeploymentId)) { '_none_' } else { $sr.observedDeploymentId }
        $ost = if ([string]::IsNullOrWhiteSpace($sr.observedStatus)) { '_none_' } else { $sr.observedStatus }
        $eid = if ([string]::IsNullOrWhiteSpace($sr.endpointDeploymentId)) { '_none_' } else { $sr.endpointDeploymentId }
        [void]$sb.AppendLine(("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |" -f `
            $sr.service, $cap, $rid, $obs, $ost, $eid,
            $(if ($sr.restored) { 'yes' } else { 'NO' }),
            $(if ($sr.identityOk) { 'PASS' } else { 'FAIL' }),
            $(if ($sr.readyOk) { 'PASS' } else { 'FAIL' }),
            $mig,
            $(if ($sr.verified) { 'yes' } else { 'NO' })))
    }
    return $sb.ToString()
}

Export-ModuleMember -Function Get-ReleaseSafety, Get-ServiceRelease, Wait-ServiceRelease, Test-ServiceReady,
    Invoke-VerifiedDeploy, Invoke-Recovery, New-DeploySummaryTable, New-RecoverySummaryTable
