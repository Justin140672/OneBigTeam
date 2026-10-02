Set-StrictMode -Version Latest

$script:PollyIdPattern = '^Polly(\..+)?$'
$script:ExcludedDirectories = @('.git', 'bin', 'obj', 'node_modules', 'TestResults')
$script:DeclarationElements = @('PackageReference', 'PackageVersion', 'GlobalPackageReference', 'PackageDownload')

function Get-RepositoryFiles {
    param([string]$Root, [string[]]$Names, [string[]]$Extensions)

    $pending = [System.Collections.Generic.Stack[string]]::new()
    $pending.Push($Root)
    while ($pending.Count -gt 0) {
        $dir = $pending.Pop()
        foreach ($entry in [System.IO.Directory]::EnumerateFileSystemEntries($dir)) {
            $name = [System.IO.Path]::GetFileName($entry)
            if ([System.IO.Directory]::Exists($entry)) {
                if ($script:ExcludedDirectories -notcontains $name) { $pending.Push($entry) }
            }
            elseif (($Names -contains $name) -or ($Extensions -contains [System.IO.Path]::GetExtension($name))) {
                $entry
            }
        }
    }
}

function Get-RelativePath {
    param([string]$Root, [string]$Path)
    [System.IO.Path]::GetRelativePath($Root, $Path).Replace('\', '/')
}

function New-Violation {
    param([string]$Rule, [string]$Package, [string]$Resolved, [string]$Expected, [string]$File, [string]$Detail, [string]$ReviewProcess)

    [pscustomobject]@{
        Rule     = $Rule
        Package  = $Package
        Resolved = $Resolved
        Expected = $Expected
        File     = $File
        Message  = "[$Rule] package '$Package' resolved '$Resolved' (expected '$Expected') in '$File': $Detail Required review: $ReviewProcess"
    }
}

function Test-PollyDependencyPolicy {
    <#
    .SYNOPSIS
    Checks committed lockfiles and MSBuild files against the approved Polly package set.
    Returns every violation found (an empty array means the policy holds). Never modifies files.
    #>
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$PolicyPath
    )

    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
    $policy = Get-Content -LiteralPath $PolicyPath -Raw | ConvertFrom-Json
    $review = [string]$policy.reviewProcess
    $approved = @{}
    $canonicalNames = @{}
    foreach ($p in $policy.approvedPackages.PSObject.Properties) {
        $approved[$p.Name.ToLowerInvariant()] = [string]$p.Value
        $canonicalNames[$p.Name.ToLowerInvariant()] = $p.Name
    }

    $violations = [System.Collections.Generic.List[object]]::new()
    $seenVersions = @{}

    $lockfiles = @(Get-RepositoryFiles -Root $root -Names @('packages.lock.json') -Extensions @())
    if ($lockfiles.Count -eq 0) {
        $violations.Add((New-Violation 'NO-LOCKFILES' '(none)' '(none)' '(n/a)' '.' "No packages.lock.json files found under '$root'; the check would verify nothing." $review))
    }

    foreach ($lockfile in $lockfiles) {
        $rel = Get-RelativePath $root $lockfile
        try {
            $lock = Get-Content -LiteralPath $lockfile -Raw | ConvertFrom-Json -ErrorAction Stop
            if ($null -eq $lock -or $null -eq $lock.PSObject.Properties['dependencies']) { throw 'missing top-level "dependencies" object' }
        }
        catch {
            $violations.Add((New-Violation 'UNREADABLE-LOCKFILE' '(unknown)' '(unknown)' '(n/a)' $rel "Lockfile could not be parsed ($($_.Exception.Message))." $review))
            continue
        }

        foreach ($tfm in $lock.dependencies.PSObject.Properties) {
            if ($null -eq $tfm.Value) { continue }
            foreach ($dep in $tfm.Value.PSObject.Properties) {
                if ($dep.Name -notmatch $script:PollyIdPattern) { continue }

                $id = $dep.Name
                $key = $id.ToLowerInvariant()
                $entry = $dep.Value
                $resolved = if ($entry.PSObject.Properties['resolved']) { [string]$entry.resolved } else { '(none)' }
                $type = if ($entry.PSObject.Properties['type']) { [string]$entry.type } else { '' }
                $where = "$rel [$($tfm.Name)]"

                if (-not $approved.ContainsKey($key)) {
                    $violations.Add((New-Violation 'UNAPPROVED-PACKAGE' $id $resolved '(not approved)' $where 'New Polly package appeared in the dependency graph.' $review))
                    continue
                }

                $expected = $approved[$key]
                if ($resolved -ne $expected) {
                    $violations.Add((New-Violation 'VERSION-MISMATCH' $id $resolved $expected $where 'Resolved version differs from the approved version.' $review))
                }
                if ($type -eq 'Direct') {
                    $violations.Add((New-Violation 'DIRECT-DEPENDENCY' $id $resolved $expected $where 'Polly became a direct dependency; it must stay transitive.' $review))
                }
                elseif ($type -eq 'CentralTransitive') {
                    $violations.Add((New-Violation 'CENTRAL-PIN' $id $resolved $expected $where 'Polly is centrally pinned (CentralTransitive); it must stay a plain transitive dependency.' $review))
                }

                if (-not $seenVersions.ContainsKey($key)) { $seenVersions[$key] = @{} }
                if (-not $seenVersions[$key].ContainsKey($resolved)) { $seenVersions[$key][$resolved] = [System.Collections.Generic.List[string]]::new() }
                $seenVersions[$key][$resolved].Add($where)
            }
        }
    }

    foreach ($key in $seenVersions.Keys) {
        $versions = $seenVersions[$key]
        if ($versions.Count -le 1) { continue }
        $parts = foreach ($v in ($versions.Keys | Sort-Object)) {
            $files = $versions[$v]
            $text = "$v in " + (($files | Select-Object -First 3) -join ', ')
            if ($files.Count -gt 3) { $text += ", +$($files.Count - 3) more" }
            $text
        }
        $violations.Add((New-Violation 'VERSION-CONFLICT' $canonicalNames[$key] (($versions.Keys | Sort-Object) -join ' | ') $approved[$key] 'multiple projects/target frameworks' ("Different Polly versions resolve across the solution: " + ($parts -join '; ') + '.') $review))
    }

    $msbuildFiles = @(Get-RepositoryFiles -Root $root -Names @() -Extensions @('.csproj', '.fsproj', '.vbproj', '.props', '.targets'))
    foreach ($file in $msbuildFiles) {
        $rel = Get-RelativePath $root $file
        try {
            $xml = [xml](Get-Content -LiteralPath $file -Raw)
        }
        catch {
            $violations.Add((New-Violation 'UNREADABLE-MSBUILD' '(unknown)' '(unknown)' '(n/a)' $rel "MSBuild file could not be parsed as XML ($($_.Exception.Message))." $review))
            continue
        }

        foreach ($element in $xml.SelectNodes('//*')) {
            if ($script:DeclarationElements -notcontains $element.LocalName) { continue }
            foreach ($attribute in 'Include', 'Update') {
                $value = $element.GetAttribute($attribute)
                if ([string]::IsNullOrWhiteSpace($value)) { continue }
                foreach ($id in ($value -split ';' | ForEach-Object { $_.Trim() })) {
                    if ($id -notmatch $script:PollyIdPattern) { continue }
                    $version = $element.GetAttribute('Version')
                    if (-not $version) { $version = '(unversioned)' }
                    $rule = switch ($element.LocalName) {
                        'PackageVersion' { 'CENTRAL-PACKAGEVERSION' }
                        'GlobalPackageReference' { 'GLOBAL-PACKAGEREFERENCE' }
                        default { 'DIRECT-PACKAGEREFERENCE' }
                    }
                    $expected = if ($approved.ContainsKey($id.ToLowerInvariant())) { $approved[$id.ToLowerInvariant()] } else { '(not approved)' }
                    $violations.Add((New-Violation $rule $id $version $expected $rel "Prohibited <$($element.LocalName)> declaration; Polly must remain a transitive dependency of Microsoft.Extensions.Http.Resilience." $review))
                }
            }
        }
    }

    $violations.ToArray()
}

Export-ModuleMember -Function Test-PollyDependencyPolicy
