# Idempotency Replay Data Policy (Ticket 3 follow-up)

What an idempotency record is allowed to store, for how long, and how it's removed.

## What gets stored

Each module's `idempotency_keys` table stores, per `(operation_id, company_id, actor_id, key)`:

- The request fingerprint (a SHA-256 hash, not the request body itself).
- The HTTP status code of the original response.
- The response body, serialized as JSON, in `response_body_json`.
- `created_at` / `expires_at`.

`response_body_json` is the only field that can leak something sensitive — it's a full
serialization of whatever DTO the handler passed to `SaveIdempotentAsync`.

## Permitted contents

A response DTO stored for replay must contain only what the client needs to reconstruct its own
original response — the same fields the endpoint would return to the caller on a fresh (non-replay)
call. It must **never** contain:

- Access or refresh tokens, session/bearer credentials, support-session tokens.
- Password-reset, invitation, or email-verification tokens.
- Signed/pre-signed document or object-storage URLs.
- Raw authentication-provider (e.g. Supabase) responses.
- API keys, client secrets, connection strings, or other credential material.
- Large document/export bodies, or personal data broader than the endpoint's normal response.

If an endpoint's natural response contains any of the above, it must not pass that response
straight to `SaveIdempotentAsync` — introduce a narrower, endpoint-specific replay DTO that omits
the sensitive fields, or (see [Endpoints that must not be idempotent](#endpoints-that-must-not-be-idempotent))
decide the endpoint shouldn't be idempotency-wrapped at all.

### Enforcement

`HR.SharedKernel.Idempotency.ReplayResponsePolicy.EnsureReplaySafe<TResponse>()` runs automatically
inside `SaveIdempotentAsync` before anything is serialized. It rejects (throws
`InvalidOperationException`, failing the request) any response type whose properties — recursively,
including nested objects and collection element types — match a sensitive-name pattern (`token`,
`secret`, `password`, `credential`, `signature`, `apikey`, `connectionstring`, `privatekey`,
`signedurl`, etc.). It is a naming heuristic, not a type allowlist, and deliberately errs toward
false positives over false negatives. See `tests/HR.SharedKernel.Tests/ReplayResponsePolicyTests.cs`.

This guard already caught one real violation during this rollout:
`GenerateSupportSessionResponse.Token` — a live support-session bearer token — was being persisted
in plaintext into `response_body_json`, even though the corresponding `SupportSession` business row
only ever stores the token's hash. `GenerateSupportSession` has been reverted to a plain
(non-idempotent) save; see below for why.

### Endpoints that must not be idempotent

Some operations must never replay a stored result, independent of what fields it contains:

- **Anything that issues a fresh, single-use credential or session** (e.g.
  `GenerateSupportSession`) — replaying an old token could hand back one that's since expired or
  been revoked, and storing it at all duplicates a secret the business table deliberately only
  hashes.
- **Operations whose provider-side effect isn't repeatable from a locally-cached response** (e.g. a
  password reset email dispatch) — these need their own durable state machine or provider-side
  idempotency key, not this mechanism.

## Retention

`DbContextIdempotencyExtensions.DefaultRetention` is **7 days**. That comfortably exceeds any
legitimate client retry window — a user re-clicking "Try again", a page rerender resubmitting a
pending request, or the HTTP client's own bounded resilience budget (`TotalRequestTimeout`,
currently 120s) — while still bounding exposure of whatever the stored response contains.

## Cleanup

Every module with an `idempotency_keys` table registers a recurring maintenance job (see each
module's `Jobs/IdempotencyMaintenanceJob.cs` and its `Use<Module>RecurringJobs()` registration) that
calls `CleanupExpiredIdempotencyRecordsAsync` repeatedly until a batch returns fewer rows than the
batch size, deleting only rows already past `ExpiresAt` in bounded batches via a single `DELETE`
statement (safe under concurrent execution — see that method's doc comment).

## Encryption and erasure

Idempotency records live in the same Postgres database as the business data they protect, under
the same at-rest protections as every other table — no separate encryption-at-rest requirement
beyond what the platform already provides for the database as a whole. Because replay records are
scoped to `(operation_id, company_id, actor_id, key)` and always expire within 7 days, a company's
idempotency rows are:

- Automatically removed within the retention window without any explicit action, and
- Deleted immediately as part of a company/tenant deletion, since they live in the same
  company-scoped module database that deletion process already tears down (they are not exempted
  or held back — there is no separate retention hold on this table).

No idempotency record should ever contain the kind of protected personal data that
`docs/security/sensitive-data-encryption.md` covers (application-level field encryption); if a
future replay DTO were to include such a field, it would need that same encryption applied before
being placed in `response_body_json`, in addition to passing the `ReplayResponsePolicy` guard.
