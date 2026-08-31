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
- `global.json` pins the .NET SDK feature band. See `docs/development-environment.md`.

## Transitive security pins

- `Newtonsoft.Json` is pinned to `13.0.3` via a `GlobalPackageReference` in
  `Directory.Packages.props`. `Hangfire.Core 1.8.24` otherwise resolves the vulnerable
  `Newtonsoft.Json 11.0.1` (GHSA-5crp-9r3c-p9vr). Remove the pin only once every consumer of
  Hangfire brings a patched transitive version.

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
