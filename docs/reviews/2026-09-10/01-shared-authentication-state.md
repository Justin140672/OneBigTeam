# [P1] Isolate authentication tokens from pooled HTTP handlers in both web apps

## Problem and impact
The named `hrapi` client pools its handler chain, including a scoped `SupabaseSessionAccessor`. That scope belongs to the HTTP handler, not the requesting user or Blazor circuit. Consequently one visitor's bearer token is reused for other requests. In the admin app the first token wins even when a second request carries a different cookie. In HR.Web, a missing cookie or absent HttpContext falls back to the most recently cached visitor's token.

This can execute API reads and writes as another user, bypassing the intended account boundary. Company-scoped routes may reject a mismatched tenant, but self/session routes and same-company requests are authenticated as the wrong person; admin requests can retain a previous administrator's authority. Treat this as a release-blocking security defect.

## Evidence
- [src/HR.Web/Program.cs:33](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/HR.Web/Program.cs#L33) and [src/HR.Admin.Web/Program.cs:19](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/HR.Admin.Web/Program.cs#L19) register the accessor inside pooled handlers.
- [src/HR.Web/Services/SupabaseSessionAccessor.cs:44](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/HR.Web/Services/SupabaseSessionAccessor.cs#L44) caches a present token and returns it when the current cookie is missing.
- [src/HR.Admin.Web/Services/SupabaseSessionAccessor.cs:21](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/HR.Admin.Web/Services/SupabaseSessionAccessor.cs#L21) returns the first captured token without checking the new request.

## Reproduction / observed result
Executed a probe using the actual accessor/handler source, real IHttpClientFactory, fresh request scopes, synthetic cookies, and an in-memory transport. Within one handler lifetime, send requests with cookie A, cookie B, no cookie, then no HttpContext.
- HR.Web sends **A, B, B, B**.
- HR.Admin.Web sends **A, A, A, A**.
The last two requests must not inherit an unrelated session. This is a runtime reproduction of the DI/transport boundary, not a live browser exploit.

## Suggested fix
Attach authentication from the actual request/circuit scope outside the pooled handler, or pass an explicit per-request token from a correctly scoped service. Keep pooled transport components stateless with respect to users. Missing, expired, disposed, and logged-out sessions must fail closed. Apply the same design to both apps.

## Acceptance criteria
- [ ] Concurrent and sequential sessions A/B never exchange identities, including same-company users and platform administrators.
- [ ] A request without a session never sends a previously cached bearer token.
- [ ] Interactive Blazor actions retain their own identity after another user logs in and after handler rotation.
- [ ] Logout and cookie removal cannot fall back to an earlier identity.
- [ ] Add real factory-based regression tests plus two-browser-session checks against self/session and privileged endpoints.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

