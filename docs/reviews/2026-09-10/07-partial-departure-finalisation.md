# [P1] Make departure finalisation resumable after partial commits and downstream failures

## Problem and impact
The departure finaliser marks the employee FormerEmployee and completes the leaving process before invoking the manager-reassignment helper. For a manager with reports, that helper calls SaveChanges on the shared Employees context, prematurely persisting those final statuses before access settings and offboarding state are resolved.

If a subsequent dependency fails, a Hangfire retry does not resume the employee because the scanning job selects only employees still marked Leaving. Even without reports, a notification failure after the main save skips the departure integration event and timeline permanently. Missing events can leave leave-policy assignments active and offboarding unreconciled.

## Evidence
- [src/Modules/HR.Modules.Employees/Services/EmployeeDepartureFinalizer.cs:33](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Employees/Services/EmployeeDepartureFinalizer.cs#L33) stages terminal statuses, then calls the cascade helper at line 43.
- [src/Modules/HR.Modules.Employees/Services/EmployeeDepartureFinalizer.cs:154](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Employees/Services/EmployeeDepartureFinalizer.cs#L154) saves the entire shared context inside that helper.
- Settings/offboarding lookups occur at lines 46–53; main save is line 56; notification precedes the departure event.
- [src/Modules/HR.Modules.Employees/Jobs/ProcessLeavingEmployeesJob.cs:30](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Employees/Jobs/ProcessLeavingEmployeesJob.cs#L30) selects only Leaving employees.
- `LeaveYearRolloverService` relies on departure-event-driven policy deactivation to exclude departed employees.

## Reproduction
1. Create a due leaver with at least one direct report and an in-progress leaving process.
2. Inject a failure in the leaving-settings or offboarding-status reader after manager cascade has saved.
3. Reload: the employee is already FormerEmployee and the process Completed, although required finalisation work did not finish.
4. Retry the daily job: that employee is not selected.
5. Also test a non-manager with a failing notification writer after the main commit: audit/event/timeline work is skipped and not resumed.
This is a confirmed static control-flow defect; these failure paths were not executed against a live database.

## Suggested fix
Stage same-database changes and commit the core departure transition atomically after prerequisites succeed. Persist explicit completion/recovery work for cross-module effects and notifications, and let a recovery job resume incomplete steps regardless of the employment status. Isolate one employee's failure so other due departures can progress. Coordinate with the separate Identity-disablement ticket, which addresses the wrong account flag even on successful finalisation.

## Acceptance criteria
- [ ] No helper persists terminal state before required core steps are ready.
- [ ] Failure injection before and after every persistence/delivery boundary converges on a fully completed departure after retry.
- [ ] Access removal, leave-policy deactivation, offboarding reconciliation, audit, notifications, and timeline are recoverable and idempotent.
- [ ] A completed employment status never hides unfinished finalisation work from recovery.
- [ ] One employee's failure does not indefinitely block other due employees.
- [ ] Add PostgreSQL failure-injection tests and a reconciliation path for previously stranded departures.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

