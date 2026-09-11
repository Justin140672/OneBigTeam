# [P1] Make position-role revocation recoverable when integration-event handling fails

## Problem and impact
Changing an employee's position commits the Employees record before publishing the event that expires old Identity role assignments. The integration-event publisher catches every consumer exception, logs it, and returns success without a durable retry. A transient Identity failure can therefore leave the old position's privileges active indefinitely after HR believes the transfer succeeded.

Startup reconciliation is additive-only: it creates the new position assignment but does not remove the stale old one. Consequently a restart is not a reliable security repair. There is also an expired-row edge case: reconciliation inserts a new composite-key row when an existing matching assignment has expired, rather than reopening it.

## Evidence
- [src/Modules/HR.Modules.Employees/Features/UpdateEmploymentDetails/Handler.cs:232](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Employees/Features/UpdateEmploymentDetails/Handler.cs#L232) saves the employee before publishing the position event at line 260.
- [src/Shared/HR.SharedKernel/IntegrationEventPublisher.cs:23](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Shared/HR.SharedKernel/IntegrationEventPublisher.cs#L23) swallows consumer failures.
- [src/Modules/HR.Modules.Identity/Features/OnEmployeePositionChanged/Handler.cs:36](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Identity/Features/OnEmployeePositionChanged/Handler.cs#L36) is responsible for expiring the old grant.
- `IdentityAuthorizationService.GetEffectiveRolesAsync` trusts active `UserPositions` rather than the authoritative employee position.
- [src/Modules/HR.Modules.Identity/IdentityModule.cs:378](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Identity/IdentityModule.cs#L378) reconciles missing active assignments only and never revokes stale ones.

## Reproduction / verification
Assign a privileged position A, transfer the employee to unprivileged B, and inject a consumer failure before Identity persists the expiry. The employee change remains committed and the publisher reports success. The old A role remains effective; restarting adds B but leaves A active. The executed generic production-publisher probe confirmed a throwing consumer returns apparent success after one call. The full role-transfer failure-injection scenario was verified by code tracing, not run against PostgreSQL.

## Suggested fix
Use a narrowly scoped durable delivery/reconciliation mechanism for security-relevant role transitions. Preserve handler isolation while recording failed delivery and retrying it idempotently. Reconcile derived assignments against the current authoritative position, while respecting deliberate direct roles/overrides. Do not require a universal outbox solely for architectural consistency.

## Acceptance criteria
- [ ] Failed revocation cannot be silently acknowledged as fully applied; stale authority is blocked or the transition remains explicitly pending with safe access behavior.
- [ ] Recovery eventually removes old derived roles and grants the current position's roles exactly once.
- [ ] Duplicate/out-of-order events converge to the current authoritative assignment.
- [ ] Reconciliation repairs stale grants and handles existing expired composite-key rows without startup failure.
- [ ] Direct roles and intentional overrides survive correctly; cross-tenant changes are rejected.
- [ ] Add failure-injection and restart tests, and provide repair guidance for existing mismatches.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

