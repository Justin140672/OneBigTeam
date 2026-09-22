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

- Postmark server token, incident identifier `PM-TOKEN-2026-09-INC1` (value redacted;
  see rotation evidence below — do not reconstruct or re-paste the raw value here or
  anywhere else in the repository).
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

The following actions require Postmark account access and must be performed by a
human with the appropriate access (Justin Etherington). **Do not paste the raw token
value into this file, a commit message, a ticket, or chat when completing these
steps** — use the incident identifier `PM-TOKEN-2026-09-INC1` to refer to it.

1. **Revoke the exposed token.**
   - Log in to the Postmark account at https://account.postmarkapp.com.
   - Open the server that owned `Infrastructure:Postmark:ServerToken` (the server used
     for HR.Api transactional email — invitations, password resets, notifications).
   - Go to **Servers → [server] → API Tokens**.
   - Locate the token matching the last-known committed value (ending `...27740f1a`)
     and click **Regenerate**/**Revoke**. This immediately invalidates the exposed
     token; Postmark issues a new one in its place.
2. **Deploy the replacement token via the environment secret store only.**
   - Set the new token as `Infrastructure__Postmark__ServerToken` in Railway's
     environment variables for every service that sends transactional email
     (HR.Api at minimum; check HR.Admin.Api once it exists — see
     [[project_hr_deployment_architecture]] in engineering memory).
   - Do not place the new value in `appsettings.json`, `appsettings.*.json`, user
     secrets committed to the repo, or any file tracked by git.
   - Confirm via a test send (e.g. a password-reset or invitation in
     staging/production) that transactional email works with the new token before
     considering this step complete.
3. **Audit Postmark activity for the exposure window (28 Aug 2026 – 22 Sep 2026).**
   - In the Postmark dashboard, open **Activity** for the affected server and filter
     to that date range.
   - Review: outbound message volume vs. expected baseline, any bounces/spam
     complaints outside normal pattern, any messages to unfamiliar/unexpected
     recipient domains.
   - Under **Servers → [server] → Settings**, check for any webhooks, API tokens, or
     sender signatures added during the window that you did not add yourself.
   - Under **Account → API Tokens** (account level), check for any new account-level
     tokens created during the window.
   - Record the outcome below (clean / anomalies found — link findings).
4. Record rotation evidence below: new token creation timestamp (from Postmark's
   token audit log) and confirmation it's deployed in every required environment.

## Residual local (untracked) exposure — no repo/history action needed

A repo-wide search for the raw token value found no occurrences in git-tracked
files. It does still appear in several **gitignored, untracked, local-only** paths
on the developer machine where this incident was investigated:

- A build-output copy under `src/HR.Api/bin/verify/` and per-target `Debug`/`Release`
  build directories (`.claude-scratch-fullbuild/`, `src/HR.Api/.codex-build/`,
  `tests/HR.Integration.Tests/.codex-build/`) — stale compiled `appsettings.json`
  copies from prior local builds, all matched by `.gitignore` (`[Bb]in/`,
  `.claude-scratch*/`).
- A checked-out copy under `.claude/worktrees/nice-greider-120806/src/HR.Api/appsettings.json`
  — a separate git worktree excluded via `.git/info/exclude`, not part of the
  primary working tree or any branch history distinct from what's already in `main`.

None of these are committed, pushed, or reachable by anyone without access to this
specific machine, so they are not part of the repository's exposure surface and
don't require a history rewrite or remote coordination. They should still be
deleted or rebuilt locally as routine hygiene once the token is revoked (step 1
above) — after revocation, the value in these files is inert. No separate tracking
item is needed beyond this note.

## Containment plan approved in lieu of a git history rewrite

A full history rewrite (`git filter-repo` / BFG + force-push + mandatory re-clone by
all forks/clones) was considered and **rejected** as the containment mechanism, given
its blast radius on a shared repository. Approved alternative, agreed with the repo
owner:

- The token is being **revoked** (step 1 above), which invalidates the credential
  regardless of how many copies of it exist in history, forks, clones, CI logs, or
  build artifacts. A revoked token has no exploitable value, so scrubbing historical
  commits provides no additional security benefit once revocation is confirmed.
- The current tree (as of this change) no longer contains the raw value anywhere,
  including this incident report — only the non-reconstructible incident identifier
  `PM-TOKEN-2026-09-INC1` is used.
- CI secret scanning (gitleaks, added to `.github/workflows/ci.yml` as part of this
  change) covers the full history on scan runs and the current tree on every PR, so
  regressions and any other pre-existing historical exposures are now detected.
- If a future audit determines historical exposure of *this specific* token caused
  concrete harm, or if a different, still-active secret is found in history, this
  decision should be revisited and a rewrite scheduled as its own coordinated,
  user-approved operation (not folded into routine remediation).
- This plan explicitly covers: forks (none known to exist outside the primary
  remote), clones (developer machines — no action needed since revocation makes the
  historical value inert), CI logs (Postmark token is not printed in CI; verified no
  workflow echoes `Infrastructure__Postmark__ServerToken`), and build artifacts (token
  is runtime configuration, not baked into build output).

## Secret scanning evidence

- CI job: `secret-scan` (gitleaks) in `.github/workflows/ci.yml`, required by the
  `ci-success` gate.
- [ ] Attach/link the first clean (or triaged) run of this job against the full
  history and current tree here once it has executed, as evidence for closure.

## Closure checklist

- [ ] **Owner:** Justin Etherington
- [ ] **Token revoked:** _(timestamp, from Postmark)_
- [ ] **Replacement deployed via env/secret store:** _(environments confirmed)_
- [ ] **Activity review result:** _(clean / anomalies — link findings)_
- [ ] **Secret scan evidence attached** (see above)
- [ ] **Completion date:**
- **Status:** remains **Open** until every box above is checked.
