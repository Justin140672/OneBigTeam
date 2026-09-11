# Isolated review probes

These probes reproduce the current defects using production source/configuration and synthetic data. They are diagnostic evidence, not regression tests asserting the correct future behavior. They deliberately exit successfully when the documented defective behavior is observed; once the defects are fixed, update or replace these probes with proper regression tests.

## Run

From the repository root, with .NET 10 and the project's normal restore prerequisites:

```powershell
dotnet restore docs/reviews/2026-09-10/probes/ReviewProbes.csproj
dotnet run --project docs/reviews/2026-09-10/probes/ReviewProbes.csproj --no-restore
```

The assembly name uses the existing Leave module test-friend assembly name so it can exercise internal production handlers. It has its own output folder, is not added to the solution, and does not overwrite the actual test project. The project links the two apps' real accessor/handler source, links the import validator/storage source and existing leave test fakes, and references the production Leave and ServiceDefaults projects. No running API, Supabase, browser, or database is required. The default host logging may require normal Windows event-log permissions.

## Final observed output

```text
WEB expected A,B,anonymous,anonymous; actual synthetic-A,synthetic-B,synthetic-B,synthetic-B
ADMIN expected A,B,anonymous,anonymous; actual synthetic-A,synthetic-A,synthetic-A,synthetic-A
RETRY one POST, first response 500 after simulated commit: 2 sends; final 200
LEAVE negative forbidden: approved=10, used=10, remaining=-5
LEAVE stale snapshots: approved=10, used=5, remaining=15
IMPORT traversal filename accepted=True; production storage path outside root=True; no file written
EVENT consumer throws; publisher returns success; calls=1
```

Process exit code: **0**. Intermediate HTTP/Polly logs omitted above; they showed the first 500 response, a retry, and the final 200 response. Final run completed on the review machine with SDK 10.0.401.

## What this does and does not prove

- Authentication: real pooled IHttpClientFactory handler behavior across different request scopes, including a missing cookie and a null HttpContext. No browser exploit or real tokens used.
- Retry: the production resilience pipeline repeats an unsafe request. A post-commit server error is simulated by the transport; a real business record is not duplicated here.
- Leave: actual approval handlers reproduce sequential negative balance and stale-snapshot lost usage in EF InMemory. PostgreSQL locking/conflict behavior must be verified during remediation.
- Import: actual filename validation and storage URL/path resolution accept an out-of-root path. No write is performed and no full workbook is posted to the endpoint.
- Events: an actual publisher invocation with a throwing synthetic consumer returns without propagating the failure. Identity role revocation and restart repair were separately traced in code.

