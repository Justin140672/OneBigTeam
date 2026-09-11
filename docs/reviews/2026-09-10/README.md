# Significant repository review — 10 September 2026

Review began on 10 September and completed after midnight on 11 September (Europe/London).

## Outcome

Eight actionable tickets are saved alongside this index. No application code was changed by this review. GitHub searches returned no matching issues, but the subsequent GitHub create-issue request was declined; **no GitHub issues were created**. The Markdown files are the complete tickets, ready for implementation or later publication.

Prioritise authentication isolation and departure account enforcement first. P1 means high-priority security/data-integrity/reliability work; P2 means a significant business-rule defect to schedule next. These are recommendations, not an assertion that production has already experienced an incident.

| Priority | Ticket | Main impact | Verification |
|---|---|---|---|
| P1 | [Isolate authentication tokens](01-shared-authentication-state.md) | Requests can run as another visitor, including a previous platform administrator | Production-source HTTP factory probe reproduced both apps |
| P1 | [Enforce departure account disablement](02-departure-account-access.md) | Former employee keeps an active Identity account; early checklist completion can disable access prematurely | Production flow and tests inspected; full authenticated scenario pending |
| P1 | [Prevent unsafe mutation retries](03-unsafe-http-retries.md) | One action can apply twice after an error following commit | Actual shared HTTP pipeline repeated a POST |
| P1 | [Protect concurrent leave writes](04-leave-concurrent-writes.md) | Ten approved days can be recorded as only five used | Actual handlers with stale EF InMemory snapshots reproduced loss |
| P2 | [Recheck leave at approval](05-leave-approval-overdraft.md) | Sequential approvals exceed balance despite a no-negative policy | Actual approval handlers produced minus five days |
| P1 | [Recover position-role revocations](06-lost-security-events.md) | Failed event leaves old privileged roles effective | Publisher failure behavior reproduced; role-recovery path traced |
| P1 | [Resume partial departure finalisation](07-partial-departure-finalisation.md) | Terminal status hides unfinished access/workflow work from retries | Shared-context save and job selection traced |
| P1 | [Confine import storage paths](08-import-path-traversal.md) | Client filename escapes tenant/storage directories | Actual validator and storage resolver reproduced escape; no file written |

## Scope and evidence

The review examined the API pipeline; both web apps' authentication/HTTP-client lifetimes; Identity role projection and account enforcement; leave submission, approval, cancellation, adjustments and TOIL; employee departure and offboarding interactions; import validation/storage and confirmation; document authorization/upload paths; representative reporting/export and asset paths; Stripe webhook processing; CI configuration; product decisions and existing local tickets. This was a risk-focused cross-repository review, not a line-by-line audit of every module.

Base commit: `db86f446bd504dba167b8e8dc3c43aa388097998`. The working tree contained substantial unrelated recruitment/UI work and continued changing during the review. Findings refer to the inspected source; the review did not edit or revert that work. Ticket source links use the base commit for stable navigation; where the file is being edited concurrently, compare with the current working copy before implementing.

The checks use synthetic tokens and records, an in-memory HTTP transport, and EF InMemory. They make no API/network requests and do not inspect live customer data. Their small host object is never started as an application. Package restoration used the existing local NuGet cache; vulnerability auditing was disabled only for this offline probe restore, not changed in repository configuration. Restore/build produced existing NU1510 package-pruning warnings. An initial sandbox run was interrupted by Windows event-log permissions; the final run with normal permissions completed successfully.

See [probe instructions and captured observations](probes/README.md). The final probe process exited 0 because it deliberately asserts the observed defects, **not** because the application meets the desired acceptance criteria.

## Limits and implementation guidance

- Full solution, full unit/integration suites, PostgreSQL concurrency tests and browser E2E tests were not run. Each ticket names the remaining verification needed.
- The approval balance and concurrent-write tickets are independent: serialisation alone does not prevent sequential over-approval, and a balance check alone does not prevent lost updates.
- The departure access and partial-finalisation tickets are independent: fixing the wrong account flag does not make partial commits recoverable.
- Durable delivery is recommended for the concrete security/lifecycle failures found, consistent with the product decision that a universal outbox is not mandatory.
- No cosmetic, naming, speculative dependency, or deferred-product-feature tickets were added.

