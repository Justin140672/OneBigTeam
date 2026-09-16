# Onboarding & Offboarding Lifecycle — Focused Quality and Specification Audit

**Audit date:** 2026-07-11
**Reviewed revision:** `bbac33a` plus the pre-existing uncommitted working tree (the entire `HR.Modules.Offboarding` module, its tests, its architecture-test coverage, and several onboarding-adjacent files are currently untracked/modified — see Limitations).
**Method:** Read-only review of source, tests, migrations, and specifications for the Onboarding and Offboarding lifecycles. Architecture, Onboarding-module and Offboarding-module unit tests were executed read-only; `HR.Integration.Tests` and `HR.Web.E2E.Tests` were read but **not executed**, per standing session policy. The only repository change made by this audit is this report.
**Scope:** `src/Modules/HR.Modules.Onboarding/`, `src/Modules/HR.Modules.Offboarding/`, plus the specific cross-module surfaces they depend on (Tasks completion/notification, Assets assignment, Documents requests, Probation summary, Employees/manager reads). Recruitment, DataImport and other unrelated modules were not re-audited except where they directly affect these two lifecycles.

## Decision list — product-owner confirmation required

| # | Topic | Question | Classification |
|---|---|---|---|
| DEC-OB-1 | Offboarding reminder coverage | `OffboardingReminderJob` and `OnboardingReminderJob` both intentionally send zero reminder for HR-assigned / unassigned overdue tasks (locked in by passing tests — see `OffboardingReminderJobTests.ExecuteAsync_Sends_No_Notification_For_Overdue_HR_Assigned_Task` and the Onboarding equivalent for `Unassigned`). Should overdue HR/unassigned tasks generate a proactive notification (e.g. to all HR Administrators, or a "days overdue" surfacing in the HR Inbox), or is passive visibility via `GetUnassignedTasks` (the HR Inbox widget) considered sufficient? | Genuine open product decision |
| DEC-OB-2 | Offboarding dashboard/manager visibility | Onboarding has "My Onboarding Tasks" + "Team Onboarding" dashboard widgets and a timeline; Offboarding has neither a dashboard widget nor a timeline — only the Employee-detail tab. Is this intentionally out of scope for the current release, or is it a usability gap for managers/HR who have no way to see "who is leaving soon" without opening each employee record? | Genuine open product decision (see §6 recommendation) |
| DEC-OB-3 | `GetTeamOnboarding` authorization | `GetTeamOnboarding` (`src/Modules/HR.Modules.Onboarding/Features/GetTeamOnboarding/Endpoint.cs:12`) still uses `Policies("authenticated")` and trusts the route `managerId` with no check that the caller *is* that manager or HR. This is the same defect the prior full-repository audit recorded as `SEC-004` on 2026-07-11; it is **still present** in the current tree. Confirming remediation scope (self-manager-only vs. self-manager-or-HR) is a product decision already implicitly answered by the full audit's confirmed role/scope matrix (§7 items 2–3), so this is safe to fix without further confirmation. | Likely implementation defect — safe to fix without further decision |

## 1. Executive summary

Onboarding and Offboarding are architecturally sound: both modules own a single schema-scoped `DbContext`, communicate with the rest of the system exclusively through the `HR.Infrastructure.Abstractions` reader-port pattern (`IEmployeeNameReader`, `IManagerReader`, `IAssignedAssetReader`, `IOutstandingDocumentRequestReader`, `IOutstandingAssetAcknowledgementReader`, `IProbationSummaryReader`, `ITaskCreator`, `ITaskCompletionAction`, `INotificationWriter`), and pass all 171 architecture tests, all 70 Onboarding unit tests, and all 57 Offboarding unit tests as of this review. The build is clean for these two modules (0 errors). No cross-module project reference, generic repository, or business logic outside a module was found in either.

The two most important findings are not module-boundary violations — they are (a) a **live, unresolved authorization gap carried over from the prior repository-wide audit** (`GetTeamOnboarding` — SEC-004, still unfixed) and (b) a **more severe sibling of the same class of bug that this audit newly identified in the Tasks module**: `CompleteTask` (`src/Modules/HR.Modules.Tasks/Features/CompleteTask/Endpoint.cs:11`) lets *any* authenticated same-company user complete *any* task by ID — including another employee's onboarding or offboarding tasks — with no check that the caller is the assignee, their manager, or HR. Because onboarding/offboarding plan completion, HR review-task creation, and start/completion notifications all cascade off `CompleteTask`, this is a correctness and integrity bug for these two lifecycles specifically, not just a read-side privacy issue.

Beyond authorization, the domain layer has an identical, previously-flagged gap in **both** modules rather than only Offboarding: `OnboardingTaskStatus`/`OffboardingTaskStatus` both define `InProgress = 2`, but neither `OnboardingTask` nor `OffboardingTask` exposes any method to transition into it — it is unreachable dead state in both modules, not an Offboarding-specific omission. Both reminder jobs also correctly filter on it, so the code silently tolerates a state its own domain can never produce.

Test coverage is genuinely strong for what has been built: extensive edge-case coverage for zero-assets/zero-outstanding-documents/duplicate-start/terminal-state-reuse in `StartOffboardingHandlerTests`, and matching idempotency/notification-routing coverage in both reminder-job test suites. The Offboarding module, however, has no defensive guard analogous to its own `StartOffboarding` "active plan already exists" conflict check inside the *Onboarding* module's event-driven plan creation (`EmployeeCreatedHandler`) — a latent inconsistency that is not currently exploitable (no outbox/redelivery mechanism exists to double-fire the event) but would silently create duplicate plans/tasks the moment retry-based event delivery is introduced, contrary to the Event Architecture specification's idempotent-consumer requirement.

### Top five findings

1. **`CompleteTask` has no ownership/assignment check** — any same-company authenticated user can complete another employee's onboarding or offboarding task by ID, triggering plan-completion cascades and notifications on their behalf (new finding, Tasks module, directly affects both lifecycles).
2. **`GetTeamOnboarding` is still exploitable** — `Policies("authenticated")` plus an untrusted route `managerId` (confirmed still present; matches the prior audit's `SEC-004`).
3. **`OnboardingTaskStatus.InProgress` / `OffboardingTaskStatus.InProgress` are unreachable in both modules** — identical gap, not unique to Offboarding as originally suspected.
4. **HR-assigned/unassigned overdue tasks never generate a reminder in either job** — confirmed by name in both test suites; open product decision, not obviously a bug.
5. **Offboarding has no dashboard visibility or timeline**, unlike Onboarding's two widgets + timeline — an intentional-looking but undocumented scope asymmetry; recommendation given in §6.

## 2. Repository/module map (scoped)

| Module | Schema | DbContext | Endpoints | Domain entities | Reminder job |
|---|---|---|---|---|---|
| `HR.Modules.Onboarding` | `onboarding` | `OnboardingDbContext` (internal) | `GetOnboardingOverview`, `GetTeamOnboarding` | `OnboardingPlan`, `OnboardingTask`, `OnboardingTaskTemplate` (all internal) | `OnboardingReminderJob` — `Cron.Daily(7)` |
| `HR.Modules.Offboarding` | `offboarding` | `OffboardingDbContext` (internal) | `StartOffboarding`, `GetOffboardingOverview` | `OffboardingPlan`, `OffboardingTask` (all internal) | `OffboardingReminderJob` — `Cron.Daily(8)` |

Onboarding has no direct "start" endpoint — plan creation is entirely event-driven (`EmployeeCreatedIntegrationEvent` → `EmployeeCreatedHandler`). Offboarding has no event-driven entry point — plan creation is entirely endpoint-driven (`StartOffboarding`). This is a legitimate, deliberate design difference (onboarding begins automatically when an employee record is created; offboarding requires an explicit HR action with a last working day), not a defect, but it is the root cause of finding 4a below (only Offboarding has a duplicate-start guard, because only Offboarding has a "start" call that can be repeated).

## 3. Commands executed and results

| Command | Result |
|---|---|
| `dotnet build OneBigTeam.slnx -c Release --no-restore` | Succeeded: 0 errors, 12 warnings (all pre-existing `MSB3277` EF Core Relational 10.0.4/10.0.9 version-conflict warnings, now also emitted for `HR.Modules.Onboarding.Tests` — see §8; consistent with `ARC-005` in the prior full audit, not a new defect). |
| `dotnet test tests/HR.Architecture.Tests/HR.Architecture.Tests.csproj -c Release --no-build` | Passed: 171/171, 0 failed, 0 skipped. |
| `dotnet test tests/HR.Modules.Onboarding.Tests/HR.Modules.Onboarding.Tests.csproj -c Release --no-build` | Passed: 70/70, 0 failed, 0 skipped. |
| `dotnet test tests/HR.Modules.Offboarding.Tests/HR.Modules.Offboarding.Tests.csproj -c Release --no-build` | Passed: 57/57, 0 failed, 0 skipped. |
| `HR.Integration.Tests`, `HR.Web.E2E.Tests` | **Not executed** (out of scope per session policy). Read and assessed structurally in §7. |
| `git status --short` | Working tree is dirty: the entire Offboarding module, its unit tests, its architecture-test coverage, `GetOffboardingOverviewEndpointTests.cs`, and several onboarding-support files (`IAssignedAssetReader`, `AssignedAssetItem`, `AssignedAssetReader`) are untracked; several existing files are modified (see Limitations). |

## 4. Domain correctness

### 4.1 `OnboardingTaskStatus` / `OffboardingTaskStatus.InProgress` is unreachable in both modules

- `src/Modules/HR.Modules.Onboarding/Domain/OnboardingTaskStatus.cs:6` and `src/Modules/HR.Modules.Offboarding/Domain/OffboardingTaskStatus.cs:6` both declare `InProgress = 2`.
- `src/Modules/HR.Modules.Onboarding/Domain/OnboardingTask.cs:46-57` exposes only `Complete(DateTimeOffset)` and `Skip(DateTimeOffset)` — no method sets `InProgress`. Identically, `src/Modules/HR.Modules.Offboarding/Domain/OffboardingTask.cs:44-55` exposes only `Complete` and `Skip`.
- Both reminder jobs filter overdue tasks on `Status == Pending || Status == InProgress` (`OnboardingReminderJob.cs:24`, `OffboardingReminderJob.cs:24`), and `GetOnboardingOverviewHandler`/`GetOffboardingOverviewHandler` both render `Status.ToString()` including a UI badge for `"InProgress"` (`EmployeeOffboardingTab.razor:220`). All of this code is unreachable today because no code path can ever produce that state.
- **Classification: confirmed, identical pre-existing domain-completeness gap in both modules — not Offboarding-specific as the build log suspected.** It is cosmetic today (dead code, not a data-integrity risk) but should either be removed from both enums (simplify to `Pending/Completed/Skipped`) or implemented consistently (e.g. a `Start()`/`MarkInProgress()` method invoked when a `CompleteTask` action type other than `Complete` fires, or when a Tasks-module "start" action exists). No test in either `OnboardingTaskTests.cs` or `OffboardingTaskTests.cs` exercises `InProgress`, which is additional confirmation the state is genuinely dead, not merely under-tested.

### 4.2 Plan state machines are otherwise complete and consistent

- `OnboardingPlan` (`Domain/OnboardingPlan.cs:37-54`) and `OffboardingPlan` (`Domain/OffboardingPlan.cs:37-54`) are byte-for-byte structurally identical: `Create → NotStarted`, `Start → InProgress`, `Complete → Completed`, `Cancel → Cancelled`. Neither exposes a `Reopen`/`Uncancel`, which is consistent (no such requirement was described) and matches how both are actually driven.
- **Design difference, not a defect:** `OnboardingPlan` is created directly into `NotStarted` and only transitions to `InProgress` reactively, the first time any task on the plan completes (`CompleteOnboardingTaskFromTaskAction.cs:51-53`, `isStarting` branch). `OffboardingPlan` is created and immediately started in the same handler call (`StartOffboardingHandler.cs:43-46`, `plan.Start(now)` runs right after `Create`). This means a fresh Onboarding plan can sit in `NotStarted` indefinitely if nobody ever completes a task, whereas a fresh Offboarding plan is always `InProgress` from the moment `StartOffboarding` returns `201 Created`. This is consistent with each module's own UX (onboarding is passive until acted on; offboarding is explicitly started by HR) but is worth documenting explicitly since a reader comparing the two `Plan.Start()` call sites in isolation could otherwise mistake it for an oversight.

### 4.3 Onboarding plan creation has no duplicate-plan guard; Offboarding does

- `StartOffboardingHandler.HandleAsync` (`Features/StartOffboarding/Handler.cs:29-39`) explicitly queries for an existing non-terminal plan and returns `Result.Failure(...Conflict(...))` if one exists — this is tested directly (`StartOffboardingHandlerTests.HandleAsync_Returns_Conflict_When_Active_Plan_Already_Exists`, both `NotStarted` and `InProgress` variants, plus the inverse "terminal plans allow a new one" case).
- `EmployeeCreatedHandler.HandleAsync` (`Features/CreateOnboardingPlanOnEmployeeCreated/EmployeeCreatedHandler.cs:15-133`) has **no equivalent check** — it unconditionally calls `OnboardingPlan.Create(...)` and adds tasks every time it runs for a given `EmployeeId`. No test exercises "handler invoked twice for the same employee."
- **Classification: latent defect, not currently a live bug.** `IntegrationEventPublisher.PublishAsync` (`src/Shared/HR.SharedKernel/IntegrationEventPublisher.cs:6-12`) is a synchronous, in-process, single-dispatch call with no outbox and no Hangfire-based redelivery — it is invoked exactly once per successful `CreateEmployeeHandler.HandleAsync` call, and that handler itself is protected against duplicate submission by the Employees module's own work-email uniqueness check (`CreateEmployeeHandler.cs:53-63`). So today, nothing can actually cause `EmployeeCreatedHandler` to run twice for the same employee. However, the confirmed product decision in the existing full audit (§7 item 5) is that "an outbox is not required for v1" but "transactions, uniqueness and idempotency remain required where workflows can retry or partially fail," and the Event Architecture spec (`specifications/architecture/04-event-architecture.md:242-253`) explicitly requires idempotent consumers ("CandidateHired event received twice → Employee should only be created once"). Recommend adding an `AnyAsync` guard mirroring `StartOffboardingHandler`'s pattern before this module gains any retry-capable delivery mechanism, for consistency and defense in depth — not urgent given the current architecture.

## 5. Endpoint/authorization correctness

All FastEndpoints in both modules, enumerated directly from source:

| Endpoint | Route | Policy | Assessment |
|---|---|---|---|
| `GetOnboardingOverview` | `GET /api/companies/{companyId}/employees/{employeeId}/onboarding-overview` | `employee:manage` (HR Administrator only, per `IdentityModule.cs:52-53`) | Appropriate — this is the full plan/task detail view surfaced only inside the HR-gated `EmployeeEdit.razor` page (itself gated by `Session.CanManageEmployees`, `EmployeeEdit.razor:643-647`). Consistent with `GetOffboardingOverview`. |
| `GetTeamOnboarding` | `GET /api/companies/{companyId}/employees/{managerId}/team-onboarding` | `authenticated` only | **Confirmed still vulnerable** — see §5.1. Matches the prior full audit's `SEC-004` (unfixed as of `bbac33a`). |
| `GetOffboardingOverview` | `GET /api/companies/{companyId}/employees/{employeeId}/offboarding-overview` | `employee:manage` | Appropriate, same reasoning as `GetOnboardingOverview`. |
| `StartOffboarding` | `POST /api/companies/{companyId}/employees/{employeeId}/offboarding/start` | `employee:manage` | Appropriate — starting an exit process is correctly restricted to HR Administrator. |

### 5.1 `GetTeamOnboarding` — SEC-004 is still live

- `src/Modules/HR.Modules.Onboarding/Features/GetTeamOnboarding/Endpoint.cs:11-12` declares only `Policies("authenticated")` and binds `managerId` straight from the route. `GetTeamOnboardingHandler.HandleAsync` (`Handler.cs:13-20`) passes that `managerId` directly into `IDirectReportsReader.GetDirectReportIdsAsync` with no check that the caller's own `EmployeeId`/role equals or is authorized to view that manager's team.
- The Blazor UI never exploits this — `TeamOnboardingWidget.razor:78-84` always calls `GetTeamOnboardingAsync(Session.CompanyId, Session.EmployeeId.Value)`, i.e. it only ever asks for the caller's own team, and the widget itself is additionally gated by `Session.CanManageEmployees` (`TeamOnboardingWidget.razor:5`). But the API contract does not enforce this — any authenticated same-company user (including a plain Employee role with no `CanManageEmployees`) can call the endpoint directly with an arbitrary `managerId` and receive that manager's direct reports' names, onboarding status, and task-completion percentages.
- `GetTeamOnboardingEndpointTests.cs` in `HR.Integration.Tests` tests the happy path and an anonymous-401 case, but has **no negative test** for a non-manager/non-HR caller supplying someone else's `managerId` — so this gap is not caught by the existing (unexecuted) integration suite either.
- **Correction:** derive the manager identity from `ICurrentUser`/claims for the "my team" case, and/or additionally accept `employee:manage`/HR company-wide scope, mirroring the pattern the prior full audit recommended for `SEC-004`. This is safe to implement without further product confirmation — the full audit's §7 item 2 role/scope matrix already establishes that managers may view their own hierarchy and HR may view company-wide.

### 5.2 New finding: `CompleteTask` has no ownership check (Tasks module, but directly drives both lifecycles)

- `src/Modules/HR.Modules.Tasks/Features/CompleteTask/Endpoint.cs:8-12` declares `Policies("authenticated")` and derives `CompletedBy` from the caller's own `sub` claim (good — this part correctly avoids trusting a client-supplied identity). However, `CompleteTaskHandler.HandleAsync` (`Handler.cs:21-24`) looks the task up only `by Id` and `CompanyId` — there is no check that `task.AssignedEmployeeId == completedBy`, nor any manager/HR override check.
- Consequence for these two lifecycles specifically: any same-company authenticated employee who learns or guesses another employee's onboarding/offboarding task ID (task IDs are returned in `StartOffboardingResponse.GeneratedTaskIds`, appear in `GetOnboardingOverviewResponse`/`GetOffboardingOverviewResponse` — which are HR-scoped reads, but also flow into the general Tasks module's own list endpoints) can `POST /api/companies/{companyId}/tasks/{id}/complete` for a task that is not theirs. Because `CompleteTaskHandler` unconditionally dispatches to `ITaskCompletionAction` implementations (`Handler.cs:61-73`), this would falsely mark, e.g., another employee's "Return asset" offboarding task or "Set up workstation" onboarding task complete, potentially cascading into `plan.Complete()`, an HR "plan completed" review task, and completion notifications — all attributable to a party who was never assigned the task.
- **Classification: correctness/integrity bug, not merely a privacy leak.** It is a mutation-side sibling of the read-side `SEC-002` finding in the prior full audit (which covered `GetEmployeeTasks`/`GetLeaveRequest` reads only) — `CompleteTask` was not called out there and appears to be a materially more severe instance of the same missing-scope-check pattern, because it corrupts state rather than merely disclosing it.
- **Correction:** in `CompleteTaskHandler`, require `task.AssignedEmployeeId == request.CompletedBy` OR the caller holds `employee:manage`/manager-of-assignee scope, returning `403`/`Forbidden` otherwise. Add HTTP tests for: assignee completes own task (200), a different same-company employee attempts to complete someone else's task (403), HR completes an unassigned task (200, if that is the intended override path).
- This is outside the Onboarding/Offboarding module boundary (the fix belongs in `HR.Modules.Tasks`), so per the audit's scope it is reported here as an item affecting these two lifecycles' correctness rather than remediated directly.

## 6. Cross-module integration correctness

- The reader-interface pattern is used consistently: every cross-module read in both modules goes through an interface defined in `HR.Infrastructure.Abstractions` (`IEmployeeNameReader`, `IManagerReader`, `IDirectReportsReader`, `IAssignedAssetReader`, `IOutstandingDocumentRequestReader`, `IOutstandingAssetAcknowledgementReader`, `IProbationSummaryReader`) and implemented in the owning module (e.g. `AssignedAssetReader` in `HR.Modules.Assets/Services/`, confirmed via the untracked `IAssignedAssetReader.cs`/`AssignedAssetItem.cs` files added alongside Offboarding). Neither `HR.Modules.Onboarding.csproj` nor `HR.Modules.Offboarding.csproj` references another `HR.Modules.*` project directly.
- `HR.Architecture.Tests` does enforce this: `ModuleDependencyBoundariesTests.cs:21-22` includes both `OnboardingModule` and `OffboardingModule` assemblies in its cross-module-reference theory data, and `OffboardingModuleArchitectureTests.cs` (untracked, 12 tests) independently re-verifies internal visibility, schema name, snake_case columns, GUID primary keys, and `company_id NOT NULL` for both `OffboardingPlan` and `OffboardingTask`. All 171 architecture tests pass, confirming these checks are live, not aspirational.
- `ITaskCompletionAction`/`TaskCompletionDispatcher` wiring (`HR.Modules.Tasks/Services/TaskCompletionDispatcher.cs:5-12`) is a simple `IEnumerable<ITaskCompletionAction>` fan-out keyed on `(Source, ActionType)`; both `CompleteOnboardingTaskFromTaskAction` and `CompleteOffboardingTaskFromTaskAction` register correctly against `TaskSource.Onboarding`/`TaskSource.Offboarding` + `TaskActionType.Complete` in their respective `*Module.cs` files. No module references the Tasks module's types directly beyond the shared `HR.Infrastructure.Abstractions` contracts (`ITaskCreator`, `ITaskCompletionAction`, `TaskCompletionContext`, `TaskSource`, `TaskActionType`), which is the correct boundary.

## 7. Task-generation and completion logic

### 7.1 Onboarding task generation (`EmployeeCreatedHandler.cs`)

- If the employee's position profile has an active onboarding template, its tasks are used (`GetTemplateTasksAsync`, `EmployeeCreatedHandler.cs:141-158`); otherwise a hardcoded fallback of three tasks (workstation setup, welcome email, induction meeting) is created. The fallback path is only taken if `PositionProfileId` is null, no template is linked, or the template has zero active tasks — this triple-fallback condition is deliberate and commented (`EmployeeCreatedHandler.cs:135-140`).
- `IsImported` employees are correctly skipped entirely (`EmployeeCreatedHandler.cs:17-18`), consistent with the confirmed product decision that imported employees are existing workforce, not new starters (prior full audit §7 item 8, `BUS-002`).

### 7.2 Offboarding task generation (`StartOffboardingHandler.cs`)

- Zero assigned assets → zero asset-return tasks generated, no error (`CreateAssetReturnTasksAsync`, tested by `HandleAsync_Creates_No_Asset_Tasks_When_No_Assets_Assigned`).
- Zero outstanding document requests → the HR document-review task is still created, with description text switched to "No outstanding document requests." rather than being skipped (`CreateDocumentReviewTaskAsync:119-121`, tested by `HandleAsync_HR_Task_Description_States_No_Outstanding_Requests_When_None`). This is a reasonable design choice (HR still gets a checkable task confirming the review happened) rather than a bug.
- Manager exit checklist (4 fixed tasks) is always created regardless of whether the employee has a manager; if no manager exists, `assignedEmployeeId`/`assignedUserId` are simply `null` (tested by `HandleAsync_Manager_Checklist_Tasks_Have_Null_Assignees_When_No_Manager`) — these fall into the HR Inbox as unassigned tasks rather than being silently dropped.
- Calling `StartOffboarding` twice for the same employee while a plan is `NotStarted`/`InProgress` correctly returns `409 Conflict` (both a unit test and `StartOffboardingEndpointTests.Post_StartOffboarding_Returns_Conflict_When_Plan_Already_Exists` confirm this at the HTTP layer); starting again after a plan reaches `Completed`/`Cancelled` correctly succeeds.

### 7.3 Completion cascade (`CompleteOnboardingTaskFromTaskAction` / `CompleteOffboardingTaskFromTaskAction`)

- Both actions correctly no-op if the target task is already `Completed`/`Skipped` (`CompleteOnboardingTaskFromTaskAction.cs:35-36`, `CompleteOffboardingTaskFromTaskAction.cs:34-35`), which prevents duplicate notification/plan-completion side effects if `CompleteTask` were somehow invoked twice for the same underlying task.
- Minor inconsistency: `CompleteOffboardingTaskFromTaskAction.cs:54` additionally guards `plan.Status != OffboardingStatus.Completed` before recomputing `isCompleting`; `CompleteOnboardingTaskFromTaskAction.cs:59-60` does not have the equivalent `plan.Status != OnboardingStatus.Completed` guard. In practice this makes no observable difference today, because the per-task early-return above already prevents re-entering this code path once every task on the plan is terminal — but it is a small, unexplained asymmetry between two otherwise line-for-line-identical files, worth aligning for consistency rather than treating as a live bug.
- Neither action wraps its "read tasks → compute isCompleting → save → notify/create review task" sequence in a database transaction beyond EF's own per-`SaveChangesAsync` unit of work; two concurrent completions of the last two remaining tasks on the same plan could both observe `isCompleting == true` and both attempt `plan.Complete()`/`CreateHrCompletionReviewTaskAsync`, producing two duplicate "plan completed" review tasks. This is the same category of non-atomic-side-effect risk the prior full audit already recorded generally as `PER-002`/`JOB-001`; it is not new to these two modules and is not a heightened risk here (task completion is a low-concurrency, human-driven action), so it is noted but not re-scored as a new independent finding.

## 8. Reminder jobs

| | `OnboardingReminderJob` | `OffboardingReminderJob` |
|---|---|---|
| Schedule | `Cron.Daily(7)` (7am) | `Cron.Daily(8)` (8am) |
| "Overdue" definition | `DueDate < today && Status ∈ {Pending, InProgress}` | Identical |
| `AssignTo = Manager` | Notifies the employee's manager via `IManagerReader` | Identical |
| `AssignTo = NewHire` / `Employee` | Notifies the employee directly | Identical (Offboarding's equivalent bucket is named `Employee`, not `NewHire`) |
| `AssignTo = Unassigned` / `HR` | **No notification sent** (falls through `default` in the `switch`) | **No notification sent** (falls through `default` in the `switch`) |
| Idempotency | `INotificationWriter.ExistsAsync(employeeId, taskId, type)` check-then-write, no DB unique constraint | Identical pattern |
| Batching/cancellation | Single unbounded `ToListAsync()` across all tenants, no `CancellationToken` parameter on `ExecuteAsync()` | Identical |

- Confirmed by name in both test suites, not just inferred from reading: `OffboardingReminderJobTests.ExecuteAsync_Sends_No_Notification_For_Overdue_HR_Assigned_Task` (`OffboardingReminderJobTests.cs:117-133`) and `OnboardingReminderJobTests.ExecuteAsync_Sends_No_Notification_For_Overdue_Unassigned_Task` (`OnboardingReminderJobTests.cs:116-134`) both assert **zero** notifications for their respective "nobody in particular" assignment bucket. This is deliberate, test-locked current behaviour in both modules, not an oversight that slipped past review — but see **DEC-OB-1** above: it means the one offboarding task type that always exists on every plan (the HR document-review task) can silently become overdue forever with no proactive nudge to anyone, mitigated only by HR happening to check the HR Inbox (`GetUnassignedTasks`) or the per-employee overview.
- The idempotency pattern (`ExistsAsync` then `WriteAsync`, no DB unique constraint) mirrors `PER-001` from the prior full audit exactly, extended into these two new jobs. `OffboardingReminderJobTests.ExecuteAsync_Does_Not_Duplicate_Notification_When_Run_Twice` (and the Onboarding equivalent) confirm the application-level check works under sequential re-execution, but neither test (nor the underlying code) proves protection under concurrent execution of two job instances, which `PER-001` already flagged as a systemic risk with no unique constraint backing it.
- Batching by tenant/cancellation-token support: both jobs inherit the same unbounded, non-cancellable pattern the prior audit flagged generically as `JOB-002` for `OnboardingReminderJob.cs` specifically (already named in that audit) and now, by construction, also for `OffboardingReminderJob.cs`. Not re-scored as a new finding; noted for completeness since the task brief asked for a direct comparison.

## 9. UI completeness and consistency

| | Onboarding | Offboarding |
|---|---|---|
| Employee-detail tab | Yes (`EmployeeOnboardingTab.razor`) | Yes (`EmployeeOffboardingTab.razor`) |
| Dedicated progress-panel component | Yes (`OnboardingProgressPanel.razor`) | No — inlined directly into the tab (`EmployeeOffboardingTab.razor:17-47`) |
| Dedicated checklist component | Yes (`OnboardingChecklist.razor`) | No — inlined as a plain table (`EmployeeOffboardingTab.razor:49-92`) |
| Timeline | Yes (`OnboardingTimeline.razor`) | **None** |
| Dashboard widgets | Two: "My Onboarding Tasks", "Team Onboarding" | **None** |
| Start action | N/A (event-driven) | "Start Offboarding" dialog with client + server validation |

- This asymmetry is functionally complete for what was built (both tabs render a status badge, a progress bar, and a checklist; Offboarding's is just less componentized), and the E2E test suite documents the scope difference explicitly in a code comment (`EmployeeOffboardingTabTests.cs:8-18`: "Unlike Onboarding... there is no seed data or auto-provisioning path"). It is not a bug.
- **Recommendation:** the missing dashboard visibility is the more consequential half of the gap. Onboarding's "Team Onboarding" widget gives a manager/HR a single place to see who is currently onboarding without opening each employee record; there is no equivalent for offboarding, so a manager/HR must already know which specific employees are leaving and open each one's profile to check exit-task progress. Given offboarding tasks are time-boxed against a hard `LastWorkingDay` (unlike onboarding, which has no equivalent single hard deadline), a "Departing Soon" or "Team Offboarding" widget arguably has *higher* proactive value than its onboarding counterpart, not lower. Recommend treating this as a near-term follow-up rather than permanently out of scope — flagged as **DEC-OB-2** above for explicit product confirmation either way.
- The timeline gap is lower priority: Onboarding's timeline is a nice-to-have chronological view over the same task data already fully exposed by the checklist; Offboarding's flat checklist table conveys equivalent information (task, assign-to, due date, status, completed-at) with less visual polish but no missing data.

## 10. Test coverage

### 10.1 Executed counts (this audit)

| Project | Result |
|---|---|
| `HR.Architecture.Tests` | 171 passed, 0 failed, 0 skipped |
| `HR.Modules.Onboarding.Tests` | 70 passed, 0 failed, 0 skipped |
| `HR.Modules.Offboarding.Tests` | 57 passed, 0 failed, 0 skipped |

Both module test projects cover: domain entity/state tests (`OnboardingPlanTests`, `OnboardingTaskTests`, `OnboardingTaskTemplateTests`, `OffboardingPlanTests`, `OffboardingTaskTests`), handler tests for every feature (`CreateOnboardingPlanOnEmployeeCreatedHandlerTests`, `GetOnboardingOverviewHandlerTests`, `GetTeamOnboardingHandlerTests`, `StartOffboardingHandlerTests`, `GetOffboardingOverviewHandlerTests`), task-completion-action tests (`CompleteOnboardingTaskFromTaskActionTests`, `CompleteOffboardingTaskFromTaskActionTests`), reminder-job tests, and a `DbContext`-mapping smoke test (`OnboardingDbContextTests`, `OffboardingDbContextTests`). There is no `Validator` test file for Offboarding despite one existing (`StartOffboardingValidator`) — validator coverage appears to be folded into `StartOffboardingHandlerTests`' `Post_StartOffboarding_Returns_ValidationError_When_LastWorkingDay_Is_Missing`-style assertions at the integration level rather than a dedicated unit-level validator test class; this is a minor deviation from the "Validator.cs + dedicated validator tests" vertical-slice convention, not a coverage gap in substance.

### 10.2 Integration tests (read, not executed)

Both modules have HTTP-level coverage in `tests/HR.Integration.Tests/`, structurally consistent with the repository's established pattern (`ApiWebApplicationFactory`, `TestRoleSeeder`, `TestAuthHandler`):

- `GetOnboardingOverviewEndpointTests.cs`, `GetTeamOnboardingEndpointTests.cs` — Onboarding.
- `StartOffboardingEndpointTests.cs`, `GetOffboardingOverviewEndpointTests.cs` — Offboarding.

All four cover anonymous-401, happy path, and at least one 404/409/validation-failure case, matching the brief's required shape. The one gap: **none of the four test files include a "wrong-tenant-scope but same-company, non-HR/non-manager caller" negative case** for the two endpoints that would need it most (`GetTeamOnboarding` for §5.1's live vulnerability; `StartOffboarding`/`GetOffboardingOverview` are HR-only via `employee:manage`, so a same-policy negative test would need a non-HR role, which is also absent). Recommend adding these once §5.1 is fixed, so the fix is regression-protected.

### 10.3 E2E tests (read, not executed)

- `tests/HR.Web.E2E.Tests/Tests/EmployeeOnboardingTabTests.cs`, `OnboardingWidgetsTests.cs`, and `EmployeeOffboardingTabTests.cs` all follow the established Page-Object pattern (`EmployeeOffboardingTab.cs`, `OnboardingTemplateEditPage.cs` under `Infrastructure/PageObjects/`) and the same login/create-employee/assert-state structure as `EmploymentTypeManagementTests.cs`/`LeaveTypeManagementTests.cs`. They are structurally sound by inspection: correct fixture usage (`[Collection("E2E")]`, `AppFixture`), unique per-test data (`Guid.NewGuid().ToString("N")[..8]` suffixes), and specific (not loose) assertions on checklist task titles/status/percentages.
- Coverage is proportionate to what was built: Onboarding's tests cover the tab, both dashboard widgets, and a full claim-then-complete-then-verify-progress flow; Offboarding's tests cover the tab, the Start dialog (success + validation failure), and the fixed-checklist-generation case, with no widget tests (because no widgets exist — consistent with §9's finding, not a test-suite defect).

## 11. Build status

`dotnet build OneBigTeam.slnx -c Release --no-restore`: **0 errors, 12 warnings**, all `MSB3277` assembly-version-conflict warnings between `Microsoft.EntityFrameworkCore.Relational` 10.0.4 (test projects) and 10.0.9 (production), now also emitted against `HR.Modules.Onboarding.Tests.csproj` in addition to the projects already named in the prior full audit's `ARC-005`. No warning is specific to Onboarding/Offboarding application code; both modules' own source compiles cleanly. No `dotnet ef migrations add` or database mutation was run.

## 12. Items inspected with no material problems found

- No direct `HR.Modules.*` project reference from either module (`ModuleDependencyBoundariesTests` — 171/171 pass, includes both assemblies).
- Both `DbContext`s are internal, schema-scoped, snake_case, GUID-keyed, and `company_id NOT NULL` on every tenant table (`OffboardingModuleArchitectureTests.cs` proves this for Offboarding directly; Onboarding's equivalent architecture coverage passes as part of the same 171-test run).
- File-scoped namespaces used throughout both modules — no block-scoped `namespace X { }` found in any inspected file.
- No generic repository, no business logic found in `HR.Web`/`HR.Api`/`HR.Infrastructure` for these two lifecycles — `EmployeeOffboardingTab.razor`/`EmployeeOnboardingTab.razor` only call `OffboardingService`/`OnboardingService` HTTP wrappers and render results; all business rules (conflict checks, task generation, notification routing) live in the module handlers/domain.
- `TaskCreator`/`ITaskCreator` calls in both modules pass `CancellationToken` consistently.
- Neither module's migrations touch an existing table outside its own schema.

## 13. Recommended remediation order

1. Fix `CompleteTask`'s missing ownership/assignment check (§5.2) — highest-impact, affects correctness of both lifecycles' completion cascade, not just this repository's general authorization posture.
2. Fix `GetTeamOnboarding`'s still-open `SEC-004` (§5.1) — carried over from the prior audit, safe to fix without further product confirmation.
3. Resolve **DEC-OB-1** and **DEC-OB-2** with the product owner (reminder coverage for HR/unassigned tasks; offboarding dashboard/timeline scope) and implement whichever direction is chosen.
4. Add the `AnyAsync` duplicate-plan guard to `EmployeeCreatedHandler` for defense in depth (§4.3), low urgency given no current redelivery mechanism exists.
5. Reconcile the `InProgress` dead-state gap in both `OnboardingTask`/`OffboardingTask` (§4.1) — either implement a real transition or remove the unused enum value from both, plus the corresponding unreachable branches in both reminder jobs.
6. Cosmetic: align `CompleteOnboardingTaskFromTaskAction`'s `isCompleting` guard with Offboarding's `plan.Status != Completed` check for consistency (§7.3).

## 14. Limitations of this review

- The entire `HR.Modules.Offboarding` module, its unit tests, its architecture-test file, and its integration test files are currently **untracked** in git (not the prior full audit's revision `5c9b48a`, and not yet part of `HEAD` at `bbac33a` either — confirmed via `git status --short`). This audit reviewed the current working-tree state as instructed by the task brief, consistent with how the prior full audit handled its own uncommitted `SEC-004` finding.
- `HR.Integration.Tests` and `HR.Web.E2E.Tests` were read for structural soundness only; per standing session policy they were not executed, so no behavioural (as opposed to static) confirmation of their pass/fail status is claimed here.
- This review did not re-examine Recruitment, DataImport, or other unrelated modules beyond the specific reader-port surfaces (Assets, Documents, Probation, Employees/manager hierarchy) that Onboarding/Offboarding directly consume.
- No dynamic/penetration testing was performed; the `CompleteTask` and `GetTeamOnboarding` findings are static-analysis-confirmed code-path conclusions (the missing check is directly visible in the handler source), not verified via a live exploit request.

---

**Assessment:** Onboarding and Offboarding are architecturally compliant modules with strong unit-test coverage and passing architecture tests. They are not currently safe to treat as "authorization-complete," because a live cross-tenant-safe-but-same-company IDOR (`GetTeamOnboarding`, carried over unresolved from the prior audit) and a newly identified, more severe write-side IDOR in the Tasks module (`CompleteTask`) both directly undermine the integrity of these two lifecycles' task-completion and plan-completion cascades. Neither is a module-boundary or persistence-standards violation; both are endpoint-level authorization gaps with a clear, narrow fix.
