# Reliability review finding 5 (P1): proves every thin wrapper of the reusable deploy.yml workflow
# actually forwards API_HEALTH_BEARER_TOKEN (and RAILWAY_TOKEN), and that deploy.yml itself declares
# API_HEALTH_BEARER_TOKEN as a REQUIRED secret in its workflow_call contract (not optional — a caller
# that forgets to wire it must fail workflow binding immediately, not deploy with a blank token that
# silently degrades /health/startup-migrations to a false-pass minimal payload).
#
# Text/regex based rather than a full YAML parser: this repo's CI runners don't have a YAML module
# preinstalled, and the workflow files are hand-authored with a stable, simple shape, so a regex
# check on the `secrets:` blocks is sufficient and keeps this test dependency-free.

# Resolved at DISCOVERY time (top-level script scope), not inside BeforeAll: Pester evaluates a
# Describe/It block's -ForEach expression during discovery, before any BeforeAll in this file has
# run, so a $script: variable only assigned in BeforeAll is still $null when -ForEach reads it.
$script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$script:WorkflowsDir = Join-Path $script:RepoRoot '.github/workflows'
$script:DeployYmlPath = Join-Path $script:WorkflowsDir 'deploy.yml'

# Every thin wrapper that `uses: ./.github/workflows/deploy.yml`.
$script:WrapperFiles = Get-ChildItem -Path $script:WorkflowsDir -Filter '*.yml' |
    Where-Object {
        $_.Name -ne 'deploy.yml' -and
        (Select-String -Path $_.FullName -Pattern 'uses:\s*\./\.github/workflows/deploy\.yml' -Quiet)
    }

BeforeAll {
    # Re-assigned here too: Pester invokes this file's top-level code once during Discovery (to
    # resolve -ForEach) and again in a fresh scope during Run (to execute the Its), so a $script:
    # variable set only at the top level does not survive into the It bodies below.
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
    $script:WorkflowsDir = Join-Path $script:RepoRoot '.github/workflows'
    $script:DeployYmlPath = Join-Path $script:WorkflowsDir 'deploy.yml'
    $script:WrapperFiles = Get-ChildItem -Path $script:WorkflowsDir -Filter '*.yml' |
        Where-Object {
            $_.Name -ne 'deploy.yml' -and
            (Select-String -Path $_.FullName -Pattern 'uses:\s*\./\.github/workflows/deploy\.yml' -Quiet)
        }

    # Line-based helper (no multi-line/backtracking regex): given a file's lines, find the line
    # index of a top-level key like "<SecretName>:" and return the value of the next "required:"
    # line found after it, before the next same-or-lower-indented key starts a new secret entry.
    function Get-RequiredValueAfterKey {
        param([string[]]$Lines, [string]$KeyName)

        $keyLineIndex = -1
        for ($i = 0; $i -lt $Lines.Count; $i++) {
            if ($Lines[$i] -match "^\s*${KeyName}:\s*$") { $keyLineIndex = $i; break }
        }
        if ($keyLineIndex -eq -1) { return $null }

        for ($i = $keyLineIndex + 1; $i -lt $Lines.Count; $i++) {
            $line = $Lines[$i]
            if ($line -match '^\s*required:\s*(true|false)\s*$') { return $Matches[1] }
            # Stop scanning once another top-level-ish "name:" secret entry starts (two-space indent
            # under `secrets:`), so we never read past this secret's own block.
            if ($line -match '^\s{6}[A-Za-z_][A-Za-z0-9_]*:\s*$' -and $i -gt $keyLineIndex + 1) { break }
        }
        return $null
    }
}

Describe 'deploy.yml reusable workflow secret contract' {

    It 'declares API_HEALTH_BEARER_TOKEN as required (not optional)' {
        $lines = Get-Content $script:DeployYmlPath
        $required = Get-RequiredValueAfterKey -Lines $lines -KeyName 'API_HEALTH_BEARER_TOKEN'
        $required | Should -Not -BeNullOrEmpty -Because "expected to find a 'required:' setting under API_HEALTH_BEARER_TOKEN in deploy.yml"
        $required | Should -Be 'true'
    }

    It 'declares RAILWAY_TOKEN as required' {
        $lines = Get-Content $script:DeployYmlPath
        $required = Get-RequiredValueAfterKey -Lines $lines -KeyName 'RAILWAY_TOKEN'
        $required | Should -Not -BeNullOrEmpty -Because "expected to find a 'required:' setting under RAILWAY_TOKEN in deploy.yml"
        $required | Should -Be 'true'
    }

    It 'has at least one wrapper workflow that calls it (sanity check the discovery itself works)' {
        $script:WrapperFiles.Count | Should -BeGreaterThan 0
    }
}

Describe 'deploy.yml wrapper workflows forward the required secrets' {

    It 'every caller of deploy.yml passes API_HEALTH_BEARER_TOKEN and RAILWAY_TOKEN in its secrets: block' -ForEach @(
        $script:WrapperFiles | ForEach-Object { @{ Wrapper = $_ } }
    ) {
        $lines = Get-Content $Wrapper.FullName

        $secretsLineIndex = -1
        for ($i = 0; $i -lt $lines.Count; $i++) {
            if ($lines[$i] -match '^\s*secrets:\s*$') { $secretsLineIndex = $i; break }
        }
        $secretsLineIndex | Should -Not -Be -1 -Because "expected a secrets: block in $($Wrapper.Name)"

        # Each wrapper has exactly one job/step, so everything after `secrets:` to end of file is
        # that block.
        $secretsBlock = ($lines[($secretsLineIndex + 1)..($lines.Count - 1)]) -join "`n"

        $secretsBlock | Should -Match 'API_HEALTH_BEARER_TOKEN:\s*\$\{\{\s*secrets\.\S+\s*\}\}'
        $secretsBlock | Should -Match 'RAILWAY_TOKEN:\s*\$\{\{\s*secrets\.\S+\s*\}\}'
    }
}
