# Railway.psm1
#
# Testable wrapper over Railway. Two transports, both injectable so tests substitute fakes:
#   * $Runner    — a scriptblock that executes the `railway` CLI and returns @{ ExitCode; Output }.
#                  Used only for actions the CLI is good at: `railway status --json` (project/service/
#                  environment id discovery), `railway variables --set` (release-identity env vars) and
#                  `railway up` (build + deploy from the working-tree commit).
#   * $ApiSender — a scriptblock that POSTs a GraphQL request to the Railway Public API and returns the
#                  parsed response. Used for everything that needs to target a SPECIFIC historical
#                  deployment: listing deployments and rolling back to one by id.
#
# Why the GraphQL API for restore (Ticket 5 follow-up, defect 1):
#   `railway redeploy` has NO historical-deployment targeting — its only flags are -s/--service,
#   -y/--yes, --json, and it always redeploys the *latest* deployment (docs.railway.com/cli/redeploy).
#   After a partial rollout "latest" is the broken release, so a CLI redeploy restores the wrong thing.
#   The Railway Public GraphQL API `deploymentRollback(id: String!)` mutation rolls back to a specific
#   historical deployment by id, using that deployment's own image AND its variable snapshot
#   ("Both the Docker image and custom variables are restored during the rollback process" —
#   docs.railway.com/deployments/deployment-actions). It is only permitted when that deployment has
#   `canRollback: true`; deployments older than the plan's retention policy report `canRollback:false`
#   and cannot be restored automatically.
#
# GraphQL contract relied on (confirmed 2026-09-10 — see docs/tickets/ticket-5-verified-releases-and-recovery.md):
#   * Endpoint : https://backboard.railway.com/graphql/v2
#   * Auth     : project token via the `Project-Access-Token` header (NOT `Authorization: Bearer`,
#                which is for account / workspace / OAuth tokens).
#   * mutation deploymentRollback(id: String!) { id status }
#   * query    deployments(input: { projectId, serviceId, environmentId }, first: N)
#                { edges { node { id status createdAt url staticUrl canRollback meta } } }
#   * query    environment(id) { serviceInstances { edges { node { serviceId latestDeployment { id status meta } } } } }
#   * `meta` is a loosely-typed object; for git-triggered deploys it carries `commitHash`.
#   * deployment status enum: BUILDING, DEPLOYING, SUCCESS, FAILED, CRASHED, REMOVED, REMOVING,
#     SKIPPED, WAITING, QUEUED, SLEEPING, INITIALIZING. "Healthy / serving" = SUCCESS; a service
#     scaled to zero = SLEEPING. Only those two are accepted as a known-good anchor.

Set-StrictMode -Version Latest

$script:RailwayGraphQlEndpoint = 'https://backboard.railway.com/graphql/v2'

function New-RailwayCommandRunner {
    # Default CLI runner: execute `railway` for real and capture stdout + exit code.
    return {
        param([string[]]$RailwayArgs)
        $output = & railway @RailwayArgs 2>&1
        [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output   = ($output | Out-String)
        }
    }
}

function New-RailwayApiSender {
    # Default GraphQL sender: real HTTP POST to the Railway Public API. Honours an optional
    # $Request.TimeoutSec (defect 4 — recovery threads its remaining per-service budget in).
    return {
        param([hashtable]$Request)
        $timeout = if ($Request.ContainsKey('TimeoutSec') -and $Request.TimeoutSec) { [int]$Request.TimeoutSec } else { 30 }
        Invoke-RestMethod -Uri $Request.Uri -Method Post -Headers $Request.Headers `
            -Body $Request.Body -ContentType 'application/json' -TimeoutSec $timeout
    }
}

function Invoke-Railway {
    param(
        [Parameter(Mandatory)] [string[]]$RailwayArgs,
        [Parameter(Mandatory)] [scriptblock]$Runner
    )
    return (& $Runner $RailwayArgs)
}

function Invoke-RailwayApi {
    <#
      POST a GraphQL query/mutation to the Railway Public API through the injectable $ApiSender.
      Returns the `data` object. Throws on transport failure or any GraphQL `errors` entry so the
      caller can convert it into an explicit per-service "not restored / manual-required" outcome —
      it MUST NEVER fall back to a different release.
    #>
    param(
        [Parameter(Mandatory)] [string]$Query,
        [hashtable]$Variables = @{},
        [Parameter(Mandatory)] [string]$Token,
        [string]$Endpoint = $script:RailwayGraphQlEndpoint,
        [Parameter(Mandatory)] [scriptblock]$ApiSender,
        # Defect 4: per-call HTTP timeout. Recovery passes min(30, remaining per-service budget) so a
        # hung Railway API call can never blow past the recovery deadline.
        [int]$TimeoutSec = 30
    )

    if ([string]::IsNullOrWhiteSpace($Token)) {
        throw "Railway API token is empty — cannot call the Railway GraphQL API."
    }
    if ($TimeoutSec -le 0) {
        throw "Railway GraphQL API call skipped — no time remaining in the recovery budget."
    }

    $body = @{ query = $Query; variables = $Variables } | ConvertTo-Json -Depth 12 -Compress
    $headers = @{ 'Project-Access-Token' = $Token }

    $response = & $ApiSender @{ Uri = $Endpoint; Headers = $headers; Body = $body; TimeoutSec = $TimeoutSec }

    if ($null -eq $response) {
        throw "Railway GraphQL API returned an empty response."
    }
    if (($response.PSObject.Properties.Name -contains 'errors') -and $response.errors) {
        $msg = (@($response.errors | ForEach-Object { $_.message }) -join '; ')
        throw "Railway GraphQL API error: $msg"
    }
    if (-not ($response.PSObject.Properties.Name -contains 'data')) {
        throw "Railway GraphQL API response has no 'data' field."
    }
    return $response.data
}

function Get-RailwayContext {
    <#
      Resolve { ProjectId; EnvironmentId; EnvironmentName; ServiceIds(hashtable name->id) } from
      `railway status --json` (the CLI is authenticated with the project token).

      Ticket 5 follow-up (defect 1) — FAIL when the requested environment cannot be resolved
      UNAMBIGUOUSLY. There is NO "first environment" fallback: a mis-set $Environment must abort
      before any Railway state change, never silently deploy to / roll back a different environment.
        * 0 environment edges named $Environment              -> throw (lists the available names)
        * >1 edges named $Environment                         -> throw ("ambiguous")
        * the single match has no node.id                     -> throw
        * ProjectId empty / EnvironmentId empty               -> throw
        * any -RequiredServices id missing                    -> throw (lists the missing ones)
      One resolved identity (id + name) is then used consistently for capture, deploy, rollback and
      verification.
    #>
    param(
        [Parameter(Mandatory)] [string]$Environment,
        [Parameter(Mandatory)] [scriptblock]$Runner,
        # admin-api stays excluded until a deployable HR.Admin.Api host exists.
        [string[]]$RequiredServices = @('api', 'app', 'marketing', 'admin')
    )

    $res = Invoke-Railway -Runner $Runner -RailwayArgs @('status', '--json')
    if ($res.ExitCode -ne 0) {
        throw "railway status --json failed (exit $($res.ExitCode)): $($res.Output)"
    }

    $status = $null
    try { $status = $res.Output | ConvertFrom-Json -ErrorAction Stop } catch {
        throw "Could not parse `railway status --json` output: $($_.Exception.Message)"
    }

    $projectId = $null
    foreach ($key in 'id', 'projectId') {
        if (($status.PSObject.Properties.Name -contains $key) -and $status.$key) { $projectId = [string]$status.$key; break }
    }

    $envEdges = @()
    if (($status.PSObject.Properties.Name -contains 'environments') -and $status.environments -and
        ($status.environments.PSObject.Properties.Name -contains 'edges')) {
        $envEdges = @($status.environments.edges)
    }
    $availableNames = @($envEdges |
        Where-Object { $_.node -and ($_.node.PSObject.Properties.Name -contains 'name') } |
        ForEach-Object { [string]$_.node.name })

    $matches = @($envEdges | Where-Object {
        $_.node -and ($_.node.PSObject.Properties.Name -contains 'name') -and ([string]$_.node.name -eq $Environment)
    })

    if ($matches.Count -eq 0) {
        throw "environment '$Environment' not found in project (available: $($availableNames -join ', '))."
    }
    if ($matches.Count -gt 1) {
        throw "environment name '$Environment' is ambiguous ($($matches.Count) matches) — cannot resolve a single environment id."
    }
    $environmentId = if ($matches[0].node.PSObject.Properties.Name -contains 'id') { [string]$matches[0].node.id } else { $null }
    if ([string]::IsNullOrWhiteSpace($environmentId)) {
        throw "environment '$Environment' resolved but carries no id in `railway status --json`."
    }

    $serviceIds = @{}
    if (($status.PSObject.Properties.Name -contains 'services') -and $status.services -and
        ($status.services.PSObject.Properties.Name -contains 'edges')) {
        foreach ($edge in $status.services.edges) {
            if (($edge.node.PSObject.Properties.Name -contains 'name') -and $edge.node.name -and
                ($edge.node.PSObject.Properties.Name -contains 'id') -and $edge.node.id) {
                $serviceIds[[string]$edge.node.name] = [string]$edge.node.id
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($projectId)) {
        throw "railway status --json did not yield a project id."
    }

    $missing = @($RequiredServices | Where-Object {
        -not $serviceIds.ContainsKey($_) -or [string]::IsNullOrWhiteSpace($serviceIds[$_])
    })
    if ($missing.Count -gt 0) {
        throw "railway status --json is missing service id(s) for required service(s): $($missing -join ', ') (environment '$Environment')."
    }

    return [pscustomobject]@{
        ProjectId       = $projectId
        EnvironmentId   = $environmentId
        EnvironmentName = $Environment
        ServiceIds      = $serviceIds
    }
}

function New-RailwayApiClient {
    <#
      Bundle everything the GraphQL helpers need into one object so downstream function signatures
      stay small. Tests build this by hand with a fake Sender.
    #>
    param(
        [Parameter(Mandatory)] [string]$Token,
        [Parameter(Mandatory)] [string]$ProjectId,
        [Parameter(Mandatory)] [string]$EnvironmentId,
        [Parameter(Mandatory)] [hashtable]$ServiceIds,
        [string]$Endpoint = $script:RailwayGraphQlEndpoint,
        [scriptblock]$Sender = (New-RailwayApiSender)
    )
    return [pscustomobject]@{
        Token         = $Token
        ProjectId     = $ProjectId
        EnvironmentId = $EnvironmentId
        ServiceIds    = $ServiceIds
        Endpoint      = $Endpoint
        Sender        = $Sender
    }
}

function ConvertTo-RailwayDeploymentNode {
    param([Parameter(Mandatory)] $Node)
    $commitHash = $null
    if (($Node.PSObject.Properties.Name -contains 'meta') -and $Node.meta) {
        foreach ($k in 'commitHash', 'commit_hash', 'commitSHA') {
            if (($Node.meta.PSObject.Properties.Name -contains $k) -and $Node.meta.$k) { $commitHash = [string]$Node.meta.$k; break }
        }
    }
    $canRollback = $false
    if (($Node.PSObject.Properties.Name -contains 'canRollback') -and $null -ne $Node.canRollback) {
        $canRollback = [bool]$Node.canRollback
    }
    return [pscustomobject]@{
        Id          = [string]$Node.id
        Status      = if ($Node.PSObject.Properties.Name -contains 'status') { [string]$Node.status } else { $null }
        CreatedAt   = if ($Node.PSObject.Properties.Name -contains 'createdAt') { [string]$Node.createdAt } else { $null }
        CanRollback = $canRollback
        CommitHash  = $commitHash
    }
}

function Resolve-RailwayServiceId {
    param([Parameter(Mandatory)] [object]$Api, [Parameter(Mandatory)] [string]$Service)
    if (-not $Api.ServiceIds.ContainsKey($Service) -or [string]::IsNullOrWhiteSpace($Api.ServiceIds[$Service])) {
        throw "No Railway service id is known for '$Service' (railway status --json did not list it)."
    }
    return [string]$Api.ServiceIds[$Service]
}

function Get-RailwayDeployments {
    <#
      List recent deployments for one service/environment, newest first. Returns an array of
      [pscustomobject]@{ Id; Status; CreatedAt; CanRollback; CommitHash }.
    #>
    param(
        [Parameter(Mandatory)] [object]$Api,
        [Parameter(Mandatory)] [string]$Service,
        [int]$First = 20,
        [int]$TimeoutSec = 30
    )
    $serviceId = Resolve-RailwayServiceId -Api $Api -Service $Service
    $query = @'
query deployments($input: DeploymentListInput!, $first: Int) {
  deployments(input: $input, first: $first) {
    edges { node { id status createdAt url staticUrl canRollback meta } }
  }
}
'@
    $vars = @{
        input = @{ projectId = $Api.ProjectId; serviceId = $serviceId; environmentId = $Api.EnvironmentId }
        first = $First
    }
    $data = Invoke-RailwayApi -Query $query -Variables $vars -Token $Api.Token -Endpoint $Api.Endpoint -ApiSender $Api.Sender -TimeoutSec $TimeoutSec

    $nodes = @()
    if ($data -and ($data.PSObject.Properties.Name -contains 'deployments') -and $data.deployments -and
        ($data.deployments.PSObject.Properties.Name -contains 'edges')) {
        foreach ($edge in $data.deployments.edges) {
            $nodes += (ConvertTo-RailwayDeploymentNode -Node $edge.node)
        }
    }
    return $nodes
}

$script:RailwayServingStatuses = @('SUCCESS', 'SLEEPING')

function Get-RailwayActiveDeployment {
    <#
      The latest deployment for a service in this environment, via the Railway API
      (`environment.serviceInstances[].latestDeployment`). Returns
      [pscustomobject]@{ Id; Status; CommitHash } or $null when the service has none.

      This is the INDEPENDENT artifact-identity anchor for post-restore verification — never trust a
      matching /health/release sha on its own because RELEASE_SHA is a reassignable env var.

      IMPORTANT (defect 2): this returns the *latest* deployment, which is NOT necessarily the one
      serving traffic. While its Status is BUILDING/DEPLOYING/INITIALIZING/WAITING/QUEUED the
      PREVIOUS SUCCESS deployment is still serving. Callers deciding "is the restore live yet?" must
      require Status in {SUCCESS, SLEEPING}; FAILED/CRASHED is a hard fail.
    #>
    param(
        [Parameter(Mandatory)] [object]$Api,
        [Parameter(Mandatory)] [string]$Service,
        [int]$TimeoutSec = 30
    )
    $serviceId = Resolve-RailwayServiceId -Api $Api -Service $Service
    $query = @'
query environmentActive($id: String!) {
  environment(id: $id) {
    serviceInstances {
      edges { node { serviceId latestDeployment { id status meta } } }
    }
  }
}
'@
    $data = Invoke-RailwayApi -Query $query -Variables @{ id = $Api.EnvironmentId } `
        -Token $Api.Token -Endpoint $Api.Endpoint -ApiSender $Api.Sender -TimeoutSec $TimeoutSec

    if (-not ($data -and ($data.PSObject.Properties.Name -contains 'environment') -and $data.environment)) { return $null }
    $env = $data.environment
    if (-not (($env.PSObject.Properties.Name -contains 'serviceInstances') -and $env.serviceInstances -and
              ($env.serviceInstances.PSObject.Properties.Name -contains 'edges'))) { return $null }

    foreach ($edge in $env.serviceInstances.edges) {
        if ([string]$edge.node.serviceId -ne $serviceId) { continue }
        $ld = $edge.node.latestDeployment
        if ($null -eq $ld) { return $null }
        $node = ConvertTo-RailwayDeploymentNode -Node $ld
        return [pscustomobject]@{ Id = $node.Id; Status = $node.Status; CommitHash = $node.CommitHash }
    }
    return $null
}

function Set-RailwayReleaseVars {
    <#
      Inject RELEASE_SHA / PLATFORM_VERSION onto a Railway service so the running instance's
      GET /health/release reports the exact release. Railway's Nixpacks build does not run CI's
      assembly stamping, so this env override is the only way /health/release can report a real sha.
      MUST run before the `railway up` for the same service. Display-truth only — NOT an identity gate.
    #>
    param(
        [Parameter(Mandatory)] [string]$Service,
        [Parameter(Mandatory)] [string]$Environment,
        [Parameter(Mandatory)] [string]$ReleaseSha,
        [Parameter(Mandatory)] [string]$Version,
        [Parameter(Mandatory)] [scriptblock]$Runner
    )
    $res = Invoke-Railway -Runner $Runner -RailwayArgs @(
        'variables', '--service', $Service, '--environment', $Environment,
        '--set', "RELEASE_SHA=$ReleaseSha", '--set', "PLATFORM_VERSION=$Version", '--skip-deploys')
    if ($res.ExitCode -ne 0) {
        throw "railway variables (release identity) failed for service '$Service' (exit $($res.ExitCode)): $($res.Output)"
    }
    return $res
}

function Start-RailwayServiceDeploy {
    param(
        [Parameter(Mandatory)] [string]$Service,
        [Parameter(Mandatory)] [string]$Environment,
        [Parameter(Mandatory)] [scriptblock]$Runner
    )
    $res = Invoke-Railway -Runner $Runner -RailwayArgs @(
        'up', '--service', $Service, '--environment', $Environment, '--ci', '--detach')
    if ($res.ExitCode -ne 0) {
        throw "railway up failed for service '$Service' (exit $($res.ExitCode)): $($res.Output)"
    }
    return $res
}

function Restore-RailwayDeployment {
    <#
      Restore a service to a SPECIFIC recorded known-good deployment via the Railway GraphQL API
      `deploymentRollback(id: <recordedId>)`. There is NO CLI fallback: `railway redeploy` cannot
      target a historical deployment and would restore the wrong release after a partial rollout.

      Returns [pscustomobject]@{ Service; Restored; RolledBackToId; ReturnedDeploymentId; Detail }.
        RolledBackToId       = the recorded known-good id we asked Railway to restore.
        ReturnedDeploymentId = the id in the `deploymentRollback` response. Railway's docs show only
                               `{ id status }` and it is NOT definitively documented whether this is
                               the same id or a fresh deployment node (see the ticket doc). The
                               recovery identity check accepts a new id only with an explicit
                               relationship (same commitHash AND this returned id), never on an
                               unrelated commit-hash match alone.
        Restored = $false  =>  caller marks the service "not restored", names the service + recorded
                               deployment id in the operator instructions, and NEVER substitutes a
                               different release.
    #>
    param(
        [Parameter(Mandatory)] [object]$Api,
        [Parameter(Mandatory)] [string]$Service,
        [AllowNull()] [string]$DeploymentId,
        [int]$TimeoutSec = 30
    )

    if ([string]::IsNullOrWhiteSpace($DeploymentId)) {
        return [pscustomobject]@{ Service = $Service; Restored = $false; RolledBackToId = $null; ReturnedDeploymentId = $null
            Detail = "No recorded known-good deployment id for '$Service' — automatic historical restore is not possible." }
    }

    $mutation = @'
mutation deploymentRollback($id: String!) {
  deploymentRollback(id: $id) { id status }
}
'@
    try {
        $data = Invoke-RailwayApi -Query $mutation -Variables @{ id = $DeploymentId } `
            -Token $Api.Token -Endpoint $Api.Endpoint -ApiSender $Api.Sender -TimeoutSec $TimeoutSec

        if ($data -and ($data.PSObject.Properties.Name -contains 'deploymentRollback') -and $data.deploymentRollback) {
            $status = if ($data.deploymentRollback.PSObject.Properties.Name -contains 'status') { [string]$data.deploymentRollback.status } else { 'unknown' }
            $returnedId = if ($data.deploymentRollback.PSObject.Properties.Name -contains 'id') { [string]$data.deploymentRollback.id } else { $null }
            return [pscustomobject]@{ Service = $Service; Restored = $true
                RolledBackToId = $DeploymentId; ReturnedDeploymentId = $returnedId
                Detail = "deploymentRollback accepted for '$Service' -> known-good deployment $DeploymentId (response id $returnedId, status $status)." }
        }
        return [pscustomobject]@{ Service = $Service; Restored = $false; RolledBackToId = $DeploymentId; ReturnedDeploymentId = $null
            Detail = "deploymentRollback returned no deployment for '$Service' deployment id '$DeploymentId'." }
    }
    catch {
        return [pscustomobject]@{ Service = $Service; Restored = $false; RolledBackToId = $DeploymentId; ReturnedDeploymentId = $null
            Detail = "deploymentRollback FAILED for '$Service' deployment id '$DeploymentId': $($_.Exception.Message)" }
    }
}

Export-ModuleMember -Function `
    New-RailwayCommandRunner, New-RailwayApiSender, Invoke-Railway, Invoke-RailwayApi, `
    Get-RailwayContext, New-RailwayApiClient, Get-RailwayDeployments, Get-RailwayActiveDeployment, `
    Resolve-RailwayServiceId, Set-RailwayReleaseVars, Start-RailwayServiceDeploy, Restore-RailwayDeployment
