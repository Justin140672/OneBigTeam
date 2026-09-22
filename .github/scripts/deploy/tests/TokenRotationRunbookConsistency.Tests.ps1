# Documentation-consistency guard (P1 fix, Sept 2026): the deployment-pipeline runbook previously
# presented an obsolete single-token rotation order ("replace current, deploy, then update the
# GitHub secret") immediately before the dual-token procedure that supersedes it, purely as text
# marked "authoritative" rather than by deleting the obsolete paragraph. An operator skimming the
# runbook could still follow the earlier, concise, WRONG instructions and break forward
# verification or rollback authentication during a real rotation.
#
# This test has no functional dependency (StartupMigrationsCheck.psm1 etc. are exercised by
# TokenRotationOverlap.Tests.ps1) — it exists purely to keep the runbook itself from regressing back
# to describing two procedures. It fails the build if the legacy sequence, or a second/duplicate
# "authoritative" rotation section, is ever reintroduced.

BeforeAll {
    $script:RepoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
    $script:RunbookPath = Join-Path $script:RepoRoot 'specifications/runbooks/deployment-pipeline.md'
    $script:RunbookText = Get-Content -Path $script:RunbookPath -Raw
}

Describe 'Deployment runbook: exactly one token-rotation procedure (deployment-pipeline.md)' {

    It 'the runbook file exists' {
        Test-Path $script:RunbookPath | Should -Be $true
    }

    It 'does not describe the legacy single-token order (replace current, deploy, then update GitHub secret)' {
        # Matches the shape of the removed instructions regardless of exact wording: "redeploy the
        # API" (or "deploy the API") followed, within a short span, by "then" and "update" and
        # "GitHub" secret — i.e. a sequence that tells an operator to move the app-side token to a
        # single new value and redeploy BEFORE the GitHub secret changes, with no mention of a
        # Previous/dual-token config in between.
        $legacySequencePattern = '(?is)redeploy the API[^.]*picks up the new value.{0,400}?then\**\s*update the.{0,60}?GitHub Environment secret'

        $script:RunbookText | Should -Not -Match $legacySequencePattern
    }

    It 'has exactly one "Rotation order:" section' {
        $matches = [regex]::Matches($script:RunbookText, 'Rotation order:')
        $matches.Count | Should -Be 1
    }

    It 'the single rotation procedure keeps the dual-token (Previous) overlap step before the GitHub secret update step' {
        # Cheap structural check that the canonical sequence's step ordering hasn't been reshuffled:
        # "ReadinessDetailTokenPrevious" must appear (the dual-token deploy) before the GitHub
        # Environment secret is updated to the new value, within the "Rotation order:" section.
        $rotationOrderIndex = $script:RunbookText.IndexOf('Rotation order:')
        $rotationOrderIndex | Should -BeGreaterThan -1

        $rotationSection = $script:RunbookText.Substring($rotationOrderIndex)
        $previousIndex = $rotationSection.IndexOf('ReadinessDetailTokenPrevious')
        $githubSecretUpdateIndex = $rotationSection.IndexOf('Update the `API_HEALTH_BEARER_TOKEN` GitHub Environment secret')

        $previousIndex | Should -BeGreaterThan -1
        $githubSecretUpdateIndex | Should -BeGreaterThan -1
        $previousIndex | Should -BeLessThan $githubSecretUpdateIndex
    }

    It 'the single rotation procedure ends with removing the previous-token setting (closes the overlap window)' {
        $rotationOrderIndex = $script:RunbookText.IndexOf('Rotation order:')
        $rotationSection = $script:RunbookText.Substring($rotationOrderIndex)

        $rotationSection | Should -Match '(?is)HealthChecks__ReadinessDetailTokenPrevious.{0,80}removed'
    }

    It 'describes the manual workflow_dispatch bearer-token resolution as distinct from the reusable-workflow forwarding path' {
        # Guards against the inaccuracy this fix also corrected: the runbook must not claim
        # deploy.yml and deployment-health-check.yml simply "consume the same API_HEALTH_BEARER_TOKEN
        # secret" without qualifying that a manual dispatch resolves the environment-specific
        # _TEST/_STAGING/_PRODUCTION secret directly rather than going through that generic name.
        $script:RunbookText | Should -Match '(?is)workflow_dispatch.{0,600}API_HEALTH_BEARER_TOKEN_TEST'
    }
}
