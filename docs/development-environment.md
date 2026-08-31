# Development environment

Supported toolchain for building, testing and running One Big Team. Dependency policy is in
`docs/dependency-management.md`.

## Required

| Tool | Version | Notes |
|---|---|---|
| .NET SDK | **10.0.4xx** (pinned in `global.json`, `rollForward: latestFeature`) | `10.0.400` is the reference build. Any `10.0.4xx` SDK works; older feature bands are rejected by `global.json`. |
| Aspire AppHost SDK | `Aspire.AppHost.Sdk/13.4.6` | MSBuild SDK resolved from NuGet by `src/HR.AppHost`. No `dotnet workload` install required. |
| Git | any recent | - |

No `dotnet workload` components are required. Aspire is fully NuGet-based (`Aspire.Hosting.*`).

## Required only for specific test suites

| Tool | Needed by | Install |
|---|---|---|
| Docker (running daemon) | `HR.Integration.Tests`, `HR.Modules.Identity.Tests` (Testcontainers PostgreSQL) | Docker Desktop / Engine |
| Playwright browsers | `HR.Web.E2E.Tests` | `pwsh tests/HR.Web.E2E.Tests/bin/<cfg>/net10.0/playwright.ps1 install` after a build |

The unit / module / architecture test projects need **none** of the above.

## Clean-checkout verification

From a fresh clone, with only the .NET SDK installed and no other machine state:

```bash
dotnet restore OneBigTeam.slnx --locked-mode      # reproducible; fails on a stale lock file
dotnet build   OneBigTeam.slnx -c Release --no-restore
dotnet test    tests/HR.Architecture.Tests/HR.Architecture.Tests.csproj -c Release --no-build
```

All three succeed on a clean checkout with no undeclared dependencies. Findings from the NFR-09
verification:

- Restore requires only `api.nuget.org` access. No private feed or `NuGet.config` is used.
- No environment variables or user secrets are needed to build or to run architecture/unit tests.
  User-secret IDs exist in project files but are only read at application runtime.
- Design-time EF and local run defaults use development-only PostgreSQL credentials
  (`postgres`/`postgres`); production requires real configuration with no fallback.
- Full integration tests (`HR.Integration.Tests`) additionally require Docker; E2E
  (`HR.Web.E2E.Tests`) additionally requires Playwright browsers and is not run in CI.
