# [P1] Disable the actual Identity account at departure independently of offboarding completion

## Problem and impact
Departure finalisation sets `Employee.HasSystemAccess=false` and records `AccessDisabled=true`, but API authentication enforces `ApplicationUser.IsActive`. Finalisation does not change that account flag, and Identity has no consumer for `EmployeeDepartureFinalisedIntegrationEvent`. The automatic deactivation instead happens when an offboarding plan completes.

A former employee with unfinished offboarding can therefore retain API access and log in again despite the departure audit claiming access was disabled. Conversely completing offboarding early deactivates the account without checking the leaving date or the automatic-disable setting.

## Evidence
- [src/Modules/HR.Modules.Employees/Services/EmployeeDepartureFinalizer.cs:46](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Employees/Services/EmployeeDepartureFinalizer.cs#L46) only updates the Employees flag.
- [src/Modules/HR.Modules.Identity/DisabledAccountMiddleware.cs:61](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Identity/DisabledAccountMiddleware.cs#L61) enforces the separate Identity flag.
- [src/Modules/HR.Modules.Identity/Features/OnOffboardingPlanCompleted/Handler.cs:19](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Identity/Features/OnOffboardingPlanCompleted/Handler.cs#L19) unconditionally deactivates on plan completion.
- `IdentityModule.TryDevSignInAsync` / `Features/Login/Handler.cs` also use Identity account activity.
- `specifications/product-specifications/34-leaving-offboarding.md` assigns product-account disablement to Identity at finalisation when configured; offboarding completion must not control departure.

## Reproduction
1. Create an employee with an active application account and a valid authenticated session.
2. Enable automatic disablement at the leaving date; leave at least one offboarding obligation incomplete.
3. Finalise departure through the normal job or confirmed backdated-departure path.
4. Verify that Employees reports disabled access but Identity still stores an active account; exercise an authorised API request and fresh login.
5. Separately complete a plan before departure and check that the account is deactivated prematurely.

These conclusions are from traced production code. A cross-module authenticated end-to-end reproduction was not run; current finaliser unit tests only assert the Employees flag and audit payload.

## Suggested fix
Make access removal an explicit, durable Identity action tied to the authoritative departure decision and company policy. Record success only after the account used by authentication is actually disabled. Decouple operational checklist completion from account lifecycle and provide safe reconciliation of already-finalised employees.

## Acceptance criteria
- [ ] Departure with auto-disable enabled disables login and existing-token company requests even with incomplete offboarding.
- [ ] Completing offboarding early does not remove access before the configured lifecycle trigger.
- [ ] Auto-disable=false and explicit manual disablement follow the documented policy.
- [ ] Retries are idempotent; failed Identity updates remain recoverable and visible.
- [ ] Audit/reporting distinguishes requested disablement from confirmed disablement.
- [ ] Add cross-module authenticated tests and reconcile existing inconsistent records.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

