BeforeAll {
    $script:PolicyDir = Split-Path -Parent $PSScriptRoot
    $script:PolicyPath = Join-Path $script:PolicyDir 'polly-policy.json'
    Import-Module (Join-Path $script:PolicyDir 'PollyPolicy.psm1') -Force -DisableNameChecking

    function New-Lockfile {
        param([string]$Root, [string]$Project, [hashtable]$Tfms)
        $deps = [ordered]@{}
        foreach ($tfm in $Tfms.Keys) {
            $entries = [ordered]@{}
            foreach ($pkg in $Tfms[$tfm].Keys) {
                $spec = $Tfms[$tfm][$pkg]
                $entries[$pkg] = [ordered]@{ type = $spec.Type; resolved = $spec.Version; contentHash = 'x' }
            }
            $deps[$tfm] = $entries
        }
        $dir = Join-Path $Root $Project
        New-Item -ItemType Directory -Force -Path $dir | Out-Null
        ([ordered]@{ version = 2; dependencies = $deps } | ConvertTo-Json -Depth 10) | Set-Content (Join-Path $dir 'packages.lock.json')
    }

    function Get-Approved {
        param([string]$Version = '8.4.2', [string]$Core = $Version, [string]$Extensions = $Version, [string]$RateLimiting = $Version)
        @{
            'Polly.Core'         = @{ Type = 'Transitive'; Version = $Core }
            'Polly.Extensions'   = @{ Type = 'Transitive'; Version = $Extensions }
            'Polly.RateLimiting' = @{ Type = 'Transitive'; Version = $RateLimiting }
        }
    }

    function Invoke-Policy { param([string]$Root) @(Test-PollyDependencyPolicy -RepositoryRoot $Root -PolicyPath $script:PolicyPath) }
}

Describe 'Test-PollyDependencyPolicy' {
    BeforeEach {
        $script:Root = Join-Path $TestDrive ([guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $script:Root | Out-Null
    }

    It 'passes for approved packages at 8.4.2 across several projects' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        New-Lockfile $script:Root 'B' @{ 'net10.0' = (Get-Approved) }
        Invoke-Policy $script:Root | Should -BeNullOrEmpty
    }

    It 'passes for a lockfile without Polly' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = @{ 'Newtonsoft.Json' = @{ Type = 'Direct'; Version = '13.0.4' } } }
        Invoke-Policy $script:Root | Should -BeNullOrEmpty
    }

    It 'passes the real repository' {
        $repoRoot = (Resolve-Path (Join-Path $script:PolicyDir '../../..')).Path
        Invoke-Policy $repoRoot | Should -BeNullOrEmpty
    }

    It 'fails when <Package> resolves to a different version' -ForEach @(
        @{ Package = 'Polly.Core'; Overrides = @{ Core = '8.5.0' } }
        @{ Package = 'Polly.Extensions'; Overrides = @{ Extensions = '8.5.0' } }
        @{ Package = 'Polly.RateLimiting'; Overrides = @{ RateLimiting = '8.5.0' } }
    ) {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved @Overrides) }
        $v = Invoke-Policy $script:Root
        $v | Should -HaveCount 1
        $v[0].Rule | Should -Be 'VERSION-MISMATCH'
        $v[0].Package | Should -Be $Package
        $v[0].Resolved | Should -Be '8.5.0'
        $v[0].Expected | Should -Be '8.4.2'
        $v[0].File | Should -Match 'A/packages.lock.json'
        $v[0].Message | Should -Match 'dependency-management.md'
    }

    It 'fails when a new Polly package appears' {
        $packages = Get-Approved
        $packages['Polly'] = @{ Type = 'Transitive'; Version = '8.4.2' }
        New-Lockfile $script:Root 'A' @{ 'net10.0' = $packages }
        $v = Invoke-Policy $script:Root
        $v | Should -HaveCount 1
        $v[0].Rule | Should -Be 'UNAPPROVED-PACKAGE'
        $v[0].Package | Should -Be 'Polly'
    }

    It 'fails when projects resolve conflicting versions' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        New-Lockfile $script:Root 'B' @{ 'net10.0' = (Get-Approved -Core '8.5.0') }
        $v = Invoke-Policy $script:Root
        $v.Rule | Should -Contain 'VERSION-CONFLICT'
        $v.Rule | Should -Contain 'VERSION-MISMATCH'
    }

    It 'inspects every target framework' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved); 'net9.0' = (Get-Approved -Extensions '8.6.0') }
        $v = Invoke-Policy $script:Root
        ($v | Where-Object Rule -eq 'VERSION-MISMATCH').File | Should -Match 'net9.0'
        $v.Rule | Should -Contain 'VERSION-CONFLICT'
    }

    It 'fails when an approved package becomes a direct dependency' {
        $packages = Get-Approved
        $packages['Polly.Core'].Type = 'Direct'
        New-Lockfile $script:Root 'A' @{ 'net10.0' = $packages }
        (Invoke-Policy $script:Root).Rule | Should -Be 'DIRECT-DEPENDENCY'
    }

    It 'fails when a Polly package is centrally pinned in the lockfile' {
        $packages = Get-Approved
        $packages['Polly.Core'].Type = 'CentralTransitive'
        New-Lockfile $script:Root 'A' @{ 'net10.0' = $packages }
        (Invoke-Policy $script:Root).Rule | Should -Be 'CENTRAL-PIN'
    }

    It 'fails on a direct PackageReference' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        Set-Content (Join-Path $script:Root 'A/A.csproj') '<Project><ItemGroup><PackageReference Include="Polly.Core" /></ItemGroup></Project>'
        $v = Invoke-Policy $script:Root
        $v | Should -HaveCount 1
        $v[0].Rule | Should -Be 'DIRECT-PACKAGEREFERENCE'
        $v[0].File | Should -Be 'A/A.csproj'
        $v[0].Package | Should -Be 'Polly.Core'
    }

    It 'fails on a central PackageVersion' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        Set-Content (Join-Path $script:Root 'Directory.Packages.props') '<Project><ItemGroup><PackageVersion Include="Polly.Core" Version="8.4.2" /></ItemGroup></Project>'
        $v = Invoke-Policy $script:Root
        $v | Should -HaveCount 1
        $v[0].Rule | Should -Be 'CENTRAL-PACKAGEVERSION'
        $v[0].Resolved | Should -Be '8.4.2'
        $v[0].File | Should -Be 'Directory.Packages.props'
    }

    It 'fails on a GlobalPackageReference' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        Set-Content (Join-Path $script:Root 'Directory.Packages.props') '<Project><ItemGroup><GlobalPackageReference Include="Polly.Extensions" Version="8.4.2" /></ItemGroup></Project>'
        (Invoke-Policy $script:Root).Rule | Should -Be 'GLOBAL-PACKAGEREFERENCE'
    }

    It 'ignores non-Polly packages that merely contain the word' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        Set-Content (Join-Path $script:Root 'A/A.csproj') '<Project><ItemGroup><PackageReference Include="Pollyanna.Lib" /></ItemGroup></Project>'
        Invoke-Policy $script:Root | Should -BeNullOrEmpty
    }

    It 'reports a malformed lockfile' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved) }
        New-Item -ItemType Directory -Path (Join-Path $script:Root 'B') | Out-Null
        Set-Content (Join-Path $script:Root 'B/packages.lock.json') '{ not json'
        $v = Invoke-Policy $script:Root
        $v | Should -HaveCount 1
        $v[0].Rule | Should -Be 'UNREADABLE-LOCKFILE'
        $v[0].File | Should -Be 'B/packages.lock.json'
    }

    It 'fails when no lockfiles exist' {
        (Invoke-Policy $script:Root).Rule | Should -Be 'NO-LOCKFILES'
    }

    It 'reports all simultaneous violations in one run' {
        $packages = Get-Approved -Core '9.0.0'
        $packages['Polly.Contrib'] = @{ Type = 'Transitive'; Version = '1.0.0' }
        $packages['Polly.Extensions'].Type = 'Direct'
        New-Lockfile $script:Root 'A' @{ 'net10.0' = $packages }
        New-Lockfile $script:Root 'B' @{ 'net10.0' = (Get-Approved) }
        Set-Content (Join-Path $script:Root 'Directory.Packages.props') '<Project><ItemGroup><PackageVersion Include="Polly.Core" Version="9.0.0" /></ItemGroup></Project>'
        Set-Content (Join-Path $script:Root 'B/packages.lock.json.bak') 'ignored'
        $rules = (Invoke-Policy $script:Root).Rule
        $rules | Should -Contain 'VERSION-MISMATCH'
        $rules | Should -Contain 'UNAPPROVED-PACKAGE'
        $rules | Should -Contain 'DIRECT-DEPENDENCY'
        $rules | Should -Contain 'VERSION-CONFLICT'
        $rules | Should -Contain 'CENTRAL-PACKAGEVERSION'
    }

    It 'does not modify any file' {
        New-Lockfile $script:Root 'A' @{ 'net10.0' = (Get-Approved -Core '9.0.0') }
        $path = Join-Path $script:Root 'A/packages.lock.json'
        $before = (Get-FileHash $path).Hash
        Invoke-Policy $script:Root | Out-Null
        (Get-FileHash $path).Hash | Should -Be $before
    }
}
