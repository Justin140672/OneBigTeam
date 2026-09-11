# [P1] Prevent concurrent leave approvals and cancellations from losing balance updates

## Problem and impact
Leave requests, balances, and TOIL consumption use read-modify-write operations without concurrency tokens or locking over the reads. EF saves a computed absolute `UsedDays` value. Two approvals can both read the same balance, approve distinct requests, and overwrite one another's deductions. Competing approval/rejection/cancellation can also commit an outcome inconsistent with the balance. The shared version interceptor does not protect these entities because they do not implement the versioned aggregate contract.

## Evidence
- [src/Modules/HR.Modules.Leave/Domain/LeaveBalance.cs:88](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Leave/Domain/LeaveBalance.cs#L88) increments the tracked in-memory value.
- `Persistence/Configurations/LeaveBalanceConfiguration.cs` and `LeaveRequestConfiguration.cs` contain no concurrency token.
- `Features/ApproveLeaveRequest/Handler.cs` loads pending state, applies effects, then saves without a lock or conditional transition.
- `Services/ToilLedgerService.cs` computes available buckets before staging consumption.

## Reproduction / observed result
Executed actual approval handlers using separate EF InMemory contexts. Preload the same balance in both contexts before either approval (a deterministic interleaving of the concurrent-read case), then approve two distinct five-day requests. With 20 days entitlement, the result is **10 days approved, UsedDays=5, RemainingDays=15**. Expected usage is 10 and remaining is 10.

This demonstrates the lost-update logic with the current mappings; real PostgreSQL concurrency has not been exercised and is required for the fix's validation.

## Suggested fix
Protect both request state transitions and the shared employee/type/year balance, using database concurrency tokens with bounded reload/revalidation or a transaction that locks the relevant rows before reading. TOIL additionally needs protection against concurrent bucket consumption. Merely placing SaveChanges in a transaction does not protect earlier reads.

## Acceptance criteria
- [ ] PostgreSQL tests with two contexts/barriers prove distinct concurrent approvals preserve both deductions.
- [ ] Approve vs reject/cancel on one request yields one consistent final state and matching balance.
- [ ] Concurrent adjustment, cancellation, and approval do not lose deltas.
- [ ] TOIL cannot spend the same award twice when negative balances are forbidden.
- [ ] Conflicts are safely retried with fresh business validation or surfaced as an actionable conflict, never a silent overwrite.
- [ ] Audit and notification side effects correspond only to committed outcomes.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

