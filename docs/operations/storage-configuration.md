# Storage configuration (Staging/Production)

This app writes four categories of uploaded/generated files to Supabase Storage. Each category
has its own bucket and its own configuration section, resolved via the standard
environment-variable-over-appsettings pattern (`Section__Key` env vars, or a secret manager that
injects into the same configuration keys).

In Development, an explicit `Test` environment name, or the Playwright E2E harness
(`E2E_TESTING=true`), any category left unconfigured silently falls back to an ephemeral
local-disk implementation (writes under `Path.GetTempPath()`, served through a dev-only download
route). **In every other environment (Staging, Production) a missing/incomplete category makes the
API fail to start** — see `DocumentsModule.AddStorageService` and
`InfrastructureModule.Add*StorageService` for the exact checks.

## Required configuration per category

| Category | Config section | Required keys | Default bucket name |
|---|---|---|---|
| Employee/company documents | `Documents:Supabase` | `SupabaseUrl`, `ServiceRoleKey`, `BucketName` | (must be set explicitly) |
| Profile photos | `Infrastructure:Supabase:ProfilePhotos` | `SupabaseUrl`, `ServiceRoleKey` | `profile-photos` |
| Support ticket attachments | `Infrastructure:Supabase:SupportAttachments` | `SupabaseUrl`, `ServiceRoleKey` | `support-attachments` |
| Organisation data exports | `Infrastructure:Supabase:OrganisationExports` | `SupabaseUrl`, `ServiceRoleKey` | `organisation-exports` |

As environment variables (the form Railway/most hosts use), these are:

```
Documents__Supabase__SupabaseUrl
Documents__Supabase__ServiceRoleKey
Documents__Supabase__BucketName
Infrastructure__Supabase__ProfilePhotos__SupabaseUrl
Infrastructure__Supabase__ProfilePhotos__ServiceRoleKey
Infrastructure__Supabase__SupportAttachments__SupabaseUrl
Infrastructure__Supabase__SupportAttachments__ServiceRoleKey
Infrastructure__Supabase__OrganisationExports__SupabaseUrl
Infrastructure__Supabase__OrganisationExports__ServiceRoleKey
```

All four buckets must exist in the target Supabase project before the corresponding feature is
used in that environment; the app does not create buckets automatically.

## Readiness

`/health/ready` includes a "degraded" (non-critical) check per configured category —
`storage` (profile photos, the original System Health Dashboard check),
`document-storage`, `support-attachment-storage`, and `organisation-export-storage` — each doing a
cheap Supabase Storage "list buckets" call. A failing check here means uploads/downloads for that
category will fail even though the instance is otherwise healthy; it does not 503 `/health/ready`
by itself (storage being briefly unreachable is not considered fatal to serving the rest of the
platform), so alerting on the readiness detail body (via the `X-Health-Token` header) is the way to
catch this rather than relying on orchestrator restarts.
