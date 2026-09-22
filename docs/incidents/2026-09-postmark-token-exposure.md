# Incident: Postmark server token committed to source control

- **Status:** Open — token contained; activity audit and CI scan evidence pending
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

## Operator actions and remaining follow-up

The Postmark account actions are owned by Justin Etherington. **Do not paste either
the old or replacement token into this file, a commit message, a ticket, or chat** —
use the incident identifier `PM-TOKEN-2026-09-INC1` to refer to it.

1. **Revoke the exposed token — completed 22 Sep 2026.**
   - The repository owner confirmed that the affected Postmark server token was
     refreshed in the Postmark dashboard. Refreshing it invalidated the exposed
     value and issued a replacement.
   - The exact dashboard timestamp was not copied into the repository; the owner's
     confirmation on 22 Sep 2026 is the retained revocation evidence.
2. **Configure the replacement through a secret store — completed for the current
   local-only environment.**
   - The replacement is stored in the `HR.Api` .NET user-secrets store under
     `Infrastructure:Postmark:ServerToken` and is not present in a tracked file.
   - Staging and production do not exist yet, so there are currently no deployed
     environments requiring configuration. When either environment is created, set
     `Infrastructure__Postmark__ServerToken` in its managed secret store before
     enabling transactional email; never copy it into `appsettings*.json`.
   - A local transactional-email test remains advisable but is not required to prove
     that the exposed value was revoked.
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
4. Retain rotation evidence without recording token values: owner confirmation is
   recorded above, and staging/production deployment is not applicable because those
   environments do not yet exist.

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

- The token was **revoked on 22 Sep 2026** (step 1 above), which invalidates the credential
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
  `ci-success` gate. As of this change it runs with `GITLEAKS_BASELINE_PATH:
  .gitleaks-baseline.json`, a narrow baseline containing **only** the two exact
  fingerprints for the revoked `PM-TOKEN-2026-09-INC1` value (the redacted incident
  report line and the original `src/HR.Api/appsettings.json` commit) — see
  `.gitleaks-baseline.json`.
- **Local verification (2026-09-22), full history (`--log-opts="--all"`, 652 commits
  scanned):**
  - Without the baseline: gitleaks reports **7 findings** — the 2 Postmark
    fingerprints above, plus 5 unrelated pre-existing findings in test fixture files
    (fake JWTs / test idempotency keys in `HR.Modules.Recruitment.Tests` and
    `HR.Modules.Identity.Tests`) that are out of scope for this incident and are
    **not** suppressed by the baseline.
  - With the baseline applied: gitleaks reports exactly the same **5** unrelated
    findings and exit code 1 (still fails) — confirming the baseline suppresses only
    the 2 known/revoked occurrences and would **not** mask a new secret, a
    regression of the same value in a new commit, or any other historical finding.
  - Run via `docker run zricethezav/gitleaks:latest detect --source=/repo
    --log-opts="--all" --baseline-path=.gitleaks-baseline.json`, since this session
    cannot trigger a real GitHub Actions run without pushing.
- [ ] **Outstanding:** link the first real `secret-scan` CI run against this branch
  once it is pushed (the workflow change is currently uncommitted). Expected result,
  based on the local verification above: fails on the 5 unrelated pre-existing
  findings — those are real (if low-severity/test-fixture) secret-scan gaps outside
  this incident's scope and should be triaged as their own follow-up, not folded into
  this closure.

## Closure checklist

- [x] **Owner:** Justin Etherington
- [x] **Token revoked:** refreshed in Postmark on 22 Sep 2026; owner confirmed
- [x] **Replacement configured via secret store:** HR.Api local user secrets; staging
  and production do not yet exist and are therefore not applicable
- [ ] **Activity review result:** _(clean / anomalies — link findings)_ — **owner
  action required**; this cannot be performed by an assistant without Postmark
  dashboard access. See "Operator actions" step 3 above.
- [x] **Secret scan evidence attached** (see above) — local full-history
  verification complete; real CI run pending a push of this branch.
- [ ] **Completion date:**
- **Status:** remains **Open** — blocked only on the Postmark Activity-tab audit for
  28 Aug–22 Sep 2026 (step 3 above). Everything else in this incident is complete.

## Local build/worktree hygiene note (2026-09-22)

The `src/HR.Api/bin/verify/`, `.claude-scratch-fullbuild/`, `src/HR.Api/.codex-build/`,
and `tests/HR.Integration.Tests/.codex-build/` stale build-output copies noted above
have been deleted.

The `.claude/worktrees/nice-greider-120806/` copy noted above, and nine further
worktrees discovered under `.claude/worktrees/` on this machine, were **not**
deleted — several appear to belong to other active Claude Code sessions working
against this repository concurrently, and deleting their working trees would risk
destroying unrelated in-progress work without authorization. This is local-only,
gitignored, unreachable exposure (per the "Residual local exposure" section above)
and is now inert since the token is revoked; clean up any worktree directories under
`.claude/worktrees/` yourself once you've confirmed each one's owning session has
finished with it.
