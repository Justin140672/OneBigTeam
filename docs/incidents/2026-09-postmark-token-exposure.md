# Incident: Postmark server token committed to source control

- **Status:** Open — manual follow-up required
- **Severity:** P0 (secrets exposure)
- **Detected:** 2026-09-22, during a security review of the repository
- **Reported by:** Security review (see security/reliability ticket 1)

## Summary

A live-looking Postmark `ServerToken` was committed in plaintext to
`src/HR.Api/appsettings.json` (line 58, `Infrastructure:Postmark:ServerToken`).

## Exposure period

- **Introduced:** commit `e0b77ef8`, 28 Aug 2026 (per `git blame`)
- **Removed from HEAD:** 2026-09-22 (this incident's remediation), replaced with an
  empty string placeholder that must be populated from environment/secret-store
  configuration in every non-development environment.
- The value remains present in git history prior to this commit and in any forks,
  clones, CI logs, or artifacts that captured it during that window
  (28 Aug 2026 – 22 Sep 2026, ~25 days).

## What was exposed

- Postmark server token: `25370fe0-efa5-49cf-9104-8c1b27740f1a`
- This token grants API access to send transactional email (invitations, password
  resets, notifications) via the associated Postmark server, and to query that
  server's message/activity history depending on token scope.

## Remediation completed in this change

- Removed the committed token value from `src/HR.Api/appsettings.json`; the field is
  now an empty placeholder that must be supplied via environment variables or a
  secret manager in staging/production (see `Infrastructure__Postmark__ServerToken`
  or equivalent configuration provider).
- Searched the working tree for other credential-shaped committed values (API keys,
  tokens, connection strings with embedded passwords) in `*.json` / `*.config`
  files; none were found beyond the token above.
- Added a `secret-scan` job (gitleaks) to `.github/workflows/ci.yml`, required by the
  `ci-success` gate, so any future secret committed to the repository fails CI before
  it can merge to `main`.

## Manual follow-up required (cannot be performed by this change)

The following actions require Postmark account access and/or destructive git
operations, and must be performed by a human with the appropriate access:

1. **Revoke/rotate the exposed Postmark server token** in the Postmark dashboard and
   issue a new token, deployed only via environment/secret-store configuration.
2. **Review Postmark account activity** for the exposed server (message sends, API
   calls, bounce/complaint activity, any added webhooks or servers) for the full
   exposure window (28 Aug 2026 – 22 Sep 2026) to confirm no unauthorized use.
3. **Rewrite git history** (e.g. `git filter-repo` / BFG) to strip the token from all
   historical commits, then force-push and have all clones/forks re-clone. This is a
   destructive operation and must only be done with explicit user sign-off and
   coordination with anyone else who has a clone of the repository.
4. Confirm the new token is present in the Railway (or other host) environment
   configuration for every environment that sends transactional email before the
   next deploy, since the committed placeholder is intentionally empty.
