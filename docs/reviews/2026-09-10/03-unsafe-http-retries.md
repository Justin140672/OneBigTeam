# [P1] Stop automatic retries from duplicating non-idempotent business mutations

## Problem and impact
The shared HTTP resilience configuration retries failed requests without restricting methods. All web-to-API mutations inherit it. Several handlers commit a change before publishing an audit/event; if that later operation fails, the HTTP request returns an error even though the change is durable. The client then repeats the entire mutation.

For example, an adjustment of +2 days can be applied twice, or an automatically numbered asset can be created twice. A lost response or timeout after commit has the same ambiguity. The configured maximum is four retries after the original attempt.

## Evidence
- [src/HR.ServiceDefaults/Extensions.cs:41](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/HR.ServiceDefaults/Extensions.cs#L41) installs the shared resilience handler and sets `MaxRetryAttempts=4`, with no unsafe-method exclusion.
- [src/Modules/HR.Modules.Leave/Features/AdjustLeaveBalance/Handler.cs:93](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Leave/Features/AdjustLeaveBalance/Handler.cs#L93) adds a delta, commits at lines 111–112, then audits at line 116. There is no request idempotency key.
- [src/Modules/HR.Modules.Assets/Features/CreateAsset/Handler.cs:89](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Assets/Features/CreateAsset/Handler.cs#L89) saves a fresh generated entity before auditing.
- `src/HR.Web/Services/LeaveService.cs:172` sends adjustments through this client using POST.

## Reproduction / observed result
An executed probe used the production `AddServiceDefaults` configuration with a synthetic transport returning 500 after a simulated commit and then 200. One POST caused **two transport sends**, with success returned to the caller. This confirms actual configured retry behavior; duplicating a live business record was not attempted.

## Suggested fix
Disable automatic retries for unsafe mutations by default. Explicitly opt in only where an endpoint has durable idempotency or an otherwise proven replay-safe contract. For operations needing retries, persist a caller-supplied idempotency key and original result atomically with the mutation, and replay that result on duplicate delivery. Also make post-commit audit/notification delivery recoverable.

## Acceptance criteria
- [ ] Non-idempotent POST/PATCH operations are not automatically resent after 5xx, disconnect, or timeout.
- [ ] Any opted-in mutation deduplicates concurrent and sequential deliveries durably, across process restarts.
- [ ] Inject a failure after commit: one adjustment changes the balance once; one automatic asset creation creates one asset.
- [ ] Safe reads retain appropriate retries.
- [ ] Audit/notification failures do not silently cause repeated business changes.
- [ ] Verify both web apps and other callers inheriting the shared defaults.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

