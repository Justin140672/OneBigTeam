# [P2] Recheck available annual leave when approving pending requests

## Problem and impact
The no-negative-balance policy is enforced when submitting a request, but pending requests do not reserve balance and manual approval never rechecks the policy. Multiple individually valid pending requests can therefore all be approved sequentially and push the balance below zero. This occurs without any concurrency and is independent of the lost-update defect.

## Evidence
- [src/Modules/HR.Modules.Leave/Features/SubmitLeaveRequest/Handler.cs:103](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Leave/Features/SubmitLeaveRequest/Handler.cs#L103) checks available accrued days using `UsedDays`, which excludes pending requests.
- [src/Modules/HR.Modules.Leave/Services/LeaveApprovalEffectsService.cs:71](https://github.com/Justin140672/OneBigTeam/blob/db86f446bd504dba167b8e8dc3c43aa388097998/src/Modules/HR.Modules.Leave/Services/LeaveApprovalEffectsService.cs#L71) only checks that a balance row exists before unconditionally recording usage at line 76.
- `ApproveLeaveRequestHandler` never loads `AllowNegativeBalance` or revalidates accrued availability. TOIL has a separate sufficiency check; this finding concerns standard balance-tracked leave.

## Reproduction / observed result
1. Use an annual-leave policy with `AllowNegativeBalance=false` and five available days.
2. Submit two non-overlapping five-day requests before either is approved. Each submission sees five available days.
3. Approve the first, then the second.
An executed probe with actual approval handlers and two pending requests produced **10 approved/used days and RemainingDays=-5**; both approvals succeeded.

## Suggested fix
At approval, evaluate available accrued balance and applicable negative-balance policy using the same rules as preview/submission. Reject an approval that no longer fits, or define an explicit authorised and audited override. Combine with concurrency protection so two approvals cannot both pass the new check against stale data. Do not block legitimate negative balances created by separately authorised corrections/proration.

## Acceptance criteria
- [ ] The second sequential approval in the reproduction fails without mutating request, balance, or side effects.
- [ ] The reviewer receives an actionable insufficient-balance explanation.
- [ ] Monthly/fortnightly accrual and current adjustments are included consistently.
- [ ] Policies permitting negative balances still work; non-balance leave and TOIL retain their intended behavior.
- [ ] Cover both manual approval and policy-driven auto-approval, plus changed balances between submission and review.
- [ ] Validate the new rule atomically with the approval transaction.

## Review context
Found during the 10 September 2026 repository review, based on commit `db86f446bd504dba167b8e8dc3c43aa388097998` and the current working tree. Existing unrelated work was preserved. Reproduction probes are saved locally in `docs/reviews/2026-09-10/probes/`; they have not been pushed. No production data or services were used.

