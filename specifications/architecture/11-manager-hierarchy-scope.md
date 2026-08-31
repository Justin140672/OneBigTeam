# 11. Manager Hierarchy Scope for Dashboard Data

## Status

Accepted — DSH-02 (follows DSH-01, which secured team-task retrieval and deferred the
"single hierarchy rule" to this document).

## Problem

Manager-facing dashboard widgets and the Workload & HR Actions report each decided
"who is on my team" independently. Some queries used **direct reports only**
(`IDirectReportsReader.GetDirectReportIdsAsync`), others used the **full reporting
sub-tree** (`GetAllDescendantIdsAsync`), and a few trusted a browser-supplied
`{managerId}` route value. A skip-level manager could therefore see a probation
review in one widget but not the matching count in another, and drill-down lists
did not match their headline counts.

## The rule (defined once)

For every manager-facing dashboard / attention-queue / "my team" query:

1. **Identity comes from the authenticated principal.** The acting manager is
   `ICurrentUser.UserId`. A `{managerId}` value in the route or request body is only
   ever a *target selector* and must itself be authorized against the principal
   (the caller must **be** that manager, be **above** that manager in the reporting
   tree, or hold a company-wide grant). Scope is never widened by anything the
   browser sends. No `includeIndirect` / `scope` / `hierarchy` flag is accepted
   from a request whose identity is a route value.

2. **A manager's team is their entire reporting sub-tree.** All direct reports,
   their reports, and so on to any depth — resolved by
   `IDirectReportsReader.GetAllDescendantIdsAsync`. Direct-reports-only scoping is
   not used for dashboard data.

3. **Company-wide roles see the whole company.** HR Administrator (and equivalents
   each module already recognises) bypass sub-tree filtering entirely rather than
   materialising the company as an id set.

4. **Counts and drill-downs use the same population.** A widget's headline number
   and the list behind it are produced from the same id set / same query predicate.

5. **Loops and manager changes are handled by the resolver, not the caller.**
   `GetAllDescendantIdsAsync` walks the live `(id, manager_id)` projection with a
   BFS and a `visited` set, so a cycle (A manages B manages A) terminates and each
   employee is yielded at most once. Because the projection is read fresh on every
   call, a re-parented employee moves between managers' scopes immediately with no
   cache to invalidate.

## The shared service

`IDirectReportsReader` (owned by `HR.Modules.Employees`, exposed as a cross-module
contract) **is** the authorized-hierarchy service. It already provides tree
closure. `HR.SharedKernel.Authorization.EmployeeResourceAuthorizer` composes it
with each module's company-wide check to answer single-resource questions
(`CanAccessAsync`) and to produce an allow-list (`GetManagedEmployeeIdsAsync`).
DSH-02 does **not** add a competing reader; it makes every dashboard query use
`GetAllDescendantIdsAsync` (directly, or via a module `*ResourceAuthorizer` that
already wires it).

## Applied to

| Area | Endpoint / provider | Before | After |
|---|---|---|---|
| Team tasks | `Tasks/GetTeamTasks` handler | direct | full sub-tree |
| Team tasks (workload) | `ManagerTasksOverdueWorkloadActionProvider` | direct | full sub-tree |
| Leave requests | `Leave/GetRecentLeaveRequests` handler (non-HR) | direct | full sub-tree |
| Leave requests (workload) | `LeavePendingApprovalsWorkloadActionProvider` | direct | full sub-tree |
| Probation reviews (workload) | `ProbationReviewsDue` / `OverdueProbationReviews` providers | direct | full sub-tree |
| Probation reviews (widget) | `Probation/GetUpcomingProbationReviews` | full sub-tree | unchanged |
| Onboarding | `Onboarding/GetTeamOnboarding` handler | direct | full sub-tree |
| Onboarding (workload) | `OutstandingOnboardingTasksWorkloadActionProvider` | direct | full sub-tree |
| Sickness team today | `Sickness/GetTeamSicknessToday` handler | direct | full sub-tree |
| Sickness RTW / missing fit notes | `Sickness/GetOverdueReturnToWorkReviews`, `GetMissingFitNotes` | full sub-tree | unchanged |
| Team reports | `Reporting/GetLeaveSummaryReport`, `GetProbationReport`, `GetOnboardingProgressReport` | full sub-tree | unchanged |

Authorization of the browser-supplied `{managerId}` was also tightened:
`GetTeamSicknessToday` and `GetTeamOnboarding` previously accepted (or barely
checked) that value. They now derive the caller from `ICurrentUser` and authorize
via a module `*ResourceAuthorizer` (self / manager-above / company-wide), matching
`GetTeamTasks` (fixed in DSH-01).

## Intentional exceptions (documented per DSH-02)

- **`EmployeeTasksOverdueWorkloadActionProvider`** ("Employee Tasks Overdue") is
  scoped to the caller's *own* tasks only. It is a self-service category, not a
  team view, so it has no hierarchy at all.
- **`Employees/GetMyTeam`** keeps its `IncludeIndirect` request flag. Its identity
  is the principal (`me`), the flag only toggles a roster widget between "direct
  reports" and "whole team" for display, and neither setting can reach beyond the
  caller's own sub-tree — so it cannot be used to widen scope. It is a roster, not
  a dashboard count or drill-down.
- **HR-only workload providers** (documents, identity, assets, recruitment,
  offboarding, employee start/leave dates, sickness pending actions) have no
  manager tier by design and are unchanged.
