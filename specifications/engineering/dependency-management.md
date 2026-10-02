# Dependency management

This repository restores dependencies **reproducibly** and gates them for **known vulnerabilities**
(ticket NFR-09). This document is the policy; the enforcement lives in
`Directory.Build.props`, `Directory.Packages.props`, `.github/workflows/ci.yml` and
`.github/dependabot.yml`.

## Reproducible restore

- **Central Package Management.** Every package version is declared exactly once in
  `Directory.Packages.props` (`<PackageVersion>`). Project files use
  `<PackageReference Include="..." />` with **no** `Version` attribute. Do not add a version to a
  project file - add or change it centrally.
- **Lock files.** `RestorePackagesWithLockFile=true` (in `Directory.Build.props`) produces a
  committed `packages.lock.json` next to every project. CI restores with
  `dotnet restore --locked-mode`, which fails if a lock file is missing or stale.
- After changing any dependency you must run `dotnet restore OneBigTeam.slnx` and commit the
  updated `packages.lock.json` files in the same change.
- `global.json` pins the .NET SDK feature band. See `specifications/engineering/development-environment.md`.

## Transitive security pins

- `Newtonsoft.Json` is pinned to `13.0.3` via a `GlobalPackageReference` in
  `Directory.Packages.props`. `Hangfire.Core 1.8.24` otherwise resolves the vulnerable
  `Newtonsoft.Json 11.0.1` (GHSA-5crp-9r3c-p9vr). Remove the pin only once every consumer of
  Hangfire brings a patched transitive version.

## Polly governance

Polly is **not** a dependency we chose. It arrives transitively through Microsoft's resilience
package:

```text
HR.ServiceDefaults
└── Microsoft.Extensions.Http.Resilience
    └── Microsoft.Extensions.Resilience
        ├── Polly.Extensions 8.4.2 └── Polly.Core 8.4.2
        └── Polly.RateLimiting 8.4.2 └── Polly.Core 8.4.2
```

Approved packages (authoritative source: `.github/scripts/dependency-policy/polly-policy.json`):

| Package | Approved version |
|---|---:|
| `Polly.Core` | `8.4.2` |
| `Polly.Extensions` | `8.4.2` |
| `Polly.RateLimiting` | `8.4.2` |

No other package named `Polly` or `Polly.*` is approved.

Locked restore stops an *unnoticed* upgrade, but an intentional bump of
`Microsoft.Extensions.Http.Resilience` followed by lock file regeneration could move Polly without
a reviewer noticing the licensing and maintenance-fee implications. The Polly policy check closes
that gap. It reads the committed `packages.lock.json` files (every target framework) and all
`*.csproj`/`*.props`/`*.targets` files, reports **every** violation in one run, never modifies
files, and fails when:

- an approved package resolves to a version other than the approved one;
- a new `Polly` / `Polly.*` package appears;
- projects or target frameworks resolve different Polly versions;
- a Polly package becomes `Direct` or `CentralTransitive` in a lock file;
- a Polly `<PackageReference>`, `<PackageVersion>` or `<GlobalPackageReference>` is declared.

**Do not add Polly as a direct or centrally pinned dependency.** Central transitive pinning is
enabled (`CentralPackageTransitivePinningEnabled`), so a `<PackageVersion Include="Polly...">`
would promote it to an implicit top-level dependency and defeat the purpose of the guard. The
approved versions live only in the policy file above.

### Running the check

Locally, before committing any dependency update (requires PowerShell 7):

```bash
pwsh ./.github/scripts/dependency-policy/Test-PollyPolicy.ps1
```

The checker's own tests (Pester 5):

```bash
pwsh -Command "Invoke-Pester -Path .github/scripts/dependency-policy/tests -Output Detailed"
```

CI runs the same script in the `dependency-audit` job immediately after the locked restore, so a
violation fails the pipeline before build or deployment. The Pester tests run in the
`deploy-scripts-test` job. Both use the same module and policy file.

### Changing an approved Polly version

1. Raise the change (for example a `Microsoft.Extensions.Http.Resilience` update that moves
   Polly) as its own pull request, regenerate lock files normally and run the check; it will fail
   and list exactly what moved.
2. Complete the licensing and compatibility review **before** approving the new version: confirm
   the new Polly licence terms and any maintenance-fee or funding obligations with whoever owns
   dependency and licensing policy, and confirm compatibility with our retry, timeout and
   circuit-breaker settings.
3. Update `polly-policy.json` and the lock files together in the reviewed change, with the review
   outcome recorded in the pull request.

This check is a dependency-governance safeguard. It is not legal advice and not a substitute for
organisational licence review.

## Vulnerability scanning and severity thresholds

`NuGetAudit` runs on every restore (`NuGetAuditMode=all`, audits direct **and** transitive
packages, `NuGetAuditLevel=low`).

| Advisory severity | NuGet code | Local build | CI | Action |
|---|---|---|---|---|
| Critical | NU1904 | **error** (build fails) | **fails** | Fix before merge. No exception. |
| High | NU1903 | **error** (build fails) | **fails** | Fix before merge. Exception only with security sign-off. |
| Moderate | NU1902 | **error** (build fails) | **fails** | Fix before merge, or approved time-boxed exception. |
| Low | NU1901 | warning | reported, does not fail | Triage within the weekly dependency review. |

CI runs a second explicit gate: `dotnet list package --vulnerable --include-transitive` and
fails the `dependency-audit` job on any `Moderate`/`High`/`Critical` row (covers advisories that
land between restore caching windows).

### Approved exceptions

An exception is only for a moderate/high finding that cannot be fixed immediately (no patched
version, or the patch is a breaking major bump needing its own work item).

1. Open a tracking issue describing the advisory, the blocked upgrade and the target date.
2. Add the specific NuGet code to `<NoWarn>` in the **single** affected project (never repo-wide),
   with a comment: `<!-- NU1902: <advisory> - tracked in #123, remove by <date> -->`.
3. High severity additionally requires an approving review from a maintainer with security
   ownership. Critical is never excepted.
4. The weekly review removes stale exceptions.

## Automated updates and review cadence

- **Dependabot** (`.github/dependabot.yml`) opens grouped `nuget` and `github-actions` PRs weekly,
  and raises security-update PRs immediately.
- **Weekly dependency review** (Monday): triage Dependabot PRs, run
  `dotnet list OneBigTeam.slnx package --outdated` and `--deprecated`, clear low-severity audit
  warnings, and prune expired `<NoWarn>` exceptions.
- Keep the Entity Framework / ASP.NET / `Microsoft.Extensions.*` packages on a single aligned
  patch version across production and test projects (they share the Dependabot
  `microsoft-aspnetcore-ef` group).
