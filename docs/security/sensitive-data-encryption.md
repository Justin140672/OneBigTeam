# Sensitive Data Encryption (Ticket 1)

Application-level authenticated encryption for sensitive employee data persisted via
Supabase/Postgres. Encryption happens in the application layer before persistence and decryption
after read — **not** in the database. A stolen database backup, on its own, cannot reveal protected
fields: the AES-256 keys live only in environment/secret configuration, never in the application
database.

## Components

| Piece | Location |
|---|---|
| `ISensitiveDataProtector` abstraction | `src/Infrastructure/HR.Infrastructure.Abstractions/ISensitiveDataProtector.cs` |
| `SensitiveDataProtectionException` | `src/Infrastructure/HR.Infrastructure.Abstractions/SensitiveDataProtectionException.cs` |
| `AesGcmSensitiveDataProtector` (impl) | `src/Infrastructure/HR.Infrastructure/Security/AesGcmSensitiveDataProtector.cs` |
| `SensitiveDataProtectionOptions` | `src/Infrastructure/HR.Infrastructure/Security/SensitiveDataProtectionOptions.cs` |
| DI registration | `InfrastructureModule.AddSensitiveDataProtection` |
| Scrubber marker | `SensitiveDataScrubber.ProtectedValuePrefix` / `IsProtectedValue` |

## Algorithm

- **AES-256-GCM** (authenticated encryption — confidentiality + integrity).
- 96-bit nonce, randomly generated per `Protect` call (`RandomNumberGenerator.Fill`).
- 128-bit GCM authentication tag.
- Associated data = `"{scheme}:{keyId}"` (ASCII), so the version and key id are integrity-protected
  and a stored token cannot be re-labelled with a different key id without detection.

## Encrypted value format

```
OBTENC1:{keyId}:{base64( nonce[12] || ciphertext[n] || tag[16] )}
```

- `OBTENC1` — format version token. A future incompatible change becomes `OBTENC2`; decryption
  rejects any unknown scheme.
- `{keyId}` — identifies the key that encrypted the value (may not contain `:`). **Decryption selects
  the key from this embedded id**, which is what makes key rotation a configuration change rather
  than a data migration.
- The final segment is standard base64 of the concatenated nonce, ciphertext and tag.

The stored value is ciphertext only. It contains no plaintext and no key material.

## Configuration

Section `Infrastructure:SensitiveDataProtection`. Supplied via environment variables or a secret
store — **never** committed to `appsettings.json`, **never** stored in the database. Each
environment (dev/staging/prod) has its own key set, so a production backup cannot be read with a
development key.

| Key | Meaning |
|---|---|
| `Infrastructure:SensitiveDataProtection:ActiveKeyId` | Key id used to encrypt new values. Must exist in `Keys`. |
| `Infrastructure:SensitiveDataProtection:Keys:{keyId}` | base64-encoded 32-byte (AES-256) key. Keep old key ids here after a rotation so existing values still decrypt. |

Environment variable form:

```
Infrastructure__SensitiveDataProtection__ActiveKeyId=2026-09
Infrastructure__SensitiveDataProtection__Keys__2026-09=<base64 32 bytes>
Infrastructure__SensitiveDataProtection__Keys__2026-03=<base64 32 bytes>
```

Generate a key:

```bash
openssl rand -base64 32
```

Invalid or missing configuration fails fast the first time `ISensitiveDataProtector` is resolved,
with a `SensitiveDataProtectionException` whose message never contains key material.

## Key rotation

1. Generate a new key, add it as `Keys:{newId}` in every environment's secret store.
2. Point `ActiveKeyId` at `{newId}`.
3. New writes use the new key; existing values keep decrypting via their embedded old key id.
4. Optionally re-encrypt at rest by reading each protected value and writing it back (a background
   re-key pass) once all instances have the new key.
5. Only after every stored value no longer references the old key id may that key be removed.

## Logging / telemetry / events safety

- Protected values are ciphertext and safe to appear in logs; `SensitiveDataScrubber.IsProtectedValue`
  recognises the `OBTENC1:` prefix.
- **Decrypted plaintext of a protected field must never be logged, put in an exception message, a
  span/tag, a Hangfire job argument, or a domain/integration event.** These fields are already
  covered by the NFR-01 `SensitiveDataScrubber` field-name list (`salary`, `bankAccountNumber`,
  `nationalInsuranceNumber`, …) and value patterns; keep new field names aligned with that list.
- `SensitiveDataProtectionException` messages are deliberately generic for this reason.

## Protected fields

| Field | Entity / table | Status |
|---|---|---|
| _(none yet)_ | — | Mechanism delivered; no existing column retrofitted in Ticket 1 — see below. |

### Why no field is retrofitted in this ticket (reliability-first decision)

Converting an existing plaintext column (e.g. `employees.employees` salary / bank / NI columns) to
protected storage requires, in one release:

- a data migration that **backfills every existing row** through the protector (needs the production
  key available to the migration runner, and is irreversible without the key), and
- edits to existing handlers, validators, read models, exports and audit projections that currently
  read those columns as plaintext.

That is a broad, hard-to-roll-back change touching live tenant data. The reliability-first path is
to ship the encryption mechanism plus full test coverage now, and retrofit real fields as a
separate, staged change (expand/contract: add new protected column → dual-write → backfill in
batches → switch reads → drop old column), each step independently deployable and reversible.

### How to opt a field in

1. Add a persisted column for the protected value (`<name>_protected text`), keeping snake_case and
   `company_id` rules. Prefer a real column over a computed property.
2. In the owning module's feature handlers, inject `ISensitiveDataProtector`:
   - on write: `entity.SalaryProtected = _protector.Protect(request.Salary.ToString());`
   - on read: `_protector.TryUnprotect(entity.SalaryProtected, out var salary)` (tolerates
     not-yet-migrated plaintext during roll-out).
3. Never expose the protected column in a `Response`, export, audit payload or event — map to
   decrypted plaintext only where a permitted caller genuinely needs it, and confirm the field name
   is in `SensitiveDataScrubber.ProhibitedFieldNames`.
4. Add handler unit tests and endpoint integration tests covering the encrypted round-trip and that
   the stored column is not plaintext.
5. Add the field to the table above.

## Tests

`tests/HR.Infrastructure.Tests` — `AesGcmSensitiveDataProtectorTests`:
round-trip, decrypt, tampered ciphertext (auth-tag failure), wrong key, key rotation/versioning
(old key id still decrypts, active key id used for new writes), missing/invalid key configuration,
stored value is not plaintext, `IsProtected` / `TryUnprotect` behaviour.

## Operational safety (Ticket 9)

The mechanism above is necessary but not sufficient — it must also be operationally hard to deploy
with broken or missing encryption configuration. Full runbook:
`docs/runbooks/encryption-key-management.md` (initial production key creation, Railway secret
configuration, staging configuration, backup implications, key recovery requirements, rotation
procedure). Summary of the controls added in Ticket 9:

- **Fail fast at startup.** `InfrastructureModule.ValidateSensitiveDataProtectionOrThrow` is called
  from `HR.Api`'s `Program.cs` in every non-Development environment, immediately after the host is
  built and before the request pipeline is wired up. It resolves `ISensitiveDataProtector` (which
  runs the `Create` validation above) and performs a fixed non-sensitive round-trip. Any failure
  throws `SensitiveDataProtectionException` and crashes startup — the instance never serves traffic
  with broken encryption. The exception message never contains key material. Development (and the
  integration test host, which runs as Development) keeps the previous lazy-resolution behaviour so
  environments without protected data are not forced to configure keys.
- **Never a fabricated key.** `AesGcmSensitiveDataProtector.Create` has no code path that generates a
  key — missing, empty, malformed or wrong-length key configuration always throws. This is exercised
  directly by `AesGcmSensitiveDataProtectorTests` and indirectly by the startup fail-fast above.
- **Health check.** `SensitiveDataProtectionHealthCheck` (`src/Infrastructure/HR.Infrastructure/Security/SensitiveDataProtectionHealthCheck.cs`)
  is registered as the `sensitive-data-encryption` check, tag `ready`. It performs the same
  non-sensitive round-trip and is visible in `/health/ready` detail (behind `X-Health-Token`) without
  ever performing a real decrypt or disclosing key material. It is intentionally not tagged
  `critical` — the hard gate is the startup check above, which prevents an unconfigured instance from
  starting at all; the health check exists so an operator can see the state (e.g. mid-rotation, or a
  stale deploy) without needing to reproduce a startup crash.
- **Payload already carries a rotation identifier.** No format change was needed for Ticket 9: the
  `OBTENC1:{keyId}:...` token already embeds both a format version and the key id (see *Encrypted
  value format* above), which is what makes rotation a configuration change rather than a data
  migration.
- **Never logged.** `SensitiveDataProtectionException` messages, the health check description and the
  startup fail-fast message are all fixed, curated strings — none of them include key ids, key bytes,
  ciphertext or plaintext.
