# Ticket 6 — Make authentication signing-key refresh resilient

Priority: Medium · Original issue: 9

> Handle signing-key rotation and upstream failures without blocking request threads, causing refresh
> storms, or weakening token validation.
>
> Supabase access tokens are asymmetrically signed (ES256/RS256) and verified against keys fetched
> from the project JWKS endpoint. The previous implementation (`SupabaseJwksKeyResolver` in
> `Program.cs`) did a **synchronous blocking** `HttpClient.GetStringAsync(...).GetAwaiter().GetResult()`
> under a `SemaphoreSlim.Wait()` inside `IssuerSigningKeyResolver`, on the request thread, with a
> hand-rolled 10-minute cache, no timeout, no bounded wait, and an unknown `kid` forcing a fresh
> synchronous fetch on every request. This ticket replaces it with the established asynchronous
> `ConfigurationManager<OpenIdConnectConfiguration>` mechanism and adds an absolute cached-key-age cap
> so a sustained JWKS outage cannot let the API trust an arbitrarily old key set forever, while the
> refresh path recovers on its own with no restart.

## Endpoint compatibility (confirmed before selecting the mechanism)

- Supabase serves a **bare JWKS document** at `{ProjectUrl}/auth/v1/.well-known/jwks.json` (the
  configured `SupabaseAuth:JwksUrl`). It does **not** serve a usable OpenID Connect discovery
  document, so `JwtBearerOptions.MetadataAddress` auto-discovery is not an option — a custom
  `IConfigurationRetriever<OpenIdConnectConfiguration>` (`SupabaseJwksRetriever`) parses the bare JWKS
  and populates only `SigningKeys`.
- Token signing algorithms: ES256 (P-256) or RS256, asymmetric, `kid` header present. HS256 is
  Supabase-legacy and never accepted on the real path.
- Rotation model: a new key is published to the JWKS as **standby** before it starts signing, so a
  periodic background refresh picks it up with no restart; `kid` selects the key.

## Acceptance criteria mapping

| Ticket criterion | How it is met |
|---|---|
| Established async refresh mechanism compatible with the endpoint | `ConfigurationManager<OpenIdConnectConfiguration>` (`Microsoft.IdentityModel.Protocols.OpenIdConnect`) fed by the custom bare-JWKS retriever |
| No synchronous network waits in the auth path | `SupabaseJwksKeyResolver` deleted; keys come from the async `ConfigurationManager` / cached snapshot; `HttpClient.Timeout = KeyFetchTimeout` bounds any single fetch |
| Only one refresh in flight per key source | `ConfigurationManager`'s own `SemaphoreSlim(1,1)`; verified by `Concurrent_cold_requests_result_in_a_single_upstream_key_fetch` |
| Repeated unfamiliar key IDs cannot trigger unlimited upstream requests | `FreshnessGatedConfigurationManager.RequestRefresh()` honours at most one call per `RefreshInterval` (30s default); verified by the storm test |
| Accept legitimate key rotation without a restart | background `AutomaticRefreshInterval` refresh + unknown-`kid` forced refresh; verified by the standby-rotation test |
| Apply documented cache and outage behaviour consistently | single `GetGatedConfigurationAsync` path + resolver + key validator all enforce `MaximumCachedKeyAge` |
| Never accept a token whose signature cannot be verified | `ValidateIssuerSigningKey = true`; empty key set past the cap ⇒ 401; forged-signature tests |
| Preserve issuer, audience, lifetime, signature, allowed-algorithm validation | `TokenValidationParameters` unchanged except **added** `ValidAlgorithms` (ES256/RS256 real, HS256 E2E) |
| Log useful refresh failures without exposing tokens or credentials | retriever + gate log message + exception type only, never token/PII/secret material |

Scope: `HR.Api` JWT bearer wiring only —
`src/HR.Api/Authentication/SupabaseSigningKeyOptions.cs` (new),
`src/HR.Api/Authentication/SupabaseJwtBearerConfiguration.cs` (new),
`src/HR.Api/Program.cs` (JWKS wiring extracted to the above; `SupabaseJwksKeyResolver` deleted;
`ConfigurationManager` attached via a DI-aware named-options `Configure<ILoggerFactory>`),
`src/HR.Api/appsettings.json` + `appsettings.Staging.json` (config block),
and the test suite `tests/HR.Integration.Tests/SigningKeyRefreshResilienceTests.cs` (new).

No module, schema, entity, DB or authorization-model change.

---

## Intended behaviour

1. Access tokens are validated against the **most recently, successfully retrieved usable signing-key
   set** (an actual JWKS fetch that yielded at least one signing key).
2. During a short upstream outage the cached keys keep working, exactly as before.
3. Once `now - lastSuccessfulUsableFetch >= MaximumCachedKeyAge`, the API **withholds all signing
   keys** and every token validation fails closed with a controlled `401` (never a `500`).
4. Background / forced refresh attempts keep running throughout, so the first successful fetch after
   the outage restores authentication **with no restart**.
5. The following are all preserved: asynchronous non-blocking retrieval (no per-request JWKS fetch,
   no synchronous network on the request thread), the key-fetch timeout, forced-refresh throttling
   (unknown-`kid` storm cap), HTTPS-only metadata, pinned algorithms (ES256/RS256 real, HS256 E2E),
   issuer / audience / lifetime / signature validation, and the isolated E2E local-HS256 path that
   never touches the network.

### Acceptance criteria

- A valid token is accepted; a token with an unpublished key, wrong issuer, wrong audience, expired
  lifetime, tampered signature, or a disallowed algorithm is rejected.
- Keys cached then upstream fails: tokens stay valid **within** `MaximumCachedKeyAge`; bad signatures
  are still rejected; repeated failed refreshes do **not** extend the age; a successful token
  validation does **not** renew the age.
- Just before the boundary the cache still authenticates; at/after the boundary it cannot; this holds
  through the real `JwtBearerHandler` for both the current-config and last-known-good code paths.
- A later successful retrieval (including a re-fetch that returns the **same** key set) renews
  freshness and restores authentication with no restart.
- Key rotation with a standby key is picked up without a restart; an unknown `kid` triggers at most
  one bounded refresh and a burst of unknown `kid`s is throttled to the configured interval.
- Malformed / empty / keyless JWKS responses never replace the usable cache or renew freshness, and
  are logged (message + exception type only).
- The E2E local-key path performs **zero** JWKS fetches.

---

## Configuration — `SupabaseAuth:SigningKeyRefresh`

| Key | Default | Meaning |
|---|---|---|
| `KeyFetchTimeout` | `00:00:05` | `HttpClient.Timeout` for a single JWKS fetch. Exceeding it is a fetch failure, not a hang. Must be > 0. |
| `AutomaticRefreshInterval` | `00:15:00` | `ConfigurationManager.AutomaticRefreshInterval` — background proactive refresh cadence during normal operation (picks up rotated/standby keys). IdentityModel minimum 5 minutes; validated. |
| `RefreshInterval` | `00:00:30` | Forced-refresh throttle floor. At most one honoured `RequestRefresh()` per interval, which caps a refresh storm from repeated unknown `kid`s. Validated to be >= 30s. |
| `LastKnownGoodLifetime` | `1.00:00:00` | `ConfigurationManager.LastKnownGoodLifetime`. **Only** bounds IdentityModel's last-known-good *fallback* slot. It does **not** expire the current configuration when every refresh fails — on its own, keys outlive any freshness guarantee. |
| `MaximumCachedKeyAge` | `1.00:00:00` | **New.** Absolute cap on the age of the newest successfully retrieved usable key set. Measured from the last successful usable JWKS fetch. Boundary is `elapsed >= max` => expired. Withholds keys (controlled 401) past the cap while refreshes keep running. Must be > 0. |
| `RequireHttpsMetadata` | `true` | JWKS endpoint must be HTTPS. Only relaxed by the in-process test key server. |

### `MaximumCachedKeyAge` vs `LastKnownGoodLifetime`

`LastKnownGoodLifetime` governs a *secondary* slot: a configuration is only demoted to LKG when a
*later* refresh succeeds with a different/less-usable result. If refreshes simply keep **failing**,
IdentityModel serves the last good keys from the **current** slot indefinitely and the LKG lifetime
is never consulted. `MaximumCachedKeyAge` is the missing absolute bound: it is measured from the last
successful usable fetch and applies regardless of whether the keys currently live in the current or
the LKG slot.

---

## Implemented freshness mechanism

`src/HR.Api/Authentication/SupabaseJwtBearerConfiguration.cs`:

- **`FreshnessTrackingRetriever`** (`IConfigurationRetriever<OpenIdConnectConfiguration>` decorator)
  wraps `SupabaseJwksRetriever`. It fires a callback **only** when an actual network fetch returns a
  usable key set (>= 1 signing key). `SupabaseJwksRetriever` already throws on transport failure,
  non-2xx, malformed JSON, and empty/keyless documents, so those never reach the callback. Cached
  hits and token validations never enter the retriever at all.
- **`FreshnessGatedConfigurationManager`** (`: BaseConfigurationManager, IConfigurationManager<OpenIdConnectConfiguration>`)
  is the interposition point. `JwtBearerHandler` reaches signing keys through both the generic
  `IConfigurationManager<T>.GetConfigurationAsync` (pre-populates `TokenValidationParameters`) and the
  `BaseConfigurationManager` surface (`GetBaseConfigurationAsync` + `RequestRefresh`, used by the
  IdentityModel signature validator's refresh-and-retry). Both route through one private
  `GetGatedConfigurationAsync`. The gate never populates its own
  `LastKnownGoodConfiguration`, so the validator's LKG branch is inert — there is exactly one place
  that hands out keys.
- **Freshness state** is a single immutable `FreshnessSnapshot(OpenIdConnectConfiguration, DateTimeOffset)`
  held in a `volatile` reference, published by reference swap from the retriever callback
  (`RecordUsableRetrieval`). It is **scoped to the manager instance** (one per key source). The
  timestamp is taken from an injected `TimeProvider` (`TimeProvider.System` in production, a fake in
  tests). It is renewed on **every** usable retrieval, including a re-fetch that returns an unchanged
  key set; it is **never** renewed on a failed/malformed/empty refresh or on a successful token
  validation.
- **Enforcement** happens on three independent paths so a stale cache fails closed regardless of any
  configuration caching inside `JwtBearerHandler`:
  1. `GetGatedConfigurationAsync` returns an empty `OpenIdConnectConfiguration` once
     `now - snapshot.RetrievedAt >= MaximumCachedKeyAge`.
  2. `TokenValidationParameters.IssuerSigningKeyResolver` returns the cached keys only while fresh,
     otherwise an empty set.
  3. `TokenValidationParameters.IssuerSigningKeyValidator` returns `false` once past the cap — the
     final, source-independent check on whichever key actually verified the signature.
  All three keep the inner `ConfigurationManager` running, so refresh attempts continue and a later
  success re-opens the gate with no restart.
- **Forced-refresh throttle**: `RequestRefresh()` honours at most one call per `RefreshInterval` (on
  the injected clock, `Interlocked` compare-and-swap), then forwards to the inner manager. The inner
  `ConfigurationManager.RefreshInterval` is pinned to 1s so the gate is the single throttle authority.
  **Exception**: once the cached set is *past* `MaximumCachedKeyAge` the gate is already failing
  closed (empty key set), so there is no stale-key storm to cap — `GetGatedConfigurationAsync` then
  bypasses the throttle and asks the inner manager to refresh on every call (still bounded by the
  inner's own 1s real-time floor + single in-flight fetch). This makes recovery after a long outage
  prompt instead of waiting up to `AutomaticRefreshInterval`. Not done on cold start (the first inner
  fetch already covers that) and never while an in-date key set is being served.
- **Startup validation**: `SupabaseSigningKeyOptions.Validate()` throws on non-positive
  `MaximumCachedKeyAge` / `KeyFetchTimeout`, `AutomaticRefreshInterval < 5m`, or `RefreshInterval < 30s`.
  Called from `AttachConfigurationManager`, i.e. at host start.

---

## Test results

`tests/HR.Integration.Tests/SigningKeyRefreshResilienceTests.cs` — 23 tests, each spins its own
Kestrel host running the production `SupabaseJwtBearerConfiguration` pipeline plus a fully controlled
in-process JWKS server (valid / rotated / delayed / 503 / malformed / keyless responses, hit
counting). Test JWTs are minted with test-only RSA/ECDSA keys; time-sensitive tests inject a manual
`FakeTimeProvider`. No live Supabase, no long sleeps, all resources disposed.

```
dotnet test tests/HR.Integration.Tests/HR.Integration.Tests.csproj --no-build \
  --filter "FullyQualifiedName~SigningKeyRefreshResilienceTests"
=> Passed!  - Failed: 0, Passed: 23, Skipped: 0, Total: 23   (5 consecutive runs, all green)
```

Coverage: happy path (RS256 + ES256), anonymous 401, unpublished key / wrong issuer / wrong audience
/ expired / tampered signature / disallowed algorithm rejected, cold start with upstream down fails
closed promptly, slow upstream does not block cached-key requests, concurrent cold requests =>
single fetch, unknown `kid` => one bounded refresh then throttled, key rotation with standby picked
up with no restart, malformed JWKS keeps last-known-good + warns, and the full
`MaximumCachedKeyAge` group (just-before boundary authenticates, at/after boundary cannot, successful
validations do not renew, repeated failed refreshes do not extend, later success / same-key re-fetch
renews and restores auth with no restart, forged signature still rejected within the window), plus
the E2E local-key path making zero JWKS calls.

### Regression evidence

With the `MaximumCachedKeyAge` interposition temporarily reverted to LKG-only behaviour (the three
enforcement points neutered), the max-age tests were run through the real bearer pipeline:

```
Failed!  - Failed: 4, Passed: 1, Total: 5
  At_the_max_age_boundary_the_cached_keys_can_no_longer_authenticate     Expected: Unauthorized  Actual: OK
  Outage_past_maximum_cached_key_age_fails_closed_promptly               Expected: Unauthorized  Actual: OK
  Successful_token_validations_do_not_renew_signing_key_freshness        Expected: Unauthorized  Actual: OK
  Repeated_failed_refreshes_during_an_outage_do_not_extend_the_cached_key_age  Expected: Unauthorized  Actual: OK
  (Just_before_the_max_age_boundary...  still PASS — it asserts OK before the boundary)
```

i.e. without the fix, tokens are accepted indefinitely past the promised limit. Restoring the fix:

```
Passed!  - Failed: 0, Passed: 5, Total: 5
```

The deliberate regression was reverted and is not in the tree.

---

## Validation performed

Toolchain: .NET SDK `10.0.401`, `Microsoft.AspNetCore.Authentication.JwtBearer` `10.0.11`,
`Microsoft.IdentityModel.*` `8.19.2`, Windows 11.

| Command | Result |
|---|---|
| `dotnet build src/HR.Api/HR.Api.csproj` | Build succeeded, 0 errors |
| `dotnet build tests/HR.Integration.Tests/HR.Integration.Tests.csproj` | Build succeeded, 0 errors |
| `dotnet test tests/HR.Integration.Tests --filter "FullyQualifiedName~SigningKeyRefreshResilienceTests"` | Passed 23/23 (~16s), 3 consecutive runs |
| `dotnet test tests/HR.Integration.Tests --filter "LoginEndpointTests\|DisabledAccountEnforcementTests\|E2eTestingProductionGuardTests\|LogoutEndpointTests"` | Passed 18/18 |
| `dotnet test tests/HR.Modules.Identity.Tests --filter "SupabaseAuth\|SensitiveAuthLogging\|LoginHandler"` | Passed 41/41 |
| `dotnet test tests/HR.Architecture.Tests` | Passed 320/320 |

No direct `PackageReference` was added — `Microsoft.IdentityModel.Protocols.OpenIdConnect` resolves
transitively via `Microsoft.AspNetCore.Authentication.JwtBearer`, so no `Directory.Packages.props` or
`packages.lock.json` change was needed.

The full `HR.Integration.Tests` suite and all E2E suites were **not** run (per repo policy — filtered
runs only).

### Verification limitations

- No live Supabase JWKS endpoint is exercised; the controlled in-process server stands in for it.
- Real wall-clock `MaximumCachedKeyAge` expiry (24h) is proven only via an injected `TimeProvider`;
  production uses `TimeProvider.System`.
- `IdentityModel` performs its forced refresh on a background task, so a handful of tests poll a few
  hundred milliseconds for the refreshed keys to land rather than asserting same-request pickup
  (matches the ticket's "triggering or subsequent request" wording).
- Each test hosts its own Kestrel instance on a dynamic port; under heavy parallelism a rare
  first-request transport error can occur before the socket is listening — unrelated to the feature
  under test.
