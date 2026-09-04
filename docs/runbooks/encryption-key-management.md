# Encryption key management (sensitive data at rest)

Owner: Crazy Cat Software Limited
Review frequency: at least annually and after any change to hosting, key custody or the set of
encrypted fields
Related: `docs/security/sensitive-data-encryption.md` (mechanism),
`docs/compliance/data-protection-operations.md` (DPA/operational context),
`docs/runbooks/backup-and-disaster-recovery.md` (restore + deletion re-application)

Application-level AES-256-GCM protects special-category and other sensitive persisted values (today:
the `employees.employee_equality_data` answer columns). Encryption and decryption happen in the
application before/after database access, so a database backup on its own is only ciphertext.

The keys are supplied **only** through environment / secret configuration under
`Infrastructure:SensitiveDataProtection` — never in `appsettings.json`, never in any application
table, never in a backup. An architecture test
(`tests/HR.Architecture.Tests/SensitiveDataProtectionKeysNotCommittedTests.cs`) fails the build if a
committed `appsettings*.json` carries a populated `Keys` map or a non-empty `ActiveKeyId`.

## Configuration shape

| Setting | Meaning |
|---|---|
| `Infrastructure:SensitiveDataProtection:ActiveKeyId` | Key id used to encrypt new values. Must exist in `Keys`. |
| `Infrastructure:SensitiveDataProtection:Keys:{keyId}` | base64-encoded 32-byte (AES-256) key. Retain old ids here after a rotation so existing values still decrypt. |

Environment-variable form (Railway, `__` = `:`):

```
Infrastructure__SensitiveDataProtection__ActiveKeyId=2026-09
Infrastructure__SensitiveDataProtection__Keys__2026-09=<base64 32 bytes>
```

The stored token is self-describing — `OBTENC1:{keyId}:{base64(nonce|ciphertext|tag)}` — so the
format version (`OBTENC1`) and the key id travel with every value. This is what makes key rotation a
configuration change rather than a data migration; no payload change is needed to support future
rotation.

## Startup and health enforcement (operational safety)

- **Fail fast.** In every non-Development environment the API host calls
  `ValidateSensitiveDataProtectionOrThrow` immediately after build and before wiring the request
  pipeline. It validates the key set and performs a fixed non-sensitive encrypt/decrypt round-trip.
  A missing or invalid key set crashes startup — the instance never serves traffic. It never logs
  key material.
- **Never fabricated.** `AesGcmSensitiveDataProtector.Create` throws on missing/empty keys, an
  unknown or missing `ActiveKeyId`, non-base64 or wrong-length key bytes. It does **not** generate a
  temporary or default key under any circumstance.
- **Readiness signal.** The `sensitive-data-encryption` health check (tag `ready`, visible in
  `/health/ready` detail behind `X-Health-Token`) round-trips the same sentinel. It reports
  `Degraded` if encryption is not usable; it exposes no key id, key material, ciphertext or
  exception detail.

## Initial production key creation

1. On a trusted workstation (not a shared CI runner), generate a 32-byte key:
   ```
   openssl rand -base64 32
   ```
2. Choose a key id that sorts chronologically and carries no secrecy, e.g. `2026-09`.
3. Store the key id + value in the organisation password manager / secret vault entry
   "OneBigTeam — production sensitive-data encryption key(s)", with restricted access (see
   *Key recovery requirements*).
4. Set the Railway **production** service variables (below). Do the same, with a **different** key,
   for staging.
5. Deploy. Confirm the service reaches ready state and `/health/ready` detail shows
   `sensitive-data-encryption: Healthy`.
6. Record in the security records register: key id, creation date, creator, storage location,
   list of people with access. Never record the key value there.

Generate the key once per environment. Do not reuse a staging key in production or vice versa — a
production backup must not be decryptable with a non-production key.

## Railway secret configuration

Railway project → the relevant **environment** (staging / production) → the API service (and any
other service that resolves `ISensitiveDataProtector`; today only HR.Api) → **Variables**:

```
Infrastructure__SensitiveDataProtection__ActiveKeyId = 2026-09
Infrastructure__SensitiveDataProtection__Keys__2026-09 = <base64 32 bytes from step 1>
```

- Set them as service variables (or environment-shared variables scoped to that environment), not in
  any committed file and not in the repo-level `railway.json`.
- Railway variables are write-only in the UI after entry; treat a value you can no longer read as
  lost unless it is also in the vault.
- Changing `ActiveKeyId` or `Keys__*` triggers a redeploy. The new instance fails fast if the value
  is malformed, so a bad paste is caught before traffic shifts.
- Keep the number of people with Railway production access minimal; Railway access is effectively
  key access.

## Staging configuration

- Staging uses its **own** key id and value, generated the same way, stored in the same vault under a
  separate entry.
- Staging may be refreshed from a production backup for testing. Because the keys differ, encrypted
  columns will not decrypt in staging after such a refresh — this is expected. Either (a) accept that
  equality data reads back as unusable ciphertext in staging, or (b) run a one-off re-key pass in
  staging (decrypt-with-prod-key / encrypt-with-staging-key) only if a specific test needs readable
  values, and only with explicit approval, since it briefly requires the production key in staging.
- Never point staging at the production key to "make it work".

## Backup implications

- Supabase/Postgres backups contain **ciphertext only** for protected columns. The keys are not in
  the database and not in any backup.
- A backup is therefore useless for these fields without the separately-held key. This is the
  intended control and must be reflected in the DPA / security page statements about data at rest.
- Restores: after restoring an earlier database copy, reapply all still-valid customer-deletion
  obligations (`docs/runbooks/backup-and-disaster-recovery.md` §5.5) before returning to service.
  A restore does **not** need any key action as long as the key that encrypted those rows is still
  present in `Keys` (see *Key recovery requirements* and *Rotation*).
- Independent file-storage backups (Supabase Storage) are unaffected — no protected-column data is
  stored there.

## Key recovery requirements

**Losing an active key that has encrypted data = permanent, unrecoverable loss of that data.** There
is no escrow in the database, no derivation, no reset.

Required controls:

1. **Primary custody.** Every key id + value lives in the organisation secret vault / password
   manager, in a dedicated entry, access-restricted to named individuals (minimum two, to avoid a
   single point of failure) recorded in the security records register.
2. **Secondary copy.** Keep a second copy in a separate control — e.g. a sealed offline record in a
   safe, or a second vault in a different account. Test annually that it is readable and matches.
3. **Access review.** Review who can read the vault entry and who has Railway production access at
   least annually and on any leaver.
4. **Do not remove an old key id** from `Keys` until every stored value that references it has been
   re-encrypted under a newer key (see *Rotation* step 5). Removing it early strands those rows.
5. **Recovery drill.** As part of the backup/restore drill, confirm that the key needed to decrypt a
   restored dataset is retrievable from both the primary and secondary copy.
6. If a key is believed compromised, treat as a security incident
   (`docs/compliance/data-protection-operations.md` — Personal data breaches): rotate immediately
   (below), then re-encrypt at rest so the compromised key protects nothing, then retire it.

## Future key rotation approach

Rotation is a configuration change, not a data migration, because each stored value carries its own
key id.

1. Generate a new key and a new key id (e.g. `2027-03`). Add it to `Keys__2027-03` in **every**
   environment's secret store **alongside** the existing keys. Do not change `ActiveKeyId` yet.
   Deploy; confirm all instances are healthy (they now hold both keys).
2. Set `ActiveKeyId=2027-03`. Deploy. New writes use the new key; existing values keep decrypting
   via their embedded old key id.
3. Optional but recommended: run a background re-key pass — for each protected row, read the value
   (decrypts with the old key) and write it back (re-encrypts with the new active key). This can be
   done in batches; it needs only the running application, no special tooling, and both keys must be
   present throughout.
4. Verify no stored value still references the old key id (query for tokens beginning
   `OBTENC1:{oldKeyId}:`).
5. Only then remove the old `Keys__{oldKeyId}` entry from every environment, and delete/retire the
   old key value from the vault per the records-retention rules.

Routine rotation cadence: at least every 24 months, and immediately on suspected compromise or a
leaver who had key access.

## Operator actions outside the repository

- [ ] Generate distinct production and staging keys; store in vault + secondary copy.
- [ ] Set Railway variables for the production and staging API services.
- [ ] Record key metadata (not values) in the security records register.
- [ ] Add "sensitive-data encryption key present + `/health/ready` shows it Healthy" to the
      production launch gate sign-off.
- [ ] Schedule the annual key-access review and the recovery drill.
