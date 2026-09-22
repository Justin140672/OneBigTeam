# Finding B follow-up: proves deployment-health-check.yml's workflow_call contract is unchanged
# (still consumable by deploy.yml the same way) and that its workflow_dispatch path correctly maps
# each of the three environment choices to the matching repo-level secret
# (API_HEALTH_BEARER_TOKEN_TEST/_STAGING/_PRODUCTION), matching the convention deploy-test.yml /
# deploy-staging.yml / deploy-production.yml already use to forward into deploy.yml's single
# API_HEALTH_BEARER_TOKEN secret (see DeploySecretsWiring.Tests.ps1).
#
# Text/regex based like DeploySecretsWiring.Tests.ps1 for the same reason: no YAML module is
# preinstalled on this repo's CI runners, and the workflow file has a stable, simple, hand-authored
# shape.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
    $script:WorkflowPath = Join-Path $script:RepoRoot '.github/workflows/deployment-health-check.yml'
    $script:Content = Get-Content -Raw $script:WorkflowPath
}

Describe 'deployment-health-check.yml workflow_call contract (used by deploy.yml)' {

    It 'still declares api_base_url as a required workflow_call input' {
        $script:Content | Should -Match "(?s)workflow_call:.*?api_base_url:\s*\r?\n\s*required:\s*true"
    }

    It 'still declares api_bearer_token as a required workflow_call secret' {
        $script:Content | Should -Match "(?s)workflow_call:.*?secrets:.*?api_bearer_token:\s*\r?\n\s*required:\s*true"
    }

    It 'binds no fixed GitHub Environment for workflow_call (deploy.yml already runs inside its own Environment)' {
        # The job-level `environment:` value must be driven off `inputs.environment` (only present
        # under workflow_dispatch), not a hardcoded environment name, so a workflow_call invocation
        # resolves to no environment binding.
        $script:Content | Should -Match 'environment:\s*\$\{\{\s*inputs\.environment\s*\}\}'
    }
}

Describe 'deployment-health-check.yml workflow_dispatch environment selector' {

    It 'constrains the environment input to a choice of test, staging, production' {
        $script:Content | Should -Match "(?s)workflow_dispatch:.*?environment:.*?type:\s*choice.*?options:\s*\r?\n\s*-\s*test\s*\r?\n\s*-\s*staging\s*\r?\n\s*-\s*production"
    }

    It 'maps environment=test to the API_HEALTH_BEARER_TOKEN_TEST secret' {
        $script:Content | Should -Match 'DISPATCH_TOKEN_TEST:\s*\$\{\{\s*secrets\.API_HEALTH_BEARER_TOKEN_TEST\s*\}\}'
        $script:Content | Should -Match 'test\)\s*token="\$DISPATCH_TOKEN_TEST"'
    }

    It 'maps environment=staging to the API_HEALTH_BEARER_TOKEN_STAGING secret' {
        $script:Content | Should -Match 'DISPATCH_TOKEN_STAGING:\s*\$\{\{\s*secrets\.API_HEALTH_BEARER_TOKEN_STAGING\s*\}\}'
        $script:Content | Should -Match 'staging\)\s*token="\$DISPATCH_TOKEN_STAGING"'
    }

    It 'maps environment=production to the API_HEALTH_BEARER_TOKEN_PRODUCTION secret' {
        $script:Content | Should -Match 'DISPATCH_TOKEN_PRODUCTION:\s*\$\{\{\s*secrets\.API_HEALTH_BEARER_TOKEN_PRODUCTION\s*\}\}'
        $script:Content | Should -Match 'production\)\s*token="\$DISPATCH_TOKEN_PRODUCTION"'
    }

    It 'refuses to resolve a token when no case matches (fail closed, no blank-token fallback)' {
        $script:Content | Should -Match 'echo "::error::No bearer token resolved'
    }

    It 'reads vars.API_BASE_URL for workflow_dispatch, matching the per-environment variable deploy.yml already uses' {
        $script:Content | Should -Match 'DISPATCH_BASE_URL:\s*\$\{\{\s*vars\.API_BASE_URL\s*\}\}'
    }
}
