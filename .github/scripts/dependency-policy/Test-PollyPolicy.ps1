param(
    [string]$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../..')).Path,
    [string]$PolicyPath = (Join-Path $PSScriptRoot 'polly-policy.json')
)

$ErrorActionPreference = 'Stop'
Import-Module (Join-Path $PSScriptRoot 'PollyPolicy.psm1') -Force -DisableNameChecking

$violations = @(Test-PollyDependencyPolicy -RepositoryRoot $RepositoryRoot -PolicyPath $PolicyPath)

if ($violations.Count -eq 0) {
    Write-Host 'Polly dependency policy: OK (approved packages only, at approved versions, all transitive).'
    exit 0
}

foreach ($v in $violations) {
    if ($env:GITHUB_ACTIONS -eq 'true') { Write-Host "::error file=$($v.File)::$($v.Message)" }
    else { Write-Host "ERROR $($v.Message)" }
}
Write-Host "Polly dependency policy: FAILED with $($violations.Count) violation(s)."
exit 1
